using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using OrbModding.Common;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbChronomancer;

[BepInPlugin(PluginIds.ChronomancerGuid, PluginIds.ChronomancerName, PluginIds.Version)]
public sealed class Plugin : BaseUnityPlugin
{
    private const float Epsilon = 0.001f;
    private const float DefaultTwoX = 2.0f;
    private const float DefaultFourX = 4.0f;
    private const float DefaultEightX = 8.0f;
    private const string DefaultGameplayScene = "Main";

    private static Plugin? _instance;

    private Harmony? _harmony;
    private ConfigEntry<bool> _enabled = null!;
    private ConfigEntry<string> _gameplaySceneName = null!;
    private ConfigEntry<float> _maximumMultiplier = null!;
    private ConfigEntry<bool> _allowExperimentalEightX = null!;
    private ConfigEntry<float> _presetOneX = null!;
    private ConfigEntry<float> _presetTwoX = null!;
    private ConfigEntry<float> _presetFourX = null!;
    private ConfigEntry<float> _presetEightX = null!;
    private ConfigEntry<FixedDeltaPolicy> _fixedDeltaPolicy = null!;
    private ConfigEntry<bool> _fallbackToOneXOnSaveLoad = null!;
    private ConfigEntry<KeyboardShortcut> _increaseShortcut = null!;
    private ConfigEntry<KeyboardShortcut> _decreaseShortcut = null!;
    private ConfigEntry<KeyboardShortcut> _resetShortcut = null!;
    private ConfigEntry<bool> _showTransientIndicator = null!;
    private ConfigEntry<float> _indicatorSeconds = null!;
    private ConfigEntry<bool> _diagnosticLogging = null!;
    private ConfigEntry<float> _diagnosticIntervalSeconds = null!;

    private float _originalTimeScale;
    private float _originalFixedDeltaTime;
    private float _currentMultiplier = 1.0f;
    private float _nextDiagnosticAtRealtime;
    private float _notificationUntilRealtime;
    private int _safetyGuardDepth;
    private string _notificationText = string.Empty;
    private bool _loggedEightXGuard;

    internal static ManualLogSource Log { get; private set; } = null!;

    private enum FixedDeltaPolicy
    {
        ScaleWithMultiplier = 0,
        PreserveOriginal = 1,
    }

    private void Awake()
    {
        _instance = this;
        Log = Logger;
        _originalTimeScale = Time.timeScale;
        _originalFixedDeltaTime = Time.fixedDeltaTime;

        BindConfig();
        LogAssemblyStatus();
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        SceneManager.sceneLoaded += OnSceneLoaded;

        if (!_enabled.Value)
        {
            Log.LogInfo("Chronomancer is disabled by configuration.");
            return;
        }

        EnsureHarmonyInstalled();
        RestoreOriginalTiming("startup");
        Log.LogInfo(
            $"Chronomancer loaded. Base timeScale={_originalTimeScale:0.###}, " +
            $"base fixedDeltaTime={_originalFixedDeltaTime:0.#####}, max={GetEffectiveMaximumMultiplier():0.###}x.");
    }

    private void Update()
    {
        if (!_enabled.Value)
        {
            return;
        }

        if (!IsGameplaySceneActive())
        {
            if (IsAccelerated)
            {
                ForceOneX("unsupported scene");
            }

            return;
        }

        if (_increaseShortcut.Value.IsDown())
        {
            StepMultiplier(1, "increase keybind");
        }
        else if (_decreaseShortcut.Value.IsDown())
        {
            StepMultiplier(-1, "decrease keybind");
        }
        else if (_resetShortcut.Value.IsDown())
        {
            ForceOneX("reset keybind");
        }

        LogDiagnosticsIfDue();
    }

    private void OnGUI()
    {
        if (!_enabled.Value || !_showTransientIndicator.Value || Time.realtimeSinceStartup >= _notificationUntilRealtime)
        {
            return;
        }

        GUI.Box(new Rect(16.0f, 16.0f, 180.0f, 36.0f), _notificationText);
    }

    private void OnApplicationQuit()
    {
        RestoreOriginalTiming("application quit");
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        RestoreOriginalTiming("plugin unload");
        _harmony?.UnpatchSelf();
        _harmony = null;

        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }

    private void BindConfig()
    {
        _enabled = Config.Bind("General", "Enabled", true, "Enable Chronomancer.");
        _gameplaySceneName = Config.Bind("Safety", "GameplaySceneName", DefaultGameplayScene, "Only this scene may run above 1x.");
        _maximumMultiplier = Config.Bind(
            "Timing",
            "MaximumMultiplier",
            DefaultFourX,
            new ConfigDescription(
                "Highest multiplier that keybinds may select. Values above 4x require AllowExperimentalEightX=true.",
                new AcceptableValueRange<float>(1.0f, DefaultEightX)));
        _allowExperimentalEightX = Config.Bind(
            "Timing",
            "AllowExperimentalEightX",
            false,
            "Allow the 8x preset. Keep disabled until save/load and CPU timing probes pass for this game build.");
        _presetOneX = Config.Bind(
            "Presets",
            "OneX",
            1.0f,
            "Normal speed preset. The built-in 1x preset is always present; this value is clamped into the safe range.");
        _presetTwoX = Config.Bind(
            "Presets",
            "TwoX",
            DefaultTwoX,
            new ConfigDescription("Second speed preset.", new AcceptableValueRange<float>(1.0f, DefaultEightX)));
        _presetFourX = Config.Bind(
            "Presets",
            "FourX",
            DefaultFourX,
            new ConfigDescription("Third speed preset.", new AcceptableValueRange<float>(1.0f, DefaultEightX)));
        _presetEightX = Config.Bind(
            "Presets",
            "EightX",
            DefaultEightX,
            new ConfigDescription("Experimental high-speed preset, gated by AllowExperimentalEightX.", new AcceptableValueRange<float>(1.0f, DefaultEightX)));
        _fixedDeltaPolicy = Config.Bind(
            "Timing",
            "FixedDeltaPolicy",
            FixedDeltaPolicy.ScaleWithMultiplier,
            "ScaleWithMultiplier limits fixed-update CPU growth; PreserveOriginal increases fixed-update cadence with timeScale.");
        _fallbackToOneXOnSaveLoad = Config.Bind(
            "Safety",
            "FallbackToOneXOnSaveLoad",
            true,
            "Return to 1x when known save/load methods run. The previous speed is not restored automatically.");
        _increaseShortcut = Config.Bind(
            "Keybinds",
            "IncreaseSpeed",
            new KeyboardShortcut(KeyCode.Equals, KeyCode.LeftAlt),
            "Increase to the next configured preset.");
        _decreaseShortcut = Config.Bind(
            "Keybinds",
            "DecreaseSpeed",
            new KeyboardShortcut(KeyCode.Minus, KeyCode.LeftAlt),
            "Decrease to the previous configured preset.");
        _resetShortcut = Config.Bind(
            "Keybinds",
            "ResetSpeed",
            new KeyboardShortcut(KeyCode.Alpha0, KeyCode.LeftAlt),
            "Return to 1x immediately.");
        _showTransientIndicator = Config.Bind("Indicator", "ShowTransientIndicator", true, "Show a small temporary multiplier indicator.");
        _indicatorSeconds = Config.Bind(
            "Indicator",
            "IndicatorSeconds",
            1.5f,
            new ConfigDescription("How long to show the multiplier indicator.", new AcceptableValueRange<float>(0.25f, 10.0f)));
        _diagnosticLogging = Config.Bind("Diagnostics", "LogWhileAccelerated", true, "Periodically log timing values while above 1x.");
        _diagnosticIntervalSeconds = Config.Bind(
            "Diagnostics",
            "IntervalSeconds",
            10.0f,
            new ConfigDescription("Real seconds between diagnostic timing logs.", new AcceptableValueRange<float>(1.0f, 120.0f)));

        _enabled.SettingChanged += OnConfigChanged;
        _gameplaySceneName.SettingChanged += OnConfigChanged;
        _maximumMultiplier.SettingChanged += OnConfigChanged;
        _allowExperimentalEightX.SettingChanged += OnConfigChanged;
        _presetOneX.SettingChanged += OnConfigChanged;
        _presetTwoX.SettingChanged += OnConfigChanged;
        _presetFourX.SettingChanged += OnConfigChanged;
        _presetEightX.SettingChanged += OnConfigChanged;
        _fixedDeltaPolicy.SettingChanged += OnConfigChanged;
    }

    private void OnConfigChanged(object sender, EventArgs args)
    {
        if (!_enabled.Value)
        {
            RestoreOriginalTiming("configuration disabled");
            _harmony?.UnpatchSelf();
            _harmony = null;
            return;
        }

        EnsureHarmonyInstalled();

        if (!IsGameplaySceneActive())
        {
            ForceOneX("configuration changed outside gameplay scene");
            return;
        }

        ApplyMultiplier(ClampToEffectiveMaximum(_currentMultiplier), "configuration changed");
    }

    private void EnsureHarmonyInstalled()
    {
        if (_harmony is not null)
        {
            return;
        }

        _harmony = new Harmony(PluginIds.ChronomancerGuid);
        InstallSaveLoadHooks();
    }

    private void InstallSaveLoadHooks()
    {
        var saveStateManager = AccessTools.TypeByName("SaveStateManager");
        if (saveStateManager is null)
        {
            Log.LogWarning("SaveStateManager type was not found; save/load fallback hooks are unavailable.");
            return;
        }

        var patched = 0;
        patched += PatchSafetyMethods(saveStateManager, "CollectJsonData");
        patched += PatchSafetyMethods(saveStateManager, "ImplementLoadedJson");
        patched += PatchSafetyMethods(saveStateManager, "WriteFileAndBackupAsync");

        if (patched == 0)
        {
            Log.LogWarning("No SaveStateManager safety hooks were installed. Verify method names for this game build.");
            return;
        }

        Log.LogInfo($"Installed {patched} save/load safety hook(s).");
    }

    private int PatchSafetyMethods(Type declaringType, string methodName)
    {
        var count = 0;
        var methods = AccessTools.GetDeclaredMethods(declaringType);
        for (var i = 0; i < methods.Count; i++)
        {
            if (methods[i].Name != methodName)
            {
                continue;
            }

            if (PatchSafetyMethod(methods[i]))
            {
                count++;
            }
        }

        if (count == 0)
        {
            Log.LogWarning($"{declaringType.Name}.{methodName} was not found; skipping that safety hook.");
        }

        return count;
    }

    private bool PatchSafetyMethod(MethodBase method)
    {
        try
        {
            var prefix = new HarmonyMethod(typeof(Plugin), nameof(SaveLoadPrefix));
            var finalizer = new HarmonyMethod(typeof(Plugin), nameof(SaveLoadFinalizer));
            _harmony!.Patch(method, prefix: prefix, finalizer: finalizer);
            Log.LogInfo($"Patched {FormatMethod(method)} for save/load safety.");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to patch {FormatMethod(method)}: {ex.Message}");
            return false;
        }
    }

    private static string FormatMethod(MethodBase method)
    {
        return $"{method.DeclaringType?.Name ?? "<unknown>"}.{method.Name}";
    }

    private static void SaveLoadPrefix()
    {
        _instance?.EnterSaveLoadSafety();
    }

    private static Exception? SaveLoadFinalizer(Exception? __exception)
    {
        _instance?.ExitSaveLoadSafety(__exception);
        return __exception;
    }

    private void EnterSaveLoadSafety()
    {
        _safetyGuardDepth++;

        if (_fallbackToOneXOnSaveLoad.Value && IsAccelerated)
        {
            ForceOneX("save/load safety hook");
        }
    }

    private void ExitSaveLoadSafety(Exception? exception)
    {
        if (_safetyGuardDepth > 0)
        {
            _safetyGuardDepth--;
        }

        if (exception is not null)
        {
            Log.LogError($"Save/load hook observed an exception; timing restored to 1x. {exception.GetType().Name}: {exception.Message}");
            ForceOneX("save/load exception");
        }
    }

    private void OnActiveSceneChanged(Scene previousScene, Scene nextScene)
    {
        if (!_enabled.Value)
        {
            return;
        }

        if (!IsSceneAllowed(nextScene))
        {
            ForceOneX($"scene changed to {FormatScene(nextScene)}");
            return;
        }

        Log.LogInfo($"Chronomancer gameplay scene active: {FormatScene(nextScene)}.");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!_enabled.Value)
        {
            return;
        }

        if (mode == LoadSceneMode.Additive && IsGameplaySceneActive())
        {
            return;
        }

        if (!IsSceneAllowed(scene))
        {
            ForceOneX($"scene loaded: {FormatScene(scene)}");
        }
    }

    private void StepMultiplier(int direction, string reason)
    {
        if (_safetyGuardDepth > 0)
        {
            ForceOneX("safety guard active");
            return;
        }

        var presets = GetAllowedPresets();
        if (presets.Count == 0)
        {
            ForceOneX("no valid presets");
            return;
        }

        var target = _currentMultiplier;
        if (direction > 0)
        {
            target = presets[presets.Count - 1];
            for (var i = 0; i < presets.Count; i++)
            {
                if (presets[i] > _currentMultiplier + Epsilon)
                {
                    target = presets[i];
                    break;
                }
            }
        }
        else
        {
            target = presets[0];
            for (var i = presets.Count - 1; i >= 0; i--)
            {
                if (presets[i] < _currentMultiplier - Epsilon)
                {
                    target = presets[i];
                    break;
                }
            }
        }

        ApplyMultiplier(target, reason);
    }

    private void ForceOneX(string reason)
    {
        ApplyMultiplier(1.0f, reason);
    }

    private void ApplyMultiplier(float requestedMultiplier, string reason)
    {
        if (!_enabled.Value)
        {
            RestoreOriginalTiming(reason);
            return;
        }

        var multiplier = ClampToEffectiveMaximum(requestedMultiplier);
        if (multiplier > 1.0f + Epsilon && !IsGameplaySceneActive())
        {
            multiplier = 1.0f;
            reason += "; outside gameplay scene";
        }

        try
        {
            Time.timeScale = _originalTimeScale * multiplier;
            Time.fixedDeltaTime = _fixedDeltaPolicy.Value == FixedDeltaPolicy.ScaleWithMultiplier
                ? _originalFixedDeltaTime * multiplier
                : _originalFixedDeltaTime;

            _currentMultiplier = multiplier;
            _nextDiagnosticAtRealtime = Time.realtimeSinceStartup + _diagnosticIntervalSeconds.Value;
            ShowNotification(multiplier);
            Log.LogInfo(
                $"Chronomancer set {multiplier:0.###}x via {reason}. " +
                $"timeScale={Time.timeScale:0.###}, fixedDeltaTime={Time.fixedDeltaTime:0.#####}, policy={_fixedDeltaPolicy.Value}.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to apply {multiplier:0.###}x; restoring original timing. {ex.GetType().Name}: {ex.Message}");
            RestoreOriginalTiming("apply failure");
        }
    }

    private void RestoreOriginalTiming(string reason)
    {
        Time.timeScale = _originalTimeScale;
        Time.fixedDeltaTime = _originalFixedDeltaTime;
        _currentMultiplier = 1.0f;
        _notificationUntilRealtime = 0.0f;
        Log.LogInfo(
            $"Chronomancer restored original timing via {reason}. " +
            $"timeScale={Time.timeScale:0.###}, fixedDeltaTime={Time.fixedDeltaTime:0.#####}.");
    }

    private List<float> GetAllowedPresets()
    {
        var effectiveMax = GetEffectiveMaximumMultiplier();
        var rawPresets = new[]
        {
            1.0f,
            _presetOneX.Value,
            _presetTwoX.Value,
            _presetFourX.Value,
            _presetEightX.Value,
        };
        var presets = new List<float>();

        for (var i = 0; i < rawPresets.Length; i++)
        {
            var clamped = Clamp(rawPresets[i], 1.0f, effectiveMax);
            if (!ContainsApprox(presets, clamped))
            {
                presets.Add(clamped);
            }
        }

        presets.Sort();
        return presets;
    }

    private float GetEffectiveMaximumMultiplier()
    {
        var configuredMax = Clamp(_maximumMultiplier.Value, 1.0f, DefaultEightX);
        if (_allowExperimentalEightX.Value)
        {
            return configuredMax;
        }

        if (configuredMax > DefaultFourX + Epsilon && !_loggedEightXGuard)
        {
            _loggedEightXGuard = true;
            Log.LogWarning("MaximumMultiplier above 4x is ignored until AllowExperimentalEightX=true.");
        }

        return Math.Min(configuredMax, DefaultFourX);
    }

    private float ClampToEffectiveMaximum(float value)
    {
        return Clamp(value, 1.0f, GetEffectiveMaximumMultiplier());
    }

    private static float Clamp(float value, float min, float max)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return min;
        }

        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    private static bool ContainsApprox(List<float> values, float candidate)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (Math.Abs(values[i] - candidate) <= Epsilon)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsGameplaySceneActive()
    {
        return IsSceneAllowed(SceneManager.GetActiveScene());
    }

    private bool IsSceneAllowed(Scene scene)
    {
        return scene.IsValid() &&
            string.Equals(scene.name, _gameplaySceneName.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatScene(Scene scene)
    {
        return scene.IsValid() ? $"'{scene.name}'" : "<invalid>";
    }

    private void LogDiagnosticsIfDue()
    {
        if (!_diagnosticLogging.Value || !IsAccelerated || Time.realtimeSinceStartup < _nextDiagnosticAtRealtime)
        {
            return;
        }

        _nextDiagnosticAtRealtime = Time.realtimeSinceStartup + _diagnosticIntervalSeconds.Value;
        Log.LogInfo(
            $"Chronomancer diagnostics: scene={FormatScene(SceneManager.GetActiveScene())}, " +
            $"multiplier={_currentMultiplier:0.###}x, timeScale={Time.timeScale:0.###}, " +
            $"delta={Time.deltaTime:0.#####}, unscaledDelta={Time.unscaledDeltaTime:0.#####}, " +
            $"fixedDelta={Time.fixedDeltaTime:0.#####}, safetyDepth={_safetyGuardDepth}.");
    }

    private void ShowNotification(float multiplier)
    {
        if (!_showTransientIndicator.Value)
        {
            return;
        }

        _notificationText = $"Chronomancer {multiplier:0.###}x";
        _notificationUntilRealtime = Time.realtimeSinceStartup + _indicatorSeconds.Value;
    }

    private bool IsAccelerated => _currentMultiplier > 1.0f + Epsilon;

    private static void LogAssemblyStatus()
    {
        var audit = GameAssemblyAudit.Check(Paths.GameRootPath);
        if (audit.MatchesExpected)
        {
            Log.LogInfo("Game assemblies match the audited baseline.");
            return;
        }

        Log.LogWarning("Game assemblies differ from the audited baseline. Verify timing behavior before enabling high multipliers.");
    }
}
