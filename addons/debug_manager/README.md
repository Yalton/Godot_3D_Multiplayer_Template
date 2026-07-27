# Debug Manager / DebugLogger (Godot C# addon)

A lightweight, per-module debug logging system for Godot 4 / C#. Code registers
named "modules" and logs through them; each module can be toggled on/off
independently, and toggles persist to `user://debug_modules/` (hot-reloaded once
a second in non-dev builds). Ships with an editor dock for flipping modules
without running the game.

## Files

| File | Role |
|------|------|
| `DebugLogger.cs` | Autoload singleton. The logging API. |
| `DebugLogger.tscn` | Scene wrapper for the autoload. |
| `ModuleDebugConfig.cs` | `Resource` storing one module's persisted state. |
| `DebugManagerPlugin.cs` | `[Tool]` editor plugin that mounts the dock. |
| `DebugManagerUI.cs` / `debug_manager_ui.tscn` | The editor dock UI. |

## Setup in a new project

1. Drop this folder at `res://addons/debug_manager/`.
2. Register the autoload (Project Settings → Autoload):
   ```
   DebugLogger = "res://addons/debug_manager/DebugLogger.tscn"
   ```
3. (Optional) Enable the **Debug Manager** plugin in Project Settings → Plugins
   to get the editor dock.

## Usage

In a node's `_Ready()`:
```csharp
private const string MODULE_NAME = "MySystem";

public override void _Ready()
{
    DebugLogger.Instance.RegisterModule(MODULE_NAME, defaultEnabled: false);
}
```
Then anywhere:
```csharp
DebugLogger.Instance.Debug(MODULE_NAME, "something happened");
DebugLogger.Instance.Warning(MODULE_NAME, "careful");
DebugLogger.Instance.Error(MODULE_NAME, "broke");
```
In dev builds (`OS.HasFeature("dev")`) all modules are force-enabled.

## ⚠️ Porting note: DebugDraw3D dependency

`DebugLogger.cs` syncs an external addon on one line:
```csharp
DebugDraw3D.DebugEnabled = DebugEnabled;   // ~line 46
```
`DebugDraw3D` is a separate GDExtension addon. If your target project does **not**
have it installed, this line will fail to compile. To port into such a project,
**delete that single line** (the logger is otherwise fully self-contained).

Optional zero-edit alternative: guard it with a compile symbol —
`#if DEBUGDRAW3D` / `#endif` around the line — and define `DEBUGDRAW3D` in the
`.csproj` of any project that actually has the addon. Left unguarded here to keep
the live file untouched.

## Shipping as a submodule

```bash
# from a copy of just the addons/debug_manager contents
git init && git add . && git commit -m "Debug Manager addon"
git remote add origin <your-remote-url>
git push -u origin main

# in the consuming project
git rm -r addons/debug_manager        # if it was committed inline first
git submodule add <your-remote-url> addons/debug_manager
```
