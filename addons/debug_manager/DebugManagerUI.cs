#if TOOLS
using Godot;
using System.Collections.Generic;
using System.Linq;
using DebugLoggerKit;

[Tool]
public partial class DebugManagerUI : Control
{
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

		// Get global debug setting
		bool globalEnabled = true;
		if (Engine.HasSingleton("DebugLogger"))
		{
			var debugLogger = DebugLogger.Instance;
			if (debugLogger != null)
			{
				globalEnabled = debugLogger.DebugEnabled;
			}
		}

		globalToggle.ButtonPressed = globalEnabled;

		string configPath = "user://debug_modules/";
		int modulesLoaded = 0;

		using (var dir = DirAccess.Open("user://"))
		{
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

				// First pass: collect all module names
				while (!string.IsNullOrEmpty(fileName))
				{
					if (!dir.CurrentIsDir() && fileName.EndsWith(".tres"))
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
								DebugLogger.Instance?.Error("DebugManagerUI", $"Skipping corrupted config file: {fileName}");
							}
						}
						catch (System.Exception e)
						{
							DebugLogger.Instance?.Error("DebugManagerUI", $"Error loading {fileName}: {e.Message}");
						}
					}
					fileName = dir.GetNext();
				}

				// Also get active modules from DebugLogger
				if (Engine.HasSingleton("DebugLogger"))
				{
					var debugLogger = DebugLogger.Instance;
					if (debugLogger != null)
					{
						foreach (var kvp in debugLogger.RegisteredModules)
						{
							if (!modulesData.ContainsKey(kvp.Key))
							{
								var config = new ModuleDebugConfig(kvp.Key, kvp.Value);

								if (!debugLogger.DisableFileWriting)
								{
									string savePath = configPath + kvp.Key + ".tres";
									ResourceSaver.Save(config, savePath);
								}

								modulesData[kvp.Key] = config;
								if (!moduleNames.Contains(kvp.Key))
								{
									moduleNames.Add(kvp.Key);
								}
							}
						}
					}
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

		statusLabel.Text = $"Loaded {modulesLoaded} modules";
		ApplySearchFilter();
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

		if (Engine.HasSingleton("DebugLogger"))
		{
			var debugLogger = DebugLogger.Instance;
			if (debugLogger != null && debugLogger.RegisteredModules.ContainsKey(moduleName))
			{
				debugLogger.SetModuleEnabled(moduleName, enabled);
				statusLabel.Text = $"Updated {moduleName}: {(enabled ? "Enabled" : "Disabled")}";
			}
		}
	}

	private void SaveModuleConfig(string moduleName)
	{
		if (!modulesData.ContainsKey(moduleName))
			return;

		if (Engine.HasSingleton("DebugLogger"))
		{
			var debugLogger = DebugLogger.Instance;
			if (debugLogger != null && debugLogger.DisableFileWriting)
				return;
		}

		var config = modulesData[moduleName];
		string configPath = "user://debug_modules/" + moduleName + ".tres";
		var err = ResourceSaver.Save(config, configPath);

		if (err != Godot.Error.Ok)
		{
			statusLabel.Text = $"Error saving module config: {err}";
		}
	}

	private void OnGlobalToggle(bool enabled)
	{
		if (Engine.HasSingleton("DebugLogger"))
		{
			var debugLogger = DebugLogger.Instance;
			if (debugLogger != null)
			{
				debugLogger.DebugEnabled = enabled;
				statusLabel.Text = $"Global debug {(enabled ? "enabled" : "disabled")}";
			}
		}
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
		string configPath = "user://debug_modules/";
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
