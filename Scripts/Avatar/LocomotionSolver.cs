namespace MultiplayerTemplate.Avatar;

public enum LocomotionState
{
    Idle,
    Walk,
    Run,
    Jump,
    Fall,
    Land
}

/// <summary>
/// One frame of cosmetic locomotion animation state. <see cref="Blend"/> drives the
/// idle/walk/run blend space; <see cref="LandTriggered"/> is a one-frame edge.
/// </summary>
public readonly record struct LocomotionResult(LocomotionState State, float Blend, bool LandTriggered);

/// <summary>
/// Derives cosmetic locomotion state from motion every peer already has. Pure and
/// engine-free so it can be unit tested; must not reference the Godot namespace.
/// </summary>
public static class LocomotionSolver
{
    public const float IdleSpeed = 0.15f;
    public const float WalkSpeed = 3f;
    public const float RunSpeed = 6f;
    public const float RiseVelocity = 0.5f;
    public const float LandAirborneSeconds = 0.25f;

    public static LocomotionResult Solve(
        float planarSpeed, bool grounded, float verticalVelocity, float airborneSeconds, bool wasGrounded)
    {
        var blend = Math.Clamp(planarSpeed / RunSpeed, 0f, 1f);
        if (!grounded)
        {
            // At the apex the character reads as falling; carrying jump history here
            // would make the solver stateful for no visual gain.
            var airborne = verticalVelocity > RiseVelocity ? LocomotionState.Jump : LocomotionState.Fall;
            return new LocomotionResult(airborne, blend, false);
        }
        if (!wasGrounded && airborneSeconds >= LandAirborneSeconds)
            return new LocomotionResult(LocomotionState.Land, blend, true);
        var ground = planarSpeed < IdleSpeed
            ? LocomotionState.Idle
            : planarSpeed < WalkSpeed
                ? LocomotionState.Walk
                : LocomotionState.Run;
        return new LocomotionResult(ground, blend, false);
    }
}
