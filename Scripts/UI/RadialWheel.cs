using Godot;

namespace MultiplayerTemplate.UI;

/// <summary>
/// A held radial selector. It knows nothing about what it is selecting: callers hand it
/// (label, id) entries and read back the committed id. Client-only; a dedicated server
/// never builds one.
/// </summary>
public partial class RadialWheel : Control
{
    private const float Radius = 140f;
    private const float MaxDrift = 240f;

    private readonly List<(string Label, int Id)> _entries = new();
    private Vector2 _direction;

    public bool IsOpen { get; private set; }

    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless")
        {
            ProcessMode = ProcessModeEnum.Disabled;
            return;
        }
        // The wheel never eats clicks; it is driven entirely by relative motion.
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;
    }

    public void Open(IReadOnlyList<(string Label, int Id)> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries);
        _direction = Vector2.Zero;
        IsOpen = true;
        Visible = true;
        QueueRedraw();
    }

    public void Close()
    {
        IsOpen = false;
        Visible = false;
        _entries.Clear();
        _direction = Vector2.Zero;
    }

    public void AccumulateMotion(Vector2 relative)
    {
        if (!IsOpen) return;
        _direction = (_direction + relative).LimitLength(MaxDrift);
        QueueRedraw();
    }

    /// <summary>The selected id, or 0 when the pointer is inside the dead zone.</summary>
    public int Commit()
    {
        var index = RadialWheelMath.Select(
            _direction.X, _direction.Y, _entries.Count, RadialWheelMath.DeadZone);
        return index < 0 ? 0 : _entries[index].Id;
    }

    public override void _Draw()
    {
        if (!IsOpen || _entries.Count == 0) return;
        var centre = Size * 0.5f;
        var selected = RadialWheelMath.Select(
            _direction.X, _direction.Y, _entries.Count, RadialWheelMath.DeadZone);
        var font = GetThemeDefaultFont();
        var fontSize = GetThemeDefaultFontSize();
        DrawCircle(centre, RadialWheelMath.DeadZone, new Color(0, 0, 0, 0.35f));
        for (var i = 0; i < _entries.Count; i++)
        {
            // Wedge 0 is straight up and wedges advance clockwise, matching RadialWheelMath.
            var angle = i * Mathf.Tau / _entries.Count;
            var offset = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * Radius;
            var label = _entries[i].Label;
            var width = font.GetStringSize(label, fontSize: fontSize).X;
            var colour = i == selected ? Colors.White : new Color(1, 1, 1, 0.55f);
            DrawString(font, centre + offset - new Vector2(width * 0.5f, 0),
                label, HorizontalAlignment.Left, -1, fontSize, colour);
        }
    }
}
