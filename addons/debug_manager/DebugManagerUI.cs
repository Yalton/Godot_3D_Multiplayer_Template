#if TOOLS
using Godot;
using System.Collections.Generic;
using System.Linq;

[Tool]
public partial class DebugManagerUI : Control
{
	// The dock runs in the EDITOR process; the game runs in a SEPARATE process, so the
	// only channel to a running game is files under user://debug_modules/ (the game's
	// DebugLogger polls them once per second when launched from the editor).
	private const string CONFIG_DIR_PATH = "user://debug_modules/";
	private const string GLOBAL_DEBUG_CONFIG_PATH = CONFIG_DIR_PATH + "global_debug.cfg";

	private VBoxContainer moduleList = null!;
	private CheckButton globalToggle = null!;
	private Button enableAllButton = null!;
	private Button disableAllButton = null!;
	private Button refreshButton = null!;
	private Button deleteAllButton = null!;
	private Label statusLabel = null!;
	private LineEdit searchBar = null!;

	private Dictionary<string, ModuleDebugConfig> modulesData = new Dictionary<string, ModuleDebugConfig>();

	public override void _Ready()
	{
		// Get references to UI elements
		moduleList = GetNode<VBoxContainer>("VBoxContainer/HSplitContainer/PanelContainer/ScrollContainer/ModuleList");
		globalToggle = GetNode<CheckButton>("VBoxContainer/GlobalSettings/GlobalToggle");
		enableAllButton = GetNode<Button>("VBoxContainer/ModuleControls/EnableAllButton");
		disableAllButton = GetNode<Button>("VBoxContainer/ModuleControls/DisableAllButton");
		refreshButton = GetNode<Button>("VBoxContainer/ModuleControls/RefreshButton");
		deleteAllButton = GetNode<Button>("VBoxContainer/GlobalSettings/DeleteModuleFiles");
		statusLabel = GetNode<Label>("VBoxContainer/StatusBar/StatusLabel");

		// Create search bar programmatically
		var mainVBox = GetNode<VBoxContainer>("VBoxContainer");
		var hSplit = GetNode<HSplitContainer>("VBoxContainer/HSplitContainer");
		searchBar = new LineEdit();
		searchBar.PlaceholderText = "Filter modules...";
		searchBar.ClearButtonEnabled = true;
		mainVBox.AddChild(searchBar);
		mainVBox.MoveChild(searchBar, hSplit.GetIndex());
		searchBar.TextChanged += OnSearchTextChanged;

		// Connect signals
		globalToggle.Toggled += OnGlobalToggle;
		enableAllButton.Pressed += OnEnableAllPressed;
		disableAllButton.Pressed += OnDisableAllPressed;
		refreshButton.Pressed += LoadModules;
		deleteAllButton.Pressed += OnDeleteAllPressed;

		LoadModules();
	}

	private void LoadModules()
	{
		// Clear existing modules
		foreach (Node child in moduleList.GetChildren())
		{
			child.QueueFree();
		}

		modulesData.Clear();

		// Get persisted global debug setting (written by OnGlobalToggle, read by the
		// running game's DebugLogger). SetPressedNoSignal avoids re-saving on load.
		bool globalEnabled = LoadGlobalDebugSetting(fallback: true);
		globalToggle.SetPressedNoSignal(globalEnabled);

		string configPath = CONFIG_DIR_PATH;
		int modulesLoaded = 0;

		using (var dir = DirAccess.Open("user://"))
		{
			if (dir == null)
			{
				statusLabel.Text = "Error: could not open user:// directory";
				return;
			}
			if (!dir.DirExists(configPath))
			{
				dir.MakeDir(configPath);
				statusLabel.Text = "Created debug modules directory";
				return;
			}
		}

		using (var dir = DirAccess.Open(configPath))
		{
			if (dir != null)
			{
				var moduleNames = new List<string>();

				dir.ListDirBegin();
				string fileName = dir.GetNext();

				// First pass: collect all module names. Skip SaveConfigAtomic temps
				// (Module.<pid>.tmp.tres) — an orphaned temp is not a real module.
				while (!string.IsNullOrEmpty(fileName))
				{
					if (!dir.CurrentIsDir() && fileName.EndsWith(".tres") && !fileName.EndsWith(".tmp.tres"))
					{
						string moduleName = fileName.GetBaseName();
						string configFile = configPath + fileName;

						try
						{
							var resource = ResourceLoader.Load(configFile, "", ResourceLoader.CacheMode.Ignore);
							if (resource is ModuleDebugConfig config)
							{
								modulesData[moduleName] = config;
								moduleNames.Add(moduleName);
							}
							else
							{
								// File exists but wrong type - skip it
								GD.PrintErr($"[DebugManagerUI] Skipping corrupted config file: {fileName}");
							}
						}
						catch (System.Exception e)
						{
							GD.PrintErr($"[DebugManagerUI] Error loading {fileName}: {e.Message}");
						}
					}
					fileName = dir.GetNext();
				}

				// Sort module names
				moduleNames.Sort();

				// Create checkboxes in sorted order
				foreach (string moduleName in moduleNames)
				{
					var config = modulesData[moduleName];

					var checkbox = new CheckBox
					{
						Text = moduleName,
						ButtonPressed = config.Enabled
					};
					
					// Use lambda to capture moduleName correctly
					string capturedName = moduleName;
					checkbox.Toggled += (bool enabled) => OnModuleToggled(enabled, capturedName);

					moduleList.AddChild(checkbox);
					modulesLoaded++;
				}
			}
		}

		statusLabel.Text = $"Loaded {modulesLoaded} modules — global debug {(globalEnabled ? "enabled" : "disabled")}";
		ApplySearchFilter();
	}

	// Reads the persisted global debug flag; returns fallback when the file is absent
	// or unreadable (a fresh project has no global_debug.cfg yet).
	private static bool LoadGlobalDebugSetting(bool fallback)
	{
		var config = new ConfigFile();
		if (config.Load(GLOBAL_DEBUG_CONFIG_PATH) != Godot.Error.Ok)
			return fallback;

		return (bool)config.GetValue("debug", "enabled", fallback);
	}

	private void OnSearchTextChanged(string newText)
	{
		ApplySearchFilter();
	}

	private void ApplySearchFilter()
	{
		string filter = searchBar?.Text ?? "";
		foreach (Node child in moduleList.GetChildren())
		{
			if (child is CheckBox checkbox)
			{
				checkbox.Visible = string.IsNullOrEmpty(filter) ||
					checkbox.Text.Contains(filter, System.StringComparison.OrdinalIgnoreCase);
			}
		}
	}

	private void OnModuleToggled(bool enabled, string moduleName)
	{
		if (!modulesData.ContainsKey(moduleName))
			return;

		// Replace with a fresh instance to avoid Godot resource-cache weirdness
		// that caused toggles to sometimes revert on refresh
		modulesData[moduleName] = new ModuleDebugConfig(moduleName, enabled);
		SaveModuleConfig(moduleName);

		// A game launched from the editor picks the saved file up via its 1-second poll.
		statusLabel.Text = $"Updated {moduleName}: {(enabled ? "Enabled" : "Disabled")}";
	}

	private void SaveModuleConfig(string moduleName)
	{
		if (!modulesData.ContainsKey(moduleName))
			return;

		var config = modulesData[moduleName];
		string configPath = CONFIG_DIR_PATH + moduleName + ".tres";
		var err = ResourceSaver.Save(config, configPath);

		if (err != Godot.Error.Ok)
		{
			statusLabel.Text = $"Error saving module config: {err}";
		}
	}

	private void OnGlobalToggle(bool enabled)
	{
		// Persist to user://debug_modules/global_debug.cfg — the game's DebugLogger reads
		// it on startup and re-reads it in its 1-second editor poll.
		using (var dir = DirAccess.Open("user://"))
		{
			if (dir != null && !dir.DirExists(CONFIG_DIR_PATH))
			{
				dir.MakeDir(CONFIG_DIR_PATH);
			}
		}

		var config = new ConfigFile();
		config.SetValue("debug", "enabled", enabled);
		var err = config.Save(GLOBAL_DEBUG_CONFIG_PATH);

		if (err != Godot.Error.Ok)
		{
			statusLabel.Text = $"Error saving global debug setting: {err}";
			return;
		}

		statusLabel.Text = $"Global debug {(enabled ? "enabled" : "disabled")}";
	}

	private void OnEnableAllPressed()
	{
		foreach (Node child in moduleList.GetChildren())
		{
			if (child is CheckBox checkbox)
			{
				checkbox.ButtonPressed = true;
			}
		}
		statusLabel.Text = "All modules enabled";
	}

	private void OnDisableAllPressed()
	{
		foreach (Node child in moduleList.GetChildren())
		{
			if (child is CheckBox checkbox)
			{
				checkbox.ButtonPressed = false;
			}
		}
		statusLabel.Text = "All modules disabled";
	}

	private void OnDeleteAllPressed()
	{
		var confirmationDialog = new ConfirmationDialog
		{
			Title = "Delete All Module Files",
			DialogText = "Are you sure you want to delete all module config files?"
		};
		confirmationDialog.Confirmed += ConfirmDeleteAllConfigs;
		AddChild(confirmationDialog);
		confirmationDialog.PopupCentered();
	}

	private void ConfirmDeleteAllConfigs()
	{
		string configPath = CONFIG_DIR_PATH;
		int filesDeleted = 0;

		using var dir = DirAccess.Open(configPath);
		if (dir != null)
		{
			dir.ListDirBegin();
			string fileName = dir.GetNext();

			while (!string.IsNullOrEmpty(fileName))
			{
				if (!dir.CurrentIsDir() && fileName.EndsWith(".tres"))
				{
					string fullPath = configPath + fileName;
					var err = dir.Remove(fullPath);
					if (err == Godot.Error.Ok)
					{
						filesDeleted++;
					}
				}
				fileName = dir.GetNext();
			}
		}

		statusLabel.Text = $"Deleted {filesDeleted} module config files";
		LoadModules();
	}
}
#endif
