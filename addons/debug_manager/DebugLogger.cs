using Godot;
using System.Collections.Generic;
using System.Linq;

namespace DebugLoggerKit;

public partial class DebugLogger : Node
{
	[Export] public bool DebugEnabled { get; set; } = false;
	[Export] public bool DisableFileWriting { get; set; } = false;
	private static DebugLogger _instance = null!;
	public static DebugLogger Instance => _instance;
	private const string MODULE_CONFIG_PATH = "user://debug_modules/";
	private const string GLOBAL_DEBUG_CONFIG_PATH = MODULE_CONFIG_PATH + "global_debug.cfg";

	private Dictionary<string, bool> registeredModules = new Dictionary<string, bool>();
	private Dictionary<string, ModuleDebugConfig> moduleConfigs = new Dictionary<string, ModuleDebugConfig>();

	public enum LogLevel
	{
		Info,
		Warning,
		ErrorLevel,
		Debug
	}

	public override void _Ready()
	{
		_instance = this;

		// Dev preset force-enables global debug + every module regardless of the
		// scene/inspector value or per-module user:// configs. The "dev" feature
		// is set on the Dev Build export preset's custom_features.
		if (OS.HasFeature("dev"))
		{
			DebugEnabled = true;
		}

		if (!DisableFileWriting)
		{
			EnsureConfigDirectory();
		}

		// Apply the dock-persisted global debug flag (dev builds force-enable above;
		// if the file is absent the scene/inspector value stands).
		if (!DisableFileWriting && !OS.HasFeature("dev"))
		{
			ApplyGlobalDebugSettingFromDisk();
		}

		// Sync DebugDraw3D with our global debug state
#if DEBUGDRAW3D
		DebugDraw3D.DebugEnabled = DebugEnabled;
#endif

		// Poll .tres configs so toggles made in the editor plugin panel propagate to the running game.
		// Skipped in dev builds — RegisterModule already force-enables everything; reloading from disk
		// would just re-disable modules based on whatever was last persisted in a non-dev run.
		// Editor-launched games only: the dock the polling exists for cannot reach an export,
		// so shipped builds would pay per-second disk I/O for nothing.
		if (!DisableFileWriting && !OS.HasFeature("dev") && OS.HasFeature("editor"))
		{
			var reloadTimer = new Godot.Timer
			{
				WaitTime = 1.0f,
				Autostart = true,
				OneShot = false
			};
			reloadTimer.Timeout += ReloadModuleConfigsFromDisk;
			AddChild(reloadTimer);
		}

		GD.Print("=== Debug Logger Started ===");
		GD.Print($"Global debug enabled: {DebugEnabled}");
		GD.Print($"File writing disabled: {DisableFileWriting}");
		GD.Print("==========================");
	}

	private void ReloadModuleConfigsFromDisk()
	{
		// Global flag first — the editor dock persists it to the same directory.
		if (ApplyGlobalDebugSettingFromDisk())
		{
			GD.Print($"[DebugLogger] Hot-reloaded global debug: {(DebugEnabled ? "enabled" : "disabled")}");
		}

		foreach (var moduleName in registeredModules.Keys.ToList())
		{
			string configPath = MODULE_CONFIG_PATH + moduleName + ".tres";
			if (!Godot.FileAccess.FileExists(configPath))
				continue;

			// Same torn-file guard as LoadModuleConfig: a concurrent instance may be
			// mid-write, and loading a truncated .tres spams unsuppressable native errors.
			if (!HasValidResourceHeader(configPath))
				continue;

			var resource = ResourceLoader.Load(configPath, "", ResourceLoader.CacheMode.Ignore);
			if (resource is ModuleDebugConfig config && registeredModules[moduleName] != config.Enabled)
			{
				registeredModules[moduleName] = config.Enabled;
				moduleConfigs[moduleName] = config;
				if (DebugEnabled)
				{
					GD.Print($"[DebugLogger] Hot-reloaded {moduleName}: {(config.Enabled ? "enabled" : "disabled")}");
				}
			}
		}
	}

	// Reads the global debug flag persisted by the editor dock (global_debug.cfg) and
	// applies it to DebugEnabled. An absent or unreadable file leaves DebugEnabled as-is.
	// Returns true when the value actually changed.
	private bool ApplyGlobalDebugSettingFromDisk()
	{
		var config = new ConfigFile();
		if (config.Load(GLOBAL_DEBUG_CONFIG_PATH) != Godot.Error.Ok)
			return false;

		bool enabled = (bool)config.GetValue("debug", "enabled", DebugEnabled);
		if (enabled == DebugEnabled)
			return false;

		DebugEnabled = enabled;
#if DEBUGDRAW3D
		DebugDraw3D.DebugEnabled = DebugEnabled;
#endif
		return true;
	}

	public void RegisterModule(string moduleName, bool defaultEnabled = true)
	{
		if (registeredModules.ContainsKey(moduleName))
			return;

		// Dev build force-enables every module regardless of saved per-module config.
		bool isEnabled = OS.HasFeature("dev")
			? true
			: LoadModuleConfig(moduleName, defaultEnabled);
		registeredModules[moduleName] = isEnabled;

		if (DebugEnabled && isEnabled)
		{
			LogMessage(moduleName, "Module registered for debugging", LogLevel.Info);
		}
	}

	private bool LoadModuleConfig(string moduleName, bool defaultEnabled)
	{
		if (DisableFileWriting)
			return defaultEnabled;

		string configPath = MODULE_CONFIG_PATH + moduleName + ".tres";

		if (Godot.FileAccess.FileExists(configPath))
		{
			// Validate the resource header BEFORE GD.Load. A torn/truncated .tres
			// (crash mid-write, or a cross-instance write race — SplashController is
			// the first module registered at launch, so the most exposed) makes
			// GD.Load emit native C++ parse errors that a try/catch cannot suppress
			// (they're printerr'd, not thrown). Skipping the load on a bad header
			// keeps the console clean; we delete and recreate below.
			if (HasValidResourceHeader(configPath))
			{
				try
				{
					// Try to load as ModuleDebugConfig
					var resource = GD.Load(configPath);
					if (resource is ModuleDebugConfig config)
					{
						moduleConfigs[moduleName] = config;
						return config.Enabled;
					}
					else
					{
						// File exists but wrong type - delete and recreate
						GD.PrintErr($"Config file for {moduleName} is wrong type, recreating...");
					}
				}
				catch (System.Exception e)
				{
					GD.PrintErr($"Error loading config for {moduleName}: {e.Message}");
				}
			}
			else
			{
				// Corrupt header - delete and recreate without ever calling GD.Load.
				GD.PrintErr($"Config file for {moduleName} is corrupted, recreating...");
			}

			using var dir = DirAccess.Open(MODULE_CONFIG_PATH);
			dir?.Remove(configPath);
		}

		var newConfig = new ModuleDebugConfig(moduleName, defaultEnabled);

		if (!DisableFileWriting)
		{
			var err = SaveConfigAtomic(newConfig, moduleName);
			if (err != Godot.Error.Ok)
			{
				GD.PushError($"Failed to save module config for: {moduleName}");
			}
		}

		moduleConfigs[moduleName] = newConfig;
		return defaultEnabled;
	}

	// Confirms a .tres begins with the Godot resource header. Returns false for an
	// empty, truncated, or garbage file so callers can recreate it without invoking
	// GD.Load (which would print unsuppressable native parse errors on a bad file).
	private static bool HasValidResourceHeader(string path)
	{
		using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
		if (file == null)
			return false;
		return file.GetLine().StartsWith("[gd_resource");
	}

	// Atomic save: write to a per-process temp file, then rename it over the target.
	// Rename is atomic on POSIX, so a reader — or a second game instance sharing
	// user:// (host + client on one machine, the two-player F5 setup) — never sees a
	// half-written .tres, and a crash mid-write can only damage the throwaway temp.
	// This is the root-cause fix for the SplashController parse-error corruption.
	// The temp keeps the .tres extension so ResourceSaver selects the text format,
	// and embeds the PID so concurrent instances never share a temp file.
	private Godot.Error SaveConfigAtomic(ModuleDebugConfig config, string moduleName)
	{
		string configPath = MODULE_CONFIG_PATH + moduleName + ".tres";
		string tmpPath = MODULE_CONFIG_PATH + moduleName + "." + OS.GetProcessId() + ".tmp.tres";

		var err = ResourceSaver.Save(config, tmpPath);
		if (err != Godot.Error.Ok)
			return err;

		using var dir = DirAccess.Open(MODULE_CONFIG_PATH);
		if (dir == null)
			return Godot.Error.CantOpen;

		// POSIX rename clobbers atomically; on platforms where it won't, drop the
		// stale target first.
		err = dir.Rename(tmpPath, configPath);
		if (err != Godot.Error.Ok && dir.FileExists(configPath))
		{
			dir.Remove(configPath);
			err = dir.Rename(tmpPath, configPath);
		}
		if (err != Godot.Error.Ok)
			dir.Remove(tmpPath); // don't leave a stray temp behind on failure
		return err;
	}

	private void SaveModuleConfig(string moduleName, bool enabled)
	{
		if (DisableFileWriting)
			return;

		if (!moduleConfigs.TryGetValue(moduleName, out var config))
		{
			config = new ModuleDebugConfig(moduleName, enabled);
			moduleConfigs[moduleName] = config;
		}

		config.Enabled = enabled;

		var err = SaveConfigAtomic(config, moduleName);
		if (err != Godot.Error.Ok)
		{
			GD.PushError($"Failed to save module config for: {moduleName}");
		}
	}

	public int DeleteAllConfigFiles()
	{
		int filesDeleted = 0;

		using var dir = DirAccess.Open(MODULE_CONFIG_PATH);
		if (dir != null)
		{
			dir.ListDirBegin();
			string fileName = dir.GetNext();

			while (!string.IsNullOrEmpty(fileName))
			{
				if (!dir.CurrentIsDir() && fileName.EndsWith(".tres"))
				{
					string fullPath = MODULE_CONFIG_PATH + fileName;
					var err = dir.Remove(fullPath);
					if (err == Godot.Error.Ok)
					{
						filesDeleted++;
					}
				}
				fileName = dir.GetNext();
			}
		}

		return filesDeleted;
	}

	public void UnregisterModule(string moduleName)
	{
		registeredModules.Remove(moduleName);
	}

	public void SetModuleEnabled(string moduleName, bool enabled)
	{
		if (registeredModules.ContainsKey(moduleName))
		{
			registeredModules[moduleName] = enabled;
			SaveModuleConfig(moduleName, enabled);

			if (DebugEnabled)
			{
				LogMessage(moduleName, $"Debug {(enabled ? "enabled" : "disabled")}", LogLevel.Info);
			}
		}
	}

	public void SetAllModulesEnabled(bool enabled)
	{
		foreach (var module in registeredModules.Keys.ToList())
		{
			SetModuleEnabled(module, enabled);
		}
	}

	public bool IsModuleEnabled(string moduleName)
	{
		return DebugEnabled && registeredModules.ContainsKey(moduleName) && registeredModules[moduleName];
	}

	public void LogMessage(string moduleName, string message, LogLevel level = LogLevel.Debug)
	{
		string levelText = GetLevelText(level);
		string formattedMessage = $"[{moduleName}][{levelText}] {message}";

		// Warnings and errors are never suppressed. DebugEnabled defaults to false outside
		// a dev build, so gating them would make an exported game silent about the very
		// problems its player needs reported — a rejected avatar scene, a refused RPC, a
		// failed transport. They also go through Godot's own channels so they surface in
		// the editor and in the crash log, not just stdout.
		if (level == LogLevel.Warning)
		{
			GD.PushWarning(formattedMessage);
			GD.Print(formattedMessage);
			return;
		}
		if (level == LogLevel.ErrorLevel)
		{
			GD.PushError(formattedMessage);
			GD.PrintErr(formattedMessage);
			return;
		}

		// Info and Debug remain opt-in per module, as before.
		if (!IsModuleEnabled(moduleName))
			return;

		GD.Print(formattedMessage);
	}

	public void Debug(string moduleName, string message)
	{
		LogMessage(moduleName, message, LogLevel.Debug);
	}

	public void Info(string moduleName, string message)
	{
		LogMessage(moduleName, message, LogLevel.Info);
	}

	public void Warning(string moduleName, string message)
	{
		LogMessage(moduleName, message, LogLevel.Warning);
	}

	public void Error(string moduleName, string message)
	{
		LogMessage(moduleName, message, LogLevel.ErrorLevel);
	}

	private string GetLevelText(LogLevel level)
	{
		return level switch
		{
			LogLevel.Info => "INFO",
			LogLevel.Warning => "WARN",
			LogLevel.ErrorLevel => "ERROR",
			LogLevel.Debug => "DEBUG",
			_ => "UNKNOWN"
		};
	}

	public void ListModules()
	{
		if (!DebugEnabled)
			return;

		GD.Print("\n=== Debug Modules ===");
		GD.Print($"Debug system: {(DebugEnabled ? "ENABLED" : "DISABLED")}");
		GD.Print($"File writing: {(DisableFileWriting ? "DISABLED" : "ENABLED")}");

		var sortedModules = registeredModules.Keys.OrderBy(x => x).ToList();

		foreach (var module in sortedModules)
		{
			string status = registeredModules[module] ? "ENABLED" : "DISABLED";
			GD.Print($"{module}: {status}");
		}
		GD.Print("====================\n");
	}

	public void SaveAllConfigurations()
	{
		if (DisableFileWriting)
		{
			GD.Print("File writing is disabled, not saving configurations.");
			return;
		}

		foreach (var kvp in registeredModules)
		{
			SaveModuleConfig(kvp.Key, kvp.Value);
		}
		GD.Print("All debug module configurations saved.");
	}

	public void ResetAllModules()
	{
		foreach (var module in registeredModules.Keys.ToList())
		{
			SetModuleEnabled(module, true);
		}
		GD.Print("All debug modules reset to enabled state.");
	}

	public void SetFileWritingEnabled(bool enabled)
	{
		DisableFileWriting = !enabled;
		// The config directory is normally created in _Ready; if the game started with
		// file writing disabled, it does not exist yet and every save would fail.
		if (enabled)
		{
			EnsureConfigDirectory();
		}
		GD.Print($"File writing {(DisableFileWriting ? "disabled" : "enabled")}");
	}

	private static void EnsureConfigDirectory()
	{
		using var dir = DirAccess.Open("user://");
		if (dir != null && !dir.DirExists(MODULE_CONFIG_PATH))
		{
			dir.MakeDir(MODULE_CONFIG_PATH);
		}
	}

	// Property for external access to registered modules (read-only)
	public IReadOnlyDictionary<string, bool> RegisteredModules => registeredModules;

	// D-82 snake_case aliases for GDScript consumers (mirrors the consuming project's §D-82 convention).
	// Pattern: a single #pragma warning disable IDE1006 block wrapping all lowercase method/property names.
#pragma warning disable IDE1006
	public void debug(string moduleName, string message) => Debug(moduleName, message);
	public void info(string moduleName, string message) => Info(moduleName, message);
	public void warning(string moduleName, string message) => Warning(moduleName, message);
	public void error(string moduleName, string message) => Error(moduleName, message);
	public void register_module(string moduleName, bool defaultEnabled = true) => RegisterModule(moduleName, defaultEnabled);
	public bool is_module_enabled(string moduleName) => IsModuleEnabled(moduleName);
	public void set_module_enabled(string moduleName, bool enabled) => SetModuleEnabled(moduleName, enabled);

	public bool debug_enabled
	{
		get => DebugEnabled;
		set => DebugEnabled = value;
	}
	public bool disable_file_writing
	{
		get => DisableFileWriting;
		set => DisableFileWriting = value;
	}
	// registered_modules: IReadOnlyDictionary is not directly Godot-accessible via Get().
	// Expose as Godot.Collections.Dictionary for GDScript .Get("registered_modules") compatibility:
	public Godot.Collections.Dictionary registered_modules
	{
		get
		{
			var d = new Godot.Collections.Dictionary();
			foreach (var kvp in RegisteredModules)
				d[kvp.Key] = kvp.Value;
			return d;
		}
	}
#pragma warning restore IDE1006
}
