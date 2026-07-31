#if TOOLS
using Godot;

[Tool]
public partial class DebugManagerPlugin : EditorPlugin
{
	private Control debugManagerInstance = null!;
	private EditorDock debugManagerDock = null!;

	public override void _EnterTree()
	{
		var scene = GD.Load<PackedScene>("res://addons/debug_manager/debug_manager_ui.tscn");
		debugManagerInstance = scene.Instantiate<Control>();

		debugManagerDock = new EditorDock();
		debugManagerDock.DefaultSlot = EditorDock.DockSlot.LeftUr;
		debugManagerDock.AddChild(debugManagerInstance);
		AddDock(debugManagerDock);
	}

	public override void _ExitTree()
	{
		if (debugManagerDock != null)
		{
			RemoveDock(debugManagerDock);
			// Free the dock itself, not just the UI under it — RemoveDock only detaches,
			// so skipping this leaks the dock on every plugin disable/enable cycle.
			// Freeing the dock also frees debugManagerInstance (it is a child).
			debugManagerDock.QueueFree();
			debugManagerDock = null!;
			debugManagerInstance = null!;
		}
	}
}
#endif
