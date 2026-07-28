using Godot;

namespace MultiplayerTemplate.Network;

/// <summary>
/// Registers the node that owns dynamically loaded match scenes. Attach this
/// component to any main/test scene's match container; no owner-name convention required.
/// </summary>
public partial class MatchSceneMount : Node
{
    public override void _Ready() => MatchSceneCoordinator.Instance.SetMatchContainer(this);
}
