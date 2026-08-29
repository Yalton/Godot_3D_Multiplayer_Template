using Godot;

namespace MultiplayerTemplate.Network;

public readonly record struct ClockSample(int Sequence, double RttMs, double OffsetMs, ulong ServerTick);

public static class NetworkTimeMath
{
    public static long OffsetUsec(long clientSendUsec, long clientReceiveUsec, long serverReceiveUsec)
    {
        var rttUsec = Math.Max(0, clientReceiveUsec - clientSendUsec);
        return serverReceiveUsec + rttUsec / 2 - clientReceiveUsec;
    }

    public static ulong TickAt(long serverTimeUsec, int tickRate = NetworkClock.TickRate) =>
        (ulong)Math.Max(0, serverTimeUsec * tickRate / 1_000_000);

    public static double RemainingSeconds(long deadlineUsec, long nowUsec) =>
        Math.Max(0, (deadlineUsec - nowUsec) / 1_000_000.0);

    public static float Progress(long deadlineUsec, long nowUsec, double durationSeconds) =>
        durationSeconds <= 0 ? 1f :
        Math.Clamp(1f - (float)(RemainingSeconds(deadlineUsec, nowUsec) / durationSeconds), 0f, 1f);
}

public readonly record struct NetworkDeadline(long ServerDeadlineUsec)
{
    public double RemainingSeconds =>
        NetworkTimeMath.RemainingSeconds(ServerDeadlineUsec, NetworkClock.Instance.ServerTimeUsec);
    public bool Expired => NetworkClock.Instance.ServerTimeUsec >= ServerDeadlineUsec;
    public float Progress(double durationSeconds) =>
        NetworkTimeMath.Progress(ServerDeadlineUsec, NetworkClock.Instance.ServerTimeUsec, durationSeconds);
}

/// <summary>NTP-style monotonic server clock and authoritative 60 Hz simulation timeline.</summary>
public partial class NetworkClock : Node
{
    public const int TickRate = 60;
    public const string RoundTimer = "round";
    public const string MatchTimer = "match";
    public static NetworkClock Instance { get; private set; } = null!;

    public event Action<ClockSample>? SampleReceived;
    public event Action<int, long>? ProbeSent;
    public event Action<string, NetworkDeadline>? TimerStarted;
    public event Action<string>? TimerExpired;

    public bool IsSynchronized => Multiplayer.HasMultiplayerPeer() &&
        (Multiplayer.IsServer() || _receivedSamples >= 3);
    public long ServerTimeUsec => LocalUsec + (long)_smoothedOffsetUsec;
    public double ServerTimeSeconds => ServerTimeUsec / 1_000_000.0;
    public ulong AuthoritativeTick => NetworkTimeMath.TickAt(ServerTimeUsec);
    public double LastRttMs { get; private set; }

    private static long LocalUsec => (long)Time.GetTicksUsec();
    private readonly System.Collections.Generic.Dictionary<int, long> _pending = new();
    private readonly System.Collections.Generic.Dictionary<string, NetworkDeadline> _timers = new();
    private readonly System.Collections.Generic.List<ScheduledAction> _scheduled = new();
    private double _probeAccumulator;
    private double _smoothedOffsetUsec;
    private int _sequence;
    private int _receivedSamples;

    private sealed record ScheduledAction(long AtUsec, Action Callback);

    public override void _Ready() => Instance = this;

    public override void _Process(double delta)
    {
        if (!Multiplayer.HasMultiplayerPeer()) return;
        if (!Multiplayer.IsServer())
        {
            _probeAccumulator += delta;
            if (_probeAccumulator >= 0.5)
            {
                _probeAccumulator = 0;
                SendProbe();
            }
        }
        RunScheduledActions();
        CheckTimers();
    }

    public NetworkDeadline DeadlineAfter(double seconds) =>
        new(ServerTimeUsec + (long)(Math.Max(0, seconds) * 1_000_000));

    public void ScheduleAtServerTime(long serverTimeUsec, Action callback)
    {
        _scheduled.Add(new ScheduledAction(serverTimeUsec, callback));
        _scheduled.Sort((a, b) => a.AtUsec.CompareTo(b.AtUsec));
    }

    public void ScheduleAfter(double seconds, Action callback) =>
        ScheduleAtServerTime(DeadlineAfter(seconds).ServerDeadlineUsec, callback);

    public void StartTimer(string name, double durationSeconds)
    {
        if (!Multiplayer.IsServer()) return;
        Rpc(nameof(ApplyTimer), name, DeadlineAfter(durationSeconds).ServerDeadlineUsec);
    }

    public NetworkDeadline GetTimer(string name) =>
        _timers.GetValueOrDefault(name, new NetworkDeadline(ServerTimeUsec));

    public double RemainingSeconds(string name) =>
        _timers.TryGetValue(name, out var timer) ? timer.RemainingSeconds : 0;

    public void StartRoundTimer(double durationSeconds) => StartTimer(RoundTimer, durationSeconds);
    public void StartMatchTimer(double durationSeconds) => StartTimer(MatchTimer, durationSeconds);
    public double RoundSecondsRemaining => RemainingSeconds(RoundTimer);
    public double MatchSecondsRemaining => RemainingSeconds(MatchTimer);

    private void SendProbe()
    {
        var sequence = ++_sequence;
        var sent = LocalUsec;
        _pending[sequence] = sent;
        ProbeSent?.Invoke(sequence, sent);
        RpcId(1, nameof(RequestClockSample), sequence, sent);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 3)]
    private void RequestClockSample(int sequence, long clientSendUsec)
    {
        if (!Multiplayer.IsServer()) return;
        RpcId(Multiplayer.GetRemoteSenderId(), nameof(ReceiveClockSample),
            sequence, clientSendUsec, LocalUsec, AuthoritativeTick);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 3)]
    private void ReceiveClockSample(int sequence, long clientSendUsec, long serverReceiveUsec, ulong serverTick)
    {
        if (!_pending.Remove(sequence)) return;
        var received = LocalUsec;
        var rttUsec = Math.Max(0, received - clientSendUsec);
        var offsetUsec = NetworkTimeMath.OffsetUsec(clientSendUsec, received, serverReceiveUsec);
        _smoothedOffsetUsec = _receivedSamples == 0
            ? offsetUsec
            : Mathf.Lerp(_smoothedOffsetUsec, offsetUsec, 0.15);
        _receivedSamples++;
        LastRttMs = rttUsec / 1000.0;
        SampleReceived?.Invoke(new ClockSample(sequence, LastRttMs, offsetUsec / 1000.0, serverTick));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ApplyTimer(string name, long deadlineUsec)
    {
        var timer = new NetworkDeadline(deadlineUsec);
        _timers[name] = timer;
        TimerStarted?.Invoke(name, timer);
    }

    private void CheckTimers()
    {
        if (_timers.Count == 0) return;
        foreach (var (name, deadline) in _timers.ToArray())
        {
            if (!deadline.Expired) continue;
            _timers.Remove(name);
            TimerExpired?.Invoke(name);
        }
    }

    private void RunScheduledActions()
    {
        while (_scheduled.Count > 0 && _scheduled[0].AtUsec <= ServerTimeUsec)
        {
            var callback = _scheduled[0].Callback;
            _scheduled.RemoveAt(0);
            callback();
        }
    }
}
