using Godot;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Communication;

/// <summary>Server-relayed session chat with a small reusable in-game HUD.</summary>
public partial class TextChatManager : CanvasLayer
{
    public static TextChatManager Instance { get; private set; } = null!;
    public static bool IsTyping => Instance != null && Instance._input.Visible;

    private RichTextLabel _log = null!;
    private LineEdit _input = null!;
    private double _lastServerMessageMs;

    public override void _Ready()
    {
        Instance = this;
        Layer = 20;
        BuildUi();
        SessionManager.Instance.MatchEnded += CloseInput;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Multiplayer.HasMultiplayerPeer() || @event is not InputEventKey key ||
            !key.Pressed || key.Echo || key.Keycode != Key.Enter) return;

        if (!_input.Visible)
        {
            _input.Visible = true;
            _input.GrabFocus();
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else
        {
            SendCurrent();
        }
        GetViewport().SetInputAsHandled();
    }

    private void BuildUi()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        margin.Position = new Vector2(18, -238);
        margin.CustomMinimumSize = new Vector2(520, 220);
        AddChild(margin);
        var box = new VBoxContainer();
        margin.AddChild(box);
        _log = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = false,
            ScrollActive = true,
            CustomMinimumSize = new Vector2(520, 180),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        box.AddChild(_log);
        _input = new LineEdit { PlaceholderText = "Message…", MaxLength = 256, Visible = false };
        _input.TextSubmitted += _ => SendCurrent();
        box.AddChild(_input);
    }

    private void SendCurrent()
    {
        var text = _input.Text.Trim();
        _input.Clear();
        CloseInput();
        if (text.Length == 0) return;
        if (Multiplayer.IsServer()) AcceptMessage(Multiplayer.GetUniqueId(), text);
        else RpcId(1, nameof(RequestMessage), text);
    }

    private void CloseInput()
    {
        _input.Visible = false;
        _input.ReleaseFocus();
        if (SessionManager.Instance.MatchActive) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3)]
    private void RequestMessage(string text)
    {
        if (!Multiplayer.IsServer()) return;
        var now = Time.GetTicksMsec();
        if (now - _lastServerMessageMs < 200) return;
        _lastServerMessageMs = now;
        AcceptMessage(Multiplayer.GetRemoteSenderId(), text);
    }

    private void AcceptMessage(long peerId, string text)
    {
        if (!SessionManager.Instance.Players.ContainsKey(peerId)) return;
        text = text.Trim().Replace("\n", " ").Replace("\r", " ");
        if (text.Length == 0) return;
        if (text.Length > 256) text = text[..256];
        var name = SessionManager.Instance.DisplayName(peerId);
        Append(peerId, name, text);
        Rpc(nameof(ReceiveMessage), peerId, name, text);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3)]
    private void ReceiveMessage(long peerId, string name, string text) => Append(peerId, name, text);

    private void Append(long peerId, string name, string text)
    {
        if (peerId != Multiplayer.GetUniqueId() &&
            SessionManager.Instance.Players.TryGetValue(peerId, out var data) &&
            PlayerIdentityService.Instance.IsMuted(PlayerIdentity.FromDictionary(peerId, data)))
            return;
        _log.AppendText($"[color=#8fd3ff]{Escape(name)}[/color]: {Escape(text)}\n");
        _log.ScrollToLine(Mathf.Max(0, _log.GetLineCount() - 1));
    }

    private static string Escape(string value) =>
        value.Replace("[", "[lb]").Replace("]", "[rb]");
}
