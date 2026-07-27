# Dev Console (Godot C# addon)

A small, dependency-free in-game developer console for Godot 4 / C#: a command
dispatcher, a base class for command handlers, a set of built-in core commands
(`help`, `clear`, `exit`, `echo`, `list`), and a console UI with scrollback and
command history.

This is the **portable core** only. Game-specific commands (spawning, cheats,
wave control, etc.) live in your own project as additional handlers — see
"Adding your own commands" below.

Everything is under the `DevConsole` namespace and has **no dependency** on any
project-specific logger or singleton (it logs via Godot's `GD.Print` and defers
command execution with `CallDeferred`).

## Files

| File | Role |
|------|------|
| `DevConsoleManager.cs` | Autoload singleton. Command registry + dispatcher. |
| `BaseCommandHandler.cs` | Base `Node` for command handlers. Registration + arg-parse helpers. |
| `CoreCommandHandler.cs` | Built-in commands: help / clear / exit / echo / list. |
| `DevConsoleUI.cs` | `Control` for the console window (input, output, history). |
| `Log.cs` | Internal `GD.Print` logging shim. |
| `DevConsoleManager.tscn` | Autoload scene: manager + `CoreCommands` child. |
| `DevConsole.tscn` | Ready-to-use console UI (theme-free, drop into a CanvasLayer). |

> The bundled scenes reference scripts at `res://addons/dev_console/...`, so the
> folder must live at exactly that path in the consuming project (clone/copy it
> to `addons/dev_console/`).

## Setup in a new project

1. Drop this folder at `res://addons/dev_console/` (clone or copy).

2. **Register the autoload** (Project Settings → Autoload):
   ```
   DevConsoleManager = "res://addons/dev_console/DevConsoleManager.tscn"
   ```

3. **Add the UI** — instance `res://addons/dev_console/DevConsole.tscn` under a
   `CanvasLayer` in your UI so it draws on top. (It ships theme-free and uses the
   default Godot theme; restyle it however you like.)

4. **Add an input action** named `toggle_dev_console` (Project Settings → Input
   Map) — bind it to the backtick `` ` `` key or whatever you prefer.

That's it. In the editor (or an export with the `enable_console_dev` feature
tag), press the toggle key and type `help`.

## Enabling / security

`DevConsoleManager.EnableConsoleAccess` defaults to **false** and self-enables
only when:
- running from the Godot editor (`OS.HasFeature("editor")`), or
- the export preset declares the custom feature tag `enable_console_dev`.

So production builds ship with the console inert unless you opt in.

## Adding your own commands

Subclass `BaseCommandHandler`, override `RegisterCommands()`, and add it as a
child node of the `DevConsoleManager` autoload scene:

```csharp
using Godot;
using DevConsole;

public partial class MyGameCommands : BaseCommandHandler
{
    public override void _Ready()
    {
        _handlerName = "MyGameCommands";
        _commandCategory = "Game";
        base._Ready();
    }

    protected override void RegisterCommands()
    {
        RegisterCommand("god", CmdGod, "Toggle invincibility", adminOnly: true);
    }

    private void CmdGod(string[] args)
    {
        // ... your game logic ...
        OutputSystem("God mode toggled.");
    }
}
```

The base class gives you `Output/OutputSystem/OutputError/OutputWarning`,
`TryParseInt/Float/Bool`, `ValidateArgCount`, and `FormatTable`. Commands run
deferred (end of frame), so touching the scene tree from a handler is safe.

## Shipping as a submodule

To split this folder into its own repo and consume it as a submodule:

```bash
# from a copy of just the addons/dev_console contents
git init && git add . && git commit -m "Dev Console addon"
git remote add origin <your-remote-url>
git push -u origin main

# in the consuming project
git rm -r addons/dev_console          # if it was committed inline first
git submodule add <your-remote-url> addons/dev_console
```

## Notes / differences from a typical in-game version

- The UI script intentionally omits `[GlobalClass]`/`[Icon]` so it never
  collides with a host project's own `DevConsoleUI`. Attach it by path.
- Show/hide is a plain `Visible` toggle. Add your own open/close animation in
  `ShowConsole()` / `HideConsole()` if desired.
