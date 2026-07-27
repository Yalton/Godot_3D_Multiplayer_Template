using Godot;

namespace InputRemapKit;

/// <summary>
/// Minimal standalone logging shim — routes through Godot's print/push APIs so
/// the addon has no external logger dependency.
/// </summary>
internal static class Log
{
	public static void RegisterModule(string module, bool enabled = true) { }

	public static void Debug(string module, string message) => GD.Print($"[{module}] {message}");
	public static void Info(string module, string message) => GD.Print($"[{module}] {message}");
	public static void Warning(string module, string message) => GD.PushWarning($"[{module}] {message}");
	public static void Error(string module, string message) => GD.PushError($"[{module}] {message}");
}
