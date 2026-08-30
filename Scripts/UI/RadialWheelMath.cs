namespace MultiplayerTemplate.UI;

/// <summary>
/// Direction-to-wedge selection for the radial wheel. Wedge 0 is centred straight up
/// (negative Y in screen space) and wedges advance clockwise. Pure and engine-free so it
/// can be unit tested; must not reference the Godot namespace.
/// </summary>
public static class RadialWheelMath
{
    public const float DeadZone = 40f;

    public static int Select(float x, float y, int count, float deadZone)
    {
        if (count <= 0) return -1;
        if (MathF.Sqrt(x * x + y * y) < deadZone) return -1;
        // Atan2(x, -y) puts zero straight up and grows clockwise, which is how a wheel
        // reads; the raw Atan2(y, x) would put zero to the right and grow anticlockwise.
        var angle = MathF.Atan2(x, -y);
        if (angle < 0) angle += MathF.Tau;
        var wedge = MathF.Tau / count;
        var index = (int)MathF.Floor((angle + wedge * 0.5f) / wedge);
        return index % count;
    }
}
