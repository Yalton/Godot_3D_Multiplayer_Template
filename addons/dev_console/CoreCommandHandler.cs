using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DevConsoleKit;

/// <summary>
/// Handles core console commands like help, clear, exit, echo, list.
/// Portable addon copy - these commands have no project dependencies and work
/// in any project out of the box.
/// </summary>
public partial class CoreCommandHandler : BaseCommandHandler
{
	public override void _Ready()
	{
		_handlerName = "CoreCommands";
		_commandCategory = "Core";
		base._Ready();
	}

	protected override void RegisterCommands()
	{
		// Basic commands everyone can use
		RegisterCommand("help", CmdHelp, "Shows commands. Usage: help [category|all]", false);
		RegisterCommand("clear", CmdClear, "Clears the console", false);
		RegisterAlias("cls", "clear");
		RegisterCommand("exit", CmdExit, "Closes the console", false);
		RegisterAlias("quit", "exit");
		RegisterCommand("echo", CmdEcho, "Prints text to console", false);
		RegisterCommand("list", CmdList, "Lists all available commands", false);

		Log.Debug(_moduleName, "Core commands registered");
	}

	/// <summary>
	/// Help command - shows available commands with descriptions
	/// Supports: help, help [category], help all
	/// </summary>
	private void CmdHelp(string[] args)
	{
		if (args.Length == 0)
		{
			// Show all commands by default
			ShowAllCommandsGrouped();
			return;
		}

		string arg = args[0].ToLower();

		if (arg == "all")
		{
			// Show all commands grouped by category
			ShowAllCommandsGrouped();
		}
		else
		{
			// Try to find matching category
			var categories = _consoleManager.GetCategories();
			string? matchedCategory = categories.FirstOrDefault(c => c.Equals(arg, StringComparison.OrdinalIgnoreCase));

			if (matchedCategory != null)
			{
				ShowCategoryCommands(matchedCategory);
			}
			else
			{
				// Check if it's a command name for specific help
				if (_consoleManager.HasCommand(arg))
				{
					var commands = _consoleManager.GetAccessibleCommands();
					if (commands.TryGetValue(arg, out string? desc))
					{
						string? category = _consoleManager.GetCommandCategory(arg);
						OutputSystem($"Command: {arg}");
						Output($"  Category: {category}");
						Output($"  {desc}");
					}
				}
				else
				{
					OutputError($"Unknown category or command: {arg}");
					OutputSystem("Available categories:");
					foreach (var cat in categories)
					{
						Output($"  {cat.ToLower()}");
					}
				}
			}
		}
	}

	/// <summary>
	/// Show summary of categories with command counts
	/// </summary>
	private void ShowCategorySummary()
	{
		OutputSystem("=== Dev Console Help ===");
		OutputSystem("");
		OutputSystem("Usage: help [category|all]");
		OutputSystem("");
		OutputSystem("Categories:");

		var grouped = _consoleManager.GetCommandsGroupedByCategory();
		foreach (var category in grouped.Keys.OrderBy(k => k))
		{
			int count = grouped[category].Count;
			Output($"  {category.ToLower()} ({count} commands)");
		}

		OutputSystem("");
		OutputSystem("Examples:");
		Output("  help all         - Show all commands");
		Output("  help core        - Show Core commands");
	}

	/// <summary>
	/// Show all commands grouped by category
	/// </summary>
	private void ShowAllCommandsGrouped()
	{
		var grouped = _consoleManager.GetCommandsGroupedByCategory();

		foreach (var category in grouped.Keys.OrderBy(k => k))
		{
			OutputSystem($"");
			OutputSystem($"### {category} ###");

			var commands = grouped[category].OrderBy(kvp => kvp.Key).ToList();
			foreach (var cmd in commands)
			{
				if (!string.IsNullOrEmpty(cmd.Value))
				{
					Output($"  {cmd.Key} - {cmd.Value}");
				}
				else
				{
					Output($"  {cmd.Key}");
				}
			}
		}
	}

	/// <summary>
	/// Show commands for a specific category
	/// </summary>
	private void ShowCategoryCommands(string category)
	{
		var commands = _consoleManager.GetCommandsByCategory(category);

		if (commands.Count == 0)
		{
			OutputWarning($"No commands in category: {category}");
			return;
		}

		OutputSystem($"### {category} Commands ###");

		var sortedCommands = commands.OrderBy(kvp => kvp.Key).ToList();
		foreach (var cmd in sortedCommands)
		{
			if (!string.IsNullOrEmpty(cmd.Value))
			{
				Output($"  {cmd.Key} - {cmd.Value}");
			}
			else
			{
				Output($"  {cmd.Key}");
			}
		}
	}

	/// <summary>
	/// Clear command - clears the console display
	/// </summary>
	private void CmdClear(string[] args)
	{
		var consoleUI = _consoleManager.GetConsoleUI();
		if (consoleUI != null)
		{
			if (consoleUI.HasMethod("clear_console"))
			{
				consoleUI.Call("clear_console");
			}
			else if (consoleUI.HasMethod("ClearConsole"))
			{
				consoleUI.Call("ClearConsole");
			}
			else
			{
				OutputWarning("Console UI doesn't have a clear method");
			}
		}
		else
		{
			OutputWarning("Console UI not found");
		}
	}

	/// <summary>
	/// Exit command - hides the console
	/// </summary>
	private void CmdExit(string[] args)
	{
		var consoleUI = _consoleManager.GetConsoleUI();
		if (consoleUI != null)
		{
			if (consoleUI.HasMethod("hide_console"))
			{
				consoleUI.Call("hide_console");
			}
			else if (consoleUI.HasMethod("HideConsole"))
			{
				consoleUI.Call("HideConsole");
			}
			else
			{
				// Fallback to just hiding the control
				consoleUI.Hide();
			}
		}
		else
		{
			OutputWarning("Console UI not found");
		}
	}

	/// <summary>
	/// Echo command - prints text back to console
	/// </summary>
	private void CmdEcho(string[] args)
	{
		if (args.Length == 0)
		{
			Output("");
		}
		else
		{
			Output(string.Join(" ", args));
		}
	}

	/// <summary>
	/// List command - shows all available commands in a compact format
	/// </summary>
	private void CmdList(string[] args)
	{
		var commands = _consoleManager.GetAccessibleCommands();
		var sortedCommands = commands.Keys.OrderBy(k => k).ToList();

		if (sortedCommands.Count == 0)
		{
			OutputSystem("No commands available");
			return;
		}

		OutputSystem($"Commands: {string.Join(", ", sortedCommands)}");
	}
}
