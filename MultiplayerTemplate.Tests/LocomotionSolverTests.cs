using MultiplayerTemplate.Avatar;

namespace MultiplayerTemplate.Tests;

public sealed class LocomotionSolverTests
{
    [Theory]
    [InlineData(0f, LocomotionState.Idle)]
    [InlineData(0.149f, LocomotionState.Idle)]
    [InlineData(0.15f, LocomotionState.Walk)]
    [InlineData(2.999f, LocomotionState.Walk)]
    [InlineData(3f, LocomotionState.Run)]
    [InlineData(6f, LocomotionState.Run)]
    public void ClassifiesGroundSpeed(float speed, LocomotionState expected)
    {
        var result = LocomotionSolver.Solve(speed, true, 0f, 0f, true);
        Assert.Equal(expected, result.State);
        Assert.False(result.LandTriggered);
    }

    [Theory]
    [InlineData(5f, LocomotionState.Jump)]
    [InlineData(0.51f, LocomotionState.Jump)]
    [InlineData(0.5f, LocomotionState.Fall)]
    [InlineData(0f, LocomotionState.Fall)]
    [InlineData(-9f, LocomotionState.Fall)]
    public void ClassifiesAirborneByVerticalVelocity(float verticalVelocity, LocomotionState expected)
    {
        var result = LocomotionSolver.Solve(4f, false, verticalVelocity, 0.4f, false);
        Assert.Equal(expected, result.State);
    }

    [Fact]
    public void LandFiresOnceWhenGroundIsRegainedAfterLongEnoughFlight()
    {
        var result = LocomotionSolver.Solve(0f, true, 0f, 0.25f, false);
        Assert.Equal(LocomotionState.Land, result.State);
        Assert.True(result.LandTriggered);
    }

    [Fact]
    public void ShortHopsDoNotTriggerLand()
    {
        var result = LocomotionSolver.Solve(0f, true, 0f, 0.24f, false);
        Assert.Equal(LocomotionState.Idle, result.State);
        Assert.False(result.LandTriggered);
    }

    [Fact]
    public void StayingGroundedNeverTriggersLand()
    {
        var result = LocomotionSolver.Solve(0f, true, 0f, 10f, true);
        Assert.Equal(LocomotionState.Idle, result.State);
        Assert.False(result.LandTriggered);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(3f, 0.5f)]
    [InlineData(6f, 1f)]
    [InlineData(20f, 1f)]
    public void BlendIsSpeedNormalisedToRunSpeed(float speed, float expected)
    {
        Assert.Equal(expected, LocomotionSolver.Solve(speed, true, 0f, 0f, true).Blend, 3);
    }
}
