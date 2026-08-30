using Godot;
using DebugLoggerKit;
using MultiplayerTemplate.Communication;

namespace MultiplayerTemplate.Network;

public partial class PlayerIdentityService : Node
{
    public static PlayerIdentityService Instance { get; private set; } = null!;
    public event Action<long, ImageTexture>? AvatarLoaded;
    public event Action<string>? SocialStateChanged;

    private const string LogModule = "Identity";
    private const string SocialConfigPath = "user://social_profile.cfg";
    private readonly Dictionary<long, ImageTexture> _avatars = new();
    private readonly HashSet<long> _pending = new();
    private GodotObject? _steam;
    private Callable _avatarCallable;
    private readonly ConfigFile _socialConfig = new();

    public override void _Ready()
    {
        Instance = this;
        DebugLogger.Instance.RegisterModule(LogModule, true);
        _socialConfig.Load(SocialConfigPath);
        if (!Engine.HasSingleton("Steam")) return;
        _steam = Engine.GetSingleton("Steam");
        if (!_steam.HasSignal("avatar_loaded")) return;
        _avatarCallable = Callable.From<long, int, byte[]>(OnSteamAvatarLoaded);
        _steam.Connect("avatar_loaded", _avatarCallable);
    }

    public PlayerIdentity Get(long peerId) =>
        SessionManager.Instance.Players.TryGetValue(peerId, out var data)
            ? PlayerIdentity.FromDictionary(peerId, data)
            : new PlayerIdentity(peerId, $"Player {peerId}", "LAN", 0, peerId == 1, false, false);

    public bool TryGetAvatar(long steamId, out ImageTexture? texture) =>
        _avatars.TryGetValue(steamId, out texture);

    public void RequestAvatar(long steamId)
    {
        // _steam is checked before _pending.Add: the old order marked the id pending
        // and then bailed, permanently blocking retries once Steam became available.
        if (steamId == 0 || _steam == null || _avatars.ContainsKey(steamId) || !_pending.Add(steamId))
            return;
        _steam.Call("requestUserInformation", steamId, false);
        var avatarLarge = _steam.Get("AVATAR_LARGE").AsInt32();
        _steam.Call("getPlayerAvatar", avatarLarge, steamId);
    }

    public bool IsMuted(PlayerIdentity identity) =>
        (bool)_socialConfig.GetValue("muted", identity.StableSocialKey, false);

    public bool IsBlocked(PlayerIdentity identity) =>
        (bool)_socialConfig.GetValue("blocked", identity.StableSocialKey, false);

    public void SetMuted(PlayerIdentity identity, bool muted) =>
        SetSocialState("muted", identity.StableSocialKey, muted);

    public void SetBlocked(PlayerIdentity identity, bool blocked) =>
        SetSocialState("blocked", identity.StableSocialKey, blocked);

    public float GetVoiceVolume(PlayerIdentity identity) => VoiceVolumePolicy.Clamp(
        (float)_socialConfig.GetValue(
            "voice_volume", identity.StableSocialKey, VoiceVolumePolicy.Default).AsDouble());

    public void SetVoiceVolume(PlayerIdentity identity, float linear)
    {
        _socialConfig.SetValue("voice_volume", identity.StableSocialKey, VoiceVolumePolicy.Clamp(linear));
        _socialConfig.Save(SocialConfigPath);
        SocialStateChanged?.Invoke(identity.StableSocialKey);
    }

    private void SetSocialState(string section, string key, bool enabled)
    {
        _socialConfig.SetValue(section, key, enabled);
        _socialConfig.Save(SocialConfigPath);
        SocialStateChanged?.Invoke(key);
    }

    private void OnSteamAvatarLoaded(long steamId, int avatarSize, byte[] buffer)
    {
        _pending.Remove(steamId);
        if (avatarSize <= 0 || buffer.Length == 0) return;
        var image = Image.CreateFromData(avatarSize, avatarSize, false, Image.Format.Rgba8, buffer);
        var texture = ImageTexture.CreateFromImage(image);
        _avatars[steamId] = texture;
        AvatarLoaded?.Invoke(steamId, texture);
        DebugLogger.Instance.Debug(LogModule, $"Cached Steam avatar {steamId}");
    }

    public override void _ExitTree()
    {
        if (_steam?.IsConnected("avatar_loaded", _avatarCallable) == true)
            _steam.Disconnect("avatar_loaded", _avatarCallable);
    }
}
