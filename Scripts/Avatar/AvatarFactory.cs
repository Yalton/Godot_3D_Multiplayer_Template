using Godot;
using DebugLoggerKit;

namespace MultiplayerTemplate.Avatar;

/// <summary>
/// Resolves which rig to build. A misconfigured replacement rig is a logged warning
/// and a fallback to the built-in body, never a crash.
/// </summary>
public static class AvatarFactory
{
    public const string ScenePathSetting = "template/avatar/scene_path";
    private const string LogModule = "Avatar";

    public static AvatarRig? Build(long peerId)
    {
        // Servers need collision and movement, not boxes. BoneAttachment3D never
        // participates in physics, so simulation is identical without a rig.
        if (DisplayServer.GetName() == "headless") return null;
        var rig = BuildConfigured() ?? new BlockmanRig();
        rig.Name = "Avatar";
        rig.Ready += () => rig.ApplyAppearance(AvatarRig.DefaultAppearanceFor(peerId));
        return rig;
    }

    private static AvatarRig? BuildConfigured()
    {
        // AvatarFactory is a static class with no _Ready, so it registers its own
        // DebugLogger module lazily on first use (idempotent: RegisterModule no-ops
        // once a module name is already known) rather than relying on some other
        // node's _Ready to have run first. This only runs off the headless path, so
        // the server never registers a module it has no use for.
        DebugLogger.Instance.RegisterModule(LogModule, true);

        var configured = ProjectSettings.GetSetting(ScenePathSetting, "").AsString();
        if (string.IsNullOrWhiteSpace(configured)) return null;
        if (!ResourceLoader.Exists(configured))
        {
            DebugLogger.Instance.Warning(
                LogModule, $"{ScenePathSetting}='{configured}' does not exist; using the built-in rig");
            return null;
        }
        if (ResourceLoader.Load<PackedScene>(configured) is not { } scene)
        {
            DebugLogger.Instance.Warning(
                LogModule, $"{ScenePathSetting}='{configured}' is not a PackedScene; using the built-in rig");
            return null;
        }
        if (scene.Instantiate() is AvatarRig rig) return rig;
        DebugLogger.Instance.Warning(
            LogModule, $"{ScenePathSetting}='{configured}' root does not derive from AvatarRig; using the built-in rig");
        return null;
    }
}
