using System;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using OrbModding.Common;
using UnityEngine.SceneManagement;

namespace OrbAutomata;

[BepInPlugin(PluginIds.AutomataGuid, PluginIds.AutomataName, PluginIds.AutomataVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    private const float UiRetryIntervalSeconds = 5.0f;
    private const string AlchemyRecipeTypeName = "AlchemyRecipeSO";
    private Harmony? _harmony;
    private AutomataConfig? _config;
    private AutomataFeatureStatuses? _featureStatuses;
    private AutoBuyEngine? _autoBuyEngine;
    private AutoCastEngine? _autoCastEngine;
    private AutoConceptController? _autoConceptController;
    private AutoSpellLevelController? _autoSpellLevelController;
    private AutomataActionFamilyOwnership? _actionFamilyOwnership;
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
    private long _lifecycleGeneration;
    private GameLifecycleLease _lifecycleLease;
    private GameplayInvalidationBus? _invalidationBus;
    private IDisposable? _conceptInventorySubscription;
    private IDisposable? _conceptProgressionSubscription;
    private bool _knownOwnershipWarningLogged;
    private bool _nativeContractsAvailable = true;

    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;
        _config = AutomataConfig.Bind(Config);
        _lifecycleGeneration = GameLifecycleMonitor.Shared.Current.Generation;
        _featureStatuses = new AutomataFeatureStatuses(_config, _lifecycleGeneration);

        if (!LogAssemblyStatus())
        {
            _nativeContractsAvailable = false;
            _featureStatuses.ObserveContractUnavailable(
                _lifecycleGeneration,
                "Installed game assemblies do not match Automata's audited native contracts.");
            Log.LogAutomataError(
                "Automata native mutations are disabled because the installed game assemblies do not match the audited baseline.");
            return;
        }
        GameLifecycleMonitor.Shared.Transitioned += OnLifecycleTransition;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        ObserveLifecycle(GameLifecycleTransitionKind.SceneEntered, SceneManager.GetActiveScene().name);
        _lifecycleLease = GameLifecycleMonitor.Shared.CaptureLease();

        if (!_config.Enabled.Value)
        {
            Log.LogAutomataInfo("Automata is disabled by configuration.");
            return;
        }

        _harmony = new Harmony(PluginIds.AutomataGuid);
        _harmony.PatchAll(typeof(Plugin).Assembly);

        var reservePolicy = new ReservePolicy(_config);
        _actionFamilyOwnership = new AutomataActionFamilyOwnership();
        Log.LogAutomataWarning(
            "Action-family ownership is best-effort: exact known conflicts and cooperative suite owners are isolated, but unknown plugins that invoke native actions without registering cannot be proven absent and are not disabled.");
        _actionFamilyOwnership.RefreshLoadedPluginInventory(
            Chainloader.PluginInfos.Count,
            guid => Chainloader.PluginInfos.ContainsKey(guid));
        _autoCastToggleControl = new AutoCastToggleControl(
            _config,
            () => _featureStatuses.AutoCast.Current);
        _autoBuyToggleControl = new AutoBuyToggleControl(
            _config,
            () => _autoSpellLevelController?.Capability ?? AutoSpellLevelCapability.Locked,
            () => _autoBuyEngine?.LatestDecision,
            () => _featureStatuses.AutoBuy.Current,
            () => _featureStatuses.SpellLevel.Current);
        _autoBuyEngine = new AutoBuyEngine(
            _config,
            new ReflectionAutoBuyCatalog(),
            reservePolicy,
            Log,
            coordinator: SuitePerformanceCoordinator.Shared,
            readFrameIdentity: () => UnityEngine.Time.frameCount,
            featureStatus: _featureStatuses.AutoBuy,
            ownsActionFamily: kind => _actionFamilyOwnership?.OwnsAutoBuy(kind) == true);
        _autoCastEngine = new AutoCastEngine(
            _config,
            new ReflectionAutoCastCatalog(),
            reservePolicy,
            new ResourceFullnessPolicy(),
            Log,
            coordinator: SuitePerformanceCoordinator.Shared,
            readFrameIdentity: () => UnityEngine.Time.frameCount,
            featureStatus: _featureStatuses.AutoCast,
            ownsActionFamily: () => _actionFamilyOwnership?.OwnsCast == true);
        _autoConceptController = new AutoConceptController(
            _config,
            new ReflectionConceptRuntime(new AlchemyGameplayDomainClassifier()),
            Log,
            SuitePerformanceCoordinator.Shared,
            () => UnityEngine.Time.frameCount,
            _featureStatuses.AutoConcept,
            () => _actionFamilyOwnership?.OwnsConcept == true);
        _autoSpellLevelController = new AutoSpellLevelController(
            _config,
            new ReflectionSpellLevelRuntime(),
            Log,
            SuitePerformanceCoordinator.Shared,
            () => UnityEngine.Time.frameCount,
            _featureStatuses.SpellLevel,
            () => _actionFamilyOwnership?.OwnsSpellLevel == true);
        _autoConceptToggleControl = new AutoConceptToggleControl(
            _config,
            () => _featureStatuses.AutoConcept.Current);
        AutoBuyLifecycleSignal.Invalidated += OnAutoBuyLifecycleInvalidated;
        AutoBuyLifecycleSignal.StructureQueueChanged += OnStructureQueueChanged;
        AutoBuyLifecycleSignal.UpgradeQueueChanged += OnUpgradeQueueChanged;
        AutoBuyLifecycleSignal.NativeCompletion += OnNativeCompletion;
        AutoConceptLifecycleSignal.InventoryChanged += OnAutoConceptInventoryChanged;
        AutoConceptLifecycleSignal.ProgressionChanged += OnAutoConceptProgressionChanged;
        _invalidationBus = GameplayInvalidationBus.Shared;
        _conceptInventorySubscription = _invalidationBus.Subscribe(
            new GameplayInvalidationFilter(
                GameplayInvalidationKind.Inventory,
                GameplayInvalidationDomains.AutomataConcepts),
            OnAutoConceptInventoryInvalidated,
            "OrbAutomata.AutoConcept.Inventory");
        _conceptProgressionSubscription = _invalidationBus.Subscribe(
            new GameplayInvalidationFilter(
                GameplayInvalidationKind.Progression,
                GameplayInvalidationDomains.AutomataConcepts),
            OnAutoConceptProgressionInvalidated,
            "OrbAutomata.AutoConcept.Progression");
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
        if (_config is null) return;
        if (!_nativeContractsAvailable)
        {
            _featureStatuses?.ObserveContractUnavailable(
                _lifecycleGeneration,
                "Installed game assemblies do not match Automata's audited native contracts.");
            return;
        }
        if (!_config.Enabled.Value)
        {
            CancelPreparedAutomationForOwnershipRelease();
            _actionFamilyOwnership?.Refresh(_config, lifecycleReady: false, UnityEngine.Time.frameCount);
            return;
        }
        var deltaTime = UnityEngine.Time.unscaledDeltaTime;
        var lifecycleReady = IsLifecycleReady();
        _actionFamilyOwnership?.RefreshLoadedPluginInventory(
            Chainloader.PluginInfos.Count,
            guid => Chainloader.PluginInfos.ContainsKey(guid));
        if (!_knownOwnershipWarningLogged &&
            _actionFamilyOwnership?.KnownAutoBuyLoaded == true)
        {
            _knownOwnershipWarningLogged = true;
            Log.LogAutomataWarning(
                "AutobuyOrb is loaded. Automata will block Structure and Upgrade purchases because those native action families overlap; Auto Cast, Auto Concept, Spell Leveling, and Mentor remain independent.");
        }
        _actionFamilyOwnership?.Refresh(_config, lifecycleReady, UnityEngine.Time.frameCount);
        UpdateAutoCastControls(deltaTime);
        UpdateAutoBuyControl(deltaTime);
        UpdateAutoConceptControl(deltaTime);
        if (_config is not null &&
            (_config.CanStartAutoBuyActively ||
             _config.CanStartAutoCastActively ||
             _config.CanStartAutoConceptActively))
        {
            _invalidationBus?.Pump(
                UnityEngine.Time.frameCount,
                GameplayInvalidationBus.DefaultMaxOperationsPerFrame);
        }
        if (lifecycleReady)
        {
            _autoBuyEngine?.Tick(deltaTime);
            _autoCastEngine?.Tick(deltaTime);
            _autoConceptController?.Tick(deltaTime);
            _autoSpellLevelController?.Tick(deltaTime);
        }
        else if (_config is not null)
        {
            _featureStatuses?.ObserveLifecycleNotReady(_config, _lifecycleGeneration);
        }
    }

    private void OnDestroy()
    {
        AutoBuyLifecycleSignal.Invalidated -= OnAutoBuyLifecycleInvalidated;
        AutoBuyLifecycleSignal.StructureQueueChanged -= OnStructureQueueChanged;
        AutoBuyLifecycleSignal.UpgradeQueueChanged -= OnUpgradeQueueChanged;
        AutoBuyLifecycleSignal.NativeCompletion -= OnNativeCompletion;
        AutoConceptLifecycleSignal.InventoryChanged -= OnAutoConceptInventoryChanged;
        AutoConceptLifecycleSignal.ProgressionChanged -= OnAutoConceptProgressionChanged;
        _conceptInventorySubscription?.Dispose();
        _conceptInventorySubscription = null;
        _conceptProgressionSubscription?.Dispose();
        _conceptProgressionSubscription = null;
        _invalidationBus = null;
        GameLifecycleMonitor.Shared.Transitioned -= OnLifecycleTransition;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        _autoBuyEngine?.Dispose();
        _autoBuyEngine = null;
        _autoCastEngine?.Dispose();
        _autoCastEngine = null;
        _autoConceptController?.Dispose();
        _autoConceptController = null;
        _autoSpellLevelController?.Dispose();
        _autoSpellLevelController = null;
        _actionFamilyOwnership?.Dispose();
        _actionFamilyOwnership = null;
        _autoCastToggleButton?.Dispose();
        _autoCastToggleButton = null;
        _autoCastToggleControl = null;
        _autoBuyToggleButton?.Dispose();
        _autoBuyToggleButton = null;
        _autoBuyToggleControl = null;
        _autoConceptToggleButton?.Dispose();
        _autoConceptToggleButton = null;
        _autoConceptToggleControl = null;
        _featureStatuses?.Dispose();
        _featureStatuses = null;
        _harmony?.UnpatchSelf();
        _harmony = null;
    }

    private void OnDisable()
    {
        CancelPreparedAutomationForOwnershipRelease();
        _actionFamilyOwnership?.ReleaseLifecycleClaims();
    }

    private void CancelPreparedAutomationForOwnershipRelease()
    {
        _autoBuyEngine?.CancelPreparedWork();
        _autoCastEngine?.CancelPreparedWork();
        _autoConceptController?.CancelPreparedWork();
        _autoSpellLevelController?.CancelPreparedWork();
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        ObserveLifecycle(GameLifecycleTransitionKind.SceneExited, previous.name);
        ObserveLifecycle(GameLifecycleTransitionKind.SceneEntered, next.name);
    }

    private void OnAutoBuyLifecycleInvalidated(GameLifecycleTransitionKind kind, object? nativeIdentity)
    {
        ObserveLifecycle(kind, SceneManager.GetActiveScene().name, nativeIdentity);
    }

    private void OnLifecycleTransition(GameLifecycleTransition transition)
    {
        if (transition.Current.Generation == _lifecycleGeneration) return;
        _lifecycleGeneration = transition.Current.Generation;
        _autoBuyEngine?.InvalidateLifecycle();
        _autoCastEngine?.InvalidateLifecycle();
        _autoConceptController?.InvalidateLifecycle();
        _autoSpellLevelController?.InvalidateLifecycle();
        _actionFamilyOwnership?.ReleaseLifecycleClaims();
        if (_config is not null)
            _featureStatuses?.ObserveLifecycleNotReady(_config, _lifecycleGeneration);
        _lifecycleLease = GameLifecycleMonitor.Shared.CaptureLease();
    }

    private static void ObserveLifecycle(
        GameLifecycleTransitionKind kind,
        string sceneName,
        object? nativeIdentity = null)
    {
        GameLifecycleMonitor.Shared.TryObserve(
            new GameLifecycleObservation(
                kind,
                UnityEngine.Time.frameCount,
                sceneName,
                PluginIds.AutomataGuid,
                nativeIdentity),
            out _,
            out _);
    }

    private void OnStructureQueueChanged(object nativeIdentity)
    {
        if (!IsLifecycleReady()) return;
        _autoBuyEngine?.NotifyStructureQueueChanged(nativeIdentity);
        PublishAutoBuyInvalidation(
            GameplayInvalidationKind.Queue,
            nativeIdentity,
            AutoBuyCandidateKind.Structure,
            GameplayInvalidationDomains.AutomataStructures,
            "StructureSO.QueueBuild");
    }

    private void OnNativeCompletion(object nativeIdentity, AutoBuyCandidateKind completedKind)
    {
        if (!IsLifecycleReady()) return;
        _autoBuyEngine?.NotifyNativeCompletion(nativeIdentity, completedKind);
        _autoSpellLevelController?.NotifyNativeChange();
        PublishAutoBuyInvalidation(
            GameplayInvalidationKind.Progression | GameplayInvalidationKind.Queue,
            nativeIdentity,
            completedKind,
            completedKind == AutoBuyCandidateKind.Structure
                ? GameplayInvalidationDomains.AutomataStructures
                : GameplayInvalidationDomains.AutomataUpgrades,
            completedKind == AutoBuyCandidateKind.Structure
                ? "StructureSO.CompleteAction"
                : "UpgradeSO.CompleteAction");
    }

    private void OnUpgradeQueueChanged(object nativeIdentity)
    {
        if (!IsLifecycleReady()) return;
        _autoBuyEngine?.NotifyUpgradeQueueChanged(nativeIdentity);
        PublishAutoBuyInvalidation(
            GameplayInvalidationKind.Queue,
            nativeIdentity,
            AutoBuyCandidateKind.Upgrade,
            GameplayInvalidationDomains.AutomataUpgrades,
            "UpgradeSO.Purchase");
    }

    private void OnAutoConceptInventoryChanged(object? nativeRecipe)
    {
        PublishAutoConceptInvalidation(
            GameplayInvalidationKind.Inventory,
            nativeRecipe,
            "AlchemyInstanceListVariable");
    }

    private void OnAutoConceptProgressionChanged(object nativeRecipe)
    {
        PublishAutoConceptInvalidation(
            GameplayInvalidationKind.Progression,
            nativeRecipe,
            "AlchemyRecipeSO.Progression");
    }

    private void OnAutoConceptInventoryInvalidated(GameplayInvalidation _)
    {
        _autoConceptController?.NotifyNativeChange();
    }

    private void OnAutoConceptProgressionInvalidated(GameplayInvalidation _)
    {
        _autoConceptController?.NotifyNativeChange();
        _autoSpellLevelController?.NotifyNativeChange();
    }

    private void PublishAutoBuyInvalidation(
        GameplayInvalidationKind kind,
        object nativeIdentity,
        AutoBuyCandidateKind candidateKind,
        string domain,
        string source)
    {
        if (_invalidationBus is null) return;
        if (_autoBuyEngine is not null &&
            _autoBuyEngine.TryResolveInvalidationTarget(
                nativeIdentity,
                candidateKind,
                out var entityId,
                out var expectedTypeName))
        {
            _invalidationBus.Publish(
                kind,
                UnityEngine.Time.frameCount,
                domain,
                entityId,
                expectedTypeName,
                source);
            return;
        }

        _invalidationBus.Publish(
            kind,
            UnityEngine.Time.frameCount,
            domain,
            source: source);
    }

    private void PublishAutoConceptInvalidation(
        GameplayInvalidationKind kind,
        object? nativeRecipe,
        string source)
    {
        if (_invalidationBus is null) return;
        if (nativeRecipe is not null &&
            _autoConceptController is not null &&
            _autoConceptController.TryResolveInvalidationEntityId(nativeRecipe, out var entityId))
        {
            _invalidationBus.Publish(
                kind,
                UnityEngine.Time.frameCount,
                GameplayInvalidationDomains.AutomataConcepts,
                entityId,
                AlchemyRecipeTypeName,
                source);
            return;
        }

        _invalidationBus.Publish(
            kind,
            UnityEngine.Time.frameCount,
            GameplayInvalidationDomains.AutomataConcepts,
            source: source);
    }

    private bool IsLifecycleReady() =>
        SceneManager.GetActiveScene().name == "Main" &&
        GameLifecycleMonitor.Shared.Current.IsGameplayReady &&
        GameLifecycleMonitor.Shared.IsCurrent(_lifecycleLease);

    internal static bool AssemblyAuditAllowsMutation(AssemblyAuditResult audit) => audit.MatchesExpected;

    private static bool LogAssemblyStatus()
    {
        var audit = GameAssemblyAudit.Check(Paths.GameRootPath);
        if (AssemblyAuditAllowsMutation(audit))
        {
            Log.LogAutomataInfo("Game assemblies match the audited baseline.");
            return true;
        }

        Log.LogAutomataWarning("Game assemblies differ from the audited baseline. Automata native mutations will remain disabled until this game build has been validated.");
        return false;
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
