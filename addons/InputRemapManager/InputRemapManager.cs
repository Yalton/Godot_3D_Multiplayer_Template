using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace InputRemapKit;

/// <summary>
/// Autoload that lets the player rebind input actions at runtime and persists the
/// overrides. Keyboard, mouse-button, and joypad (button/axis) bindings are supported.
/// Rebinding replaces only events of the same input family (keyboard/mouse vs joypad),
/// so an action can keep one binding of each kind.
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

	private Dictionary<string, InputEvent[]> _defaults = new();
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
	/// All events per action are captured so reset can restore every default.
	/// </summary>
	private void CaptureDefaults()
	{
		foreach (string action in _rebindableActions)
		{
			var events = InputMap.ActionGetEvents(action);
			if (events.Count > 0)
			{
				_defaults[action] = events.ToArray();
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
			var loaded = DeserializeEvents(saved);
			if (loaded.Count == 0)
				continue;

			// Replace only the families present in the saved data; other-family
			// defaults (e.g. a joypad binding under an old keyboard-only save)
			// are preserved.
			var kept = InputMap.ActionGetEvents(action)
				.Where(existing => !loaded.Any(ev => IsJoypadEvent(ev) == IsJoypadEvent(existing)))
				.ToArray();

			InputMap.ActionEraseEvents(action);
			foreach (var ev in kept)
				InputMap.ActionAddEvent(action, ev);
			foreach (var ev in loaded)
				InputMap.ActionAddEvent(action, ev);
			Log.Debug(MODULE_NAME, $"Loaded override: {action} = {saved}");
		}
	}

	/// <summary>
	/// Remaps an action to a new input event. Only existing events of the same
	/// input family (keyboard/mouse vs joypad) are replaced; events of the other
	/// family are preserved. Saves and emits BindingChanged.
	/// </summary>
	public void RemapAction(string action, InputEvent newEvent)
	{
		bool newIsJoypad = IsJoypadEvent(newEvent);
		var kept = InputMap.ActionGetEvents(action)
			.Where(existing => IsJoypadEvent(existing) != newIsJoypad)
			.ToArray();

		InputMap.ActionEraseEvents(action);
		foreach (var ev in kept)
			InputMap.ActionAddEvent(action, ev);
		InputMap.ActionAddEvent(action, newEvent);

		SaveBinding(action);
		EmitSignal(SignalName.BindingChanged, action);
		Log.Debug(MODULE_NAME, $"Remapped {action} to {newEvent.AsText()}");
	}

	/// <summary>
	/// Resets a single action back to its default bindings.
	/// </summary>
	public void ResetAction(string action)
	{
		if (!_defaults.ContainsKey(action))
			return;

		InputMap.ActionEraseEvents(action);
		foreach (var ev in _defaults[action])
			InputMap.ActionAddEvent(action, ev);
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
			foreach (var ev in _defaults[action])
				InputMap.ActionAddEvent(action, ev);
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
	/// Returns the first keyboard/mouse event currently bound to an action, or null.
	/// </summary>
	public InputEvent? GetCurrentEvent(string action)
	{
		foreach (var ev in InputMap.ActionGetEvents(action))
		{
			if (!IsJoypadEvent(ev))
				return ev;
		}
		return null;
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

		if (a is InputEventJoypadButton joyA && b is InputEventJoypadButton joyB)
		{
			return joyA.ButtonIndex == joyB.ButtonIndex;
		}

		if (a is InputEventJoypadMotion motionA && b is InputEventJoypadMotion motionB)
		{
			return motionA.Axis == motionB.Axis
				&& Mathf.Sign(motionA.AxisValue) == Mathf.Sign(motionB.AxisValue);
		}

		return false;
	}

	/// <summary>
	/// True for joypad events; false for keyboard/mouse events. The two families
	/// are rebound and saved independently.
	/// </summary>
	private static bool IsJoypadEvent(InputEvent ev)
	{
		return ev is InputEventJoypadButton || ev is InputEventJoypadMotion;
	}

	private void SaveBinding(string action)
	{
		_config.Load(CONFIG_PATH);
		_config.SetValue(SECTION, action, SerializeEvents(InputMap.ActionGetEvents(action)));
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

	/// <summary>
	/// Serializes all events of an action into one config value: individual event
	/// strings joined with "|". A single event produces the same string as the old
	/// one-event-per-action format.
	/// </summary>
	private string SerializeEvents(Godot.Collections.Array<InputEvent> events)
	{
		return string.Join("|", events
			.Select(SerializeEvent)
			.Where(s => !string.IsNullOrEmpty(s)));
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

		if (ev is InputEventJoypadButton joyButtonEvent)
		{
			return $"joybtn:{(int)joyButtonEvent.ButtonIndex}";
		}

		if (ev is InputEventJoypadMotion joyMotionEvent)
		{
			int sign = joyMotionEvent.AxisValue < 0 ? -1 : 1;
			return $"joyaxis:{(int)joyMotionEvent.Axis}:{sign}";
		}

		return "";
	}

	/// <summary>
	/// Parses a saved config value: "|"-separated event strings. An old-format
	/// value (a single event string) parses as a one-element list.
	/// </summary>
	private List<InputEvent> DeserializeEvents(string data)
	{
		var events = new List<InputEvent>();
		if (string.IsNullOrEmpty(data))
			return events;

		foreach (string part in data.Split('|'))
		{
			InputEvent? ev = DeserializeEvent(part);
			if (ev != null)
				events.Add(ev);
		}
		return events;
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
		else if (data.StartsWith("joybtn:"))
		{
			if (int.TryParse(data.Substring(7), out int button))
			{
				var ev = new InputEventJoypadButton();
				ev.ButtonIndex = (JoyButton)button;
				return ev;
			}
		}
		else if (data.StartsWith("joyaxis:"))
		{
			var parts = data.Substring(8).Split(':');
			if (parts.Length == 2
				&& int.TryParse(parts[0], out int axis)
				&& int.TryParse(parts[1], out int sign))
			{
				var ev = new InputEventJoypadMotion();
				ev.Axis = (JoyAxis)axis;
				ev.AxisValue = sign < 0 ? -1f : 1f;
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
