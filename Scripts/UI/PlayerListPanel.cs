using Godot;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.UI;

using PlayerRoster = Godot.Collections.Dictionary<long, Godot.Collections.Dictionary>;

/// <summary>
/// Tab panel listing everyone in the match with presence and social controls.
/// Client-only. Toggling it frees the mouse through LocalInputContext, matching how
/// every other modal surface in the template behaves.
/// </summary>
public partial class PlayerListPanel : CanvasLayer
{
    public static PlayerListPanel Instance { get; private set; } = null!;
    private const string BlockerId = "player_list";

    private PanelContainer _panel = null!;
    private VBoxContainer _rows = null!;
    private readonly System.Collections.Generic.Dictionary<long, PlayerRow> _entries = new();

    public override void _Ready()
    {
        Instance = this;
        if (DisplayServer.GetName() == "headless")
        {
            ProcessMode = ProcessModeEnum.Disabled;
            return;
        }
        Layer = 20;
        _panel = new PanelContainer { Visible = false };
        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _panel.CustomMinimumSize = new Vector2(720, 360);
        AddChild(_panel);
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        _panel.AddChild(margin);
        _rows = new VBoxContainer();
        margin.AddChild(_rows);
        SessionManager.Instance.RosterChanged += Render;
        SessionManager.Instance.MatchEnded += Close;
    }

    public override void _ExitTree()
    {
        if (SessionManager.Instance == null) return;
        SessionManager.Instance.RosterChanged -= Render;
        SessionManager.Instance.MatchEnded -= Close;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("player_list") || !SessionManager.Instance.MatchActive) return;
        Toggle();
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (!_panel.Visible) return;
        foreach (var row in _entries.Values)
        {
            row.RefreshQuality();
            row.RefreshSpeaking();
        }
    }

    private void Toggle()
    {
        if (_panel.Visible) Close();
        else Open();
    }

    private void Open()
    {
        Render(SessionManager.Instance.Players);
        _panel.Visible = true;
        LocalInputContext.Instance.SetBlocked(BlockerId, true);
    }

    private void Close()
    {
        if (!_panel.Visible) return;
        _panel.Visible = false;
        LocalInputContext.Instance.SetBlocked(BlockerId, false);
    }

    private void Render(PlayerRoster players)
    {
        if (!_panel.Visible) return;
        foreach (var child in _rows.GetChildren()) child.QueueFree();
        _entries.Clear();
        var ownId = Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 0;
        var isHost = Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer();
        foreach (var (peerId, data) in players)
        {
            var identity = PlayerIdentity.FromDictionary(peerId, data);
            var row = new PlayerRow();
            _rows.AddChild(row);
            row.Configure(identity, new PlayerRowOptions(
                ShowReady: false,
                ShowQuality: true,
                ShowVoiceControls: true,
                ShowKick: isHost,
                LocalPeerId: ownId));
            row.Changed += () => Render(SessionManager.Instance.Players);
            _entries[peerId] = row;
            if (!identity.HasSteamIdentity) continue;
            if (PlayerIdentityService.Instance.TryGetAvatar(identity.SteamId, out var cached))
                row.Avatar.Texture = cached;
            else
                PlayerIdentityService.Instance.RequestAvatar(identity.SteamId);
        }
    }
}
