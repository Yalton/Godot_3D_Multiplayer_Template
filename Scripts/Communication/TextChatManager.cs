using Godot;
using MultiplayerTemplate.Network;
using MultiplayerTemplate.UI;

namespace MultiplayerTemplate.Communication;

public static class ChatPolicy
{
    public const int MaxLength = 256;

    public static string Sanitize(string text)
    {
        var safe = text.Trim().Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        return safe.Length > MaxLength ? safe[..MaxLength] : safe;
    }

    public static string EscapeBbCode(string value)
    {
        var escaped = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
            escaped.Append(character switch
            {
                '[' => "[lb]",
                ']' => "[rb]",
                _ => character
            });
        return escaped.ToString();
    }
}

/// <summary>Server-relayed session chat with a small reusable in-game HUD.</summary>
public partial class TextChatManager : CanvasLayer
{
    public static TextChatManager Instance { get; private set; } = null!;
    public static bool IsTyping => Instance != null && Instance.ClientUiEnabled && Instance._input.Visible;
    public bool ClientUiEnabled => ProcessMode != ProcessModeEnum.Disabled;
    public event Action<long, string, string>? MessageReceived;

    private const int MaxLogParagraphs = 200;
    private RichTextLabel _log = null!;
    private LineEdit _input = null!;
    private readonly RpcRateLimiter _rateLimiter = new();

    public override void _Ready()
    {
        Instance = this;
        Multiplayer.PeerDisconnected += _rateLimiter.Forget;
        if (DisplayServer.GetName() == "headless")
        {
            ProcessMode = ProcessModeEnum.Disabled;
            return;
        }
        Layer = 20;
        BuildUi();
        SessionManager.Instance.MatchEnded += CloseInput;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Multiplayer.HasMultiplayerPeer() || @event is not InputEventKey key ||
            DisplayServer.GetName() == "headless" ||
            !key.Pressed || key.Echo || key.Keycode != Key.Enter) return;

        if (!_input.Visible)
        {
            _input.Visible = true;
            _input.GrabFocus();
            LocalInputContext.Instance.SetBlocked("text_chat", true);
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
        _input = new LineEdit { PlaceholderText = "Message…", MaxLength = ChatPolicy.MaxLength, Visible = false };
        _input.TextSubmitted += _ => SendCurrent();
        // Escape (ui_cancel) drops the LineEdit's focus without going through SendCurrent;
        // without this, the input stays visible and the gameplay blocker stays set.
        _input.FocusExited += CloseInput;
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

    public void SubmitText(string text)
    {
        if (!Multiplayer.HasMultiplayerPeer()) return;
        text = ChatPolicy.Sanitize(text);
        if (text.Length == 0) return;
        if (Multiplayer.IsServer()) AcceptMessage(Multiplayer.GetUniqueId(), text);
        else RpcId(1, nameof(RequestMessage), text);
    }

    public void SubmitQuickChat(int id)
    {
        if (!Multiplayer.HasMultiplayerPeer()) return;
        if (Multiplayer.IsServer()) AcceptQuickChat(Multiplayer.GetUniqueId(), id);
        else RpcId(1, nameof(RequestQuickChat), id);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestQuickChat(int id)
    {
        if (!RpcSecurity.RequireServer(this, nameof(RequestQuickChat)) ||
            !RpcSecurity.RequireKnownSender(this, nameof(RequestQuickChat), out var sender) ||
            !RpcSecurity.RequireState(SessionManager.Instance.State, nameof(RequestQuickChat),
                SessionManager.SessionState.Lobby, SessionManager.SessionState.Playing)) return;
        AcceptQuickChat(sender, id);
    }

    private void AcceptQuickChat(long peerId, int id)
    {
        if (!_rateLimiter.Allow(peerId, nameof(RequestQuickChat), 5, 4000))
        {
            RpcSecurity.Reject(nameof(RequestQuickChat), peerId, "rate limit exceeded");
            return;
        }
        if (!QuickChatLines.TryGet(id, out var line))
        {
            RpcSecurity.Reject(nameof(RequestQuickChat), peerId, $"unknown quick chat line {id}");
            return;
        }
        AcceptMessage(peerId, line.Text);
    }

    private void CloseInput()
    {
        if (_input == null) return;
        _input.Visible = false;
        _input.ReleaseFocus();
        LocalInputContext.Instance.SetBlocked("text_chat", false);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3)]
    private void RequestMessage(string text)
    {
        if (!RpcSecurity.RequireServer(this, nameof(RequestMessage)) ||
            !RpcSecurity.RequireKnownSender(this, nameof(RequestMessage), out var sender) ||
            !RpcSecurity.RequireState(SessionManager.Instance.State, nameof(RequestMessage),
                SessionManager.SessionState.Lobby, SessionManager.SessionState.Loading,
                SessionManager.SessionState.Playing)) return;
        if (!_rateLimiter.Allow(sender, nameof(RequestMessage), 5, 4000))
        {
            RpcSecurity.Reject(nameof(RequestMessage), sender, "rate limit exceeded");
            return;
        }
        AcceptMessage(sender, text);
    }

    private void AcceptMessage(long peerId, string text)
    {
        if (!SessionManager.Instance.Players.ContainsKey(peerId)) return;
        text = ChatPolicy.Sanitize(text);
        if (text.Length == 0) return;
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
            SessionManager.Instance.Players.TryGetValue(peerId, out var data))
        {
            var identity = PlayerIdentity.FromDictionary(peerId, data);
            if (PlayerIdentityService.Instance.IsMuted(identity) ||
                PlayerIdentityService.Instance.IsBlocked(identity))
                return;
        }
        MessageReceived?.Invoke(peerId, name, text);
        if (!ClientUiEnabled) return;
        _log.AppendText($"[color=#8fd3ff]{ChatPolicy.EscapeBbCode(name)}[/color]: " +
                        $"{ChatPolicy.EscapeBbCode(text)}\n");
        while (_log.GetParagraphCount() > MaxLogParagraphs) _log.RemoveParagraph(0);
        _log.ScrollToLine(Mathf.Max(0, _log.GetLineCount() - 1));
    }

}
