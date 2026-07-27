# SettingsManager (Godot C# addon)

A drop-in user-settings autoload for Godot 4 / C#. Applies settings to the running
engine, persists them to `user://settings.cfg`, and emits signals for the ones
other systems react to. Dependency-free (`GD.Print` logging).

Covers the settings nearly every 3D project needs:

- **Audio** — Master / Music / SFX / Ambience volumes (creates the buses if absent).
- **Video** — vsync, fullscreen mode, resolution, FOV, FPS cap.
- **Graphics** — render scale, MSAA, FXAA, TAA, shadow quality, anisotropic,
  SSAO, SSIL, volumetric fog, glow/bloom, brightness, plus Low/Medium/High presets.
- **Input** — mouse sensitivity.
- **Accessibility** — colorblind mode, HUD scale, screen-shake / motion-blur /
  damage-flash toggles, debug-info toggle.
- **Localization** — language.

## Files

| File | Role |
|------|------|
| `SettingsManager.cs` | Autoload singleton with the full Get*/Set* API + signals. |
| `SettingsManager.tscn` | Autoload scene (single Node + script). |
| `Log.cs` | Internal `GD.Print` logging shim. |

> The scene references the script at `res://addons/SettingsManager/...`; install
> the folder at exactly that path.

## Setup

1. Copy/clone this folder to `res://addons/SettingsManager/`.
2. Register the autoload (Project Settings → Autoload):
   ```
   SettingsManager = "res://addons/SettingsManager/SettingsManager.tscn"
   ```

On first run it writes defaults to `user://settings.cfg` and creates the
Music/SFX/Ambience audio buses (routed to Master). Bus indices are resolved by
name, so it works even in a project that only had a Master bus.

## Usage

```csharp
using SettingsKit;

var s = SettingsManager.Instance;

s.SetMasterVolume(80f);          // 0–100
s.SetFullscreenMode(0);          // 0=Windowed, 1=Borderless, 2=Exclusive
s.SetResolution(new Vector2I(2560, 1440));
s.ApplyGraphicsPreset(SettingsManager.GRAPHICS_PRESET_HIGH);
s.SetMouseSensitivity(40f);

s.ResetToDefaults();             // restore + persist defaults
s.SaveCurrentSettings();         // snapshot everything to disk
```

Wire your options UI to the `Set*`/`Get*` methods. React to live changes via the
signals: `MouseSensitivityChanged(float)`, `FovChanged(float)`,
`ColorblindModeChanged(int)`, `BrightnessChanged(float)`, `DamageFlashChanged(bool)`,
`HudScaleChanged(float)`, `DebugInfoChanged(bool)`, `LanguageChanged()`.

Toggles read directly (no signal): `GetScreenShakeEnabled()`,
`GetMotionBlurEnabled()`, `GetDamageFlashEnabled()`.

### Per-scene environment settings

SSAO/SSIL/fog/glow/brightness apply to the active `WorldEnvironment`. Since each
level can have its own, call `SettingsManager.Instance.ReapplyEnvironmentSettings()`
from a level's `_Ready` so user settings take effect in that scene.

## Customizing

- `GetAvailableLanguages()` returns a hardcoded locale list — edit it to match the
  translations your project ships.
- This is the **generic** slice of a larger game settings manager; game-specific
  toggles (crosshair selection, slow-mo, etc.) were intentionally left out. Add
  your own with the same `SaveSetting` + signal pattern.

## Shipping as a submodule

```bash
git rm -r addons/SettingsManager        # if it was committed inline first
git submodule add git@github.com:Yalton/SettingsManager.git addons/SettingsManager
```
