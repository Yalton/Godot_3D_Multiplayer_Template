using Godot;

namespace DevConsoleKit;

/// <summary>
/// Minimal standalone logging shim for the Dev Console addon.
/// Mirrors the method signatures of the project's DebugLogger so the addon's
/// source is a near-verbatim copy, but routes everything through Godot's own
/// print/push APIs. This keeps the addon dependency-free: drop it into any
/// Godot C# project without a separate logger present.
/// </summary>
internal static class Log
{
	private static readonly System.Collections.Generic.Dictionary<string, bool> _modules = new();

	// Honors the enabled flag for Debug chatter; Info/Warning/Error always print.
	public static void RegisterModule(string module, bool enabled = true) => _modules[module] = enabled;

	public static void Debug(string module, string message)
	{
		if (_modules.GetValueOrDefault(module, true)) GD.Print($"[{module}] {message}");
	}

	public static void Info(string module, string message) => GD.Print($"[{module}] {message}");
	public static void Warning(string module, string message) => GD.PushWarning($"[{module}] {message}");
	public static void Error(string module, string message) => GD.PushError($"[{module}] {message}");
}
