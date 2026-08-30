using Godot;
using MultiplayerTemplate.Avatar;
using MultiplayerTemplate.Communication;
using MultiplayerTemplate.Game;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.UI;

/// <summary>
/// Autoload owning the held expression wheel, shared by two actions: "emote_wheel" (emotes)
/// and "quick_chat_wheel" (canned chat lines). Held open by whichever action pressed first
/// while a match is active; releasing commits the selection and dispatches it to that
/// action's handler. Client-only; a dedicated server never builds a wheel.
///
/// Only one wheel may be open at a time. The action that opened it is remembered, and a
/// press of the other action while one is held is ignored — it neither swaps the entries
/// under an open wheel nor commits to the wrong handler. Only a release of the action that
/// opened it commits.
///
/// Unlike <see cref="PlayerListPanel"/>, the mouse stays captured and gameplay input keeps
/// flowing while this is open — no <see cref="LocalInputContext"/> blocker is ever set. The
/// wheel consumes only the exact mouse motion that would otherwise turn the camera, and only
/// for as long as it is held.
///
/// It still reads <see cref="LocalInputContext.GameplaySuppressed"/>, though: a wheel must
/// never open while chat, the dev console, or a rebind is capturing input, and one already
/// open must close itself — without committing — the instant a menu suppresses gameplay
/// mid-hold. Reading the flag is not the same as setting a blocker; this never calls
/// <c>SetBlocked</c>.
/// </summary>
public partial class ExpressionWheels : CanvasLayer
{
    private const string EmoteWheelAction = "emote_wheel";
    private const string QuickChatWheelAction = "quick_chat_wheel";

    private RadialWheel _wheel = null!;
    private bool _disabled;
    private string? _openAction;

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

        if (@event.IsActionPressed(EmoteWheelAction))
        {
            // A wheel is already open for the other action: ignore rather than swap.
            if (_wheel.IsOpen) return;
            if (!SessionManager.Instance.MatchActive) return;
            if (LocalInputContext.Instance.GameplaySuppressed) return;
            OpenEmoteWheel();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed(QuickChatWheelAction))
        {
            if (_wheel.IsOpen) return;
            if (!SessionManager.Instance.MatchActive) return;
            if (LocalInputContext.Instance.GameplaySuppressed) return;
            OpenQuickChatWheel();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!_wheel.IsOpen) return;

        // A menu (pause, chat, dev console, a rebind, ...) suppressed gameplay while the
        // wheel was held: close it without committing rather than let it fire on release
        // or keep eating mouse motion behind whatever just opened.
        if (LocalInputContext.Instance.GameplaySuppressed)
        {
            Close();
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            _wheel.AccumulateMotion(motion.Relative);
            GetViewport().SetInputAsHandled();
            return;
        }

        // A release of the action that did not open the wheel must not commit anything.
        if (@event.IsActionReleased(EmoteWheelAction))
        {
            if (_openAction != EmoteWheelAction) return;
            var id = _wheel.Commit();
            Close();
            GetViewport().SetInputAsHandled();
            if (id != EmoteCatalogue.None) FindLocalPlayer()?.RequestEmote(id);
            return;
        }

        if (@event.IsActionReleased(QuickChatWheelAction))
        {
            if (_openAction != QuickChatWheelAction) return;
            var id = _wheel.Commit();
            Close();
            GetViewport().SetInputAsHandled();
            if (id != QuickChatLines.None) TextChatManager.Instance.SubmitQuickChat(id);
        }
    }

    private void OpenEmoteWheel()
    {
        var entries = new List<(string Label, int Id)>(EmoteCatalogue.All.Count);
        foreach (var entry in EmoteCatalogue.All) entries.Add((entry.DisplayName, entry.Id));
        _openAction = EmoteWheelAction;
        _wheel.Open(entries);
    }

    private void OpenQuickChatWheel()
    {
        var entries = new List<(string Label, int Id)>(QuickChatLines.All.Count);
        foreach (var entry in QuickChatLines.All) entries.Add((entry.Text, entry.Id));
        _openAction = QuickChatWheelAction;
        _wheel.Open(entries);
    }

    private void Close()
    {
        _wheel.Close();
        _openAction = null;
    }

    // A match ending mid-hold must not strand the wheel open with a captured mouse and a
    // dead selection.
    private void HandleMatchEnded()
    {
        if (_wheel.IsOpen) Close();
    }

    private NetworkPlayer? FindLocalPlayer()
    {
        var ownId = Multiplayer.GetUniqueId();
        foreach (var node in GetTree().GetNodesInGroup("network_players"))
            if (node is NetworkPlayer player && player.PeerId == ownId) return player;
        return null;
    }
}
