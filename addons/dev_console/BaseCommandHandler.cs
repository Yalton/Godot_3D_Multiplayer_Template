using Godot;
using System;

namespace DevConsoleKit;

/// <summary>
/// Base class for command handlers that register with DevConsoleManager.
/// Handles common functionality like logging, registration and arg parsing.
///
/// Portable addon copy. The in-game version also carried a GetSpawnPosition()
/// helper coupled to the game's Player/GameManager; that region was removed here
/// to keep the addon project-agnostic. Subclass this in your own project and add
/// game-specific helpers there.
/// </summary>
public abstract partial class BaseCommandHandler : Node
{

	/// <summary>
	/// Name of this command handler module
	/// </summary>
	[Export] protected string _handlerName = "BaseCommandHandler";

	/// <summary>
	/// Category name for commands registered by this handler
	/// </summary>
	[Export] protected string _commandCategory = "General";

	/// <summary>
	/// Whether to enable debug logging for this handler
	/// </summary>
	[Export] protected bool _enableDebug = true;

	protected string _moduleName = null!;
	protected DevConsoleManager _consoleManager = null!;
	private readonly System.Collections.Generic.List<string> _registeredCommands = new();

	public override void _Ready()
	{
		_moduleName = _handlerName;

		Log.RegisterModule(_moduleName, _enableDebug);

		// Handlers may live anywhere in the tree; being parented under the manager is
		// merely a convenient default, not a requirement.
		_consoleManager = GetParentOrNull<DevConsoleManager>() ?? DevConsoleManager.Instance;
		if (_consoleManager == null)
		{
			Log.Error(_moduleName, "DevConsoleManager not found; commands will not be registered");
			return;
		}

		// Register our commands with the main console manager
		RegisterCommands();

		Log.Info(_moduleName, "Command handler initialized");
	}

	public override void _ExitTree()
	{
		// Unregister so the manager (an autoload that outlives scene-local handlers)
		// holds no delegates targeting this freed node.
		UnregisterCommands();
		Log.Debug(_moduleName, "Command handler cleaned up");
	}

	/// <summary>
	/// Override this in child classes to register specific commands
	/// </summary>
	protected virtual void RegisterCommands()
	{
		Log.Warning(_moduleName, "No commands registered - override RegisterCommands() in child class");
	}

	/// <summary>
	/// Removes every command this handler registered. Override only when a handler
	/// needs additional cleanup beyond its own registrations.
	/// </summary>
	protected virtual void UnregisterCommands()
	{
		if (_consoleManager == null || !IsInstanceValid(_consoleManager)) return;
		foreach (var name in _registeredCommands)
		{
			_consoleManager.UnregisterCommand(name);
		}
		_registeredCommands.Clear();
	}

	/// <summary>
	/// Helper function to register a command with the main console manager
	/// </summary>
	protected void RegisterCommand(string name, Action<string[]> method, string description = "", bool adminOnly = false)
	{
		if (_consoleManager != null)
		{
			_consoleManager.RegisterCommand(name, method, description, adminOnly, _commandCategory);
			_registeredCommands.Add(name);
			Log.Debug(_moduleName, $"Registered command: {name}");
		}
		else
		{
			Log.Error(_moduleName, "DevConsoleManager not found, cannot register command");
		}
	}

	/// <summary>
	/// Helper function to register an alias
	/// </summary>
	protected void RegisterAlias(string alias, string command)
	{
		if (_consoleManager != null)
		{
			_consoleManager.RegisterAlias(alias, command);
			Log.Debug(_moduleName, $"Registered alias: {alias} -> {command}");
		}
		else
		{
			Log.Error(_moduleName, "DevConsoleManager not found, cannot register alias");
		}
	}

	/// <summary>
	/// Helper function to unregister a command
	/// </summary>
	protected void UnregisterCommand(string name)
	{
		if (_consoleManager != null)
		{
			_consoleManager.UnregisterCommand(name);
			Log.Debug(_moduleName, $"Unregistered command: {name}");
		}
	}

	#region Output Helper Methods

	/// <summary>
	/// Output normal text
	/// </summary>
	protected void Output(string text)
	{
		_consoleManager?.Output(text);
	}

	/// <summary>
	/// Output system message
	/// </summary>
	protected void OutputSystem(string text)
	{
		_consoleManager?.OutputSystem(text);
	}

	/// <summary>
	/// Output error message
	/// </summary>
	protected void OutputError(string text)
	{
		_consoleManager?.OutputError(text);
	}

	/// <summary>
	/// Output warning message
	/// </summary>
	protected void OutputWarning(string text)
	{
		_consoleManager?.OutputWarning(text);
	}

	#endregion

	#region Utility Methods

	/// <summary>
	/// Parse an integer argument safely
	/// </summary>
	protected bool TryParseInt(string arg, out int value, string paramName = "value")
	{
		if (int.TryParse(arg, out value))
		{
			return true;
		}

		OutputError($"Invalid {paramName}: '{arg}' is not a valid integer");
		value = 0;
		return false;
	}

	/// <summary>
	/// Parse a float argument safely
	/// </summary>
	protected bool TryParseFloat(string arg, out float value, string paramName = "value")
	{
		if (float.TryParse(arg, out value))
		{
			return true;
		}

		OutputError($"Invalid {paramName}: '{arg}' is not a valid number");
		value = 0f;
		return false;
	}

	/// <summary>
	/// Parse a boolean argument safely
	/// </summary>
	protected bool TryParseBool(string arg, out bool value, string paramName = "value")
	{
		arg = arg.ToLower();
		if (arg == "true" || arg == "1" || arg == "yes" || arg == "on")
		{
			value = true;
			return true;
		}
		else if (arg == "false" || arg == "0" || arg == "no" || arg == "off")
		{
			value = false;
			return true;
		}

		OutputError($"Invalid {paramName}: '{arg}' is not a valid boolean (use true/false, 1/0, yes/no, on/off)");
		value = false;
		return false;
	}

	/// <summary>
	/// Validate argument count
	/// </summary>
	protected bool ValidateArgCount(string[] args, int expectedCount, string usage = "")
	{
		if (args.Length == expectedCount)
			return true;

		OutputError($"Invalid number of arguments. Expected {expectedCount}, got {args.Length}");
		if (!string.IsNullOrEmpty(usage))
		{
			OutputSystem($"Usage: {usage}");
		}
		return false;
	}

	/// <summary>
	/// Validate minimum argument count
	/// </summary>
	protected bool ValidateMinArgCount(string[] args, int minCount, string usage = "")
	{
		if (args.Length >= minCount)
			return true;

		OutputError($"Not enough arguments. Expected at least {minCount}, got {args.Length}");
		if (!string.IsNullOrEmpty(usage))
		{
			OutputSystem($"Usage: {usage}");
		}
		return false;
	}

	/// <summary>
	/// Format a table for console output
	/// </summary>
	protected string FormatTable(string[] headers, string[][] rows)
	{
		// Calculate column widths
		int[] columnWidths = new int[headers.Length];
		for (int i = 0; i < headers.Length; i++)
		{
			columnWidths[i] = headers[i].Length;
		}

		foreach (var row in rows)
		{
			for (int i = 0; i < Math.Min(row.Length, headers.Length); i++)
			{
				columnWidths[i] = Math.Max(columnWidths[i], row[i]?.Length ?? 0);
			}
		}

		// Build table
		var result = new System.Text.StringBuilder();

		// Headers
		for (int i = 0; i < headers.Length; i++)
		{
			result.Append(headers[i].PadRight(columnWidths[i] + 2));
		}
		result.AppendLine();

		// Separator
		for (int i = 0; i < headers.Length; i++)
		{
			result.Append(new string('-', columnWidths[i] + 2));
		}
		result.AppendLine();

		// Rows
		foreach (var row in rows)
		{
			for (int i = 0; i < Math.Min(row.Length, headers.Length); i++)
			{
				result.Append((row[i] ?? "").PadRight(columnWidths[i] + 2));
			}
			result.AppendLine();
		}

		return result.ToString();
	}

	#endregion
}
