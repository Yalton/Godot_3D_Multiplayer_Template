using Godot;
using DebugLoggerKit;

namespace MultiplayerTemplate.Avatar;

/// <summary>
/// The default character: a skeleton with one rigid box per bone. Deliberately
/// asset-free. Replace it by setting <c>template/avatar/scene_path</c>.
/// </summary>
public partial class BlockmanRig : AvatarRig
{
    private const string LogModule = "Avatar";
    private const string LibraryName = "template";
    private const string ActionNode = "parameters/action/animation";
    private const string ActionRequest = "parameters/action_shot/request";
    private const string ActionActive = "parameters/action_shot/active";
    private const string GroundBlend = "parameters/locomotion/ground/blend_position";
    private const string StatePlayback = "parameters/locomotion/playback";

    private Skeleton3D _skeleton = null!;
    private AnimationPlayer _player = null!;
    private AnimationTree _tree = null!;
    private AnimationLibrary _library = null!;
    private readonly System.Collections.Generic.Dictionary<AvatarSlot, Node3D> _attachments = new();
    private readonly System.Collections.Generic.List<StandardMaterial3D> _materials = new();
    private LocomotionState _currentState = LocomotionState.Idle;

    public override bool ActionPlaying => (bool)_tree.Get(ActionActive);

    public override void _Ready()
    {
        BuildSkeleton();
        BuildBoxes();
        BuildAttachments();
        BuildAnimation();
    }

    public override void SetLocomotion(LocomotionResult state)
    {
        _tree.Set(GroundBlend, state.Blend);
        if (state.State == _currentState && !state.LandTriggered) return;
        _currentState = state.State;
        if (_tree.Get(StatePlayback).As<AnimationNodeStateMachinePlayback>() is not { } playback) return;
        playback.Travel(StateNodeName(state.State));
    }

    public override void PlayAction(string clip)
    {
        if (!_library.HasAnimation(clip))
        {
            DebugLogger.Instance.Warning(LogModule, $"Avatar has no action clip '{clip}'; ignoring");
            return;
        }
        _tree.Set(ActionNode, $"{LibraryName}/{clip}");
        _tree.Set(ActionRequest, (int)AnimationNodeOneShot.OneShotRequest.Fire);
    }

    public override void StopAction() =>
        _tree.Set(ActionRequest, (int)AnimationNodeOneShot.OneShotRequest.Abort);

    public override Node3D GetAttachment(AvatarSlot slot) => _attachments[slot];

    public override void ApplyAppearance(AvatarAppearance appearance)
    {
        foreach (var material in _materials) material.AlbedoColor = appearance.BodyColor;
    }

    private static string StateNodeName(LocomotionState state) => state switch
    {
        LocomotionState.Jump => ProceduralAnimationLibrary.Jump,
        LocomotionState.Fall => ProceduralAnimationLibrary.Fall,
        LocomotionState.Land => ProceduralAnimationLibrary.Land,
        _ => "ground"
    };

    private void BuildSkeleton()
    {
        _skeleton = new Skeleton3D { Name = ProceduralAnimationLibrary.SkeletonNodeName };
        AddChild(_skeleton);
        var hips = AddBone("hips", -1, new Vector3(0, 0.95f, 0));
        var spine = AddBone("spine", hips, new Vector3(0, 0.35f, 0));
        AddBone("head", spine, new Vector3(0, 0.35f, 0));
        AddBone("upperarm.L", spine, new Vector3(0.3f, 0.22f, 0));
        AddBone("upperarm.R", spine, new Vector3(-0.3f, 0.22f, 0));
        var thighL = AddBone("thigh.L", hips, new Vector3(0.14f, -0.05f, 0));
        var thighR = AddBone("thigh.R", hips, new Vector3(-0.14f, -0.05f, 0));
        AddBone("shin.L", thighL, new Vector3(0, -0.45f, 0));
        AddBone("shin.R", thighR, new Vector3(0, -0.45f, 0));
        _skeleton.ResetBonePoses();
    }

    private int AddBone(string name, int parent, Vector3 offset)
    {
        var index = _skeleton.AddBone(name);
        if (parent >= 0) _skeleton.SetBoneParent(index, parent);
        _skeleton.SetBoneRest(index, new Transform3D(Basis.Identity, offset));
        return index;
    }

    private void BuildBoxes()
    {
        AttachBox("hips", new Vector3(0.5f, 0.28f, 0.28f), Vector3.Zero);
        AttachBox("spine", new Vector3(0.55f, 0.42f, 0.3f), new Vector3(0, 0.16f, 0));
        AttachBox("head", new Vector3(0.34f, 0.34f, 0.34f), new Vector3(0, 0.17f, 0));
        AttachBox("upperarm.L", new Vector3(0.16f, 0.5f, 0.16f), new Vector3(0, -0.25f, 0));
        AttachBox("upperarm.R", new Vector3(0.16f, 0.5f, 0.16f), new Vector3(0, -0.25f, 0));
        AttachBox("thigh.L", new Vector3(0.2f, 0.45f, 0.2f), new Vector3(0, -0.23f, 0));
        AttachBox("thigh.R", new Vector3(0.2f, 0.45f, 0.2f), new Vector3(0, -0.23f, 0));
        AttachBox("shin.L", new Vector3(0.18f, 0.45f, 0.18f), new Vector3(0, -0.23f, 0));
        AttachBox("shin.R", new Vector3(0.18f, 0.45f, 0.18f), new Vector3(0, -0.23f, 0));
    }

    private void AttachBox(string boneName, Vector3 size, Vector3 offset)
    {
        var attachment = NewBoneAttachment(boneName, $"Box_{boneName}");
        var material = new StandardMaterial3D { AlbedoColor = Colors.White };
        _materials.Add(material);
        attachment.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            Position = offset,
            MaterialOverride = material
        });
    }

    private void BuildAttachments()
    {
        _attachments[AvatarSlot.Head] = NewMarker("head", new Vector3(0, 0.36f, 0), "Slot_Head");
        _attachments[AvatarSlot.Back] = NewMarker("spine", new Vector3(0, 0.16f, -0.18f), "Slot_Back");
        _attachments[AvatarSlot.Hips] = NewMarker("hips", new Vector3(0, 0f, -0.18f), "Slot_Hips");
        _attachments[AvatarSlot.HandLeft] = NewMarker("upperarm.L", new Vector3(0, -0.52f, 0), "Slot_HandLeft");
        _attachments[AvatarSlot.HandRight] = NewMarker("upperarm.R", new Vector3(0, -0.52f, 0), "Slot_HandRight");
    }

    private Node3D NewMarker(string boneName, Vector3 offset, string name)
    {
        var attachment = NewBoneAttachment(boneName, $"{name}_Attachment");
        var marker = new Node3D { Name = name, Position = offset };
        attachment.AddChild(marker);
        return marker;
    }

    private BoneAttachment3D NewBoneAttachment(string boneName, string nodeName)
    {
        var attachment = new BoneAttachment3D { Name = nodeName, BoneName = boneName };
        _skeleton.AddChild(attachment);
        attachment.BoneIdx = _skeleton.FindBone(boneName);
        return attachment;
    }

    private void BuildAnimation()
    {
        _library = ProceduralAnimationLibrary.Build();
        _player = new AnimationPlayer { Name = "AnimationPlayer" };
        AddChild(_player);
        _player.AddAnimationLibrary(LibraryName, _library);

        var locomotion = new AnimationNodeStateMachine();
        var ground = new AnimationNodeBlendSpace1D { MinSpace = 0f, MaxSpace = 1f };
        ground.AddBlendPoint(Clip(ProceduralAnimationLibrary.Idle), 0f);
        ground.AddBlendPoint(Clip(ProceduralAnimationLibrary.Walk), 0.5f);
        ground.AddBlendPoint(Clip(ProceduralAnimationLibrary.Run), 1f);
        locomotion.AddNode("ground", ground, new Vector2(0, 0));
        locomotion.AddNode(ProceduralAnimationLibrary.Jump, Clip(ProceduralAnimationLibrary.Jump), new Vector2(220, -80));
        locomotion.AddNode(ProceduralAnimationLibrary.Fall, Clip(ProceduralAnimationLibrary.Fall), new Vector2(220, 0));
        locomotion.AddNode(ProceduralAnimationLibrary.Land, Clip(ProceduralAnimationLibrary.Land), new Vector2(220, 80));
        string[] states =
        [
            "ground", ProceduralAnimationLibrary.Jump,
            ProceduralAnimationLibrary.Fall, ProceduralAnimationLibrary.Land
        ];
        // Travel() needs a transition to exist between any pair the solver can move between.
        foreach (var from in states)
        foreach (var to in states)
        {
            if (from == to) continue;
            locomotion.AddTransition(from, to, new AnimationNodeStateMachineTransition
            {
                SwitchMode = AnimationNodeStateMachineTransition.SwitchModeEnum.Immediate,
                AdvanceMode = AnimationNodeStateMachineTransition.AdvanceModeEnum.Disabled,
                XfadeTime = 0.12f
            });
        }

        var oneShot = new AnimationNodeOneShot { FadeInTime = 0.1f, FadeOutTime = 0.15f };
        var root = new AnimationNodeBlendTree();
        root.AddNode("locomotion", locomotion, new Vector2(0, 0));
        root.AddNode("action", new AnimationNodeAnimation(), new Vector2(0, 140));
        root.AddNode("action_shot", oneShot, new Vector2(260, 60));
        root.ConnectNode("action_shot", 0, "locomotion");
        root.ConnectNode("action_shot", 1, "action");
        root.ConnectNode("output", 0, "action_shot");

        _tree = new AnimationTree
        {
            Name = "AnimationTree",
            TreeRoot = root,
            AnimPlayer = _player.GetPath(),
            Active = true
        };
        AddChild(_tree);
    }

    private static AnimationNodeAnimation Clip(string name) =>
        new() { Animation = $"{LibraryName}/{name}" };
}
