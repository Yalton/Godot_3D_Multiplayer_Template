using Godot;
using Godot.Collections;
using MultiplayerTemplate.Network;
using SettingsKit;
using InputRemapKit;
using PlayerRoster = Godot.Collections.Dictionary<long, Godot.Collections.Dictionary>;

namespace MultiplayerTemplate.UI;

public partial class Main : Node
{
	private Control _menu = null!;
	private Control _lobby = null!;
	private Control _options = null!;
	private Label _status = null!;
	private VBoxContainer _roster = null!;
	private Button _ready = null!;
	private Button _start = null!;
	private Button _invite = null!;
	private OptionButton _privacy = null!;
	private LineEdit _name = null!;
	private LineEdit _address = null!;
	private VBoxContainer _lanList = null!;
	private VBoxContainer _steamList = null!;
	private readonly System.Collections.Generic.List<Button> _steamButtons = new();
	private readonly System.Collections.Generic.Dictionary<string, Button> _bindingButtons = new();
	private readonly System.Collections.Generic.Dictionary<long, List<TextureRect>> _avatarViews = new();
	private readonly System.Collections.Generic.Dictionary<long, PlayerRow> _rows = new();
	private string? _rebindingAction;

	public override void _Ready()
	{
		if (DisplayServer.GetName() == "headless") return;
		BuildUi();
		var session = SessionManager.Instance;
		session.ConnectionStateChanged += OnStateChanged;
		session.RosterChanged += RenderRoster;
		session.MatchStarted += OnMatchStarted;
		session.MatchEnded += OnMatchEnded;
		session.LanServersChanged += RenderLanServers;
		session.SteamLobbiesChanged += RenderSteamLobbies;
		session.SteamAvailabilityChanged += SetSteamAvailable;
		PlayerIdentityService.Instance.AvatarLoaded += OnAvatarLoaded;
		ConnectionQualityMonitor.Instance.QualityChanged += OnQualityChanged;
		SetSteamAvailable(session.Steam.Available);
		OnStateChanged("menu", "Ready.");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_rebindingAction != null && TryCaptureBinding(@event)) return;
		if (!@event.IsActionPressed("pause") || !SessionManager.Instance.MatchActive) return;
		var wasVisible = _lobby.Visible;
		_lobby.Visible = !wasVisible;
		LocalInputContext.Instance.SetBlocked("pause_menu", !wasVisible);
		GetViewport().SetInputAsHandled();
	}

	private void BuildUi()
	{
		var canvas = new CanvasLayer();
		AddChild(canvas);
		var (menuPanel, menu) = Panel("3D MULTIPLAYER TEMPLATE");
		_menu = WrapScreen(menuPanel);
		canvas.AddChild(_menu);
		_name = LineEdit("Display name", SessionManager.Instance.PlayerName);
		menu.AddChild(_name);
		menu.AddChild(Button("Host LAN / Direct IP", HostLan));
		var joinRow = new HBoxContainer();
		_address = LineEdit("IP address", "127.0.0.1");
		_address.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		joinRow.AddChild(_address);
		joinRow.AddChild(Button("Join IP", JoinIp));
		menu.AddChild(joinRow);
		menu.AddChild(Section("LAN Games"));
		_lanList = new VBoxContainer();
		menu.AddChild(_lanList);
		menu.AddChild(Button("Refresh LAN", SessionManager.Instance.SearchLan));
		menu.AddChild(Section("Steam Lobbies"));
		var steamHost = Button("Host Steam Lobby", HostSteam);
		var steamRefresh = Button("Refresh Steam Lobbies", SessionManager.Instance.RequestSteamLobbies);
		_steamButtons.AddRange([steamHost, steamRefresh]);
		menu.AddChild(steamHost);
		menu.AddChild(steamRefresh);
		_steamList = new VBoxContainer();
		menu.AddChild(_steamList);
		menu.AddChild(Button("Options", () => ShowOptions(true)));
		menu.AddChild(Button("Quit", () => GetTree().Quit()));
		_status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		menu.AddChild(_status);

		var (lobbyPanel, lobby) = Panel("LOBBY");
		_lobby = WrapScreen(lobbyPanel);
		_lobby.Visible = false;
		canvas.AddChild(_lobby);
		_roster = new VBoxContainer();
		lobby.AddChild(_roster);
		var steamSocial = new HBoxContainer();
		_invite = Button("Invite Steam Friends", SessionManager.Instance.Steam.OpenInviteDialog);
		steamSocial.AddChild(_invite);
		_privacy = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_privacy.AddItem("Public");
		_privacy.AddItem("Friends Only");
		_privacy.AddItem("Private / Invite Only");
		_privacy.ItemSelected += index => SessionManager.Instance.Steam.SetPrivacy(index switch
		{
			1 => SteamTransport.LobbyPrivacy.FriendsOnly,
			2 => SteamTransport.LobbyPrivacy.Private,
			_ => SteamTransport.LobbyPrivacy.Public
		});
		steamSocial.AddChild(_privacy);
		lobby.AddChild(steamSocial);
		_ready = Button("Ready", ToggleReady);
		lobby.AddChild(_ready);
		_start = Button("Start Match", SessionManager.Instance.StartMatch);
		lobby.AddChild(_start);
		lobby.AddChild(Button("Options", () => ShowOptions(true)));
		lobby.AddChild(Button("Disconnect", () => SessionManager.Instance.DisconnectSession()));

		_options = BuildOptions();
		_options.Visible = false;
		canvas.AddChild(_options);
		var tools = new CanvasLayer { Layer = 100 };
		AddChild(tools);
		var consoleScene = ResourceLoader.Load<PackedScene>("res://addons/dev_console/DevConsole.tscn");
		tools.AddChild(consoleScene.Instantiate());
	}

	private Control BuildOptions()
	{
		var (panel, box) = Panel("OPTIONS");
		panel.CustomMinimumSize = new Vector2(620, 520);
		var screen = WrapScreen(panel);
		var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		box.AddChild(tabs);

		var audio = new VBoxContainer { Name = "Audio" };
		tabs.AddChild(audio);
		audio.AddChild(SliderRow("Master Volume", SettingsManager.Instance.GetMasterVolume(), 0, 100, 1,
			SettingsManager.Instance.SetMasterVolume));
		audio.AddChild(SliderRow("Music Volume", SettingsManager.Instance.GetMusicVolume(), 0, 100, 1,
			SettingsManager.Instance.SetMusicVolume));
		audio.AddChild(SliderRow("SFX Volume", SettingsManager.Instance.GetSfxVolume(), 0, 100, 1,
			SettingsManager.Instance.SetSfxVolume));
		audio.AddChild(SliderRow("Ambience Volume", SettingsManager.Instance.GetAmbienceVolume(), 0, 100, 1,
			SettingsManager.Instance.SetAmbienceVolume));

		var video = new VBoxContainer { Name = "Video & Input" };
		tabs.AddChild(video);
		video.AddChild(SliderRow("Mouse Sensitivity", SettingsManager.Instance.GetMouseSensitivity(),
			1, 100, 1, SettingsManager.Instance.SetMouseSensitivity));
		video.AddChild(SliderRow("Field of View", SettingsManager.Instance.GetFov(), 60, 110, 1,
			SettingsManager.Instance.SetFov));
		var fullscreen = new CheckButton
		{
			Text = "Fullscreen", ButtonPressed = SettingsManager.Instance.GetFullscreenMode() != 0
		};
		fullscreen.Toggled += enabled => SettingsManager.Instance.SetFullscreenMode(enabled ? 2 : 0);
		video.AddChild(fullscreen);
		var vsync = new CheckButton { Text = "VSync", ButtonPressed = SettingsManager.Instance.GetVsync() };
		vsync.Toggled += SettingsManager.Instance.SetVsync;
		video.AddChild(vsync);

		var controls = new VBoxContainer { Name = "Controls" };
		tabs.AddChild(controls);
		foreach (var action in InputRemapManager.Instance.RebindableActions)
		{
			var row = new HBoxContainer();
			row.AddChild(new Label
			{
				Text = InputRemapManager.Instance.GetDisplayName(action),
				CustomMinimumSize = new Vector2(230, 0)
			});
			var binding = Button(BindingText(action), () => BeginRebind(action));
			binding.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			_bindingButtons[action] = binding;
			row.AddChild(binding);
			controls.AddChild(row);
		}
		controls.AddChild(Button("Reset All Controls", () =>
		{
			InputRemapManager.Instance.ResetAllActions();
			RefreshBindings();
		}));
		box.AddChild(Button("Back", () => ShowOptions(false)));
		return screen;
	}

	private void ShowOptions(bool visible)
	{
		_options.Visible = visible;
		LocalInputContext.Instance.SetBlocked("options", visible);
	}

	private void BeginRebind(string action)
	{
		if (_rebindingAction != null && _bindingButtons.TryGetValue(_rebindingAction, out var previous))
			previous.Text = BindingText(_rebindingAction);
		_rebindingAction = action;
		_bindingButtons[action].Text = "Press a key or mouse button…";
	}

	private bool TryCaptureBinding(InputEvent @event)
	{
		InputEvent? binding = @event switch
		{
			InputEventKey key when key.Pressed && !key.Echo => key,
			InputEventMouseButton mouse when mouse.Pressed => mouse,
			_ => null
		};
		if (binding == null) return false;
		var action = _rebindingAction!;
		var conflict = InputRemapManager.Instance.GetConflictingAction(binding, action);
		if (!string.IsNullOrEmpty(conflict))
		{
			_bindingButtons[action].Text =
				$"Already used by {InputRemapManager.Instance.GetDisplayName(conflict)}";
		}
		else
		{
			InputRemapManager.Instance.RemapAction(action, binding);
			_bindingButtons[action].Text = BindingText(action);
		}
		_rebindingAction = null;
		GetViewport().SetInputAsHandled();
		return true;
	}

	private static string BindingText(string action) =>
		InputRemapManager.Instance.GetCurrentEvent(action)?.AsText() ?? "Unbound";

	private void RefreshBindings()
	{
		foreach (var (action, button) in _bindingButtons) button.Text = BindingText(action);
	}

	private static (PanelContainer Panel, VBoxContainer Content) Panel(string title)
	{
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(480, 0) };
		var margin = new MarginContainer { Name = "Margin" };
		foreach (var side in new[] { "margin_left", "margin_right" }) margin.AddThemeConstantOverride(side, 28);
		foreach (var side in new[] { "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(side, 24);
		panel.AddChild(margin);
		var box = new VBoxContainer { Name = "VBox" };
		box.AddThemeConstantOverride("separation", 10);
		margin.AddChild(box);
		var heading = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center };
		heading.AddThemeFontSizeOverride("font_size", 28);
		box.AddChild(heading);
		return (panel, box);
	}

	private static MarginContainer WrapScreen(Control content)
	{
		// A CenterContainer performs real minimum-size-aware centering. Anchoring the panel's
		// top-left at 50% placed its left edge at screen center and caused the screenshot's
		// right/down overflow.
		var screen = new MarginContainer();
		screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
			screen.AddThemeConstantOverride(side, 24);
		var center = new CenterContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		screen.AddChild(center);
		center.AddChild(content);
		return screen;
	}

	private static HBoxContainer SliderRow(
		string text, double value, double min, double max, double step, Action<float> callback)
	{
		var row = new HBoxContainer();
		row.AddChild(new Label { Text = text, CustomMinimumSize = new Vector2(170, 0) });
		var slider = new HSlider
		{
			MinValue = min, MaxValue = max, Step = step, Value = value,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		slider.ValueChanged += value => callback((float)value);
		row.AddChild(slider);
		return row;
	}

	private static Button Button(string text, Action callback)
	{
		var button = new Button { Text = text };
		button.Pressed += callback;
		return button;
	}

	private static LineEdit LineEdit(string placeholder, string value) =>
		new() { PlaceholderText = placeholder, Text = value, MaxLength = 64 };

	private static Label Section(string text)
	{
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", 18);
		return label;
	}

	private void CaptureIdentity()
	{
		var value = _name.Text.Trim();
		if (value.Length > 24) value = value[..24];
		if (value.Length == 0) value = "Player";
		SessionManager.Instance.PlayerName = value;
		SessionManager.Instance.ServerName = $"{value}'s Game";
	}

	private void HostLan() { CaptureIdentity(); SessionManager.Instance.HostEnet(); }
	private void JoinIp() { CaptureIdentity(); SessionManager.Instance.JoinEnet(_address.Text); }
	private void HostSteam() { CaptureIdentity(); SessionManager.Instance.HostSteam(); }

	private void ToggleReady()
	{
		var id = Multiplayer.GetUniqueId();
		var current = SessionManager.Instance.Players.TryGetValue(id, out var data)
					  && data.GetValueOrDefault("ready", false).AsBool();
		SessionManager.Instance.SetReady(!current);
	}

	private void OnStateChanged(string state, string message)
	{
		_status.Text = message;
		if (state == "menu")
		{
			_menu.Visible = true;
			_lobby.Visible = false;
			LocalInputContext.Instance.SetBlocked("session_ui", true);
		}
		else if (state == "lobby")
		{
			_menu.Visible = false;
			_lobby.Visible = true;
			LocalInputContext.Instance.SetBlocked("session_ui", true);
			RenderRoster(SessionManager.Instance.Players);
		}
	}

	private void RenderRoster(PlayerRoster players)
	{
		foreach (var child in _roster.GetChildren()) child.QueueFree();
		_avatarViews.Clear();
		_rows.Clear();
		var ownId = Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 0;
		var isHost = Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer();
		foreach (var (peerId, data) in players)
		{
			var identity = PlayerIdentity.FromDictionary(peerId, data);
			var row = new PlayerRow();
			_roster.AddChild(row);
			row.Configure(identity, new PlayerRowOptions(
				ShowReady: true,
				ShowQuality: true,
				ShowVoiceControls: false,
				ShowKick: isHost,
				LocalPeerId: ownId));
			row.Changed += () => RenderRoster(SessionManager.Instance.Players);
			_rows[peerId] = row;
			if (!identity.HasSteamIdentity) continue;
			if (!_avatarViews.TryGetValue(identity.SteamId, out var views))
				_avatarViews[identity.SteamId] = views = new List<TextureRect>();
			views.Add(row.Avatar);
			if (PlayerIdentityService.Instance.TryGetAvatar(identity.SteamId, out var cached))
				row.Avatar.Texture = cached;
			else
				PlayerIdentityService.Instance.RequestAvatar(identity.SteamId);
		}
		var ownReady = players.TryGetValue(ownId, out var own)
					   && own.GetValueOrDefault("ready", false).AsBool();
		_ready.Text = ownReady ? "Unready" : "Ready";
		_start.Visible = Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer();
		UpdateSteamSocialControls();
	}

	private void OnQualityChanged(long peerId, ConnectionQuality quality)
	{
		foreach (var (_, row) in _rows)
			if (GodotObject.IsInstanceValid(row)) row.RefreshQuality();
	}

	private void OnAvatarLoaded(long steamId, ImageTexture texture)
	{
		if (!_avatarViews.TryGetValue(steamId, out var views)) return;
		foreach (var view in views)
			if (GodotObject.IsInstanceValid(view)) view.Texture = texture;
	}

	private void UpdateSteamSocialControls()
	{
		var session = SessionManager.Instance;
		var steamSession = session.Transport == SessionManager.TransportKind.Steam &&
						   session.Steam.ActiveLobbyId != 0;
		_invite.Visible = steamSession;
		_privacy.Visible = steamSession && Multiplayer.IsServer();
		_privacy.Selected = session.Steam.Privacy switch
		{
			SteamTransport.LobbyPrivacy.FriendsOnly => 1,
			SteamTransport.LobbyPrivacy.Private => 2,
			_ => 0
		};
	}

	private void RenderLanServers(Array<Dictionary> servers)
	{
		foreach (var child in _lanList.GetChildren()) child.QueueFree();
		foreach (var server in servers)
		{
			var address = server["address"].AsString();
			var port = server["port"].AsInt32();
			var button = Button(
				$"{server["name"].AsString()}  ({server["players"].AsInt32()}/{server["max"].AsInt32()})  {address}",
				() => { CaptureIdentity(); SessionManager.Instance.JoinEnet(address, port); });
			_lanList.AddChild(button);
		}
	}

	private void RenderSteamLobbies(Array<Dictionary> lobbies)
	{
		foreach (var child in _steamList.GetChildren()) child.QueueFree();
		foreach (var lobby in lobbies)
		{
			var id = lobby["id"].AsInt64();
			_steamList.AddChild(Button(
				$"{lobby["name"].AsString()}  ({lobby["players"].AsInt32()}/{lobby["max"].AsInt32()})",
				() => SessionManager.Instance.JoinSteamLobby(id)));
		}
	}

	private void SetSteamAvailable(bool available)
	{
		foreach (var button in _steamButtons)
		{
			button.Disabled = !available;
			button.TooltipText = available ? "" : "Install GodotSteam and launch through Steam.";
		}
		if (available && _name.Text == "Player")
		{
			var persona = SessionManager.Instance.Steam.PersonaName();
			if (persona.Length > 0) _name.Text = persona;
		}
	}

	private void OnMatchStarted()
	{
		_menu.Visible = false;
		_lobby.Visible = false;
		_options.Visible = false;
		LocalInputContext.Instance.Clear();
	}

	private void OnMatchEnded()
	{
		if (Multiplayer.HasMultiplayerPeer()) _lobby.Visible = true;
		LocalInputContext.Instance.SetBlocked("session_ui", true);
	}
}
