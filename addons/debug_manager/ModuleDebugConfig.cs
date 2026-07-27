using Godot;

[Tool]
[GlobalClass]
public partial class ModuleDebugConfig : Resource
{
	[Export] public string ModuleName { get; set; } = "";
	[Export] public bool Enabled { get; set; } = false;

	public ModuleDebugConfig() : this("", false) { }

	public ModuleDebugConfig(string moduleName, bool enabled)
	{
		ModuleName = moduleName;
		Enabled = enabled;
		ResourceName = "ModuleDebugConfig";
	}
}
