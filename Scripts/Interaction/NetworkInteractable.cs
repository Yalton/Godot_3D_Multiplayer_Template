using Godot;

namespace MultiplayerTemplate.Interaction;

/// <summary>Example synced toggle. Subclass and override ApplyState for doors, buttons, etc.</summary>
public partial class NetworkInteractable : StaticBody3D
{
    [Export] public string Prompt { get; set; } = "Use";
    [Export] public MeshInstance3D? Visual { get; set; }
    public bool Active { get; private set; }

    public override void _Ready() => AddToGroup("interactable");

    public virtual void ServerInteract(long peerId)
    {
        if (!Multiplayer.IsServer()) return;
        Rpc(nameof(SetActive), !Active);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SetActive(bool active)
    {
        Active = active;
        ApplyState(active);
    }

    protected virtual void ApplyState(bool active)
    {
        if (Visual is { } mesh)
        {
            var material = new StandardMaterial3D
            {
                AlbedoColor = active ? new Color(0.3f, 1f, 0.35f) : new Color(1f, 0.35f, 0.3f)
            };
            mesh.MaterialOverride = material;
        }
    }
}
