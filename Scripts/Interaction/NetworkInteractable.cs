using Godot;
using MultiplayerTemplate.Network;

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
        var active = !Active;
        // SetActive is [Rpc(CallLocal = true)], so the server applied its own state via the
        // broadcast below. Only peers with the match scene mounted have this node, so the
        // server must now apply it directly and send to ready peers explicitly instead.
        SetActive(active);
        foreach (var readyPeerId in MatchSceneRpc.ReadyPeers(this))
            RpcId(readyPeerId, nameof(SetActive), active);
    }

    /// <summary>Server-side: brings a late joiner up to date with the current state.</summary>
    public void SyncStateTo(long peerId)
    {
        if (!Multiplayer.IsServer()) return;
        RpcId(peerId, nameof(SetActive), Active);
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
