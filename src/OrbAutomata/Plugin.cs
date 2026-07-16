using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using OrbModding.Common;
using UnityEngine.SceneManagement;

namespace OrbAutomata;

[BepInPlugin(PluginIds.AutomataGuid, PluginIds.AutomataName, PluginIds.AutomataVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    private const float UiRetryIntervalSeconds = 5.0f;
    private Harmony? _harmony;
    private AutomataConfig? _config;
    private AutoBuyEngine? _autoBuyEngine;
    private AutoCastEngine? _autoCastEngine;
    private AutoCastToggleControl? _autoCastToggleControl;
    private AutoCastToggleButton? _autoCastToggleButton;
    private AutoBuyToggleControl? _autoBuyToggleControl;
    private AutoBuyToggleButton? _autoBuyToggleButton;
    private float _autoCastUiRetrySeconds;
    private float _autoBuyUiRetrySeconds;
    private float _autoCastUiFailureSeconds;
    private bool _autoCastUiFailureLogged;
    private string _autoCastUiFailureReason = string.Empty;

    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;
        _config = AutomataConfig.Bind(Config);

        LogAssemblyStatus();

        if (!_config.Enabled.Value)
        {
            Log.LogInfo("Automata is disabled by configuration.");
            return;
        }

        _harmony = new Harmony(PluginIds.AutomataGuid);
        _harmony.PatchAll(typeof(Plugin).Assembly);

        var reservePolicy = new ReservePolicy(_config);
        _autoCastToggleControl = new AutoCastToggleControl(_config);
        _autoBuyToggleControl = new AutoBuyToggleControl(_config);
        _autoBuyEngine = new AutoBuyEngine(
            _config,
            new ReflectionAutoBuyCatalog(),
            reservePolicy,
            Log,
            coordinator: SuitePerformanceCoordinator.Shared,
            readFrameIdentity: () => UnityEngine.Time.frameCount);
        _autoCastEngine = new AutoCastEngine(
            _config,
            new ReflectionAutoCastCatalog(),
            reservePolicy,
            new ResourceFullnessPolicy(),
            Log,
            coordinator: SuitePerformanceCoordinator.Shared,
            readFrameIdentity: () => UnityEngine.Time.frameCount);
        AutoBuyLifecycleSignal.Invalidated += OnAutoBuyLifecycleInvalidated;
        AutoBuyLifecycleSignal.StructureQueueChanged += OnStructureQueueChanged;
        AutoBuyLifecycleSignal.UpgradeQueueChanged += OnUpgradeQueueChanged;
        AutoBuyLifecycleSignal.NativeCompletion += OnNativeCompletion;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        Log.LogInfo(
            $"Automata loaded. AutoBuyMode={_config.AutoBuyMode.Value}, " +
            $"StructureAffordability={_config.AutoBuyAffordability.Value}, " +
            $"UpgradeAffordability={_config.UpgradeAffordability.Value}, " +
            $"AutoBuyAllowedUuidCount={CountConfiguredUuids(_config.AllowedAutoBuyUuids.Value)}, " +
            $"AutoBuyCandidateCap={_config.AutoBuyMaxCandidatesPerScan.Value}, " +
            $"AutoBuyBatchSizing={_config.AutoBuyBatchSizing.Value}, " +
            $"AutoBuyBatchSize={_config.MaxPurchasesPerBatch.Value}, " +
            $"AutoBuyStructureRepeat={_config.StructureRepeatMode.Value}, " +
            $"RespectActionMultiplier={_config.RespectActionMultiplier.Value}, " +
            $"AutoCastMode={_config.AutoCastMode.Value}, " +
            $"AutoCastStartResourcePercent={_config.AutoCastStartResourcePercent.Value}, " +
            $"OperationalLogging={_config.EnableOperationalLogging.Value}.");
    }

    private void Update()
    {
        var deltaTime = UnityEngine.Time.unscaledDeltaTime;
        UpdateAutoCastControls(deltaTime);
        UpdateAutoBuyControl(deltaTime);
        if (SceneManager.GetActiveScene().name == "Main")
        {
            _autoBuyEngine?.Tick(deltaTime);
            _autoCastEngine?.Tick(deltaTime);
        }
    }

    private void OnDestroy()
    {
        AutoBuyLifecycleSignal.Invalidated -= OnAutoBuyLifecycleInvalidated;
        AutoBuyLifecycleSignal.StructureQueueChanged -= OnStructureQueueChanged;
        AutoBuyLifecycleSignal.UpgradeQueueChanged -= OnUpgradeQueueChanged;
        AutoBuyLifecycleSignal.NativeCompletion -= OnNativeCompletion;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        _autoBuyEngine?.Dispose();
        _autoBuyEngine = null;
        _autoCastEngine?.Dispose();
        _autoCastEngine = null;
        _autoCastToggleButton?.Dispose();
        _autoCastToggleButton = null;
        _autoCastToggleControl = null;
        _autoBuyToggleButton?.Dispose();
        _autoBuyToggleButton = null;
        _autoBuyToggleControl = null;
        _harmony?.UnpatchSelf();
        _harmony = null;
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        _autoBuyEngine?.InvalidateLifecycle();
    }

    private void OnAutoBuyLifecycleInvalidated()
    {
        _autoBuyEngine?.InvalidateLifecycle();
    }

    private void OnStructureQueueChanged(object nativeIdentity)
    {
        _autoBuyEngine?.NotifyStructureQueueChanged(nativeIdentity);
    }

    private void OnNativeCompletion()
    {
        _autoBuyEngine?.NotifyNativeCompletion();
    }

    private void OnUpgradeQueueChanged(object nativeIdentity)
    {
        _autoBuyEngine?.NotifyUpgradeQueueChanged(nativeIdentity);
    }

    private static void LogAssemblyStatus()
    {
        var audit = GameAssemblyAudit.Check(Paths.GameRootPath);
        if (audit.MatchesExpected)
        {
            Log.LogInfo("Game assemblies match the audited baseline.");
            return;
        }

        Log.LogWarning("Game assemblies differ from the audited baseline. Disable Automata until this game build has been validated.");
    }

    private void UpdateAutoCastControls(float unscaledDeltaTime)
    {
        if (_config is null || _autoCastToggleControl is null)
        {
            return;
        }

        var inGameplay = SceneManager.GetActiveScene().name == "Main";
        if (inGameplay && _config.AutoCastToggleShortcut.Value.IsDown())
        {
            _autoCastToggleControl.Toggle();
        }

        if (!inGameplay || !_config.AutoCastShowToggleButton.Value)
        {
            _autoCastToggleButton?.Dispose();
            _autoCastToggleButton = null;
            _autoCastUiRetrySeconds = 0.0f;
            _autoCastUiFailureSeconds = 0.0f;
            _autoCastUiFailureLogged = false;
            return;
        }

        if (_autoCastToggleButton is not null && !_autoCastToggleButton.IsAlive)
        {
            _autoCastToggleButton.Dispose();
            _autoCastToggleButton = null;
        }

        if (_autoCastToggleButton is not null)
        {
            _autoCastToggleButton.Render();
            return;
        }

        var elapsed = Math.Max(0.0f, unscaledDeltaTime);
        _autoCastUiRetrySeconds -= elapsed;
        if (_autoCastUiRetrySeconds > 0.0f)
        {
            _autoCastUiFailureSeconds += elapsed;
            LogAutoCastUiFailureIfPersistent();
            return;
        }

        _autoCastUiRetrySeconds = UiRetryIntervalSeconds;
        if (AutoCastToggleButton.TryCreate(_autoCastToggleControl, Log, out var toggle, out var reason))
        {
            _autoCastToggleButton = toggle;
            _autoCastUiFailureSeconds = 0.0f;
            _autoCastUiFailureLogged = false;
            _autoCastUiFailureReason = string.Empty;
            return;
        }

        _autoCastUiFailureReason = reason;
        _autoCastUiFailureSeconds += elapsed;
        LogAutoCastUiFailureIfPersistent();
    }

    private void LogAutoCastUiFailureIfPersistent()
    {
        if (_autoCastUiFailureLogged || _autoCastUiFailureSeconds < 10.0f)
        {
            return;
        }

        _autoCastUiFailureLogged = true;
        Log.LogWarning($"Auto Cast toggle could not attach beside the native Auto Buy queue: {_autoCastUiFailureReason}");
    }

    private void UpdateAutoBuyControl(float unscaledDeltaTime)
    {
        if (_autoBuyToggleControl is null) return;
        if (SceneManager.GetActiveScene().name != "Main")
        {
            _autoBuyToggleButton?.Dispose();
            _autoBuyToggleButton = null;
            _autoBuyUiRetrySeconds = 0.0f;
            return;
        }
        if (_autoBuyToggleButton is not null && !_autoBuyToggleButton.IsAlive)
        {
            _autoBuyToggleButton.Dispose();
            _autoBuyToggleButton = null;
        }
        if (_autoBuyToggleButton is not null) { _autoBuyToggleButton.Render(); return; }
        _autoBuyUiRetrySeconds -= Math.Max(0.0f, unscaledDeltaTime);
        if (_autoBuyUiRetrySeconds > 0.0f) return;
        _autoBuyUiRetrySeconds = UiRetryIntervalSeconds;
        AutoBuyToggleButton.TryCreate(_autoBuyToggleControl, out _autoBuyToggleButton);
    }

    private static int CountConfiguredUuids(string value)
    {
        return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }
}
