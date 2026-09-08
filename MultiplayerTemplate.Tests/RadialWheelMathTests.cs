using MultiplayerTemplate.UI;

namespace MultiplayerTemplate.Tests;

public sealed class RadialWheelMathTests
{
    private const float Zone = 40f;

    [Fact]
    public void InsideTheDeadZoneSelectsNothing()
    {
        Assert.Equal(-1, RadialWheelMath.Select(0f, 0f, 6, Zone));
        Assert.Equal(-1, RadialWheelMath.Select(Zone - 1f, 0f, 6, Zone));
    }

    [Fact]
    public void OnTheDeadZoneEdgeSelects()
    {
        Assert.Equal(0, RadialWheelMath.Select(0f, -Zone, 6, Zone));
    }

    [Fact]
    public void StraightUpIsWedgeZero()
    {
        Assert.Equal(0, RadialWheelMath.Select(0f, -100f, 4, Zone));
    }

    [Fact]
    public void WedgesAdvanceClockwise()
    {
        Assert.Equal(1, RadialWheelMath.Select(100f, 0f, 4, Zone));
        Assert.Equal(2, RadialWheelMath.Select(0f, 100f, 4, Zone));
        Assert.Equal(3, RadialWheelMath.Select(-100f, 0f, 4, Zone));
    }

    [Fact]
    public void EveryDirectionLandsInRange()
    {
        for (var degrees = 0; degrees < 360; degrees += 7)
        {
            var radians = degrees * MathF.PI / 180f;
            var index = RadialWheelMath.Select(
                MathF.Cos(radians) * 100f, MathF.Sin(radians) * 100f, 6, Zone);
            Assert.InRange(index, 0, 5);
        }
    }

    [Fact]
    public void NoEntriesSelectsNothing()
    {
        Assert.Equal(-1, RadialWheelMath.Select(0f, -100f, 0, Zone));
    }
}
