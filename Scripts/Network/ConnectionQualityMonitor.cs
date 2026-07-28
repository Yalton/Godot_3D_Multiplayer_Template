using Godot;

namespace MultiplayerTemplate.Network;

public enum ConnectionHealth { Excellent, Good, Degraded, Timeout }

public readonly record struct ConnectionQuality(
    double PingMs, double JitterMs, double PacketLossPercent, ConnectionHealth Health)
{
    public static ConnectionQuality Host => new(0, 0, 0, ConnectionHealth.Excellent);
    public string ShortText => Health == ConnectionHealth.Timeout
        ? "TIMEOUT"
        : $"{PingMs:0} ms · {PacketLossPercent:0}%";
}

/// <summary>Rolling connection metrics derived from network-clock probes.</summary>
public partial class ConnectionQualityMonitor : CanvasLayer
{
    public static ConnectionQualityMonitor Instance { get; private set; } = null!;
    public event Action<long, ConnectionQuality>? QualityChanged;

    public ConnectionQuality LocalQuality { get; private set; } = ConnectionQuality.Host;
    public IReadOnlyDictionary<long, ConnectionQuality> PeerQuality => _peerQuality;

    private readonly System.Collections.Generic.Dictionary<int, Probe> _probes = new();
    private readonly Queue<bool> _outcomes = new();
    private readonly Queue<double> _rtts = new();
    private readonly System.Collections.Generic.Dictionary<long, ConnectionQuality> _peerQuality = new();
    private Label _overlay = null!;
    private double _updateAccumulator;
    private long _lastResponseUsec;

    private sealed record Probe(long SentUsec, bool Answered);

    public override void _Ready()
    {
        Instance = this;
        Layer = 90;
        BuildOverlay();
        NetworkClock.Instance.ProbeSent += OnProbeSent;
        NetworkClock.Instance.SampleReceived += OnSampleReceived;
        SessionManager.Instance.MatchEnded += Reset;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("network_debug"))
        {
            _overlay.Visible = !_overlay.Visible;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!Multiplayer.HasMultiplayerPeer())
        {
            _overlay.Text = "Network: offline";
            return;
        }
        _updateAccumulator += delta;
        if (_updateAccumulator < 0.5) return;
        _updateAccumulator = 0;
        ExpireProbes();
        Recalculate();
        UpdateOverlay();
        if (!Multiplayer.IsServer()) RpcId(1, nameof(ReportQuality),
            LocalQuality.PingMs, LocalQuality.JitterMs,
            LocalQuality.PacketLossPercent, (int)LocalQuality.Health);
    }

    public ConnectionQuality ForPeer(long peerId)
    {
        if (Multiplayer.HasMultiplayerPeer() && peerId == 1) return ConnectionQuality.Host;
        return _peerQuality.GetValueOrDefault(peerId,
            peerId == Multiplayer.GetUniqueId() ? LocalQuality :
            new ConnectionQuality(0, 0, 0, ConnectionHealth.Degraded));
    }

    private void OnProbeSent(int sequence, long sentUsec)
    {
        _probes[sequence] = new Probe(sentUsec, false);
        if (_lastResponseUsec == 0) _lastResponseUsec = sentUsec;
    }

    private void OnSampleReceived(ClockSample sample)
    {
        _lastResponseUsec = (long)Time.GetTicksUsec();
        if (_probes.Remove(sample.Sequence)) AddOutcome(true);
        _rtts.Enqueue(sample.RttMs);
        while (_rtts.Count > 30) _rtts.Dequeue();
    }

    private void ExpireProbes()
    {
        var cutoff = (long)Time.GetTicksUsec() - 2_000_000;
        foreach (var (sequence, probe) in _probes.ToArray())
        {
            if (probe.SentUsec >= cutoff) continue;
            _probes.Remove(sequence);
            AddOutcome(false);
        }
    }

    private void AddOutcome(bool received)
    {
        _outcomes.Enqueue(received);
        while (_outcomes.Count > 30) _outcomes.Dequeue();
    }

    private void Recalculate()
    {
        if (Multiplayer.IsServer())
        {
            LocalQuality = ConnectionQuality.Host;
            return;
        }
        var ping = _rtts.Count == 0 ? 0 : _rtts.Average();
        var jitter = 0.0;
        var samples = _rtts.ToArray();
        for (var i = 1; i < samples.Length; i++) jitter += Math.Abs(samples[i] - samples[i - 1]);
        if (samples.Length > 1) jitter /= samples.Length - 1;
        var loss = _outcomes.Count == 0 ? 0 : _outcomes.Count(ok => !ok) * 100.0 / _outcomes.Count;
        var staleSeconds = _lastResponseUsec == 0
            ? double.MaxValue
            : (Time.GetTicksUsec() - (ulong)_lastResponseUsec) / 1_000_000.0;
        var health = staleSeconds > 5 ? ConnectionHealth.Timeout
            : staleSeconds > 2 || loss >= 15 || ping >= 250 || jitter >= 80 ? ConnectionHealth.Degraded
            : loss >= 5 || ping >= 120 || jitter >= 35 ? ConnectionHealth.Good
            : ConnectionHealth.Excellent;
        LocalQuality = new ConnectionQuality(ping, jitter, loss, health);
        ApplyPeerQuality(Multiplayer.GetUniqueId(), LocalQuality);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    private void ReportQuality(double ping, double jitter, double loss, int health)
    {
        if (!Multiplayer.IsServer()) return;
        var peerId = Multiplayer.GetRemoteSenderId();
        var quality = Sanitize(ping, jitter, loss, health);
        ApplyPeerQuality(peerId, quality);
        Rpc(nameof(ReceiveQuality), peerId, quality.PingMs, quality.JitterMs,
            quality.PacketLossPercent, (int)quality.Health);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    private void ReceiveQuality(long peerId, double ping, double jitter, double loss, int health) =>
        ApplyPeerQuality(peerId, Sanitize(ping, jitter, loss, health));

    private static ConnectionQuality Sanitize(double ping, double jitter, double loss, int health) =>
        new(Math.Clamp(ping, 0, 5000), Math.Clamp(jitter, 0, 5000), Math.Clamp(loss, 0, 100),
            (ConnectionHealth)Math.Clamp(health, 0, (int)ConnectionHealth.Timeout));

    private void ApplyPeerQuality(long peerId, ConnectionQuality quality)
    {
        _peerQuality[peerId] = quality;
        QualityChanged?.Invoke(peerId, quality);
    }

    private void BuildOverlay()
    {
        _overlay = new Label
        {
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(-330, 16),
            Size = new Vector2(310, 150)
        };
        _overlay.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        AddChild(_overlay);
    }

    private void UpdateOverlay()
    {
        var clock = NetworkClock.Instance;
        _overlay.Text = $"NETWORK  [{LocalQuality.Health}]\n" +
            $"Ping  {LocalQuality.PingMs:0.0} ms\nJitter  {LocalQuality.JitterMs:0.0} ms\n" +
            $"Loss  {LocalQuality.PacketLossPercent:0.0}%\n" +
            $"Clock  {(clock.IsSynchronized ? "synced" : "syncing")}  offset {(clock.ServerTimeUsec - (long)Time.GetTicksUsec()) / 1000.0:0.0} ms\n" +
            $"Server tick  {clock.AuthoritativeTick}";
    }

    private void Reset()
    {
        _probes.Clear();
        _outcomes.Clear();
        _rtts.Clear();
        _peerQuality.Clear();
        _lastResponseUsec = 0;
        LocalQuality = ConnectionQuality.Host;
    }
}
