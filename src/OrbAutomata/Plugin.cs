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
    private AutoConceptController? _autoConceptController;
    private AutoSpellLevelController? _autoSpellLevelController;
    private AutoCastToggleControl? _autoCastToggleControl;
    private AutoCastToggleButton? _autoCastToggleButton;
    private AutoBuyToggleControl? _autoBuyToggleControl;
    private AutoBuyToggleButton? _autoBuyToggleButton;
    private AutoConceptToggleControl? _autoConceptToggleControl;
    private AutoConceptToggleButton? _autoConceptToggleButton;
    private float _autoCastUiRetrySeconds;
    private float _autoBuyUiRetrySeconds;
    private float _autoConceptUiRetrySeconds;
    private float _autoCastUiFailureSeconds;
    private float _autoConceptUiFailureSeconds;
    private bool _autoCastUiFailureLogged;
    private bool _autoConceptUiFailureLogged;
    private string _autoCastUiFailureReason = string.Empty;
    private string _autoConceptUiFailureReason = string.Empty;

    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;
        _config = AutomataConfig.Bind(Config);

        LogAssemblyStatus();

        if (!_config.Enabled.Value)
        {
            Log.LogAutomataInfo("Automata is disabled by configuration.");
            return;
        }

        _harmony = new Harmony(PluginIds.AutomataGuid);
        _harmony.PatchAll(typeof(Plugin).Assembly);

        var reservePolicy = new ReservePolicy(_config);
        _autoCastToggleControl = new AutoCastToggleControl(_config);
        _autoBuyToggleControl = new AutoBuyToggleControl(
            _config,
            () => _autoSpellLevelController?.Capability ?? AutoSpellLevelCapability.Locked);
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
        _autoConceptController = new AutoConceptController(
            _config,
            new ReflectionConceptRuntime(new AlchemyGameplayDomainClassifier()),
            Log,
            SuitePerformanceCoordinator.Shared,
            () => UnityEngine.Time.frameCount);
        _autoSpellLevelController = new AutoSpellLevelController(
            _config,
            new ReflectionSpellLevelRuntime(),
            Log,
            SuitePerformanceCoordinator.Shared,
            () => UnityEngine.Time.frameCount);
        _autoConceptToggleControl = new AutoConceptToggleControl(_config);
        AutoBuyLifecycleSignal.Invalidated += OnAutoBuyLifecycleInvalidated;
        AutoBuyLifecycleSignal.StructureQueueChanged += OnStructureQueueChanged;
        AutoBuyLifecycleSignal.UpgradeQueueChanged += OnUpgradeQueueChanged;
        AutoBuyLifecycleSignal.NativeCompletion += OnNativeCompletion;
        AutoConceptLifecycleSignal.Changed += OnAutoConceptChanged;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        Log.LogAutomataInfo(
            $"Automata loaded. AutoBuyMode={_config.AutoBuyMode.Value}, " +
            $"StructureAffordability={_config.AutoBuyAffordability.Value}, " +
            $"UpgradeAffordability={_config.UpgradeAffordability.Value}, " +
            $"AutoBuyAllowedUuidCount={CountConfiguredUuids(_config.AllowedAutoBuyUuids.Value)}, " +
            $"AutoBuyCandidateCap={_config.AutoBuyMaxCandidatesPerScan.Value}, " +
            $"AutoBuyBatchSizing={_config.AutoBuyBatchSizing.Value}, " +
            $"AutoBuyBatchSize={_config.MaxPurchasesPerBatch.Value}, " +
            $"AutoBuyStructureRepeat={_config.StructureRepeatMode.Value}, " +
            $"AutoBuyRepeatWhileAffordable={_config.RepeatWhileAffordable.Value}, " +
            $"RespectActionMultiplier={_config.RespectActionMultiplier.Value}, " +
            $"AutoCastMode={_config.AutoCastMode.Value}, " +
            $"AutoCastFullCharge={_config.AutoCastFullCharge.Value}, " +
            $"AutoCastStartResourcePercent={_config.AutoCastStartResourcePercent.Value}, " +
            $"AutoConceptMode={_config.AutoConceptMode.Value}, " +
            $"AutoConceptSlotManagement={_config.AutoConceptSlotManagement.Value}, " +
            $"AutoLevelSpells={_config.AutoLevelSpells.Value}, " +
            $"PrioritizeCostAndQualityStructures={_config.PrioritizeCostAndQualityStructures.Value}, " +
            $"OperationalLogging={_config.IsOperationalLoggingEnabled}, " +
            $"DecisionLogLevel={_config.DecisionLogLevel.Value}.");
    }

    private void Update()
    {
        var deltaTime = UnityEngine.Time.unscaledDeltaTime;
        UpdateAutoCastControls(deltaTime);
        UpdateAutoBuyControl(deltaTime);
        UpdateAutoConceptControl(deltaTime);
        if (SceneManager.GetActiveScene().name == "Main")
        {
            _autoBuyEngine?.Tick(deltaTime);
            _autoCastEngine?.Tick(deltaTime);
            _autoConceptController?.Tick(deltaTime);
            _autoSpellLevelController?.Tick(deltaTime);
        }
    }

    private void OnDestroy()
    {
        AutoBuyLifecycleSignal.Invalidated -= OnAutoBuyLifecycleInvalidated;
        AutoBuyLifecycleSignal.StructureQueueChanged -= OnStructureQueueChanged;
        AutoBuyLifecycleSignal.UpgradeQueueChanged -= OnUpgradeQueueChanged;
        AutoBuyLifecycleSignal.NativeCompletion -= OnNativeCompletion;
        AutoConceptLifecycleSignal.Changed -= OnAutoConceptChanged;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        _autoBuyEngine?.Dispose();
        _autoBuyEngine = null;
        _autoCastEngine?.Dispose();
        _autoCastEngine = null;
        _autoConceptController?.Dispose();
        _autoConceptController = null;
        _autoSpellLevelController?.Dispose();
        _autoSpellLevelController = null;
        _autoCastToggleButton?.Dispose();
        _autoCastToggleButton = null;
        _autoCastToggleControl = null;
        _autoBuyToggleButton?.Dispose();
        _autoBuyToggleButton = null;
        _autoBuyToggleControl = null;
        _autoConceptToggleButton?.Dispose();
        _autoConceptToggleButton = null;
        _autoConceptToggleControl = null;
        _harmony?.UnpatchSelf();
        _harmony = null;
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        _autoBuyEngine?.InvalidateLifecycle();
        _autoCastEngine?.InvalidateLifecycle();
        _autoConceptController?.InvalidateLifecycle();
        _autoSpellLevelController?.InvalidateLifecycle();
    }

    private void OnAutoBuyLifecycleInvalidated()
    {
        _autoBuyEngine?.InvalidateLifecycle();
        _autoCastEngine?.InvalidateLifecycle();
        _autoConceptController?.InvalidateLifecycle();
        _autoSpellLevelController?.InvalidateLifecycle();
    }

    private void OnStructureQueueChanged(object nativeIdentity)
    {
        _autoBuyEngine?.NotifyStructureQueueChanged(nativeIdentity);
    }

    private void OnNativeCompletion()
    {
        _autoBuyEngine?.NotifyNativeCompletion();
        _autoSpellLevelController?.NotifyNativeChange();
    }

    private void OnUpgradeQueueChanged(object nativeIdentity)
    {
        _autoBuyEngine?.NotifyUpgradeQueueChanged(nativeIdentity);
    }

    private void OnAutoConceptChanged()
    {
        _autoConceptController?.NotifyNativeChange();
        _autoSpellLevelController?.NotifyNativeChange();
    }

    private static void LogAssemblyStatus()
    {
        var audit = GameAssemblyAudit.Check(Paths.GameRootPath);
        if (audit.MatchesExpected)
        {
            Log.LogAutomataInfo("Game assemblies match the audited baseline.");
            return;
        }

        Log.LogAutomataWarning("Game assemblies differ from the audited baseline. Disable Automata until this game build has been validated.");
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
        Log.LogAutomataWarning($"Auto Cast toggle could not attach beside the native Auto Buy queue: {_autoCastUiFailureReason}");
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

    private void UpdateAutoConceptControl(float unscaledDeltaTime)
    {
        if (_config is null || _autoConceptToggleControl is null) return;
        var inGameplay = SceneManager.GetActiveScene().name == "Main";
        if (!inGameplay || !_config.AutoConceptShowToggleButton.Value)
        {
            _autoConceptToggleButton?.Dispose();
            _autoConceptToggleButton = null;
            _autoConceptUiRetrySeconds = 0.0f;
            _autoConceptUiFailureSeconds = 0.0f;
            _autoConceptUiFailureLogged = false;
            _autoConceptUiFailureReason = string.Empty;
            return;
        }
        if (_autoConceptToggleButton is not null && !_autoConceptToggleButton.IsAlive)
        {
            _autoConceptToggleButton.Dispose();
            _autoConceptToggleButton = null;
        }
        if (_autoConceptToggleButton is not null)
        {
            _autoConceptToggleButton.Render();
            return;
        }

        var elapsed = Math.Max(0.0f, unscaledDeltaTime);
        _autoConceptUiRetrySeconds -= elapsed;
        if (_autoConceptUiRetrySeconds > 0.0f)
        {
            _autoConceptUiFailureSeconds += elapsed;
            LogAutoConceptUiFailureIfPersistent();
            return;
        }
        _autoConceptUiRetrySeconds = UiRetryIntervalSeconds;
        if (AutoConceptToggleButton.TryCreate(
                _autoConceptToggleControl,
                out var toggle,
                out var reason))
        {
            _autoConceptToggleButton = toggle;
            _autoConceptUiFailureSeconds = 0.0f;
            _autoConceptUiFailureLogged = false;
            _autoConceptUiFailureReason = string.Empty;
            return;
        }
        _autoConceptUiFailureReason = reason;
        _autoConceptUiFailureSeconds += elapsed;
        LogAutoConceptUiFailureIfPersistent();
    }

    private void LogAutoConceptUiFailureIfPersistent()
    {
        if (_autoConceptUiFailureLogged || _autoConceptUiFailureSeconds < 10.0f) return;
        _autoConceptUiFailureLogged = true;
        Log.LogAutomataWarning(
            $"Auto Concept toggle could not attach beside the native Auto Buy queue: {_autoConceptUiFailureReason}");
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
