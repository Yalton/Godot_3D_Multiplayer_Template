using Godot;
using DebugLoggerKit;

namespace MultiplayerTemplate.Network;

/// <summary>Small, composable validation helpers for server-side RPC request handlers.</summary>
public static class RpcSecurity
{
    private const string LogModule = "RpcSecurity";
    private static readonly RpcRejectLogThrottle RejectLogThrottle = new();

    public static bool RequireServer(Node context, string action) =>
        context.Multiplayer.IsServer() || Reject(action, 0, "handler is not running on the server");

    public static bool RequireKnownSender(Node context, string action, out long sender)
    {
        sender = context.Multiplayer.GetRemoteSenderId();
        return sender > 0 && SessionManager.Instance.Players.ContainsKey(sender) ||
               Reject(action, sender, "sender is not registered");
    }

    public static bool RequireSender(long sender, long expected, string action) =>
        sender == expected || Reject(action, sender, $"sender does not own peer {expected}");

    public static bool RequireState(
        SessionManager.SessionState state, long sender, string action,
        params SessionManager.SessionState[] allowed) =>
        allowed.Contains(state) || Reject(action, sender, $"invalid session state {state}");

    public static bool WithinDistance(Vector3 from, Vector3 to, float maximum, string action, long sender) =>
        from.DistanceSquaredTo(to) <= maximum * maximum ||
        Reject(action, sender, $"distance exceeds {maximum:0.##}m");

    public static bool HasLineOfSight(
        Node3D context, Vector3 from, Vector3 to, Godot.Collections.Array<Rid> exclude,
        GodotObject expectedCollider, string action, long sender)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = exclude;
        var hit = context.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.TryGetValue("collider", out var collider) &&
               collider.AsGodotObject() == expectedCollider ||
               Reject(action, sender, "line of sight blocked");
    }

    public static bool Reject(string action, long sender, string reason)
    {
        if (RejectLogThrottle.ShouldLog(sender, action))
            DebugLogger.Instance.Warning(LogModule, $"Rejected {action} from peer {sender}: {reason}");
        return false;
    }

    public static void Forget(long peerId) => RejectLogThrottle.Forget(peerId);

    public static void Reset() => RejectLogThrottle.Clear();
}

/// <summary>Suppresses repeated reject warnings without suppressing the rejection itself.</summary>
public sealed class RpcRejectLogThrottle
{
    private const long CooldownMs = 1000;
    private readonly RpcCooldowns _cooldowns;

    public RpcRejectLogThrottle(Func<long>? nowMs = null) => _cooldowns = new RpcCooldowns(nowMs);

    public bool ShouldLog(long peerId, string action) =>
        _cooldowns.TryConsume(peerId, action, CooldownMs);

    public void Forget(long peerId) => _cooldowns.Forget(peerId);

    public void Clear() => _cooldowns.Clear();
}

/// <summary>Fixed-window per-peer/per-action RPC limiter.</summary>
public sealed class RpcRateLimiter
{
    private readonly Func<long> _nowMs;
    private readonly System.Collections.Generic.Dictionary<(long Peer, string Action), Window> _windows = new();
    private readonly record struct Window(long StartedMs, int Count);

    public RpcRateLimiter(Func<long>? nowMs = null) =>
        _nowMs = nowMs ?? (() => (long)Time.GetTicksMsec());

    public bool Allow(long peerId, string action, int maximum, long windowMs)
    {
        if (maximum <= 0 || windowMs <= 0) return false;
        var now = _nowMs();
        var key = (peerId, action);
        if (!_windows.TryGetValue(key, out var window) || now - window.StartedMs >= windowMs)
        {
            _windows[key] = new Window(now, 1);
            return true;
        }
        if (window.Count >= maximum) return false;
        _windows[key] = window with { Count = window.Count + 1 };
        return true;
    }

    public void Forget(long peerId)
    {
        foreach (var key in _windows.Keys.Where(key => key.Peer == peerId).ToArray())
            _windows.Remove(key);
    }
}

/// <summary>Per-peer/per-action server cooldown enforcement.</summary>
public sealed class RpcCooldowns
{
    private readonly Func<long> _nowMs;
    private readonly System.Collections.Generic.Dictionary<(long Peer, string Action), long> _nextAllowed = new();

    public RpcCooldowns(Func<long>? nowMs = null) =>
        _nowMs = nowMs ?? (() => (long)Time.GetTicksMsec());

    public bool TryConsume(long peerId, string action, long cooldownMs)
    {
        if (cooldownMs < 0) return false;
        var now = _nowMs();
        var key = (peerId, action);
        if (_nextAllowed.GetValueOrDefault(key) > now) return false;
        _nextAllowed[key] = now + cooldownMs;
        return true;
    }

    public void Forget(long peerId)
    {
        foreach (var key in _nextAllowed.Keys.Where(key => key.Peer == peerId).ToArray())
            _nextAllowed.Remove(key);
    }

    public void Clear() => _nextAllowed.Clear();
}
