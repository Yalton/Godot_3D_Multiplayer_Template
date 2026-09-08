using Godot;

namespace MultiplayerTemplate.Avatar;

/// <summary>
/// Builds every default animation clip in code, so the template ships no binary art.
/// Clips are authored as bone rotation tracks against a skeleton named
/// <see cref="SkeletonNodeName"/>.
/// </summary>
public static class ProceduralAnimationLibrary
{
    public const string SkeletonNodeName = "Skeleton3D";
    public const string Idle = "idle";
    public const string Walk = "walk";
    public const string Run = "run";
    public const string Jump = "jump";
    public const string Fall = "fall";
    public const string Land = "land";
    public const string EmoteWave = "emote_wave";
    public const string EmotePoint = "emote_point";
    public const string EmoteClap = "emote_clap";
    public const string EmoteCheer = "emote_cheer";
    public const string EmoteSit = "emote_sit";
    public const string EmoteDance = "emote_dance";

    public static AnimationLibrary Build()
    {
        var library = new AnimationLibrary();
        library.AddAnimation(Idle, BuildIdle());
        library.AddAnimation(Walk, BuildStride(1.0f, 0.35f, 0.28f, 0.10f));
        library.AddAnimation(Run, BuildStride(0.6f, 0.75f, 0.55f, 0.22f));
        library.AddAnimation(Jump, BuildJump());
        library.AddAnimation(Fall, BuildFall());
        library.AddAnimation(Land, BuildLand());
        library.AddAnimation(EmoteWave, BuildEmoteWave());
        library.AddAnimation(EmotePoint, BuildEmotePoint());
        library.AddAnimation(EmoteClap, BuildEmoteClap());
        library.AddAnimation(EmoteCheer, BuildEmoteCheer());
        library.AddAnimation(EmoteSit, BuildEmoteSit());
        library.AddAnimation(EmoteDance, BuildEmoteDance());
        return library;
    }

    private static Animation BuildIdle()
    {
        var animation = NewAnimation(2.4f, loop: true);
        AddRotationTrack(animation, "spine",
            (0f, Pitch(0f)), (1.2f, Pitch(0.035f)), (2.4f, Pitch(0f)));
        AddRotationTrack(animation, "upperarm.L",
            (0f, Roll(0.14f)), (1.2f, Roll(0.19f)), (2.4f, Roll(0.14f)));
        AddRotationTrack(animation, "upperarm.R",
            (0f, Roll(-0.14f)), (1.2f, Roll(-0.19f)), (2.4f, Roll(-0.14f)));
        return animation;
    }

    private static Animation BuildStride(float length, float legSwing, float armSwing, float lean)
    {
        var animation = NewAnimation(length, loop: true);
        var half = length * 0.5f;
        AddRotationTrack(animation, "spine", (0f, Pitch(-lean)), (length, Pitch(-lean)));
        AddRotationTrack(animation, "thigh.L",
            (0f, Pitch(legSwing)), (half, Pitch(-legSwing)), (length, Pitch(legSwing)));
        AddRotationTrack(animation, "thigh.R",
            (0f, Pitch(-legSwing)), (half, Pitch(legSwing)), (length, Pitch(-legSwing)));
        AddRotationTrack(animation, "shin.L",
            (0f, Pitch(-legSwing * 0.5f)), (half, Pitch(-legSwing * 0.1f)), (length, Pitch(-legSwing * 0.5f)));
        AddRotationTrack(animation, "shin.R",
            (0f, Pitch(-legSwing * 0.1f)), (half, Pitch(-legSwing * 0.5f)), (length, Pitch(-legSwing * 0.1f)));
        AddRotationTrack(animation, "upperarm.L",
            (0f, Pitch(-armSwing)), (half, Pitch(armSwing)), (length, Pitch(-armSwing)));
        AddRotationTrack(animation, "upperarm.R",
            (0f, Pitch(armSwing)), (half, Pitch(-armSwing)), (length, Pitch(armSwing)));
        return animation;
    }

    private static Animation BuildJump()
    {
        var animation = NewAnimation(0.4f, loop: false);
        AddRotationTrack(animation, "thigh.L", (0f, Pitch(0f)), (0.4f, Pitch(0.7f)));
        AddRotationTrack(animation, "thigh.R", (0f, Pitch(0f)), (0.4f, Pitch(0.45f)));
        AddRotationTrack(animation, "shin.L", (0f, Pitch(0f)), (0.4f, Pitch(-0.9f)));
        AddRotationTrack(animation, "shin.R", (0f, Pitch(0f)), (0.4f, Pitch(-0.6f)));
        AddRotationTrack(animation, "upperarm.L", (0f, Roll(0.2f)), (0.4f, Roll(1.4f)));
        AddRotationTrack(animation, "upperarm.R", (0f, Roll(-0.2f)), (0.4f, Roll(-1.4f)));
        return animation;
    }

    private static Animation BuildFall()
    {
        var animation = NewAnimation(0.8f, loop: true);
        AddRotationTrack(animation, "spine", (0f, Pitch(0.12f)), (0.4f, Pitch(0.2f)), (0.8f, Pitch(0.12f)));
        AddRotationTrack(animation, "thigh.L", (0f, Pitch(0.3f)), (0.8f, Pitch(0.3f)));
        AddRotationTrack(animation, "thigh.R", (0f, Pitch(0.15f)), (0.8f, Pitch(0.15f)));
        AddRotationTrack(animation, "upperarm.L", (0f, Roll(1.5f)), (0.4f, Roll(1.7f)), (0.8f, Roll(1.5f)));
        AddRotationTrack(animation, "upperarm.R", (0f, Roll(-1.5f)), (0.4f, Roll(-1.7f)), (0.8f, Roll(-1.5f)));
        return animation;
    }

    private static Animation BuildLand()
    {
        var animation = NewAnimation(0.35f, loop: false);
        AddRotationTrack(animation, "spine", (0f, Pitch(0.35f)), (0.35f, Pitch(0f)));
        AddRotationTrack(animation, "thigh.L", (0f, Pitch(0.55f)), (0.35f, Pitch(0f)));
        AddRotationTrack(animation, "thigh.R", (0f, Pitch(0.55f)), (0.35f, Pitch(0f)));
        AddRotationTrack(animation, "shin.L", (0f, Pitch(-0.9f)), (0.35f, Pitch(0f)));
        AddRotationTrack(animation, "shin.R", (0f, Pitch(-0.9f)), (0.35f, Pitch(0f)));
        return animation;
    }

    private static Animation BuildEmoteWave()
    {
        // Not looping: a one-shot must finish so the AnimationNodeOneShot ends on its own.
        var animation = NewAnimation(1.2f, loop: false);
        AddRotationTrack(animation, "upperarm.R",
            (0f, Roll(-0.14f)),
            (0.15f, Roll(-2.2f)),
            (0.4f, Roll(-2.2f) * Pitch(0.3f)),
            (0.7f, Roll(-2.2f) * Pitch(-0.3f)),
            (1.0f, Roll(-2.2f) * Pitch(0.3f)),
            (1.2f, Roll(-0.14f)));
        return animation;
    }

    private static Animation BuildEmotePoint()
    {
        var animation = NewAnimation(0.8f, loop: false);
        // The character's forward/aim direction is -Z. Rotating a resting, downward arm
        // by a positive pitch raises it into -Z; a negative pitch points behind the player.
        AddRotationTrack(animation, "upperarm.R",
            (0f, Roll(-0.14f)), (0.2f, Pitch(1.3f)), (0.8f, Pitch(1.3f)));
        AddRotationTrack(animation, "spine",
            (0f, Pitch(0f)), (0.2f, Pitch(0.12f)), (0.8f, Pitch(0.12f)));
        return animation;
    }

    private static Animation BuildEmoteClap()
    {
        var animation = NewAnimation(1.0f, loop: false);
        AddRotationTrack(animation, "upperarm.L",
            (0f, Roll(0.14f)),
            (0.15f, Roll(1.3f)),
            (0.35f, Roll(0.3f)),
            (0.55f, Roll(1.3f)),
            (0.75f, Roll(0.3f)),
            (1.0f, Roll(0.14f)));
        AddRotationTrack(animation, "upperarm.R",
            (0f, Roll(-0.14f)),
            (0.15f, Roll(-1.3f)),
            (0.35f, Roll(-0.3f)),
            (0.55f, Roll(-1.3f)),
            (0.75f, Roll(-0.3f)),
            (1.0f, Roll(-0.14f)));
        return animation;
    }

    private static Animation BuildEmoteCheer()
    {
        var animation = NewAnimation(1.0f, loop: false);
        AddRotationTrack(animation, "upperarm.L",
            (0f, Roll(0.14f)), (0.2f, Roll(2.7f)), (0.5f, Roll(2.5f)), (0.8f, Roll(2.7f)), (1.0f, Roll(2.6f)));
        AddRotationTrack(animation, "upperarm.R",
            (0f, Roll(-0.14f)), (0.2f, Roll(-2.7f)), (0.5f, Roll(-2.5f)), (0.8f, Roll(-2.7f)), (1.0f, Roll(-2.6f)));
        AddRotationTrack(animation, "hips",
            (0f, Pitch(0f)), (0.25f, Pitch(0.08f)), (0.5f, Pitch(0f)), (0.75f, Pitch(0.08f)), (1.0f, Pitch(0f)));
        return animation;
    }

    private static Animation BuildEmoteSit()
    {
        // Looping: the seat pose must persist until StopAction(), so key 0 == key length
        // for every bone here, matching BuildIdle/BuildStride.
        var animation = NewAnimation(2.0f, loop: true);
        AddRotationTrack(animation, "thigh.L", (0f, Pitch(1.45f)), (2.0f, Pitch(1.45f)));
        AddRotationTrack(animation, "thigh.R", (0f, Pitch(1.45f)), (2.0f, Pitch(1.45f)));
        AddRotationTrack(animation, "shin.L", (0f, Pitch(-1.5f)), (2.0f, Pitch(-1.5f)));
        AddRotationTrack(animation, "shin.R", (0f, Pitch(-1.5f)), (2.0f, Pitch(-1.5f)));
        AddRotationTrack(animation, "spine",
            (0f, Pitch(-0.1f)), (1.0f, Pitch(-0.07f)), (2.0f, Pitch(-0.1f)));
        return animation;
    }

    private static Animation BuildEmoteDance()
    {
        // Looping: same seam requirement as BuildEmoteSit, applied to every bone below.
        var animation = NewAnimation(1.6f, loop: true);
        AddRotationTrack(animation, "hips",
            (0f, Roll(0f)), (0.4f, Roll(0.18f)), (0.8f, Roll(0f)), (1.2f, Roll(-0.18f)), (1.6f, Roll(0f)));
        AddRotationTrack(animation, "upperarm.L",
            (0f, Roll(0.14f)), (0.4f, Roll(1.0f)), (0.8f, Roll(0.14f)), (1.2f, Roll(1.0f)), (1.6f, Roll(0.14f)));
        AddRotationTrack(animation, "upperarm.R",
            (0f, Roll(-0.14f)), (0.4f, Roll(-1.0f)), (0.8f, Roll(-0.14f)), (1.2f, Roll(-1.0f)), (1.6f, Roll(-0.14f)));
        AddRotationTrack(animation, "thigh.L",
            (0f, Pitch(0.15f)), (0.4f, Pitch(-0.15f)), (0.8f, Pitch(0.15f)), (1.2f, Pitch(-0.15f)), (1.6f, Pitch(0.15f)));
        AddRotationTrack(animation, "thigh.R",
            (0f, Pitch(-0.15f)), (0.4f, Pitch(0.15f)), (0.8f, Pitch(-0.15f)), (1.2f, Pitch(0.15f)), (1.6f, Pitch(-0.15f)));
        return animation;
    }

    private static Animation NewAnimation(float length, bool loop)
    {
        var animation = new Animation { Length = length };
        animation.LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
        return animation;
    }

    private static void AddRotationTrack(
        Animation animation, string boneName, params (float Time, Quaternion Rotation)[] keys)
    {
        var track = animation.AddTrack(Animation.TrackType.Rotation3D);
        animation.TrackSetPath(track, $"{SkeletonNodeName}:{boneName}");
        animation.TrackSetInterpolationType(track, Animation.InterpolationType.Linear);
        foreach (var (time, rotation) in keys)
            animation.RotationTrackInsertKey(track, time, rotation);
    }

    private static Quaternion Pitch(float radians) =>
        new Quaternion(Vector3.Right, radians);

    private static Quaternion Roll(float radians) =>
        new Quaternion(Vector3.Forward, radians);
}
