using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DevConsoleKit;

/// <summary>
/// Singleton that manages dev console commands and processing with permission and log system.
///
/// Portable addon copy of the project's DevConsoleManager. Differences from the
/// in-game version:
///   - Lives in the DevConsole namespace so it never collides with a project's
///     own console types.
///   - Logs through the standalone Log shim instead of a project DebugLogger.
///   - Defers command execution via Callable.CallDeferred() instead of a
///     project-specific physics-deferred queue.
/// The public API (methods + signals) is identical, so command handlers port 1:1.
/// </summary>
public partial class DevConsoleManager : Node
{
	private const string MODULE_NAME = "DevConsoleManager";

	// Singleton instance
	private static DevConsoleManager _instance = null!;
	public static DevConsoleManager Instance => _instance;

	[Signal]
	public delegate void CommandProcessedEventHandler(string command, string[] args);

	[Signal]
	public delegate void OutputRequestedEventHandler(string text, string type);

	[Signal]
	public delegate void RebootedEventHandler();

	[Signal]
	public delegate void DiagRunEventHandler();

	// Default-deny in exported builds. _Ready re-enables when running from the
	// Godot editor (OS.HasFeature("editor")) or when the export preset opts in
	// with the "enable_console_dev" custom feature tag.
	[Export] public bool EnableConsoleAccess { get; set; } = false;

	// Command storage
	private Dictionary<string, CommandData> _commands = new Dictionary<string, CommandData>();
	private Dictionary<string, string> _commandAliases = new Dictionary<string, string>();

	// References
	private Control _devConsoleUI = null!;
	private bool _isAdmin = false;

	// Command data structure
	private class CommandData
	{
		public Action<string[]> Method { get; set; } = null!;
		public string Description { get; set; } = null!;
		public bool AdminOnly { get; set; }
		public string Category { get; set; } = null!;
	}

	public override void _Ready()
	{
		_instance = this;

		// Enable console only when running from the Godot editor or when the
		// export preset explicitly opts in via the "enable_console_dev" feature tag.
		// Public / production exports default to disabled. Dev contexts also start
		// with admin so AdminOnly commands work while iterating; exports must grant
		// it explicitly through SetAdminStatus.
		if (OS.HasFeature("editor") || OS.HasFeature("enable_console_dev"))
		{
			EnableConsoleAccess = true;
			_isAdmin = true;
		}

		Log.RegisterModule(MODULE_NAME, true);

		Log.Debug(MODULE_NAME, "DevConsoleManager initialized");
	}

	public override void _ExitTree()
	{
		if (_instance == this)
		{
			_instance = null!;
		}

		Log.Debug(MODULE_NAME, "DevConsoleManager cleaned up");
	}

	/// <summary>
	/// Set the console UI reference. Admin status is controlled exclusively through
	/// SetAdminStatus — the UI must never be able to grant itself permissions.
	/// </summary>
	public void SetConsoleUI(Control consoleUI)
	{
		_devConsoleUI = consoleUI;
		Log.Debug(MODULE_NAME, "Console UI reference set");
	}

	/// <summary>
	/// Register a command with the console system
	/// </summary>
	public void RegisterCommand(string name, Action<string[]> method, string description = "", bool adminOnly = false, string category = "General")
	{
		string lowerName = name.ToLower();
		_commands[lowerName] = new CommandData
		{
			Method = method,
			Description = description,
			AdminOnly = adminOnly,
			Category = category
		};

		Log.Debug(MODULE_NAME, $"Registered command: {name} (category: {category}, admin_only: {adminOnly})");
	}

	/// <summary>
	/// Register an alias for a command
	/// </summary>
	public void RegisterAlias(string alias, string command)
	{
		_commandAliases[alias.ToLower()] = command.ToLower();
		Log.Debug(MODULE_NAME, $"Registered alias: {alias} -> {command}");
	}

	/// <summary>
	/// Unregister a command and its aliases
	/// </summary>
	public void UnregisterCommand(string name)
	{
		string lowerName = name.ToLower();
		_commands.Remove(lowerName);

		// Remove any aliases pointing to this command
		var aliasesToRemove = _commandAliases
			.Where(kvp => kvp.Value == lowerName)
			.Select(kvp => kvp.Key)
			.ToList();

		foreach (var alias in aliasesToRemove)
		{
			_commandAliases.Remove(alias);
		}

		Log.Debug(MODULE_NAME, $"Unregistered command: {name}");
	}

	/// <summary>
	/// Process a command input string
	/// </summary>
	public void ProcessCommand(string input)
	{
		// Defense-in-depth: refuse all commands when console is disabled
		if (!EnableConsoleAccess)
		{
			return;
		}

		var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0)
			return;

		string command = parts[0].ToLower();
		string[] args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

		// Check for alias
		if (_commandAliases.TryGetValue(command, out string? actualCommand))
		{
			command = actualCommand;
		}

		// Execute command
		if (_commands.TryGetValue(command, out CommandData? cmdData))
		{
			// Check permissions
			if (cmdData.AdminOnly && !_isAdmin)
			{
				OutputError("Permission denied. Admin access required.");
				return;
			}

			Log.Debug(MODULE_NAME, $"Executing command: {command} with args: [{string.Join(", ", args)}]");

			// Defer the command handler to the end of the current frame so any
			// handler that touches the scene tree / physics runs in a safe
			// context. One frame of latency is imperceptible for console
			// commands. (The in-game version routes this through a physics
			// deferred queue; the standalone addon uses CallDeferred.)
			var capturedCommand = command;
			var capturedArgs = args;
			var capturedMethod = cmdData.Method;
			Callable.From(() =>
			{
				try
				{
					capturedMethod.Invoke(capturedArgs);
					EmitSignal(SignalName.CommandProcessed, capturedCommand, capturedArgs);
				}
				catch (Exception ex)
				{
					OutputError($"Error executing command: {ex.Message}");
					Log.Error(MODULE_NAME, $"Command execution error: {ex}");
				}
			}).CallDeferred();
		}
		else
		{
			OutputError($"Unknown command: {command}");
			var suggestions = ClosestMatches(command, _commands.Keys.Concat(_commandAliases.Keys));
			if (suggestions.Length > 0)
			{
				OutputSystem("Did you mean: " + string.Join(", ", suggestions) + "?");
			}
			OutputSystem("Type 'help' for available commands");
		}
	}

	/// <summary>
	/// Rank candidates by similarity to the input and return the closest few — used for
	/// "did you mean" assistance on unknown commands (and by game handlers for unknown
	/// argument ids, e.g. perk ids). A candidate qualifies if it contains the input as a
	/// substring or sits within a length-scaled Levenshtein distance.
	/// </summary>
	public static string[] ClosestMatches(string input, IEnumerable<string> candidates, int max = 3)
	{
		string needle = input.ToLowerInvariant();
		int threshold = Math.Max(2, needle.Length / 3);

		return candidates
			.Distinct()
			.Select(c =>
			{
				string cand = c.ToLowerInvariant();
				// Substring hits rank ahead of pure edit-distance hits (distance 0/1 bucket).
				int distance = cand.Contains(needle) || needle.Contains(cand)
					? 1
					: LevenshteinDistance(needle, cand);
				return (Name: c, Distance: distance);
			})
			.Where(x => x.Distance <= threshold)
			.OrderBy(x => x.Distance)
			.ThenBy(x => x.Name)
			.Take(max)
			.Select(x => x.Name)
			.ToArray();
	}

	private static int LevenshteinDistance(string a, string b)
	{
		if (a.Length == 0) return b.Length;
		if (b.Length == 0) return a.Length;

		// Single-row rolling DP — candidate lists are small (dozens), so O(len_a*len_b) is fine.
		var row = new int[b.Length + 1];
		for (int j = 0; j <= b.Length; j++) row[j] = j;

		for (int i = 1; i <= a.Length; i++)
		{
			int diagonal = row[0];
			row[0] = i;
			for (int j = 1; j <= b.Length; j++)
			{
				int previous = row[j];
				int cost = a[i - 1] == b[j - 1] ? 0 : 1;
				row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + cost);
				diagonal = previous;
			}
		}
		return row[b.Length];
	}

	/// <summary>
	/// Output normal text
	/// </summary>
	public void Output(string text)
	{
		EmitSignal(SignalName.OutputRequested, text, "normal");
	}

	/// <summary>
	/// Output system message
	/// </summary>
	public void OutputSystem(string text)
	{
		EmitSignal(SignalName.OutputRequested, text, "system");
	}

	/// <summary>
	/// Output error message
	/// </summary>
	public void OutputError(string text)
	{
		EmitSignal(SignalName.OutputRequested, text, "error");
	}

	/// <summary>
	/// Output warning message
	/// </summary>
	public void OutputWarning(string text)
	{
		EmitSignal(SignalName.OutputRequested, text, "warning");
	}

	/// <summary>
	/// Get all registered commands
	/// </summary>
	public Dictionary<string, string> GetCommands()
	{
		var result = new Dictionary<string, string>();
		foreach (var kvp in _commands)
		{
			result[kvp.Key] = kvp.Value.Description;
		}
		return result;
	}

	/// <summary>
	/// Get all commands accessible to current user
	/// </summary>
	public Dictionary<string, string> GetAccessibleCommands()
	{
		var result = new Dictionary<string, string>();
		foreach (var kvp in _commands)
		{
			if (!kvp.Value.AdminOnly || _isAdmin)
			{
				result[kvp.Key] = kvp.Value.Description;
			}
		}
		return result;
	}

	/// <summary>
	/// Get all available categories
	/// </summary>
	public string[] GetCategories()
	{
		var categories = new HashSet<string>();
		foreach (var kvp in _commands)
		{
			if (!kvp.Value.AdminOnly || _isAdmin)
			{
				categories.Add(kvp.Value.Category);
			}
		}
		return categories.OrderBy(c => c).ToArray();
	}

	/// <summary>
	/// Get commands by category (accessible to current user)
	/// </summary>
	public Dictionary<string, string> GetCommandsByCategory(string category)
	{
		var result = new Dictionary<string, string>();
		foreach (var kvp in _commands)
		{
			if ((!kvp.Value.AdminOnly || _isAdmin) &&
				kvp.Value.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
			{
				result[kvp.Key] = kvp.Value.Description;
			}
		}
		return result;
	}

	/// <summary>
	/// Get all accessible commands grouped by category
	/// </summary>
	public Dictionary<string, Dictionary<string, string>> GetCommandsGroupedByCategory()
	{
		var result = new Dictionary<string, Dictionary<string, string>>();

		foreach (var kvp in _commands)
		{
			if (!kvp.Value.AdminOnly || _isAdmin)
			{
				string category = kvp.Value.Category;
				if (!result.ContainsKey(category))
				{
					result[category] = new Dictionary<string, string>();
				}
				result[category][kvp.Key] = kvp.Value.Description;
			}
		}

		return result;
	}

	/// <summary>
	/// Get the category of a command
	/// </summary>
	public string? GetCommandCategory(string command)
	{
		if (_commands.TryGetValue(command.ToLower(), out var data))
		{
			return data.Category;
		}
		return null;
	}

	/// <summary>
	/// Check if a command exists
	/// </summary>
	public bool HasCommand(string command)
	{
		return _commands.ContainsKey(command.ToLower());
	}

	/// <summary>
	/// Check if current user is admin
	/// </summary>
	public bool IsAdmin()
	{
		return _isAdmin;
	}

	/// <summary>
	/// Set admin status
	/// </summary>
	public void SetAdminStatus(bool isAdmin)
	{
		_isAdmin = isAdmin;
		Log.Info(MODULE_NAME, $"Admin status changed to: {isAdmin}");
	}

	/// <summary>
	/// Emit the rebooted signal
	/// </summary>
	public void EmitRebooted()
	{
		EmitSignal(SignalName.Rebooted);
	}

	/// <summary>
	/// Emit the diag run signal
	/// </summary>
	public void EmitDiagRun()
	{
		EmitSignal(SignalName.DiagRun);
	}

	/// <summary>
	/// Get all command aliases
	/// </summary>
	public Dictionary<string, string> GetAliases()
	{
		return new Dictionary<string, string>(_commandAliases);
	}

	/// <summary>
	/// Get the console UI reference, or null when no valid UI is registered
	/// (e.g. its scene was unloaded without a replacement).
	/// </summary>
	public Control? GetConsoleUI()
	{
		return _devConsoleUI != null && IsInstanceValid(_devConsoleUI) ? _devConsoleUI : null;
	}
}
