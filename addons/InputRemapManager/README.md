# InputRemapManager (Godot C# addon)

Runtime input rebinding for Godot 4 / C#: rebind keyboard and mouse-button
actions, detect conflicts, reset to defaults, and persist overrides — all on top
of Godot's built-in `InputMap`. Dependency-free (`GD.Print` logging, own config
file).

## Files

| File | Role |
|------|------|
| `InputRemapManager.cs` | Autoload singleton with the rebinding API. |
| `InputRemapManager.tscn` | Autoload scene (single Node + script). |
| `Log.cs` | Internal `GD.Print` logging shim. |

> The scene references the script at `res://addons/InputRemapManager/...`; install
> the folder at exactly that path.

## Setup

1. Copy/clone this folder to `res://addons/InputRemapManager/`.
2. Register the autoload (Project Settings → Autoload):
   ```
   InputRemapManager = "res://addons/InputRemapManager/InputRemapManager.tscn"
   ```

That's it — it works out of the box. On startup it snapshots your current
`InputMap` as the defaults and applies any saved overrides from
`user://input_bindings.cfg`.

## Which actions are rebindable?

By default it **auto-discovers** every action in your project's Input Map, minus
the built-in `ui_*` actions. To curate the list, set either export on the autoload
node (in the scene, or from a subclass):

- `RebindableActionsOverride : string[]` — explicit list; overrides auto-discovery.
- `DisplayNameOverrides : Dictionary` — per-action labels (`"action" -> "Label"`).
  Actions not listed get a prettified name (`move_left` → `Move Left`).

## API

```csharp
using InputRemapKit;

var mgr = InputRemapManager.Instance;

foreach (string action in mgr.RebindableActions)
    GD.Print($"{mgr.GetDisplayName(action)} = {mgr.GetCurrentEvent(action)?.AsText()}");

// Apply a new binding (from a captured InputEvent in your rebind UI)
string conflict = mgr.GetConflictingAction(newEvent, excludeAction: action);
if (conflict == null)
    mgr.RemapAction(action, newEvent);   // saves + emits BindingChanged(action)

mgr.ResetAction("jump");
mgr.ResetAllActions();
```

`BindingChanged(string actionName)` fires whenever a binding changes — connect to
it to refresh your controls UI and any on-screen key hints.

## Notes / scope

- Persists keyboard (`InputEventKey`, physical keycode preferred) and mouse-button
  (`InputEventMouseButton`) events. Joypad events aren't serialized (extend
  `SerializeEvent`/`DeserializeEvent` if you need them).
- No `[GlobalClass]`, so no host-project type collisions.

## Shipping as a submodule

```bash
git rm -r addons/InputRemapManager      # if it was committed inline first
git submodule add git@github.com:Yalton/InputRemapManager.git addons/InputRemapManager
```
