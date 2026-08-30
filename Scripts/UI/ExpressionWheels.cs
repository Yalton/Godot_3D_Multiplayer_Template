using Godot;
using MultiplayerTemplate.Avatar;
using MultiplayerTemplate.Game;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.UI;

/// <summary>
/// Autoload owning the held emote wheel. Held open by "emote_wheel" while a match is active;
/// releasing commits the selection and asks the local player's <see cref="NetworkPlayer"/> to
/// broadcast it. Client-only; a dedicated server never builds a wheel.
///
/// Unlike <see cref="PlayerListPanel"/>, the mouse stays captured and gameplay input keeps
/// flowing while this is open — no <see cref="LocalInputContext"/> blocker is ever set. The
/// wheel consumes only the exact mouse motion that would otherwise turn the camera, and only
/// for as long as it is held.
/// </summary>
public partial class ExpressionWheels : CanvasLayer
{
    private RadialWheel _wheel = null!;
    private bool _disabled;

    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless")
        {
            _disabled = true;
            ProcessMode = ProcessModeEnum.Disabled;
            return;
        }
        Layer = 20;
        _wheel = new RadialWheel();
        AddChild(_wheel);
        SessionManager.Instance.MatchEnded += HandleMatchEnded;
    }

    public override void _ExitTree()
    {
        if (SessionManager.Instance == null) return;
        SessionManager.Instance.MatchEnded -= HandleMatchEnded;
    }

    // _Input runs before the GUI phase and before NetworkPlayer._UnhandledInput, which
    // consumes mouse motion for camera look. Handling this in _UnhandledInput would mean
    // the camera always wins the motion first.
    public override void _Input(InputEvent @event)
    {
        if (_disabled) return;

        if (@event.IsActionPressed("emote_wheel"))
        {
            if (!SessionManager.Instance.MatchActive) return;
            Open();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!_wheel.IsOpen) return;

        if (@event is InputEventMouseMotion motion)
        {
            _wheel.AccumulateMotion(motion.Relative);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionReleased("emote_wheel"))
        {
            var id = _wheel.Commit();
            _wheel.Close();
            GetViewport().SetInputAsHandled();
            if (id != EmoteCatalogue.None) FindLocalPlayer()?.RequestEmote(id);
        }
    }

    private void Open()
    {
        var entries = new List<(string Label, int Id)>(EmoteCatalogue.All.Count);
        foreach (var entry in EmoteCatalogue.All) entries.Add((entry.DisplayName, entry.Id));
        _wheel.Open(entries);
    }

    // A match ending mid-hold must not strand the wheel open with a captured mouse and a
    // dead selection.
    private void HandleMatchEnded()
    {
        if (_wheel.IsOpen) _wheel.Close();
    }

    private NetworkPlayer? FindLocalPlayer()
    {
        var ownId = Multiplayer.GetUniqueId();
        foreach (var node in GetTree().GetNodesInGroup("network_players"))
            if (node is NetworkPlayer player && player.PeerId == ownId) return player;
        return null;
    }
}
