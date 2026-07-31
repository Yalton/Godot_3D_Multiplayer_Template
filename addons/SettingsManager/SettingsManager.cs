using Godot;
using System;

namespace SettingsKit;

/// <summary>
/// Autoload that owns the game's user settings: audio volumes, video/display,
/// advanced graphics, input sensitivity, accessibility, and localization. Applies
/// them to the running engine, persists them to a ConfigFile, and emits signals
/// for the settings other systems react to.
///
/// Portable addon copy: namespaced, dependency-free logging, persists to
/// user://settings.cfg, and resolves audio-bus indices by name (so it works in a
/// fresh project that only has a Master bus). Game-specific toggles (crosshair,
/// slow-mo, resource orbs, first-play tracking) were stripped; wire any of those
/// in your own game code.
/// </summary>
public partial class SettingsManager : Node
{
	private const string MODULE_NAME = "SettingsManager";

	// Audio bus indices, resolved by name at startup (see EnsureAudioBuses).
	private int _masterBus = 0;
	private int _musicBus = -1;
	private int _sfxBus = -1;
	private int _ambienceBus = -1;

	// Default settings
	private const float DEFAULT_MASTER_VOLUME = 100.0f;
	private const float DEFAULT_MUSIC_VOLUME = 100.0f;
	private const float DEFAULT_SFX_VOLUME = 100.0f;
	private const float DEFAULT_AMBIENCE_VOLUME = 100.0f;
	private const float DEFAULT_MOUSE_SENSITIVITY = 37.5f;
	private const float DEFAULT_FOV = 80.0f;

	// Video defaults
	private const bool DEFAULT_VSYNC = true;
	private const int DEFAULT_FULLSCREEN_MODE = 0; // Windowed
	private static readonly Vector2I DEFAULT_RESOLUTION = new Vector2I(1280, 720);

	// Accessibility defaults (motion sickness)
	private const bool DEFAULT_SCREEN_SHAKE_ENABLED = true;
	private const bool DEFAULT_MOTION_BLUR_ENABLED = false;

	// Language default
	private const string DEFAULT_LANGUAGE = "en";

	// Accessibility defaults
	private const int DEFAULT_COLORBLIND_MODE = 0; // None

	// Advanced graphics defaults
	private const int DEFAULT_FPS_CAP = 120;
	private const float DEFAULT_RENDER_SCALE = 1.0f;
	private const int DEFAULT_MSAA_3D = 0;             // 0=Off,1=2x,2=4x,3=8x
	private const int DEFAULT_SCREEN_SPACE_AA = 0;     // 0=Off,1=FXAA
	private const bool DEFAULT_USE_TAA = false;
	private const int DEFAULT_SHADOW_QUALITY = 2;      // 0=Off,1=Low,2=Medium,3=High
	private const int DEFAULT_SSAO_QUALITY = 0;        // 0=Off,1=Low,2=Medium,3=High
	private const int DEFAULT_SSIL_QUALITY = 0;        // 0=Off,1=Low,2=Medium,3=High
	private const bool DEFAULT_VOLUMETRIC_FOG_ENABLED = false;
	private const bool DEFAULT_GLOW_ENABLED = true;
	private const int DEFAULT_ANISOTROPIC_LEVEL = 2;   // 0=Off,1=2x,2=4x,3=8x,4=16x
	private const float DEFAULT_BRIGHTNESS = 1.0f;

	// Extra accessibility feature defaults
	private const bool DEFAULT_DAMAGE_FLASH_ENABLED = true;
	private const float DEFAULT_HUD_SCALE = 1.0f;
	private const bool DEFAULT_SHOW_DEBUG_INFO = false;

	// Available resolutions
	private readonly Vector2I[] resolutions = new Vector2I[]
	{
		new Vector2I(1280, 720),
		new Vector2I(1366, 768),
		new Vector2I(1600, 900),
		new Vector2I(1920, 1080),
		new Vector2I(2560, 1440),
		new Vector2I(3840, 2160)
	};

	private static SettingsManager? _instance;
	public static SettingsManager Instance =>
		_instance ?? throw new InvalidOperationException("SettingsManager autoload is not ready.");

	// Cached config file to avoid repeated file I/O
	private ConfigFile _config = new ConfigFile();
	private const string CONFIG_FILE = "settings.cfg";
	private static string CONFIG_PATH => "user://" + CONFIG_FILE;

	[Signal]
	public delegate void LanguageChangedEventHandler();

	[Signal]
	public delegate void MouseSensitivityChangedEventHandler(float sensitivity);

	[Signal]
	public delegate void FovChangedEventHandler(float fov);

	[Signal]
	public delegate void ColorblindModeChangedEventHandler(int mode);

	[Signal]
	public delegate void BrightnessChangedEventHandler(float brightness);

	[Signal]
	public delegate void DamageFlashChangedEventHandler(bool enabled);

	[Signal]
	public delegate void HudScaleChangedEventHandler(float scale);

	[Signal]
	public delegate void DebugInfoChangedEventHandler(bool enabled);

	// VENDOR-EDIT (Terry Lingers): game-specific accessibility toggles folded in from the
	// retired AccessibilityManager. Same public shape so consumers swap Instance only.
	public bool ReducedFlashing { get; private set; }
	public bool AimAssistEnabled { get; private set; }

	[Signal]
	public delegate void FlashingChangedEventHandler();

	public override void _Ready()
	{
		_instance = this;
		Log.RegisterModule(MODULE_NAME, false);

		// Load config file once
		var loadErr = _config.Load(CONFIG_PATH);
		if (loadErr != Error.Ok && loadErr != Error.FileNotFound)
		{
			Log.Warning(MODULE_NAME, $"Failed to load config file: {loadErr}, using defaults");
			_config = new ConfigFile();
		}

		// Ensure audio buses exist, then resolve their indices by name
		EnsureAudioBuses();

		// Load settings on game startup
		LoadSettings();
		LoadGameToggles(); // VENDOR-EDIT (Terry Lingers)

		Log.Info(MODULE_NAME, "Settings Manager initialized");
	}

	// Create audio buses if they don't exist, then cache their indices by name so
	// volume control works regardless of bus layout in the host project.
	private void EnsureAudioBuses()
	{
		if (AudioServer.GetBusIndex("Music") == -1)
		{
			AudioServer.AddBus();
			int musicBusIdx = AudioServer.BusCount - 1;
			AudioServer.SetBusName(musicBusIdx, "Music");
			AudioServer.SetBusSend(musicBusIdx, "Master");
		}

		if (AudioServer.GetBusIndex("SFX") == -1)
		{
			AudioServer.AddBus();
			int sfxBusIdx = AudioServer.BusCount - 1;
			AudioServer.SetBusName(sfxBusIdx, "SFX");
			AudioServer.SetBusSend(sfxBusIdx, "Master");
		}

		if (AudioServer.GetBusIndex("Ambience") == -1)
		{
			AudioServer.AddBus();
			int ambienceBusIdx = AudioServer.BusCount - 1;
			AudioServer.SetBusName(ambienceBusIdx, "Ambience");
			AudioServer.SetBusSend(ambienceBusIdx, "Master");
		}

		_masterBus = AudioServer.GetBusIndex("Master");
		if (_masterBus < 0) _masterBus = 0;
		_musicBus = AudioServer.GetBusIndex("Music");
		_sfxBus = AudioServer.GetBusIndex("SFX");
		_ambienceBus = AudioServer.GetBusIndex("Ambience");
	}

	// Load settings from cached config file. Every key falls back to its default
	// individually, so a file missing any single section or key loads fine — only a
	// completely empty/absent file triggers writing the defaults out.
	public void LoadSettings()
	{
		if (_config.GetSections().Length > 0)
		{
			try
			{
				Log.Info(MODULE_NAME, "Loading settings from config file");

				// Load volume settings (clamped: out-of-range values reach LinearToDb
				// as garbage — a negative volume becomes NaN dB on the bus)
				float masterVolume = Mathf.Clamp((float)_config.GetValue("audio", "master_volume", DEFAULT_MASTER_VOLUME), 0f, 100f);
				float musicVolume = Mathf.Clamp((float)_config.GetValue("audio", "music_volume", DEFAULT_MUSIC_VOLUME), 0f, 100f);
				float sfxVolume = Mathf.Clamp((float)_config.GetValue("audio", "sfx_volume", DEFAULT_SFX_VOLUME), 0f, 100f);
				float ambienceVolume = Mathf.Clamp((float)_config.GetValue("audio", "ambience_volume", DEFAULT_AMBIENCE_VOLUME), 0f, 100f);

				// Apply volume settings to audio buses
				AudioServer.SetBusVolumeDb(_masterBus, Mathf.LinearToDb(masterVolume / 100));
				AudioServer.SetBusVolumeDb(_musicBus, Mathf.LinearToDb(musicVolume / 100));
				AudioServer.SetBusVolumeDb(_sfxBus, Mathf.LinearToDb(sfxVolume / 100));
				AudioServer.SetBusVolumeDb(_ambienceBus, Mathf.LinearToDb(ambienceVolume / 100));

				// Load and apply video settings
				bool vsync = (bool)_config.GetValue("video", "vsync", DEFAULT_VSYNC);
				SetVsync(vsync);

				int fullscreenMode = (int)_config.GetValue("video", "fullscreen_mode", DEFAULT_FULLSCREEN_MODE);
				SetFullscreenMode(fullscreenMode);

				Vector2I resolution = (Vector2I)_config.GetValue("video", "resolution", DEFAULT_RESOLUTION);
				SetResolution(resolution);

				// Load and apply language setting
				string language = (string)_config.GetValue("localization", "language", DEFAULT_LANGUAGE);
				SetLanguage(language);

				// Load mouse sensitivity
				float mouseSensitivity = (float)_config.GetValue("input", "mouse_sensitivity", DEFAULT_MOUSE_SENSITIVITY);
				EmitSignal(SignalName.MouseSensitivityChanged, mouseSensitivity);

				// Load FOV
				float fov = (float)_config.GetValue("video", "fov", DEFAULT_FOV);
				EmitSignal(SignalName.FovChanged, fov);

				// Load colorblind mode
				int colorblindMode = (int)_config.GetValue("accessibility", "colorblind_mode", DEFAULT_COLORBLIND_MODE);
				EmitSignal(SignalName.ColorblindModeChanged, colorblindMode);

				// Seed accessibility toggles if missing (read directly by consumers)
				if (!_config.HasSectionKey("accessibility", "screen_shake_enabled"))
				{
					_config.SetValue("accessibility", "screen_shake_enabled", DEFAULT_SCREEN_SHAKE_ENABLED);
				}
				if (!_config.HasSectionKey("accessibility", "motion_blur_enabled"))
				{
					_config.SetValue("accessibility", "motion_blur_enabled", DEFAULT_MOTION_BLUR_ENABLED);
				}

				// Apply advanced graphics settings (global / viewport / rendering server)
				ApplyFpsCap(GetFpsCap());
				ApplyRenderScale(GetRenderScale());
				ApplyMsaa(GetMsaa());
				ApplyScreenSpaceAa(GetScreenSpaceAa());
				ApplyTaa(GetTaa());
				ApplyShadowQuality(GetShadowQuality());
				ApplyAnisotropic(GetAnisotropic());
				ApplySsaoQuality(GetSsao());
				ApplySsilQuality(GetSsil());
				ApplyHudScale(GetHudScale());

				// Env-dependent settings - try applying now, but levels should also call
				// ReapplyEnvironmentSettings() on load so settings take effect per scene.
				ReapplyEnvironmentSettings();

				// Emit signals for settings consumed by gameplay systems
				EmitSignal(SignalName.BrightnessChanged, GetBrightness());
				EmitSignal(SignalName.DamageFlashChanged, GetDamageFlashEnabled());
				EmitSignal(SignalName.HudScaleChanged, GetHudScale());
				EmitSignal(SignalName.DebugInfoChanged, GetShowDebugInfo());

				Log.Debug(MODULE_NAME, $"Settings loaded - Master Vol: {masterVolume}%, Music Vol: {musicVolume}%, SFX Vol: {sfxVolume}%, Language: {language}, Mouse Sensitivity: {mouseSensitivity}%, FOV: {fov}, Colorblind: {colorblindMode}");
			}
			catch (System.Exception ex)
			{
				// Do NOT rewrite the file here: signals emitted above run consumer
				// handlers synchronously, so this catch also fires for exceptions that
				// have nothing to do with the file. Destroying the user's settings over
				// a startup-order hiccup in a subscriber is far worse than running one
				// session on partially applied values.
				Log.Error(MODULE_NAME, $"Failed to apply saved settings: {ex.Message}");
			}
		}
		else
		{
			Log.Warning(MODULE_NAME, "No settings file found, using defaults");
			ApplyDefaults();
			SaveDefaultSettings();
		}
	}

	// VENDOR-EDIT (Terry Lingers) — load the folded-in game toggles from [accessibility].
	private void LoadGameToggles()
	{
		ReducedFlashing = (bool)_config.GetValue("accessibility", "reduced_flashing", false);
		AimAssistEnabled = (bool)_config.GetValue("accessibility", "aim_assist", false);
	}

	// VENDOR-EDIT (Terry Lingers) — photosensitivity suppression. Persists + emits FlashingChanged
	// (parameterless) on change so looping effects can tear down a running Tween mid-flight.
	public void SetReducedFlashing(bool on)
	{
		if (ReducedFlashing == on)
			return;
		ReducedFlashing = on;
		SaveSetting("accessibility", "reduced_flashing", on);
		EmitSignal(SignalName.FlashingChanged);
	}

	// VENDOR-EDIT (Terry Lingers) — throw aim-assist. Persists; no signal (read at throw-time).
	public void SetAimAssistEnabled(bool on)
	{
		if (AimAssistEnabled == on)
			return;
		AimAssistEnabled = on;
		SaveSetting("accessibility", "aim_assist", on);
	}

	// Reset every setting back to its default, apply it to the running game, and persist to disk.
	public void ResetToDefaults()
	{
		ApplyDefaults();
		SaveDefaultSettings();
	}

	private void ApplyDefaults()
	{
		AudioServer.SetBusVolumeDb(_masterBus, Mathf.LinearToDb(DEFAULT_MASTER_VOLUME / 100));
		AudioServer.SetBusVolumeDb(_musicBus, Mathf.LinearToDb(DEFAULT_MUSIC_VOLUME / 100));
		AudioServer.SetBusVolumeDb(_sfxBus, Mathf.LinearToDb(DEFAULT_SFX_VOLUME / 100));
		AudioServer.SetBusVolumeDb(_ambienceBus, Mathf.LinearToDb(DEFAULT_AMBIENCE_VOLUME / 100));
		SetVsync(DEFAULT_VSYNC);
		SetFullscreenMode(DEFAULT_FULLSCREEN_MODE);
		SetResolution(DEFAULT_RESOLUTION);
		SetLanguage(DEFAULT_LANGUAGE);
		EmitSignal(SignalName.MouseSensitivityChanged, DEFAULT_MOUSE_SENSITIVITY);
		EmitSignal(SignalName.FovChanged, DEFAULT_FOV);
		EmitSignal(SignalName.ColorblindModeChanged, DEFAULT_COLORBLIND_MODE);

		ApplyFpsCap(DEFAULT_FPS_CAP);
		ApplyRenderScale(DEFAULT_RENDER_SCALE);
		ApplyMsaa(DEFAULT_MSAA_3D);
		ApplyScreenSpaceAa(DEFAULT_SCREEN_SPACE_AA);
		ApplyTaa(DEFAULT_USE_TAA);
		ApplyShadowQuality(DEFAULT_SHADOW_QUALITY);
		ApplyAnisotropic(DEFAULT_ANISOTROPIC_LEVEL);
		ApplySsaoQuality(DEFAULT_SSAO_QUALITY);
		ApplySsilQuality(DEFAULT_SSIL_QUALITY);
		ApplyHudScale(DEFAULT_HUD_SCALE);
		ReapplyEnvironmentSettings();

		EmitSignal(SignalName.BrightnessChanged, DEFAULT_BRIGHTNESS);
		EmitSignal(SignalName.DamageFlashChanged, DEFAULT_DAMAGE_FLASH_ENABLED);
		EmitSignal(SignalName.HudScaleChanged, DEFAULT_HUD_SCALE);
		EmitSignal(SignalName.DebugInfoChanged, DEFAULT_SHOW_DEBUG_INFO);
	}

	// Save default settings to create initial config file
	private void SaveDefaultSettings()
	{
		_config.SetValue("audio", "master_volume", DEFAULT_MASTER_VOLUME);
		_config.SetValue("audio", "music_volume", DEFAULT_MUSIC_VOLUME);
		_config.SetValue("audio", "sfx_volume", DEFAULT_SFX_VOLUME);
		_config.SetValue("audio", "ambience_volume", DEFAULT_AMBIENCE_VOLUME);
		_config.SetValue("video", "vsync", DEFAULT_VSYNC);
		_config.SetValue("video", "fullscreen_mode", DEFAULT_FULLSCREEN_MODE);
		_config.SetValue("video", "resolution", DEFAULT_RESOLUTION);
		_config.SetValue("localization", "language", DEFAULT_LANGUAGE);
		_config.SetValue("input", "mouse_sensitivity", DEFAULT_MOUSE_SENSITIVITY);
		_config.SetValue("video", "fov", DEFAULT_FOV);
		_config.SetValue("accessibility", "colorblind_mode", DEFAULT_COLORBLIND_MODE);
		_config.SetValue("accessibility", "screen_shake_enabled", DEFAULT_SCREEN_SHAKE_ENABLED);
		_config.SetValue("accessibility", "motion_blur_enabled", DEFAULT_MOTION_BLUR_ENABLED);

		_config.SetValue("video", "fps_cap", DEFAULT_FPS_CAP);
		_config.SetValue("video", "render_scale", DEFAULT_RENDER_SCALE);
		_config.SetValue("video", "msaa", DEFAULT_MSAA_3D);
		_config.SetValue("video", "screen_space_aa", DEFAULT_SCREEN_SPACE_AA);
		_config.SetValue("video", "taa", DEFAULT_USE_TAA);
		_config.SetValue("graphics", "shadow_quality", DEFAULT_SHADOW_QUALITY);
		_config.SetValue("graphics", "ssao_quality", DEFAULT_SSAO_QUALITY);
		_config.SetValue("graphics", "ssil_quality", DEFAULT_SSIL_QUALITY);
		_config.SetValue("graphics", "volumetric_fog_enabled", DEFAULT_VOLUMETRIC_FOG_ENABLED);
		_config.SetValue("graphics", "glow_enabled", DEFAULT_GLOW_ENABLED);
		_config.SetValue("video", "anisotropic_level", DEFAULT_ANISOTROPIC_LEVEL);
		_config.SetValue("graphics", "brightness", DEFAULT_BRIGHTNESS);
		_config.SetValue("accessibility", "damage_flash_enabled", DEFAULT_DAMAGE_FLASH_ENABLED);
		_config.SetValue("accessibility", "hud_scale", DEFAULT_HUD_SCALE);
		_config.SetValue("debug", "show_debug_info", DEFAULT_SHOW_DEBUG_INFO);

		var err = _config.Save(CONFIG_PATH);
		if (err == Error.Ok)
		{
			Log.Info(MODULE_NAME, "Default settings saved successfully");
		}
		else
		{
			Log.Error(MODULE_NAME, $"Error saving default settings: {err}");
		}
	}

	// Language Settings
	public void SetLanguage(string languageCode)
	{
		Log.Debug(MODULE_NAME, $"Setting language to: {languageCode}");

		TranslationServer.SetLocale(languageCode);

		SaveSetting("localization", "language", languageCode);
		EmitSignal(SignalName.LanguageChanged);
	}

	public string GetCurrentLanguage()
	{
		return TranslationServer.GetLocale();
	}

	/// <summary>
	/// Locales the settings UI may offer. Edit to match the translations your
	/// project ships.
	/// </summary>
	public string[] GetAvailableLanguages()
	{
		return new string[] { "en", "fr", "es", "de", "eo" };
	}

	// Video Settings Functions
	public void SetVsync(bool enabled)
	{
		Log.Debug(MODULE_NAME, $"Setting VSync to: {enabled}");

		if (enabled)
		{
			DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
		}
		else
		{
			DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		}

		SaveSetting("video", "vsync", enabled); // VENDOR-EDIT: persist (load reads video/vsync)
	}

	// TAA, SSAO, SSIL, and volumetric fog only exist in the Forward+ renderer; applying
	// them under Compatibility (this template's default) or Mobile just emits engine
	// errors while the UI pretends the setting took effect.
	private static bool SupportsAdvancedEnvironment =>
		(string)ProjectSettings.GetSetting("rendering/renderer/rendering_method", "forward_plus")
			== "forward_plus";

	private WorldEnvironment? FindWorldEnvironment(Node node)
	{
		if (node is WorldEnvironment worldEnv)
		{
			return worldEnv;
		}

		foreach (Node child in node.GetChildren())
		{
			var result = FindWorldEnvironment(child);
			if (result != null)
			{
				return result;
			}
		}

		return null;
	}

	public void SetFullscreenMode(int mode)
	{
		Log.Debug(MODULE_NAME, $"Setting fullscreen mode to: {mode}");

		switch (mode)
		{
			case 0: // Windowed
				GetWindow().Mode = Window.ModeEnum.Windowed;
				GetWindow().Borderless = false; // undo borderless-fullscreen's flag
				break;
			case 1: // Borderless Fullscreen
				GetWindow().Mode = Window.ModeEnum.Fullscreen;
				GetWindow().Borderless = true;
				break;
			case 2: // Fullscreen
				GetWindow().Mode = Window.ModeEnum.ExclusiveFullscreen;
				GetWindow().Borderless = false;
				break;
		}

		SaveSetting("video", "fullscreen_mode", mode); // VENDOR-EDIT: persist (load reads video/fullscreen_mode)
	}

	public void SetResolution(Vector2I resolution)
	{
		Log.Debug(MODULE_NAME, $"Setting resolution to: {resolution}");

		SaveSetting("video", "resolution", resolution); // VENDOR-EDIT: persist to disk (was in-memory only)

		if (GetWindow().Mode == Window.ModeEnum.Windowed)
		{
			GetWindow().Size = resolution;

			var screenSize = DisplayServer.ScreenGetSize();
			var windowPosition = (screenSize - resolution) / 2;
			GetWindow().Position = windowPosition;
		}
	}

	// Get current settings values
	public bool GetVsync()
	{
		return DisplayServer.WindowGetVsyncMode() == DisplayServer.VSyncMode.Enabled;
	}

	public int GetFullscreenMode()
	{
		var mode = GetWindow().Mode;
		if (mode == Window.ModeEnum.Windowed)
		{
			return 0;
		}
		else if (mode == Window.ModeEnum.Fullscreen)
		{
			return 1;
		}
		else
		{
			return 2;
		}
	}

	public Vector2I GetCurrentResolution()
	{
		return (Vector2I)_config.GetValue("video", "resolution", DEFAULT_RESOLUTION);
	}

	public int GetResolutionIndex(Vector2I resolution)
	{
		for (int i = 0; i < resolutions.Length; i++)
		{
			if (resolutions[i] == resolution)
			{
				return i;
			}
		}
		return 3; // Default to 1920x1080
	}

	public Vector2I[] GetAvailableResolutions()
	{
		return resolutions;
	}

	// Helper methods for getting current volume values
	public float GetMasterVolume()
	{
		return Mathf.DbToLinear(AudioServer.GetBusVolumeDb(_masterBus)) * 100;
	}

	public float GetMusicVolume()
	{
		return Mathf.DbToLinear(AudioServer.GetBusVolumeDb(_musicBus)) * 100;
	}

	public float GetSfxVolume()
	{
		return Mathf.DbToLinear(AudioServer.GetBusVolumeDb(_sfxBus)) * 100;
	}

	public float GetAmbienceVolume()
	{
		return Mathf.DbToLinear(AudioServer.GetBusVolumeDb(_ambienceBus)) * 100;
	}

	// Helper methods for setting volume values. Clamped: a negative volume would reach
	// the bus as NaN dB via LinearToDb.
	public void SetMasterVolume(float volume)
	{
		volume = Mathf.Clamp(volume, 0f, 100f);
		AudioServer.SetBusVolumeDb(_masterBus, Mathf.LinearToDb(volume / 100));
		SaveSetting("audio", "master_volume", volume);
	}

	public void SetMusicVolume(float volume)
	{
		volume = Mathf.Clamp(volume, 0f, 100f);
		AudioServer.SetBusVolumeDb(_musicBus, Mathf.LinearToDb(volume / 100));
		SaveSetting("audio", "music_volume", volume);
	}

	public void SetSfxVolume(float volume)
	{
		volume = Mathf.Clamp(volume, 0f, 100f);
		AudioServer.SetBusVolumeDb(_sfxBus, Mathf.LinearToDb(volume / 100));
		SaveSetting("audio", "sfx_volume", volume);
	}

	public void SetAmbienceVolume(float volume)
	{
		volume = Mathf.Clamp(volume, 0f, 100f);
		AudioServer.SetBusVolumeDb(_ambienceBus, Mathf.LinearToDb(volume / 100));
		SaveSetting("audio", "ambience_volume", volume);
	}

	// Mouse sensitivity methods (clamped so a corrupted file cannot zero or explode
	// look input)
	public float GetMouseSensitivity()
	{
		return Mathf.Clamp((float)_config.GetValue("input", "mouse_sensitivity", DEFAULT_MOUSE_SENSITIVITY), 1f, 100f);
	}

	public void SetMouseSensitivity(float sensitivity)
	{
		sensitivity = Mathf.Clamp(sensitivity, 1f, 100f);
		SaveSetting("input", "mouse_sensitivity", sensitivity);
		EmitSignal(SignalName.MouseSensitivityChanged, sensitivity);
		Log.Debug(MODULE_NAME, $"Mouse sensitivity set to: {sensitivity}%");
	}

	// FOV methods (clamped: Camera3D rejects FOV outside (0, 180) and extreme values
	// are unusable anyway)
	public float GetFov()
	{
		return Mathf.Clamp((float)_config.GetValue("video", "fov", DEFAULT_FOV), 40f, 140f);
	}

	public void SetFov(float fov)
	{
		fov = Mathf.Clamp(fov, 40f, 140f);
		SaveSetting("video", "fov", fov);
		EmitSignal(SignalName.FovChanged, fov);
		Log.Debug(MODULE_NAME, $"FOV set to: {fov}");
	}

	// Generic method to save a single setting. The write is debounced: a slider drag
	// calls setters every tick, and a synchronous whole-file save per tick is pure
	// disk churn. Changes flush half a second after they stop (and on exit).
	private void SaveSetting(string section, string key, Variant value)
	{
		_config.SetValue(section, key, value);
		RequestSave();
		Log.Debug(MODULE_NAME, $"Queued setting: {section}/{key} = {value}");
	}

	private bool _savePending;

	private void RequestSave()
	{
		if (_savePending)
			return;
		_savePending = true;
		GetTree().CreateTimer(0.5).Timeout += FlushPendingSave;
	}

	private void FlushPendingSave()
	{
		if (!_savePending)
			return;
		_savePending = false;
		var err = _config.Save(CONFIG_PATH);
		if (err != Error.Ok)
		{
			Log.Error(MODULE_NAME, $"Failed to save settings: {err}");
		}
	}

	// Method to save all current settings
	public void SaveCurrentSettings()
	{
		_config.SetValue("audio", "master_volume", GetMasterVolume());
		_config.SetValue("audio", "music_volume", GetMusicVolume());
		_config.SetValue("audio", "sfx_volume", GetSfxVolume());
		_config.SetValue("audio", "ambience_volume", GetAmbienceVolume());
		_config.SetValue("video", "vsync", GetVsync());
		_config.SetValue("video", "fullscreen_mode", GetFullscreenMode());
		_config.SetValue("video", "resolution", GetCurrentResolution());
		_config.SetValue("localization", "language", GetCurrentLanguage());
		_config.SetValue("input", "mouse_sensitivity", GetMouseSensitivity());
		_config.SetValue("video", "fov", GetFov());
		_config.SetValue("accessibility", "colorblind_mode", GetColorblindMode());
		_config.SetValue("accessibility", "screen_shake_enabled", GetScreenShakeEnabled());
		_config.SetValue("accessibility", "motion_blur_enabled", GetMotionBlurEnabled());

		_config.SetValue("video", "fps_cap", GetFpsCap());
		_config.SetValue("video", "render_scale", GetRenderScale());
		_config.SetValue("video", "msaa", GetMsaa());
		_config.SetValue("video", "screen_space_aa", GetScreenSpaceAa());
		_config.SetValue("video", "taa", GetTaa());
		_config.SetValue("graphics", "shadow_quality", GetShadowQuality());
		_config.SetValue("graphics", "ssao_quality", GetSsao());
		_config.SetValue("graphics", "ssil_quality", GetSsil());
		_config.SetValue("graphics", "volumetric_fog_enabled", GetVolumetricFogEnabled());
		_config.SetValue("graphics", "glow_enabled", GetGlowEnabled());
		_config.SetValue("video", "anisotropic_level", GetAnisotropic());
		_config.SetValue("graphics", "brightness", GetBrightness());
		_config.SetValue("accessibility", "damage_flash_enabled", GetDamageFlashEnabled());
		_config.SetValue("accessibility", "hud_scale", GetHudScale());
		_config.SetValue("debug", "show_debug_info", GetShowDebugInfo());

		var err = _config.Save(CONFIG_PATH);
		if (err == Error.Ok)
		{
			Log.Info(MODULE_NAME, "Current settings saved successfully");
		}
		else
		{
			Log.Error(MODULE_NAME, $"Error saving current settings: {err}");
		}
	}

	// Colorblind mode
	public int GetColorblindMode()
	{
		return (int)_config.GetValue("accessibility", "colorblind_mode", DEFAULT_COLORBLIND_MODE);
	}

	public void SetColorblindMode(int mode)
	{
		SaveSetting("accessibility", "colorblind_mode", mode);
		EmitSignal(SignalName.ColorblindModeChanged, mode);
		Log.Debug(MODULE_NAME, $"Colorblind mode set to: {mode}");
	}

	// Screen shake setting
	public bool GetScreenShakeEnabled()
	{
		return (bool)_config.GetValue("accessibility", "screen_shake_enabled", DEFAULT_SCREEN_SHAKE_ENABLED);
	}

	public void SetScreenShakeEnabled(bool enabled)
	{
		SaveSetting("accessibility", "screen_shake_enabled", enabled);
		Log.Debug(MODULE_NAME, $"Screen shake set to: {enabled}");
	}

	// Motion blur setting
	public bool GetMotionBlurEnabled()
	{
		return (bool)_config.GetValue("accessibility", "motion_blur_enabled", DEFAULT_MOTION_BLUR_ENABLED);
	}

	public void SetMotionBlurEnabled(bool enabled)
	{
		SaveSetting("accessibility", "motion_blur_enabled", enabled);
		Log.Debug(MODULE_NAME, $"Motion blur set to: {enabled}");
	}

	// ============ Advanced Graphics Settings ============

	// FPS cap
	public int GetFpsCap()
	{
		return (int)_config.GetValue("video", "fps_cap", DEFAULT_FPS_CAP);
	}

	public void SetFpsCap(int fps)
	{
		SaveSetting("video", "fps_cap", fps);
		ApplyFpsCap(fps);
	}

	private void ApplyFpsCap(int fps)
	{
		Engine.MaxFps = Mathf.Max(0, fps);
		Log.Debug(MODULE_NAME, $"FPS cap applied: {fps}");
	}

	// Render scale (50–200%)
	public float GetRenderScale()
	{
		return (float)_config.GetValue("video", "render_scale", DEFAULT_RENDER_SCALE);
	}

	public void SetRenderScale(float scale)
	{
		scale = Mathf.Clamp(scale, 0.25f, 2.0f);
		SaveSetting("video", "render_scale", scale);
		ApplyRenderScale(scale);
	}

	private void ApplyRenderScale(float scale)
	{
		GetTree().Root.Scaling3DScale = Mathf.Clamp(scale, 0.25f, 2.0f);
		Log.Debug(MODULE_NAME, $"Render scale applied: {scale}");
	}

	// MSAA 3D
	public int GetMsaa()
	{
		return (int)_config.GetValue("video", "msaa", DEFAULT_MSAA_3D);
	}

	public void SetMsaa(int level)
	{
		SaveSetting("video", "msaa", level);
		ApplyMsaa(level);
	}

	private void ApplyMsaa(int level)
	{
		GetTree().Root.Msaa3D = (Viewport.Msaa)Mathf.Clamp(level, 0, 3);
		Log.Debug(MODULE_NAME, $"MSAA applied: {level}");
	}

	// Screen-space AA (FXAA)
	public int GetScreenSpaceAa()
	{
		return (int)_config.GetValue("video", "screen_space_aa", DEFAULT_SCREEN_SPACE_AA);
	}

	public void SetScreenSpaceAa(int mode)
	{
		SaveSetting("video", "screen_space_aa", mode);
		ApplyScreenSpaceAa(mode);
	}

	private void ApplyScreenSpaceAa(int mode)
	{
		GetTree().Root.ScreenSpaceAA = (Viewport.ScreenSpaceAAEnum)Mathf.Clamp(mode, 0, 1);
		Log.Debug(MODULE_NAME, $"Screen-space AA applied: {mode}");
	}

	// TAA
	public bool GetTaa()
	{
		return (bool)_config.GetValue("video", "taa", DEFAULT_USE_TAA);
	}

	public void SetTaa(bool enabled)
	{
		SaveSetting("video", "taa", enabled);
		ApplyTaa(enabled);
	}

	private void ApplyTaa(bool enabled)
	{
		if (!SupportsAdvancedEnvironment)
			return;
		GetTree().Root.UseTaa = enabled;
		Log.Debug(MODULE_NAME, $"TAA applied: {enabled}");
	}

	// Shadow quality (0=Off, 1=Low, 2=Medium, 3=High)
	public int GetShadowQuality()
	{
		return (int)_config.GetValue("graphics", "shadow_quality", DEFAULT_SHADOW_QUALITY);
	}

	public void SetShadowQuality(int quality)
	{
		SaveSetting("graphics", "shadow_quality", quality);
		ApplyShadowQuality(quality);
	}

	private void ApplyShadowQuality(int quality)
	{
		quality = Mathf.Clamp(quality, 0, 3);
		int[] atlasSizes = { 0, 1024, 2048, 4096 };
		int size = atlasSizes[quality];
		RenderingServer.DirectionalShadowAtlasSetSize(size, true);
		GetTree().Root.PositionalShadowAtlasSize = size;

		var shadowQuality = quality switch
		{
			0 => RenderingServer.ShadowQuality.Hard,
			1 => RenderingServer.ShadowQuality.SoftLow,
			2 => RenderingServer.ShadowQuality.SoftMedium,
			3 => RenderingServer.ShadowQuality.SoftHigh,
			_ => RenderingServer.ShadowQuality.SoftMedium
		};
		RenderingServer.DirectionalSoftShadowFilterSetQuality(shadowQuality);
		RenderingServer.PositionalSoftShadowFilterSetQuality(shadowQuality);
		Log.Debug(MODULE_NAME, $"Shadow quality applied: {quality}");
	}

	// Anisotropic filtering
	public int GetAnisotropic()
	{
		return (int)_config.GetValue("video", "anisotropic_level", DEFAULT_ANISOTROPIC_LEVEL);
	}

	public void SetAnisotropic(int level)
	{
		SaveSetting("video", "anisotropic_level", level);
		ApplyAnisotropic(level);
	}

	private void ApplyAnisotropic(int level)
	{
		GetTree().Root.AnisotropicFilteringLevel = (Viewport.AnisotropicFiltering)Mathf.Clamp(level, 0, 4);
		Log.Debug(MODULE_NAME, $"Anisotropic filtering applied: {level}");
	}

	// SSAO
	public int GetSsao()
	{
		return (int)_config.GetValue("graphics", "ssao_quality", DEFAULT_SSAO_QUALITY);
	}

	public void SetSsao(int quality)
	{
		SaveSetting("graphics", "ssao_quality", quality);
		ApplySsaoQuality(quality);
		ApplySsaoEnvironment(quality);
	}

	private void ApplySsaoQuality(int quality)
	{
		if (!SupportsAdvancedEnvironment || quality <= 0) return;
		var rsQuality = quality switch
		{
			1 => RenderingServer.EnvironmentSsaoQuality.Low,
			2 => RenderingServer.EnvironmentSsaoQuality.Medium,
			3 => RenderingServer.EnvironmentSsaoQuality.High,
			_ => RenderingServer.EnvironmentSsaoQuality.Medium
		};
		RenderingServer.EnvironmentSetSsaoQuality(rsQuality, true, 0.5f, 2, 50.0f, 300.0f);
		Log.Debug(MODULE_NAME, $"SSAO quality applied: {quality}");
	}

	private void ApplySsaoEnvironment(int quality)
	{
		if (!SupportsAdvancedEnvironment)
			return;
		var env = FindWorldEnvironment(GetTree().Root);
		if (env?.Environment != null)
		{
			env.Environment.SsaoEnabled = quality > 0;
		}
	}

	// SSIL
	public int GetSsil()
	{
		return (int)_config.GetValue("graphics", "ssil_quality", DEFAULT_SSIL_QUALITY);
	}

	public void SetSsil(int quality)
	{
		SaveSetting("graphics", "ssil_quality", quality);
		ApplySsilQuality(quality);
		ApplySsilEnvironment(quality);
	}

	private void ApplySsilQuality(int quality)
	{
		if (!SupportsAdvancedEnvironment || quality <= 0) return;
		var rsQuality = quality switch
		{
			1 => RenderingServer.EnvironmentSsilQuality.Low,
			2 => RenderingServer.EnvironmentSsilQuality.Medium,
			3 => RenderingServer.EnvironmentSsilQuality.High,
			_ => RenderingServer.EnvironmentSsilQuality.Medium
		};
		RenderingServer.EnvironmentSetSsilQuality(rsQuality, true, 0.5f, 4, 50.0f, 300.0f);
		Log.Debug(MODULE_NAME, $"SSIL quality applied: {quality}");
	}

	private void ApplySsilEnvironment(int quality)
	{
		if (!SupportsAdvancedEnvironment)
			return;
		var env = FindWorldEnvironment(GetTree().Root);
		if (env?.Environment != null)
		{
			env.Environment.SsilEnabled = quality > 0;
		}
	}

	// Volumetric Fog
	public bool GetVolumetricFogEnabled()
	{
		return (bool)_config.GetValue("graphics", "volumetric_fog_enabled", DEFAULT_VOLUMETRIC_FOG_ENABLED);
	}

	public void SetVolumetricFogEnabled(bool enabled)
	{
		SaveSetting("graphics", "volumetric_fog_enabled", enabled);
		ApplyVolumetricFog(enabled);
	}

	private void ApplyVolumetricFog(bool enabled)
	{
		if (!SupportsAdvancedEnvironment)
			return;
		var env = FindWorldEnvironment(GetTree().Root);
		if (env?.Environment != null)
		{
			env.Environment.VolumetricFogEnabled = enabled;
		}
		Log.Debug(MODULE_NAME, $"Volumetric fog applied: {enabled}");
	}

	// Glow / Bloom
	public bool GetGlowEnabled()
	{
		return (bool)_config.GetValue("graphics", "glow_enabled", DEFAULT_GLOW_ENABLED);
	}

	public void SetGlowEnabled(bool enabled)
	{
		SaveSetting("graphics", "glow_enabled", enabled);
		ApplyGlow(enabled);
	}

	private void ApplyGlow(bool enabled)
	{
		var env = FindWorldEnvironment(GetTree().Root);
		if (env?.Environment != null)
		{
			env.Environment.GlowEnabled = enabled;
		}
		Log.Debug(MODULE_NAME, $"Glow applied: {enabled}");
	}

	// Brightness (Environment.AdjustmentBrightness)
	public float GetBrightness()
	{
		return (float)_config.GetValue("graphics", "brightness", DEFAULT_BRIGHTNESS);
	}

	public void SetBrightness(float brightness)
	{
		SaveSetting("graphics", "brightness", brightness);
		ApplyBrightness(brightness);
		EmitSignal(SignalName.BrightnessChanged, brightness);
	}

	private void ApplyBrightness(float brightness)
	{
		var env = FindWorldEnvironment(GetTree().Root);
		if (env?.Environment != null)
		{
			env.Environment.AdjustmentEnabled = true;
			env.Environment.AdjustmentBrightness = brightness;
		}
		Log.Debug(MODULE_NAME, $"Brightness applied: {brightness}");
	}

	// Reapply all environment-dependent settings to the currently loaded WorldEnvironment.
	// Levels should call this from their _Ready so per-scene envs pick up user settings.
	public void ReapplyEnvironmentSettings()
	{
		ApplySsaoEnvironment(GetSsao());
		ApplySsilEnvironment(GetSsil());
		ApplyVolumetricFog(GetVolumetricFogEnabled());
		ApplyGlow(GetGlowEnabled());
		ApplyBrightness(GetBrightness());
	}

	// Damage Flash
	public bool GetDamageFlashEnabled()
	{
		return (bool)_config.GetValue("accessibility", "damage_flash_enabled", DEFAULT_DAMAGE_FLASH_ENABLED);
	}

	public void SetDamageFlashEnabled(bool enabled)
	{
		SaveSetting("accessibility", "damage_flash_enabled", enabled);
		EmitSignal(SignalName.DamageFlashChanged, enabled);
	}

	// HUD Scale. Clamped everywhere: a hand-edited hud_scale=0 would zero
	// ContentScaleFactor and make the whole UI — including the settings menu that
	// could undo it — unusable.
	public float GetHudScale()
	{
		return Mathf.Clamp((float)_config.GetValue("accessibility", "hud_scale", DEFAULT_HUD_SCALE), 0.5f, 2f);
	}

	public void SetHudScale(float scale)
	{
		scale = Mathf.Clamp(scale, 0.5f, 2f);
		SaveSetting("accessibility", "hud_scale", scale);
		ApplyHudScale(scale);
		EmitSignal(SignalName.HudScaleChanged, scale);
	}

	private void ApplyHudScale(float scale)
	{
		GetTree().Root.ContentScaleFactor = Mathf.Clamp(scale, 0.5f, 2f);
		Log.Debug(MODULE_NAME, $"HUD scale applied: {scale}");
	}

	// Debug info panel (FPS / performance overlay)
	public bool GetShowDebugInfo()
	{
		return (bool)_config.GetValue("debug", "show_debug_info", DEFAULT_SHOW_DEBUG_INFO);
	}

	public void SetShowDebugInfo(bool enabled)
	{
		SaveSetting("debug", "show_debug_info", enabled);
		EmitSignal(SignalName.DebugInfoChanged, enabled);
	}

	// Graphics quality presets (0=Low, 1=Medium, 2=High)
	// Sets the major GPU-cost knobs in one call. Each individual setter still
	// persists and applies, so the existing options UI just needs to refresh.
	public const int GRAPHICS_PRESET_LOW = 0;
	public const int GRAPHICS_PRESET_MEDIUM = 1;
	public const int GRAPHICS_PRESET_HIGH = 2;

	public void ApplyGraphicsPreset(int preset)
	{
		Log.Info(MODULE_NAME, $"Applying graphics preset: {preset}");

		switch (preset)
		{
			case GRAPHICS_PRESET_LOW:
				SetRenderScale(0.75f);
				SetMsaa(0);              // Off
				SetScreenSpaceAa(1);     // FXAA
				SetTaa(false);
				SetShadowQuality(0);     // Off
				SetSsao(0);
				SetSsil(0);
				SetVolumetricFogEnabled(false);
				SetGlowEnabled(false);
				SetAnisotropic(0);
				break;

			case GRAPHICS_PRESET_MEDIUM:
				SetRenderScale(1.0f);
				SetMsaa(0);              // Off
				SetScreenSpaceAa(1);     // FXAA
				SetTaa(false);
				SetShadowQuality(2);     // Medium
				SetSsao(1);              // Low
				SetSsil(0);
				SetVolumetricFogEnabled(false);
				SetGlowEnabled(true);
				SetAnisotropic(2);       // 4x
				break;

			case GRAPHICS_PRESET_HIGH:
				SetRenderScale(1.0f);
				SetMsaa(2);              // 4x
				SetScreenSpaceAa(0);     // Off (TAA handles it)
				SetTaa(true);
				SetShadowQuality(3);     // High
				SetSsao(2);              // Medium
				SetSsil(1);              // Low
				SetVolumetricFogEnabled(true);
				SetGlowEnabled(true);
				SetAnisotropic(4);       // 16x
				break;

			default:
				Log.Warning(MODULE_NAME, $"Unknown graphics preset: {preset}");
				return;
		}

		ReapplyEnvironmentSettings();
	}

	public override void _ExitTree()
	{
		FlushPendingSave();
		if (_instance == this)
			_instance = null;
	}
}
