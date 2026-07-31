using Godot;
using System.Collections.Generic;
using System.Linq;

namespace DevConsoleKit;

/// <summary>
/// Console UI: input field, scrollable output, command history.
///
/// Portable addon copy. Differences from the in-game version:
///   - No [GlobalClass]/[Icon] (those collide with / point at project-specific
///     resources). Attach this script to your console scene by path instead.
///   - Logs through the standalone Log shim.
///   - Show/hide is a plain Visible toggle instead of the project's UIAnimator.
///     Swap in your own open/close animation if you want one.
/// </summary>
public partial class DevConsoleUI : Control
{
	[Export] public RichTextLabel output_label = null!;
	[Export] public LineEdit input_field = null!;
	[Export] public ScrollContainer scroll_container = null!;
	[Export] public int max_lines = 100;
	[Export] public Color system_color = Colors.Green;
	[Export] public Color error_color = Colors.Red;
	[Export] public Color warning_color = Colors.Yellow;
	[Export] public bool enable_debug = true;
	[Export] public bool enable_terminal = true;
	[Export] public float scroll_speed = 50.0f; // Pixels per scroll step

	[Signal]
	public delegate void console_openedEventHandler();
	[Signal]
	public delegate void console_closedEventHandler();

	private const string MODULE_NAME = "DevConsoleUI";

	private List<string> console_history = new List<string>();
	private List<string> command_history = new List<string>();
	private int history_index = -1;
	private bool was_mouse_captured = false;
	private DevConsoleManager _devConsoleManager = null!;
	private bool _waitingForScrollUpdate = false;

	public override void _Ready()
	{
		Log.RegisterModule(MODULE_NAME, enable_debug);

		// Initially hide
		Visible = false;

		// Connect input field signals
		if (input_field != null)
		{
			input_field.TextSubmitted += OnInputSubmitted;
			input_field.GuiInput += OnInputGuiInput;
		}

		// Clear output on start
		if (output_label != null)
		{
			output_label.Text = "";
		}

		// Get DevConsoleManager reference properly
		_devConsoleManager = DevConsoleManager.Instance;


		if (_devConsoleManager != null)
		{
			// Connect to the OutputRequested signal
			_devConsoleManager.OutputRequested += OnOutputRequested;
			_devConsoleManager.SetConsoleUI(this);
			Log.Debug(MODULE_NAME, "Connected to DevConsoleManager");
		}
		else
		{
			Log.Error(MODULE_NAME, "DevConsoleManager not found!");
		}

		// Add welcome message
		AddLine($"[color=#{system_color.ToHtml()}]Dev Console v1.0[/color]", false);
		AddLine($"[color=#{system_color.ToHtml()}]Type 'help' for available commands[/color]", false);

		Log.Debug(MODULE_NAME, "Dev Console UI initialized");
	}

	public override void _ExitTree()
	{
		// Cleanup signal connections
		if (_devConsoleManager != null)
		{
			_devConsoleManager.OutputRequested -= OnOutputRequested;
		}
		if (_waitingForScrollUpdate)
		{
			GetTree().ProcessFrame -= OnNextFrame;
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (enable_terminal && _devConsoleManager is { EnableConsoleAccess: true })
		{
			// Toggle console with the configured action
			if (@event.IsActionPressed("toggle_dev_console"))
			{
				ToggleConsole();
				GetViewport().SetInputAsHandled();
			}
		}

		// Handle mouse wheel scrolling when console is visible
		if (Visible && @event is InputEventMouseButton mouseEvent)
		{
			if (mouseEvent.ButtonIndex == MouseButton.WheelUp || mouseEvent.ButtonIndex == MouseButton.WheelDown)
			{
				HandleMouseScroll(mouseEvent);
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void HandleMouseScroll(InputEventMouseButton mouseEvent)
	{
		if (scroll_container == null)
		{
			return;
		}

		float scrollAmount = scroll_speed;

		if (mouseEvent.ButtonIndex == MouseButton.WheelUp)
		{
			scroll_container.ScrollVertical -= (int)scrollAmount;
		}
		else if (mouseEvent.ButtonIndex == MouseButton.WheelDown)
		{
			scroll_container.ScrollVertical += (int)scrollAmount;
		}

		Log.Debug(MODULE_NAME, $"Scrolled to position: {scroll_container.ScrollVertical}");
	}

	public void ToggleConsole()
	{
		if (Visible)
		{
			HideConsole();
		}
		else
		{
			ShowConsole();
		}
	}

	public void ShowConsole()
	{
		if (Visible)
		{
			return;
		}

		// Remember current mouse state
		was_mouse_captured = Input.MouseMode == Input.MouseModeEnum.Captured;

		// Show mouse cursor
		Input.MouseMode = Input.MouseModeEnum.Visible;

		// Show the console
		Visible = true;

		// Focus input field
		if (input_field != null)
		{
			input_field.GrabFocus();
			input_field.Clear();
		}

		// Ensure we're scrolled to bottom when opening
		CallDeferred(nameof(ScrollToBottom));

		EmitSignal(SignalName.console_opened);
		Log.Debug(MODULE_NAME, "Console opened");
	}

	public void HideConsole()
	{
		if (!Visible)
		{
			return;
		}

		// Hide the console
		Visible = false;

		// Clear input field
		if (input_field != null)
		{
			input_field.Clear();
		}

		// Restore mouse mode if it was captured
		if (was_mouse_captured)
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}

		EmitSignal(SignalName.console_closed);
		Log.Debug(MODULE_NAME, "Console closed");
	}

	private void OnInputSubmitted(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}

		// Add command to history
		command_history.Add(text);
		history_index = command_history.Count;

		// Display the command in console (escaped: typed input is not BBCode)
		AddLine("> " + EscapeBbCode(text));

		// Process the command using proper C# method calls
		if (_devConsoleManager != null)
		{
			// Set this console UI as active
			_devConsoleManager.SetConsoleUI(this);

			// Process the command
			_devConsoleManager.ProcessCommand(text);
		}
		else
		{
			AddErrorMessage("DevConsoleManager not available");
		}

		// Clear input
		input_field.Clear();
	}

	private void OnInputGuiInput(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			// Navigate command history with up/down arrows
			if (keyEvent.Keycode == Key.Up)
			{
				NavigateHistory(-1);
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Key.Down)
			{
				NavigateHistory(1);
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void NavigateHistory(int direction)
	{
		if (command_history.Count == 0)
		{
			return;
		}

		history_index = Mathf.Clamp(history_index + direction, 0, command_history.Count);

		if (history_index < command_history.Count)
		{
			input_field.Text = command_history[history_index];
			input_field.CaretColumn = input_field.Text.Length;
		}
		else
		{
			input_field.Clear();
		}
	}

	private void OnOutputRequested(string text, string type)
	{
		// Handle output from DevConsoleManager
		switch (type)
		{
			case "system":
				AddSystemMessage(text);
				break;
			case "error":
				AddErrorMessage(text);
				break;
			case "warning":
				AddWarningMessage(text);
				break;
			default:
				AddLine(text);
				break;
		}

		Log.Debug(MODULE_NAME, $"Output received: [{type}] {text}");
	}

	public void AddLine(string text, bool useBbcode = true)
	{
		if (output_label == null)
		{
			return;
		}

		// Add timestamp
		var time = Time.GetTimeStringFromSystem();
		var line = $"[{time}] {text}";

		console_history.Add(line);

		// Limit history size
		if (console_history.Count > max_lines)
		{
			while (console_history.Count > max_lines)
			{
				console_history.RemoveAt(0);
			}
			// Rebuild when truncated
			output_label.Text = string.Join("\n", console_history);
		}
		else
		{
			// Append new line
			if (output_label.Text.Length > 0)
				output_label.Text += "\n" + line;
			else
				output_label.Text = line;
		}

		// Auto-scroll to bottom after adding new content
		CallDeferred(nameof(ScrollToBottom));
	}

	private void ScrollToBottom()
	{
		if (scroll_container != null && !_waitingForScrollUpdate)
		{
			// Set flag to prevent multiple connections
			_waitingForScrollUpdate = true;
			// Wait one frame to ensure content is updated
			GetTree().ProcessFrame += OnNextFrame;
		}
	}

	private void OnNextFrame()
	{
		// Disconnect the signal immediately
		GetTree().ProcessFrame -= OnNextFrame;
		_waitingForScrollUpdate = false;

		if (scroll_container != null)
		{
			var scrollBar = scroll_container.GetVScrollBar();
			if (scrollBar != null)
			{
				scroll_container.ScrollVertical = (int)scrollBar.MaxValue;
				Log.Debug(MODULE_NAME, $"Scrolled to bottom: {scrollBar.MaxValue}");
			}
		}
	}

	public void AddSystemMessage(string text)
	{
		AddLine($"[color=#{system_color.ToHtml()}]{text}[/color]");
	}

	public void AddErrorMessage(string text)
	{
		AddLine($"[color=#{error_color.ToHtml()}]ERROR: {text}[/color]");
	}

	public void AddWarningMessage(string text)
	{
		AddLine($"[color=#{warning_color.ToHtml()}]WARNING: {text}[/color]");
	}

	public void ClearConsole()
	{
		console_history.Clear();
		if (output_label != null)
		{
			output_label.Text = "";
		}
		AddSystemMessage("Console cleared");
	}

	public bool IsConsoleOpen()
	{
		return Visible;
	}

	private static string EscapeBbCode(string value)
	{
		var escaped = new System.Text.StringBuilder(value.Length);
		foreach (var character in value)
		{
			if (character == '[') escaped.Append("[lb]");
			else if (character == ']') escaped.Append("[rb]");
			else escaped.Append(character);
		}
		return escaped.ToString();
	}
}
