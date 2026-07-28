using Godot;
using DevConsoleKit;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.UI;

/// <summary>Single source of truth for local gameplay-input and mouse-capture policy.</summary>
public partial class LocalInputContext : Node
{
    public static LocalInputContext Instance { get; private set; } = null!;
    private readonly HashSet<string> _blockers = new();

    public bool GameplaySuppressed =>
        _blockers.Count > 0 || IsTextEntryFocused() || IsConsoleOpen();

    public override void _Ready() => Instance = this;

    public void SetBlocked(string owner, bool blocked)
    {
        if (blocked) _blockers.Add(owner);
        else _blockers.Remove(owner);
        RefreshMouseMode();
    }

    public void Clear()
    {
        _blockers.Clear();
        RefreshMouseMode();
    }

    public void RefreshMouseMode()
    {
        if (DisplayServer.GetName() == "headless") return;
        Input.MouseMode = SessionManager.Instance.MatchActive && !GameplaySuppressed
            ? Input.MouseModeEnum.Captured
            : Input.MouseModeEnum.Visible;
    }

    private bool IsTextEntryFocused()
    {
        var focus = GetViewport().GuiGetFocusOwner();
        return focus is LineEdit or TextEdit or CodeEdit;
    }

    private static bool IsConsoleOpen()
    {
        var manager = DevConsoleManager.Instance;
        return manager != null && manager.GetConsoleUI() is { Visible: true };
    }
}
