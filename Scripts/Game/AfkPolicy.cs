namespace MultiplayerTemplate.Game;

/// <summary>
/// Idle-detection rule. Pure and engine-free so it can be unit tested; must not
/// reference the Godot namespace.
/// </summary>
public static class AfkPolicy
{
    public const double DefaultThresholdSeconds = 90;

    /// <summary>
    /// Once flagged, a player stays away until input actually arrives and resets the
    /// timer. Without this a very small threshold would flap on and off every frame.
    /// </summary>
    public const double ExitSeconds = 0.5;

    public static bool Evaluate(double secondsSinceInput, double thresholdSeconds, bool currentlyAfk)
    {
        if (thresholdSeconds <= 0) return false;
        return currentlyAfk
            ? secondsSinceInput > ExitSeconds
            : secondsSinceInput >= thresholdSeconds;
    }

    public static bool ShouldKick(double secondsSinceInput, double kickThresholdSeconds) =>
        kickThresholdSeconds > 0 && secondsSinceInput >= kickThresholdSeconds;
}
