using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace InputRemapKit;

/// <summary>
/// Autoload that lets the player rebind input actions at runtime and persists the
/// overrides. Keyboard and mouse-button bindings are supported.
///
/// Portable addon copy: namespaced, dependency-free logging, persists to its own
/// config file, and — instead of a hardcoded game action list — auto-discovers
/// rebindable actions from the project's InputMap (excluding the built-in `ui_*`
/// actions). Curate via RebindableActionsOverride / DisplayNameOverrides.
/// </summary>
public partial class InputRemapManager : Node
{
	private const string MODULE_NAME = "InputRemapManager";
	private const string CONFIG_FILE = "input_bindings.cfg";
	private static string CONFIG_PATH => "user://" + CONFIG_FILE;
	private const string SECTION = "input_bindings";

	private static InputRemapManager? _instance;
	public static InputRemapManager Instance =>
		_instance ?? throw new InvalidOperationException("InputRemapManager autoload is not ready.");

	[Signal]
	public delegate void BindingChangedEventHandler(string actionName);

	/// <summary>
	/// Optional explicit list of actions the player may rebind. When non-empty it
	/// overrides auto-discovery. Leave empty to auto-discover all non-"ui_" actions.
	/// </summary>
	[Export] public string[] RebindableActionsOverride { get; set; } = Array.Empty<string>();

	/// <summary>
	/// Optional per-action display-name overrides (action name -> label). Actions
	/// not listed here fall back to a prettified version of the action name.
	/// </summary>
	[Export] public Godot.Collections.Dictionary DisplayNameOverrides { get; set; } = new();

	private string[] _rebindableActions = Array.Empty<string>();

	/// <summary>Actions the player is allowed to rebind (resolved at _Ready).</summary>
	public string[] RebindableActions => _rebindableActions;

	private Dictionary<string, InputEvent> _defaults = new();
	private ConfigFile _config = new ConfigFile();

	public override void _Ready()
	{
		_instance = this;
		Log.RegisterModule(MODULE_NAME, false);

		ResolveRebindableActions();
		CaptureDefaults();
		LoadOverrides();

		Log.Info(MODULE_NAME, "InputRemapManager initialized");
	}

	/// <summary>
	/// Picks the rebindable action set: the explicit override if provided, else
	/// every InputMap action that doesn't start with "ui_".
	/// </summary>
	private void ResolveRebindableActions()
	{
		if (RebindableActionsOverride != null && RebindableActionsOverride.Length > 0)
		{
			_rebindableActions = RebindableActionsOverride;
		}
		else
		{
			_rebindableActions = InputMap.GetActions()
				.Select(a => a.ToString())
				.Where(a => !a.StartsWith("ui_"))
				.ToArray();
		}
		Log.Debug(MODULE_NAME, $"Rebindable actions: {_rebindableActions.Length}");
	}

	/// <summary>
	/// Human-readable label for an action: the override if present, else the
	/// action name prettified (underscores to spaces, title-cased).
	/// </summary>
	public string GetDisplayName(string action)
	{
		if (DisplayNameOverrides != null && DisplayNameOverrides.ContainsKey(action))
		{
			return DisplayNameOverrides[action].AsString();
		}
		return Prettify(action);
	}

	private static string Prettify(string action)
	{
		var words = action.Replace('_', ' ').Trim();
		if (string.IsNullOrEmpty(words))
			return action;
		return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words);
	}

	/// <summary>
	/// Snapshots the current InputMap bindings before any overrides are applied.
	/// </summary>
	private void CaptureDefaults()
	{
		foreach (string action in _rebindableActions)
		{
			var events = InputMap.ActionGetEvents(action);
			if (events.Count > 0)
			{
				_defaults[action] = events[0];
			}
		}
		Log.Debug(MODULE_NAME, $"Captured defaults for {_defaults.Count} actions");
	}

	/// <summary>
	/// Loads saved bindings from config and applies them to InputMap.
	/// </summary>
	private void LoadOverrides()
	{
		var loadErr = _config.Load(CONFIG_PATH);
		if (loadErr != Error.Ok && loadErr != Error.FileNotFound)
		{
			Log.Warning(MODULE_NAME, $"Failed to load config: {loadErr}, using defaults");
			return;
		}

		if (!_config.HasSection(SECTION))
		{
			Log.Debug(MODULE_NAME, "No saved bindings found, using defaults");
			return;
		}

		foreach (string action in _rebindableActions)
		{
			if (!_config.HasSectionKey(SECTION, action))
				continue;

			string saved = (string)_config.GetValue(SECTION, action, "");
			InputEvent? ev = DeserializeEvent(saved);
			if (ev == null)
				continue;

			InputMap.ActionEraseEvents(action);
			InputMap.ActionAddEvent(action, ev);
			Log.Debug(MODULE_NAME, $"Loaded override: {action} = {saved}");
		}
	}

	/// <summary>
	/// Remaps an action to a new input event. Saves and emits BindingChanged.
	/// </summary>
	public void RemapAction(string action, InputEvent newEvent)
	{
		InputMap.ActionEraseEvents(action);
		InputMap.ActionAddEvent(action, newEvent);
		SaveBinding(action, newEvent);
		EmitSignal(SignalName.BindingChanged, action);
		Log.Debug(MODULE_NAME, $"Remapped {action} to {newEvent.AsText()}");
	}

	/// <summary>
	/// Resets a single action back to its default binding.
	/// </summary>
	public void ResetAction(string action)
	{
		if (!_defaults.ContainsKey(action))
			return;

		InputMap.ActionEraseEvents(action);
		InputMap.ActionAddEvent(action, _defaults[action]);
		RemoveSavedBinding(action);
		EmitSignal(SignalName.BindingChanged, action);
		Log.Debug(MODULE_NAME, $"Reset {action} to default");
	}

	/// <summary>
	/// Resets all actions back to their defaults.
	/// </summary>
	public void ResetAllActions()
	{
		foreach (string action in _rebindableActions)
		{
			if (!_defaults.ContainsKey(action))
				continue;

			InputMap.ActionEraseEvents(action);
			InputMap.ActionAddEvent(action, _defaults[action]);
		}

		// Clear entire section
		_config.Load(CONFIG_PATH);
		if (_config.HasSection(SECTION))
		{
			foreach (string action in _rebindableActions)
			{
				if (_config.HasSectionKey(SECTION, action))
					_config.SetValue(SECTION, action, new Variant());
			}
			var err = _config.Save(CONFIG_PATH);
			if (err != Error.Ok)
			{
				Log.Error(MODULE_NAME, $"Failed to save after resetting bindings: {err}");
			}
		}

		foreach (string action in _rebindableActions)
		{
			EmitSignal(SignalName.BindingChanged, action);
		}

		Log.Debug(MODULE_NAME, "Reset all actions to defaults");
	}

	/// <summary>
	/// Returns the first input event currently bound to an action.
	/// </summary>
	public InputEvent? GetCurrentEvent(string action)
	{
		var events = InputMap.ActionGetEvents(action);
		return events.Count > 0 ? events[0] : null;
	}

	/// <summary>
	/// Checks if the given event is already bound to another rebindable action.
	/// Returns the conflicting action name or null.
	/// </summary>
	public string? GetConflictingAction(InputEvent ev, string excludeAction)
	{
		foreach (string action in _rebindableActions)
		{
			if (action == excludeAction)
				continue;

			var events = InputMap.ActionGetEvents(action);
			foreach (var existing in events)
			{
				if (EventsMatch(ev, existing))
					return action;
			}
		}
		return null;
	}

	private bool EventsMatch(InputEvent a, InputEvent b)
	{
		if (a is InputEventKey keyA && b is InputEventKey keyB)
		{
			Key codeA = keyA.PhysicalKeycode != Key.None ? keyA.PhysicalKeycode : keyA.Keycode;
			Key codeB = keyB.PhysicalKeycode != Key.None ? keyB.PhysicalKeycode : keyB.Keycode;
			return codeA == codeB;
		}

		if (a is InputEventMouseButton mouseA && b is InputEventMouseButton mouseB)
		{
			return mouseA.ButtonIndex == mouseB.ButtonIndex;
		}

		return false;
	}

	private void SaveBinding(string action, InputEvent ev)
	{
		_config.Load(CONFIG_PATH);
		_config.SetValue(SECTION, action, SerializeEvent(ev));
		var err = _config.Save(CONFIG_PATH);
		if (err != Error.Ok)
		{
			Log.Error(MODULE_NAME, $"Failed to save binding for {action}: {err}");
		}
	}

	private void RemoveSavedBinding(string action)
	{
		_config.Load(CONFIG_PATH);
		if (_config.HasSectionKey(SECTION, action))
		{
			_config.SetValue(SECTION, action, new Variant());
			var err = _config.Save(CONFIG_PATH);
			if (err != Error.Ok)
			{
				Log.Error(MODULE_NAME, $"Failed to save after removing binding for {action}: {err}");
			}
		}
	}

	private string SerializeEvent(InputEvent ev)
	{
		if (ev is InputEventKey keyEvent)
		{
			Key code = keyEvent.PhysicalKeycode != Key.None ? keyEvent.PhysicalKeycode : keyEvent.Keycode;
			return $"key:{(long)code}";
		}

		if (ev is InputEventMouseButton mouseEvent)
		{
			return $"mouse:{(int)mouseEvent.ButtonIndex}";
		}

		return "";
	}

	private InputEvent? DeserializeEvent(string data)
	{
		if (string.IsNullOrEmpty(data))
			return null;

		if (data.StartsWith("key:"))
		{
			if (long.TryParse(data.Substring(4), out long keycode))
			{
				var ev = new InputEventKey();
				ev.PhysicalKeycode = (Key)keycode;
				return ev;
			}
		}
		else if (data.StartsWith("mouse:"))
		{
			if (int.TryParse(data.Substring(6), out int button))
			{
				var ev = new InputEventMouseButton();
				ev.ButtonIndex = (MouseButton)button;
				return ev;
			}
		}

		return null;
	}

	public override void _ExitTree()
	{
		if (_instance == this)
			_instance = null;
	}
}
