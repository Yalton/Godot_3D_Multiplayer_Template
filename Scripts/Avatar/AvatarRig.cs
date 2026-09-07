using Godot;

namespace MultiplayerTemplate.Avatar;

/// <summary>Attachment points a rig must expose for props and cosmetics.</summary>
public enum AvatarSlot
{
    Head,
    Back,
    Hips,
    HandLeft,
    HandRight
}

/// <summary>
/// Replicated-cosmetic description of a character. Values are catalogue IDs only.
/// </summary>

/// <summary>
/// Replacement seam for the character body. Supply your own rig by setting the
/// project setting <c>template/avatar/scene_path</c> to a scene whose root derives
/// from this class. See docs/EXTENDING.md.
/// </summary>
public abstract partial class AvatarRig : Node3D
{
    /// <summary>Cosmetic, called every physics frame on every peer.</summary>
    public abstract void SetLocomotion(LocomotionResult state);

    /// <summary>Plays a one-shot clip over locomotion. Unknown clips are ignored.</summary>
    public abstract void PlayAction(string clip);

    public abstract void StopAction();

    public abstract bool ActionPlaying { get; }

    public abstract Node3D GetAttachment(AvatarSlot slot);

    public abstract void ApplyAppearance(AvatarAppearance appearance);

    /// <summary>
    /// The per-peer hue the template used for its capsule players, kept so a fresh
    /// session still gives every player a distinguishable colour before P2 adds
    /// real customization.
    /// </summary>
    public static AvatarAppearance DefaultAppearanceFor(long peerId) => AvatarAppearance.Default;
}
