using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using OrbAutomata;
using OrbMentor;
using OrbModConfig;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Control;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace.Control;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.World;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Control;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbModding;

/// <summary>
/// The suite's single BepInEx entry point. One DLL, one loader identity, one configuration file:
/// automation, mastery catch-up and the configuration browser share one lifecycle. On an unknown
/// complete game build, only the browser and verifier load until the exact pair is acknowledged.
/// </summary>
[BepInPlugin(PluginIds.SuiteGuid, PluginIds.SuiteName, PluginIds.Version)]
public sealed class Plugin : BaseUnityPlugin
{
#if SERVICE_CYCLE_PROFILE
    private const bool AutoStartServiceCycleDiagnostics = true;
    private static readonly KeyboardShortcut UiValidationContinueShortcut =
        new((KeyCode)293);
    private static readonly KeyboardShortcut UiValidationOpenModsShortcut =
        new((KeyCode)292);
    private static readonly KeyboardShortcut UiValidationNextPageShortcut =
        new((KeyCode)291);
#else
    private const bool AutoStartServiceCycleDiagnostics = false;
#endif
    private const float UiRetryIntervalSeconds = 5.0f;
    private const float UiInstallDelaySeconds = 2.0f;
    private const float UiIntegrityIntervalSeconds = 5.0f;

    /// <summary>
    /// Every patch class this plugin installs, named one by one. Scanning an assembly for
    /// <see cref="HarmonyPatch"/> classes would silently adopt whatever else ends up compiled
    /// alongside it — which is the whole suite now that it ships as one DLL. An explicit list can
    /// only widen in a diff.
    /// </summary>
    internal static readonly Type[] HarmonyPatchTypes =
    {
        typeof(SpellFirePatch),
        typeof(MentorSpellMasteryPatch),
        typeof(MentorAlchemyMasteryPatch),
        typeof(MentorArtifactTickPatch),
        typeof(MentorArtifactExperiencePatch),
    };

    /// <summary>
    /// The native transitions the whole suite watches: a save loading, the game initialising, a
    /// runtime reset, a New Game+. They are what moves <see cref="GameLifecycleMonitor"/>'s
    /// generation, and with it the collected epoch that the world collector's structural-fact skip
    /// and Auto Buy's boundary check compare against — an unobserved save-load leaves both comparing
    /// stale to stale. They are installed from <see cref="ComposeAutomata"/> rather than beside
    /// Mentor's optional hooks, where they used to sit, for the reason W56 already gives for the
    /// completion postfix: that composition returns early when Mentor's own mastery hook is
    /// unavailable, and a blocked Mentor must not take the suite's lifecycle observation down with
    /// it. Named here rather than written inline so losing one is a diff rather than an omission.
    /// </summary>
    internal static readonly (string Target, string Handler, bool Postfix)[] LifecycleObservationHooks =
    {
        ("SaveStateManager:ImplementLoadedJson", nameof(BeforeSaveLoad), false),
        ("SaveStateManager:ImplementLoadedJson", nameof(AfterSaveLoaded), true),
        ("GameManager:InitGame", nameof(AfterGameInitialized), true),
        ("GameManager:ResetGameState", nameof(BeforeRuntimeReset), false),
        ("PersistentResetManager:PersistentResetLogic", nameof(BeforePersistentReset), false),
    };

    private Harmony? _harmony;

    private BepInExAutomataConfiguration? _automataConfig;
    private AutomataConfigurationStore? _configurationStore;
    private string? _shortcutAuditSignature;
    private AutomataFeatureStatuses? _featureStatuses;
    private readonly SpellLevelCapabilityState _spellLevelCapability = new();

    // Held by the plugin rather than by the feature because the Harmony patch that feeds it outlives
    // any one registration: the hook is installed once and the service is registered per lifecycle.
    private readonly AutoCastManualPauseState _autoCastManualPause = new();
    private readonly MentorMasteryEventJournal _mentorMasteryJournal = new();
    private AutomataActionFamilyOwnership? _automataActionFamilyOwnership;
    private AutomataServiceCycleActivation? _serviceCycleActivation;
    private AutoCastToggleControl? _autoCastToggleControl;
    private AutoCastToggleButton? _autoCastToggleButton;
    private AutoBuyToggleControl? _autoBuyToggleControl;
    private AutoBuyToggleButton? _autoBuyToggleButton;
    private AutoConceptToggleControl? _autoConceptToggleControl;
    private AutoConceptToggleButton? _autoConceptToggleButton;
    private AutoHarvestToggleControl? _autoHarvestToggleControl;
    private AutoHarvestToggleButton? _autoHarvestToggleButton;
    private EmergencyStopControl? _emergencyStopControl;
    private EmergencyStopButton? _emergencyStopButton;
    private AutomataDifferentialVerificationControl? _mathVerification;
    private float _autoCastUiRetrySeconds;
    private float _autoBuyUiRetrySeconds;
    private float _autoConceptUiRetrySeconds;
    private float _autoHarvestUiRetrySeconds;
    private string _autoBuyUiFailureReason = string.Empty;
    private string _autoCastUiFailureReason = string.Empty;
    private string _autoConceptUiFailureReason = string.Empty;
    private string _autoHarvestUiFailureReason = string.Empty;
    private string _mentorUiFailureReason = string.Empty;
    private string _emergencyStopUiFailureReason = string.Empty;
    private float _quickStripFailureSeconds;
    private bool _knownOwnershipWarningLogged;
    private bool _nativeContractsAvailable = true;
    private bool _auditedBuild;
    private bool _runtimeActivationAllowed;
    private string _observedBuildFingerprint = string.Empty;
    private bool _runtimeComposed;
    private bool _runtimeCompositionAttempted;
    private float _emergencyStopUiRetrySeconds;

    private MentorConfig? _mentorConfig;
    private MentorActionFamilyOwnership? _mentorActionFamilyOwnership;
    private MentorToggleButton? _mentorButton;
    private float _mentorUiRetrySeconds;

    private ModConfigSettings? _modConfigSettings;
    private float _mainSceneElapsed;
    private float _uiRetrySeconds;
    private float _uiIntegritySeconds;
    private bool _uiFailureLogged;
    private int _uiFailureAttempts;
    private bool _uiMaintenanceDue;
    private bool _uiIntegrityDue;
    private int _deferInstallUntilFrame;
    private ModConfigUiShell? _uiShell;
    private ConfigCatalogSnapshot? _catalog;
    private ConfigCatalogGeneration _catalogGeneration;
    private ModConfigNavigationBookmark _catalogNavigation = ModConfigNavigationBookmark.Runtime;
    private ModConfigFrameWork? _uiWork;
    private ModConfigRuntimeSources? _runtimeSources;
    private ModConfigFeatureCommands? _modConfigFeatureCommands;
    private SuiteUiSurfaceDiagnostics? _uiSurfaceDiagnostics;
    private Action? _runUiMaintenance;

    // One lifecycle generation, one lease and one invalidation bus for the whole suite: the three
    // plugins each tracked their own copies of the same shared monitor and the same shared bus.
    private long _lifecycleGeneration;
    private GameLifecycleLease _lifecycleLease;
    private GameplayInvalidationBus? _invalidationBus;

    internal static Plugin? Instance { get; private set; }

    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        // First, before configuration or anything else. An incomplete audit refuses everything. A
        // complete unknown pair may load only the control plane; mutation remains quarantined until
        // the exact pair is explicitly accepted.
        var loadDecision = SuiteLoadGate.Evaluate(Paths.GameRootPath);
        if (!loadDecision.CanLoadControlPlane)
        {
            Logger.LogError(loadDecision.Message);
            return;
        }

        var configuration = SuiteConfiguration.TryBind(Config);
        if (!configuration.Success)
        {
            Logger.LogError(configuration.Status.Reason);
            return;
        }
        var suite = configuration.Config!;
        _automataConfig = suite.Automata;
        _mentorConfig = suite.Mentor;
        _modConfigSettings = suite.ModConfig;
        _auditedBuild = loadDecision.ShouldLoad;
        _observedBuildFingerprint = loadDecision.ObservedBuildFingerprint;
        var compatibility = UnverifiedBuildCompatibilityPolicy.AtStartup(
            _auditedBuild,
            _observedBuildFingerprint,
            _automataConfig.AllowUnverifiedGameBuild.Value,
            _automataConfig.AcceptedUnverifiedBuildFingerprint.Value);
        if (compatibility.ResetOverride)
            _automataConfig.SetAllowUnverifiedGameBuild(false);
        if (compatibility.EngageEmergencyStop)
            _automataConfig.SetEmergencyStop(true);
        _runtimeActivationAllowed = compatibility.RuntimeAllowed;
        _nativeContractsAvailable = _runtimeActivationAllowed;
        foreach (var diagnostic in configuration.Diagnostics)
            Logger.LogInfo($"Configuration migration {diagnostic.Kind}: {diagnostic.Source}; {diagnostic.Detail}");

        _lifecycleGeneration = GameLifecycleMonitor.Shared.Current.Generation;
        _configurationStore = new AutomataConfigurationStore(
            _automataConfig,
            PublishConfiguration);
        _featureStatuses = new AutomataFeatureStatuses(
            _configurationStore.Current,
            _lifecycleGeneration,
            configurationGeneration: _configurationStore.CurrentGeneration);
        _mathVerification = new AutomataDifferentialVerificationControl(
            message => Log.LogAutomataInfo(message));
        _emergencyStopControl = new EmergencyStopControl(
            _configurationStore,
            ReadEmergencyStopResumePreview,
            OnEmergencyStopChanged,
            canResume: () => _runtimeActivationAllowed);
        ValidateSuiteShortcuts();

        if (_auditedBuild)
            Log.LogAutomataInfo(loadDecision.Message);
        else
        {
            Log.LogAutomataWarning(loadDecision.Message);
            if (_runtimeActivationAllowed)
            {
                Log.LogAutomataWarning(
                    "A persisted acknowledgement matches this exact unverified assembly pair. Runtime composition is permitted at the player's own risk.");
            }
        }
        GameLifecycleMonitor.Shared.Transitioned += OnLifecycleTransition;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        ObserveLifecycle(GameLifecycleTransitionKind.SceneEntered, SceneManager.GetActiveScene().name);
        _lifecycleLease = GameLifecycleMonitor.Shared.CaptureLease();

        ComposeModConfig();
        if (_runtimeActivationAllowed && _configurationStore.Current.General.Enabled)
        {
            EnsureRuntimeComposition();
        }
        else if (!_runtimeActivationAllowed)
        {
            Log.LogAutomataWarning(
                "Compatibility emergency stop is active. Clear Emergency disable in Mods > General to accept and resume, or use Advanced to accept while keeping STOP engaged.");
        }
        else
        {
            Log.LogAutomataInfo(
                "Orb Of Creation automation is disabled by General/Enabled; configuration and emergency recovery remain available.");
        }
    }

    private void EnsureRuntimeComposition()
    {
        if (_runtimeComposed) return;
        _runtimeCompositionAttempted = true;
        _harmony = new Harmony(PluginIds.SuiteGuid);
        MentorMasteryPatchBridge.Install(
            _mentorMasteryJournal,
            () => GameLifecycleMonitor.Shared.Current.Generation);
        foreach (var patchType in HarmonyPatchTypes)
            _harmony.CreateClassProcessor(patchType).Patch();
        ComposeAutomata();
        _runtimeComposed = true;
    }

    private void ComposeAutomata()
    {
        var featureStatuses = _featureStatuses!;

        _automataActionFamilyOwnership = new AutomataActionFamilyOwnership();
        _mentorActionFamilyOwnership = new MentorActionFamilyOwnership();
        Log.LogAutomataWarning(
            "Action-family ownership is best-effort: exact known conflicts and cooperative suite owners are isolated, but unknown plugins that invoke native actions without registering cannot be proven absent and are not disabled.");
        _automataActionFamilyOwnership.RefreshLoadedPluginInventory(
            Chainloader.PluginInfos.Count,
            guid => Chainloader.PluginInfos.ContainsKey(guid));
        _autoCastToggleControl = new AutoCastToggleControl(
            _configurationStore!,
            () => featureStatuses.AutoCast.Current);
        _autoBuyToggleControl = new AutoBuyToggleControl(
            _configurationStore!,
            () => _spellLevelCapability.Current,
            () => featureStatuses.AutoBuy.Current,
            () => featureStatuses.SpellLevel.Current);
        _autoHarvestToggleControl = new AutoHarvestToggleControl(
            _configurationStore!,
            () => featureStatuses.AutoHarvest.Current);
        Func<long> readAutoHarvestLifecycleEpoch =
            () => GameLifecycleMonitor.Shared.Current.Generation;
        var autoHarvestRegistryResolver = TypedRegistryResolver.Shared;
        _serviceCycleActivation = new AutomataServiceCycleActivation(
            IsLifecycleReady,
            (configuration, configurationGeneration) =>
            {
                // One frame counter shared by every feature below. Resolving it per feature
                // would let two services disagree about what frame it is — a wiring mistake
                // that compiles and looks like a quiet game. The world publication is not
                // here at all: the registry owns it, because there is one game.
                Func<long> readFrameIdentity = static () => Time.frameCount;
                return AutomataServiceCycleComposition.TryCreate(
                    configuration,
                    configurationGeneration,
                    new AutomataServiceCycleHostDependencies(
                        readFrameIdentity,
                        readAutoHarvestLifecycleEpoch,
                        pumpTiming: ServiceCyclePumpTimingRegistry.Shared,
                        observability: new AutomataServiceCycleObservabilityOptions(
                            AutomataFullTracePathPolicy.Create(
                                ManualFullTraceControlRegistry.Shared),
                            AutomataDecisionJournalPathPolicy.Create(
                                DecisionJournalStatusRegistry.Shared),
                            AutomataHostTraceDumpPathPolicy.Create(
                                HostTraceDumpRegistry.Shared),
                            AutoStartServiceCycleDiagnostics)),
                    new IAutomataServiceCycleFeature[]
                    {
                        // Registered first so the world is collected before the services that
                        // read it evaluate. Ordering between services is not enforced and this
                        // does not make it so — it only avoids every consumer's first cycle
                        // reading the empty snapshot for no reason.
                        new AutomataWorldCollectionFeature(
                            readFrameIdentity,
                            readAutoHarvestLifecycleEpoch,
                            static report =>
                            {
                                if (report.IsComplete) Log.LogInfo(report.Describe());
                                else Log.LogWarning(report.Describe());
                            },
                            createCollector: () =>
                                new GameWorldCollector(_mentorMasteryJournal)),
                        new AutoHarvestServiceCycleFeature(
                            new AutoHarvestFeatureDependencies(
                                autoHarvestRegistryResolver,
                                ownsActionFamily: () => _automataActionFamilyOwnership!.OwnsHarvest,
                                tryCaptureMutationPermit: () =>
                                    _automataActionFamilyOwnership!.TryCaptureHarvestMutationPermit(),
                                runtimeDiagnostics: RuntimeDiagnosticsRegistry.Shared,
                                featureStatus: featureStatuses.AutoHarvest)),
                        new AutoItemsServiceCycleFeature(
                            new AutoItemsFeatureDependencies(
                                autoHarvestRegistryResolver,
                                readAutoHarvestLifecycleEpoch,
                                ownsActionFamily: () =>
                                    _automataActionFamilyOwnership!.OwnsItems,
                                captureMutationPermit: () =>
                                    _automataActionFamilyOwnership!.TryCaptureItemMutationPermit(),
                                featureStatus: featureStatuses.AutoItems)),
                        new AutoBuyServiceCycleFeature(
                            new AutoBuyFeatureDependencies(
                                readAutoHarvestLifecycleEpoch,
                                ownershipMask: () =>
                                    _automataActionFamilyOwnership!.EffectiveAutoBuyOwnership(
                                        _configurationStore!.Current.AutoBuy),
                                runtimeDiagnostics: RuntimeDiagnosticsRegistry.Shared,
                                featureStatus: featureStatuses.AutoBuy,
                                // A purchase the game refuses is a planner bug, so Auto Buy
                                // writes down both halves of the disagreement and turns its
                                // own setting off rather than retrying into a livelock.
                                refusalResponse: new AutoBuyRefusalResponder(
                                    () => _configurationStore!.Current.AutoBuy.Mode ==
                                        AutoBuyOperationMode.Active,
                                    StandDownAutoBuy,
                                    new AutoBuyRefusalBundleWriter(
                                        () => AutomataTraceRunRoot.Stable("diagnostics")),
                                    message => Log.LogAutomataError(message)))),
                        new SpellLevelServiceCycleFeature(
                            new SpellLevelFeatureDependencies(
                                readAutoHarvestLifecycleEpoch,
                                ownsActionFamily: () => _automataActionFamilyOwnership!.OwnsSpellLevel,
                                capability: _spellLevelCapability,
                                featureStatus: featureStatuses.SpellLevel)),
                        new AutoCastServiceCycleFeature(
                            new AutoCastFeatureDependencies(
                                readAutoHarvestLifecycleEpoch,
                                ownsActionFamily: () => _automataActionFamilyOwnership!.OwnsCast,
                                _autoCastManualPause,
                                featureStatus: featureStatuses.AutoCast)),
                        new AutoConceptServiceCycleFeature(
                            new AutoConceptFeatureDependencies(
                                readAutoHarvestLifecycleEpoch,
                                ownsActionFamily: () => _automataActionFamilyOwnership!.OwnsConcept,
                                featureStatus: featureStatuses.AutoConcept)),
                        new MentorServiceCycleFeature(
                            new MentorFeatureDependencies(
                                readAutoHarvestLifecycleEpoch,
                                captureMutationPermit: domain =>
                                    _mentorActionFamilyOwnership!.TryCaptureMutationPermit(
                                        domain switch
                                        {
                                            MasteryExperienceDomain.Spell => MentorDomain.Spells,
                                            MasteryExperienceDomain.Artifact => MentorDomain.Artifacts,
                                            _ => MentorDomain.Alchemy,
                                        }) == true,
                                featureStatus: featureStatuses.Mentor)),
                    },
                    Log);
            },
            _configurationStore!.Current,
            _configurationStore!.CurrentGeneration,
            featureStatuses.ObserveServiceCycleUnavailable);
        _autoConceptToggleControl = new AutoConceptToggleControl(
            _configurationStore,
            () => featureStatuses.AutoConcept.Current);
        foreach (var hook in LifecycleObservationHooks)
            PatchOptional(hook.Target, hook.Handler, hook.Postfix);
        var runtimeConfig = _configurationStore.Current;
        Log.LogAutomataInfo(
            $"Automata loaded. AutoBuyMode={runtimeConfig.AutoBuy.Mode}, " +
            $"StructureAffordability={runtimeConfig.AutoBuy.StructureAffordability}, " +
            $"UpgradeAffordability={runtimeConfig.AutoBuy.UpgradeAffordability}, " +
            $"AutoBuyAllowedUuidCount={CountConfiguredUuids(runtimeConfig.AutoBuy.AllowedUuids)}, " +
            $"AutoBuyBatchSizing={runtimeConfig.AutoBuy.BatchSizing}, " +
            $"AutoBuyBatchSize={runtimeConfig.AutoBuy.MaxPurchasesPerBatch}, " +
            $"AutoBuyPurchaseGrouping={runtimeConfig.AutoBuy.PurchaseGrouping}, " +
            $"AutoBuyFixedGroupSize={runtimeConfig.AutoBuy.FixedGroupSize}, " +
            $"AutoCastMode={runtimeConfig.AutoCast.Mode}, " +
            $"AutoCastFullCharge={runtimeConfig.AutoCast.FullCharge}, " +
            $"AutoCastStartResourcePercent={runtimeConfig.AutoCast.StartResourcePercent}, " +
            $"AutoConceptMode={runtimeConfig.AutoConcept.Mode}, " +
            $"AutoConceptSlotManagement={runtimeConfig.AutoConcept.SlotManagement}, " +
            $"AutoHarvestMode={runtimeConfig.AutoHarvest.Mode}, " +
            $"AutoHarvestFruitTrees={runtimeConfig.AutoHarvest.CollectFruitTrees}, " +
            $"AutoHarvestTreasureTrees={runtimeConfig.AutoHarvest.CollectTreasureTrees}, " +
            $"AutoLevelSpells={runtimeConfig.AutoBuy.AutoLevelSpells}, " +
            $"PrioritizeCostAndQualityStructures={runtimeConfig.AutoBuy.PrioritizeCostAndQualityStructures}.");
    }

    /// <summary>
    /// Composed last so the catalog the browser later discovers already sees the automation and
    /// mastery feature statuses published above it.
    /// </summary>
    private void ComposeModConfig()
    {
        _invalidationBus ??= GameplayInvalidationBus.Shared;
        _uiSurfaceDiagnostics = new SuiteUiSurfaceDiagnostics(
            RuntimeDiagnosticsRegistry.Shared,
            message => Log.LogAutomataInfo(message),
            message => Log.LogAutomataError(message));
        _runtimeSources = new ModConfigRuntimeSources(
            ConfigurationSchemaStatusRegistry.Shared,
            FeatureStatusRegistry.Shared,
            RuntimeDiagnosticsRegistry.Shared,
            ServiceCyclePumpTimingRegistry.Shared,
            ManualFullTraceControlRegistry.Shared,
            HostTraceDumpRegistry.Shared,
            _mathVerification ??
            throw new InvalidOperationException("Differential verification control was not composed."),
            DecisionJournalStatusSources.Shared
#if SERVICE_CYCLE_PROFILE
            , PerformanceProfileControlRegistry.Shared
#endif
            );
        _modConfigFeatureCommands = new ModConfigFeatureCommands(
            _configurationStore ??
            throw new InvalidOperationException("Configuration store was not composed."),
            _featureStatuses ??
            throw new InvalidOperationException("Feature statuses were not composed."));
        _runUiMaintenance = RunUiMaintenance;
        _uiWork = new ModConfigFrameWork(() => Time.frameCount);
        ResetSceneState(SceneManager.GetActiveScene());
    }

    private void Update()
    {
        if (_automataConfig is null) return;
#if SERVICE_CYCLE_PROFILE
        UpdateUiValidationNavigation();
#endif
        UpdateBuildCompatibilityOverride();
        PublishChangedConfiguration();
        ValidateSuiteShortcuts();
        UpdateEmergencyStopControl();
        if (!_runtimeActivationAllowed || !_configurationStore!.Current.General.Enabled)
        {
            _runtimeCompositionAttempted = false;
        }
        else if (!_runtimeComposed && !_runtimeCompositionAttempted)
        {
            try
            {
                EnsureRuntimeComposition();
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not activate automation after the master switch was enabled: " +
                                ex.GetBaseException().Message);
                _featureStatuses?.ObserveServiceCycleUnavailable(
                    _configurationStore.Current,
                    _configurationStore.CurrentGeneration);
            }
        }

        // The shared bus owns one process-wide operation cap and sequence cutoff per Unity frame.
        if (_modConfigSettings is not null)
        {
            _invalidationBus?.Pump(
                Time.frameCount,
                GameplayInvalidationBus.DefaultMaxOperationsPerFrame);
        }

        UpdateAutomata();
        UpdateMentor();
        UpdateQuickStripSurface(Time.unscaledDeltaTime);
        UpdateModConfig();
    }

    private void UpdateBuildCompatibilityOverride()
    {
        if (_auditedBuild || _automataConfig is null) return;

        var emergencyClearRequested = _automataConfig.TryTakeEmergencyClearRequest();
        if (emergencyClearRequested && !_runtimeActivationAllowed)
            _automataConfig.SetAllowUnverifiedGameBuild(true);

        var decision = UnverifiedBuildCompatibilityPolicy.AfterExplicitChange(
            audited: false,
            _observedBuildFingerprint,
            _automataConfig.AllowUnverifiedGameBuild.Value,
            _automataConfig.AcceptedUnverifiedBuildFingerprint.Value);
        if (decision.AcceptObserved)
            _automataConfig.AcceptUnverifiedBuild(_observedBuildFingerprint);

        if (!decision.RuntimeAllowed &&
            decision.EngageEmergencyStop &&
            !_automataConfig.EmergencyDisable.Value)
        {
            _automataConfig.SetEmergencyStop(true);
        }

        if (decision.RuntimeAllowed == _runtimeActivationAllowed) return;
        if (decision.RuntimeAllowed && !emergencyClearRequested)
            _automataConfig.SetEmergencyStop(true);
        _runtimeActivationAllowed = decision.RuntimeAllowed;
        _nativeContractsAvailable = decision.RuntimeAllowed;

        if (decision.RuntimeAllowed)
        {
            Logger.LogWarning(emergencyClearRequested
                ? "The player cleared the General emergency stop and accepted this exact unverified game assembly pair. Runtime composition is now permitted at the player's own risk."
                : "The player accepted this exact unverified game assembly pair. Runtime composition is now permitted at the player's own risk; the emergency stop remains engaged until explicitly resumed.");
            return;
        }

        Logger.LogError(
            "The unverified-build acknowledgement was removed. The compatibility emergency stop is engaged; restart the game to unload already-installed patches.");
    }

#if SERVICE_CYCLE_PROFILE
    private void UpdateUiValidationNavigation()
    {
        if (SceneManager.GetActiveScene().name == "Start" &&
            UiValidationContinueShortcut.IsDown())
        {
            var managerType = AccessTools.TypeByName("SaveStateManager");
            var manager = managerType is null
                ? null
                : Resources.FindObjectsOfTypeAll(managerType).FirstOrDefault();
            var startGame = AccessTools.Method("SaveStateManager:StartGame");
            if (manager is null || startGame is null)
            {
                Logger.LogError("UI validation navigation could not resolve the native Continue action.");
                return;
            }
            Logger.LogWarning("UI validation navigation: invoking the native Continue action.");
            startGame.Invoke(manager, Array.Empty<object>());
        }

        if (SceneManager.GetActiveScene().name == "Main" &&
            UiValidationOpenModsShortcut.IsDown())
        {
            if (_uiShell is null)
            {
                Logger.LogError("UI validation navigation could not resolve the suite Mods shell.");
                return;
            }
            _uiShell.Toggle();
        }

        if (SceneManager.GetActiveScene().name == "Main" &&
            UiValidationNextPageShortcut.IsDown())
            _uiShell?.SelectNextPageForValidation();
    }
#endif

    private void ValidateSuiteShortcuts()
    {
        if (_automataConfig is null || _mentorConfig is null) return;
        var autoCast = _automataConfig.AutoCastToggleShortcut.Value;
        var mentor = _mentorConfig.ToggleShortcut.Value;
        var signature = autoCast + "\u001f" + mentor;
        if (string.Equals(signature, _shortcutAuditSignature, StringComparison.Ordinal)) return;
        _shortcutAuditSignature = signature;
        var listeners = SuiteShortcutCollisionValidator.Inventory(autoCast, mentor);
        var collisions = SuiteShortcutCollisionValidator.Validate(listeners);
        foreach (var listener in listeners)
        {
            if (listener.Kind == SuiteShortcutListenerKind.RuntimePageButton)
            {
                Logger.LogInfo(listener.DisplayName + " uses a Mods Runtime button and has no key listener.");
                continue;
            }
            if (!collisions.Any(collision =>
                    string.Equals(collision.ListenerId, listener.Id, StringComparison.Ordinal)))
            {
                Logger.LogInfo(
                    $"Shortcut audit: {listener.DisplayName} ({listener.Shortcut}) has no audited native default collision.");
            }
        }
        foreach (var collision in collisions)
        {
            if (collision.IsSuiteListener)
            {
                Logger.LogWarning(
                    $"Shortcut audit: {collision.ListenerDisplayName} and " +
                    $"{collision.ConflictingBinding} are both bound to the exact chord " +
                    $"{collision.Key}; one press will run both listeners.");
                continue;
            }
            Logger.LogWarning(
                $"Shortcut audit: {collision.ListenerDisplayName} uses {collision.Key} as " +
                (collision.IsMainKey ? "its main key" : "a held modifier") +
                $", which also drives the native {collision.ConflictingBinding} binding.");
        }
    }

    private void UpdateAutomata()
    {
        var configuration = _configurationStore!.Current;
        _mathVerification?.Tick();
        if (!_nativeContractsAvailable)
        {
            _featureStatuses?.ObserveContractUnavailable(
                configuration,
                _lifecycleGeneration,
                "Installed game assemblies are quarantined pending an exact-build acknowledgement.",
                _configurationStore.CurrentGeneration);
            return;
        }
        var deltaTime = Time.unscaledDeltaTime;
        UpdateAutoCastControls(deltaTime);
        UpdateAutoBuyControl(deltaTime);
        UpdateAutoConceptControl(deltaTime);
        UpdateAutoHarvestControl(deltaTime);
        if (!configuration.General.Enabled)
        {
            CancelPreparedAutomationForOwnershipRelease();
            _automataActionFamilyOwnership?.Refresh(
                configuration,
                lifecycleReady: false,
                Time.frameCount);
            return;
        }
        var lifecycleReady = IsLifecycleReady();
        _automataActionFamilyOwnership?.RefreshLoadedPluginInventory(
            Chainloader.PluginInfos.Count,
            guid => Chainloader.PluginInfos.ContainsKey(guid));
        if (!_knownOwnershipWarningLogged &&
            _automataActionFamilyOwnership?.KnownAutoBuyLoaded == true)
        {
            _knownOwnershipWarningLogged = true;
            Log.LogAutomataWarning(
                "AutobuyOrb is loaded. Automata will block Structure and Upgrade purchases because those native action families overlap; Auto Cast, Auto Concept, Spell Leveling, and Mentor remain independent.");
        }
        _automataActionFamilyOwnership?.Refresh(configuration, lifecycleReady, Time.frameCount);
        if (lifecycleReady)
        {
            _serviceCycleActivation?.Tick(deltaTime);
        }
        else
        {
            _featureStatuses?.ObserveLifecycleNotReady(
                configuration,
                _lifecycleGeneration,
                _configurationStore.CurrentGeneration);
        }
    }

    private void UpdateMentor()
    {
        if (_mentorConfig is null) return;
        if (!_nativeContractsAvailable)
        {
            _mentorButton?.Dispose();
            _mentorButton = null;
            _mentorUiFailureReason = string.Empty;
            return;
        }
        _mentorActionFamilyOwnership?.Refresh(_mentorConfig, IsGameplayScene(), Time.frameCount);
        if (SceneManager.GetActiveScene().name == "Main" && _mentorConfig.ToggleShortcut.Value.IsDown())
        {
            _configurationStore!.ToggleMentor();
            Logger.LogInfo($"Orb Mentor is now {_mentorConfig.Mode.Value}.");
        }
        if (SceneManager.GetActiveScene().name != "Main")
        {
            _mentorButton?.Dispose();
            _mentorButton = null;
            _mentorUiFailureReason = string.Empty;
            return;
        }
        if (_mentorButton is not null && !_mentorButton.IsAlive) { _mentorButton.Dispose(); _mentorButton = null; }
        if (_mentorButton is not null) { _mentorButton.Render(); return; }
        _mentorUiRetrySeconds -= Time.unscaledDeltaTime;
        if (_mentorUiRetrySeconds <= 0)
        {
            _mentorUiRetrySeconds = UiRetryIntervalSeconds;
            if (MentorToggleButton.TryCreate(
                _mentorConfig,
                _configurationStore!,
                () => _featureStatuses!.Mentor.Current,
                out _mentorButton,
                out var reason))
                _mentorUiFailureReason = string.Empty;
            else
                _mentorUiFailureReason = reason;
        }
    }

    private void UpdateModConfig()
    {
        if (_modConfigSettings is null)
        {
            DeactivateUiWork(disposeShell: true);
            return;
        }

        if (SceneManager.GetActiveScene().name != "Main")
        {
            DeactivateUiWork(disposeShell: false);
            return;
        }

        _mainSceneElapsed += Time.unscaledDeltaTime;
        if (_mainSceneElapsed < UiInstallDelaySeconds)
        {
            _uiWork?.SetState(true, false);
            return;
        }

        if (_modConfigSettings.EnableUiShell.Value != true)
        {
            DeactivateUiWork(disposeShell: true);
            return;
        }

        if (_uiShell is not null && !_uiShell.IsAlive)
            _uiMaintenanceDue = true;
        else if (_uiShell is null)
        {
            if (Time.frameCount >= _deferInstallUntilFrame)
            {
                _uiRetrySeconds -= Math.Max(0.0f, Time.unscaledDeltaTime);
                if (_uiRetrySeconds <= 0.0f) _uiMaintenanceDue = true;
            }
        }
        else
        {
            if (_uiShell.ScheduleRefresh(Time.unscaledDeltaTime)) _uiMaintenanceDue = true;
            if (AdvanceCadence(ref _uiIntegritySeconds, Time.unscaledDeltaTime, UiIntegrityIntervalSeconds))
            {
                _uiIntegrityDue = true;
                _uiMaintenanceDue = true;
            }
        }

        var runUiMaintenance = _runUiMaintenance;
        if (runUiMaintenance is not null)
            _uiWork?.TryRun(true, _uiMaintenanceDue, runUiMaintenance);
        _uiWork?.SetState(true, _uiMaintenanceDue);
    }

    private void LateUpdate()
    {
    }

    private void OnDisable()
    {
        CancelPreparedAutomationForOwnershipRelease();
        _automataActionFamilyOwnership?.ReleaseLifecycleClaims();
        _mentorActionFamilyOwnership?.ReleaseLifecycleClaims();
    }

    private void OnDestroy()
    {
        _emergencyStopButton?.Dispose();
        _emergencyStopButton = null;
        _emergencyStopControl = null;
        _uiShell?.Dispose();
        _uiShell = null;
        _uiWork?.Dispose();
        _uiWork = null;
        _runUiMaintenance = null;
        _runtimeSources = null;
        _modConfigFeatureCommands = null;
        _uiSurfaceDiagnostics?.Dispose();
        _uiSurfaceDiagnostics = null;
        _configurationStore = null;

        _mentorButton?.Dispose();
        _mentorButton = null;
        _mentorActionFamilyOwnership?.Dispose();
        _mentorActionFamilyOwnership = null;

        _invalidationBus = null;
        GameLifecycleMonitor.Shared.Transitioned -= OnLifecycleTransition;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        _serviceCycleActivation?.Dispose();
        _serviceCycleActivation = null;
        _automataActionFamilyOwnership?.Dispose();
        _automataActionFamilyOwnership = null;
        _autoCastToggleButton?.Dispose();
        _autoCastToggleButton = null;
        _autoCastToggleControl = null;
        _autoBuyToggleButton?.Dispose();
        _autoBuyToggleButton = null;
        _autoBuyToggleControl = null;
        _autoConceptToggleButton?.Dispose();
        _autoConceptToggleButton = null;
        _autoConceptToggleControl = null;
        _autoHarvestToggleButton?.Dispose();
        _autoHarvestToggleButton = null;
        _autoHarvestToggleControl = null;
        _mathVerification = null;
        _featureStatuses?.Dispose();
        _featureStatuses = null;

        _harmony?.UnpatchSelf();
        _harmony = null;
        Instance = null;
    }

    private void CancelPreparedAutomationForOwnershipRelease()
    {
        _serviceCycleActivation?.CancelPreparedWork();
    }

    private void UpdateEmergencyStopControl()
    {
        if (_emergencyStopControl is null) return;
        if (SceneManager.GetActiveScene().name != "Main")
        {
            _emergencyStopButton?.Dispose();
            _emergencyStopButton = null;
            _emergencyStopUiFailureReason = string.Empty;
            return;
        }
        if (_emergencyStopButton is not null && !_emergencyStopButton.IsAlive)
        {
            _emergencyStopButton.Dispose();
            _emergencyStopButton = null;
        }
        if (_emergencyStopButton is not null)
        {
            _emergencyStopButton.Render();
            return;
        }
        _emergencyStopUiRetrySeconds -= Time.unscaledDeltaTime;
        if (_emergencyStopUiRetrySeconds > 0) return;
        _emergencyStopUiRetrySeconds = UiRetryIntervalSeconds;
        if (EmergencyStopButton.TryCreate(
                _emergencyStopControl,
                out _emergencyStopButton,
                out var reason))
            _emergencyStopUiFailureReason = string.Empty;
        else
            _emergencyStopUiFailureReason = reason;
    }

    private void OnEmergencyStopChanged(bool stopped)
    {
        CancelPreparedAutomationForOwnershipRelease();
        if (stopped)
            _mentorActionFamilyOwnership?.ReleaseLifecycleClaims();
        _uiMaintenanceDue = true;
        Logger.LogWarning(stopped
            ? "Suite emergency stop engaged; prepared automation work was discarded."
            : "Suite emergency stop cleared after resume confirmation.");
    }

    private System.Collections.Generic.IReadOnlyList<string> ReadEmergencyStopResumePreview()
    {
        var result = new System.Collections.Generic.List<string>();
        var config = _configurationStore?.Current;
        if (config is not null)
        {
            if (config.AutoBuy.Mode == AutoBuyOperationMode.Active) result.Add("Auto Buy");
            if (config.AutoBuy.Mode == AutoBuyOperationMode.Active && config.AutoBuy.AutoLevelSpells)
                result.Add("Spell Leveling");
            if (config.AutoCast.Mode == AutoCastOperationMode.Active) result.Add("Auto Cast");
            if (config.AutoConcept.Mode == AutoConceptOperationMode.Active) result.Add("Auto Concept");
            if (config.AutoHarvest.Mode == AutoHarvestOperationMode.Active &&
                (config.AutoHarvest.CollectFruitTrees || config.AutoHarvest.CollectTreasureTrees))
                result.Add("Auto Harvest");
        }
        if (_mentorConfig?.Mode.Value == MentorOperationMode.Active) result.Add("Mentor");
        return result;
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        ObserveLifecycle(GameLifecycleTransitionKind.SceneExited, previous.name);
        ObserveLifecycle(GameLifecycleTransitionKind.SceneEntered, next.name);
    }

    private void OnLifecycleTransition(GameLifecycleTransition transition)
    {
        if (transition.Current.Generation == _lifecycleGeneration) return;
        _lifecycleGeneration = transition.Current.Generation;
        _serviceCycleActivation?.InvalidateLifecycle();
        _automataActionFamilyOwnership?.ReleaseLifecycleClaims();
        if (_configurationStore is not null)
            _featureStatuses?.ObserveLifecycleNotReady(
                _configurationStore.Current,
                _lifecycleGeneration,
                _configurationStore.CurrentGeneration);
        MentorMasteryPatchBridge.ResetLifecycle(_lifecycleGeneration);
        _mentorActionFamilyOwnership?.ReleaseLifecycleClaims();
        _lifecycleLease = GameLifecycleMonitor.Shared.CaptureLease();

        // Mod Config rebuilds its shell only when the game entered a scene; every other transition
        // leaves the installed UI alone.
        if (transition.Current.LastTransition != GameLifecycleTransitionKind.SceneEntered) return;
        _uiShell?.Dispose();
        _uiShell = null;
        ResetSceneState(SceneManager.GetActiveScene());
    }

    private static void ObserveLifecycle(
        GameLifecycleTransitionKind kind,
        string sceneName,
        object? nativeIdentity = null)
    {
        GameLifecycleMonitor.Shared.TryObserve(
            new GameLifecycleObservation(
                kind,
                Time.frameCount,
                sceneName,
                PluginIds.SuiteGuid,
                nativeIdentity),
            out _,
            out _);
    }

    /// <summary>
    /// Commits a pending external or staged settings change at the start of the application frame.
    /// </summary>
    /// <remarks>
    /// Every source counts. This used to hang off the invalidation the suite's own settings panel
    /// raises, so a setting changed through BepInEx's configuration manager or by editing the file
    /// updated what the panel showed and never advanced a generation: the services kept deciding
    /// against the previous reading until something unrelated republished.
    /// </remarks>
    private void PublishChangedConfiguration()
    {
        _configurationStore?.PublishPending();
    }

    private void PublishConfiguration(
        SuiteRuntimeConfiguration configuration,
        ConfigGeneration configurationGeneration)
    {
        _featureStatuses?.ObserveConfiguration(configuration, configurationGeneration);
        _serviceCycleActivation?.PublishSavedConfiguration(
            configuration,
            configurationGeneration);
    }

    private void StandDownAutoBuy(string summary)
    {
        if (!_configurationStore!.DisableAutoBuy()) return;
        _featureStatuses!.ObserveAutoBuyInvariantStandDown(
            summary,
            _configurationStore.CurrentGeneration);
    }

    private bool IsLifecycleReady() =>
        SceneManager.GetActiveScene().name == "Main" &&
        GameLifecycleMonitor.Shared.Current.IsGameplayReady &&
        GameLifecycleMonitor.Shared.IsCurrent(_lifecycleLease);

    internal static bool AssemblyAuditAllowsMutation(AssemblyAuditResult audit) => audit.MatchesExpected;

    private void UpdateAutoCastControls(float unscaledDeltaTime)
    {
        if (_automataConfig is null || _autoCastToggleControl is null)
        {
            return;
        }

        var inGameplay = SceneManager.GetActiveScene().name == "Main";
        if (inGameplay && _automataConfig.IsAutoCastTogglePressed())
        {
            _autoCastToggleControl.Toggle();
        }

        if (!inGameplay || !_configurationStore!.Current.AutoCast.ShowToggleButton)
        {
            _autoCastToggleButton?.Dispose();
            _autoCastToggleButton = null;
            _autoCastUiRetrySeconds = 0.0f;
            _autoCastUiFailureReason = string.Empty;
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
            return;

        _autoCastUiRetrySeconds = UiRetryIntervalSeconds;
        if (AutoCastToggleButton.TryCreate(_autoCastToggleControl, Log, out var toggle, out var reason))
        {
            _autoCastToggleButton = toggle;
            _autoCastUiFailureReason = string.Empty;
            return;
        }

        _autoCastUiFailureReason = reason;
    }

    private void UpdateAutoBuyControl(float unscaledDeltaTime)
    {
        if (_autoBuyToggleControl is null) return;
        if (SceneManager.GetActiveScene().name != "Main")
        {
            _autoBuyToggleButton?.Dispose();
            _autoBuyToggleButton = null;
            _autoBuyUiRetrySeconds = 0.0f;
            _autoBuyUiFailureReason = string.Empty;
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
        if (AutoBuyToggleButton.TryCreate(
                _autoBuyToggleControl,
                out _autoBuyToggleButton,
                out var reason))
            _autoBuyUiFailureReason = string.Empty;
        else
            _autoBuyUiFailureReason = reason;
    }

    private void UpdateAutoConceptControl(float unscaledDeltaTime)
    {
        if (_automataConfig is null || _autoConceptToggleControl is null) return;
        var inGameplay = SceneManager.GetActiveScene().name == "Main";
        if (!inGameplay || !_configurationStore!.Current.AutoConcept.ShowToggleButton)
        {
            _autoConceptToggleButton?.Dispose();
            _autoConceptToggleButton = null;
            _autoConceptUiRetrySeconds = 0.0f;
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
            return;
        _autoConceptUiRetrySeconds = UiRetryIntervalSeconds;
        if (AutoConceptToggleButton.TryCreate(
                _autoConceptToggleControl,
                out var toggle,
                out var reason))
        {
            _autoConceptToggleButton = toggle;
            _autoConceptUiFailureReason = string.Empty;
            return;
        }
        _autoConceptUiFailureReason = reason;
    }

    private void UpdateAutoHarvestControl(float unscaledDeltaTime)
    {
        if (_autoHarvestToggleControl is null) return;
        if (SceneManager.GetActiveScene().name != "Main")
        {
            _autoHarvestToggleButton?.Dispose();
            _autoHarvestToggleButton = null;
            _autoHarvestUiRetrySeconds = 0.0f;
            _autoHarvestUiFailureReason = string.Empty;
            return;
        }
        if (_autoHarvestToggleButton is not null && !_autoHarvestToggleButton.IsAlive)
        {
            _autoHarvestToggleButton.Dispose();
            _autoHarvestToggleButton = null;
        }
        if (_autoHarvestToggleButton is not null)
        {
            _autoHarvestToggleButton.Render();
            return;
        }
        _autoHarvestUiRetrySeconds -= Math.Max(0.0f, unscaledDeltaTime);
        if (_autoHarvestUiRetrySeconds > 0.0f) return;
        _autoHarvestUiRetrySeconds = UiRetryIntervalSeconds;
        if (AutoHarvestToggleButton.TryCreate(
                _autoHarvestToggleControl,
                out _autoHarvestToggleButton,
                out var reason))
            _autoHarvestUiFailureReason = string.Empty;
        else
            _autoHarvestUiFailureReason = reason;
    }

    private void UpdateQuickStripSurface(float unscaledDeltaTime)
    {
        if (_uiSurfaceDiagnostics is null ||
            SceneManager.GetActiveScene().name != "Main")
            return;

        var failures = new System.Collections.Generic.List<string>(6);
        AddMissingUiControl(
            failures,
            "STOP",
            _emergencyStopControl is not null,
            _emergencyStopButton is not null,
            _emergencyStopUiFailureReason);
        AddMissingUiControl(
            failures,
            "Mentor",
            _mentorConfig is not null,
            _mentorButton is not null,
            _mentorUiFailureReason);
        AddMissingUiControl(
            failures,
            "Auto Buy",
            _autoBuyToggleControl is not null,
            _autoBuyToggleButton is not null,
            _autoBuyUiFailureReason);
        AddMissingUiControl(
            failures,
            "Auto Cast",
            _autoCastToggleControl is not null &&
            _configurationStore!.Current.AutoCast.ShowToggleButton,
            _autoCastToggleButton is not null,
            _autoCastUiFailureReason);
        AddMissingUiControl(
            failures,
            "Auto Concept",
            _autoConceptToggleControl is not null &&
            _configurationStore!.Current.AutoConcept.ShowToggleButton,
            _autoConceptToggleButton is not null,
            _autoConceptUiFailureReason);
        AddMissingUiControl(
            failures,
            "Auto Harvest",
            _autoHarvestToggleControl is not null,
            _autoHarvestToggleButton is not null,
            _autoHarvestUiFailureReason);

        if (failures.Count == 0)
        {
            _quickStripFailureSeconds = 0.0f;
            _uiSurfaceDiagnostics.ReportSuccess(SuiteUiSurface.QuickStrip);
            return;
        }

        _quickStripFailureSeconds += Math.Max(0.0f, unscaledDeltaTime);
        var reason = string.Join("; ", failures);
        if (_quickStripFailureSeconds < 10.0f)
        {
            _uiSurfaceDiagnostics.ReportWaiting(SuiteUiSurface.QuickStrip, reason);
            return;
        }
        _uiSurfaceDiagnostics.ReportFailure(SuiteUiSurface.QuickStrip, reason);
    }

    private static void AddMissingUiControl(
        System.Collections.Generic.ICollection<string> failures,
        string displayName,
        bool required,
        bool installed,
        string reason)
    {
        if (!required || installed) return;
        failures.Add(displayName + ": " +
                     (string.IsNullOrWhiteSpace(reason)
                         ? "native objects are not ready; retry is pending"
                         : reason));
    }

    private static int CountConfiguredUuids(string value)
    {
        return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static void BeforeSaveLoad(object __instance) =>
        ObserveLifecycle(GameLifecycleTransitionKind.SaveLoadStarted, SceneManager.GetActiveScene().name, __instance);
    private static void AfterSaveLoaded(object __instance) =>
        ObserveLifecycle(GameLifecycleTransitionKind.SaveLoaded, SceneManager.GetActiveScene().name, __instance);
    private static void AfterGameInitialized(object __instance)
    {
        ObserveLifecycle(GameLifecycleTransitionKind.RegistryRebuilt, SceneManager.GetActiveScene().name, __instance);
        ObserveLifecycle(GameLifecycleTransitionKind.RuntimeReady, SceneManager.GetActiveScene().name, __instance);
    }
    private static void BeforePersistentReset(object __instance) =>
        ObserveLifecycle(GameLifecycleTransitionKind.NewGamePlusStarted, SceneManager.GetActiveScene().name, __instance);
    private static void BeforeRuntimeReset() =>
        ObserveLifecycle(GameLifecycleTransitionKind.ResetStarted, SceneManager.GetActiveScene().name);

    private static bool IsGameplayScene() => Instance is { } plugin && plugin.IsLifecycleReady();

    private void PatchOptional(string targetName, string patchName, bool postfix)
    {
        var target = AccessTools.Method(targetName);
        if (target is null) { Logger.LogWarning($"Optional native hook unavailable: {targetName}."); return; }
        var patch = new HarmonyMethod(typeof(Plugin), patchName);
        try
        {
            if (postfix) _harmony!.Patch(target, postfix: patch); else _harmony!.Patch(target, prefix: patch);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Optional native hook failed: {targetName}: {ex.GetBaseException().Message}");
        }
    }

    internal static void ShowNotice(string message, RectTransform? anchor)
    {
        try
        {
            var nodeType = Type.GetType("TooltipNode, Assembly-CSharp", false);
            var popupType = Type.GetType("UIPopupText, Assembly-CSharp", false);
            if (nodeType is null || popupType is null || anchor is null) return;
            var node = Activator.CreateInstance(nodeType, message, Color.white);
            var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(nodeType);
            var list = (IList)Activator.CreateInstance(listType)!; list.Add(node);
            var method = popupType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(m => m.Name == "CreateOn" && m.GetParameters().Length == 3);
            method?.Invoke(null, new object[] { list, anchor, new Vector2(0, 32) });
        }
        catch { }
    }

    private void RunUiMaintenance()
    {
        _uiMaintenanceDue = false;
        if (_uiShell is not null && !_uiShell.IsAlive)
        {
            _uiShell.Dispose();
            _uiShell = null;
            _uiRetrySeconds = 0f;
            _deferInstallUntilFrame = Time.frameCount + 1;
            return;
        }
        if (_uiShell is not null)
        {
            _uiShell.RunPendingRefresh();
            if (_uiIntegrityDue)
            {
                var loadedSources = ConfigCatalog.CaptureLoadedSources();
                var generation = ConfigCatalogGeneration.Capture(loadedSources);
                if (!ModConfigCatalogSession.IsCurrent(
                        _catalog,
                        _catalogGeneration,
                        generation))
                {
                    _catalogNavigation = _uiShell.CaptureNavigation();
                    _uiShell.Dispose();
                    _uiShell = null;
                    _catalog = ModConfigCatalogSession.GetOrDiscover(
                        ref _catalog,
                        ref _catalogGeneration,
                        generation,
                        () => ConfigCatalog.Build(loadedSources, _runtimeSources!.SchemaStatuses),
                        LogCatalog);
                    _uiIntegrityDue = false;
                    _uiRetrySeconds = 0f;
                    _uiMaintenanceDue = true;
                    return;
                }
                _uiShell.RefreshNavigation();
                _uiIntegrityDue = false;
                _uiIntegritySeconds = UiIntegrityIntervalSeconds;
            }
            _uiMaintenanceDue = _uiIntegrityDue || _uiShell.HasPendingRefresh;
            return;
        }

        _uiRetrySeconds = UiRetryIntervalSeconds;
        var invalidationBus = _invalidationBus ??
                              throw new InvalidOperationException("Mod Config invalidation bus was not composed.");
        var runtimeSources = _runtimeSources ??
                             throw new InvalidOperationException("Mod Config runtime sources were not composed.");
        var featureCommands = _modConfigFeatureCommands ??
                              throw new InvalidOperationException("Mod Config feature commands were not composed.");
        var loadedCatalogSources = ConfigCatalog.CaptureLoadedSources();
        var currentCatalogGeneration = ConfigCatalogGeneration.Capture(loadedCatalogSources);
        var catalog = ModConfigCatalogSession.GetOrDiscover(
            ref _catalog,
            ref _catalogGeneration,
            currentCatalogGeneration,
            () => ConfigCatalog.Build(loadedCatalogSources, runtimeSources.SchemaStatuses),
            LogCatalog);
        if (!ModConfigUiShell.TryCreate(
                Logger,
                catalog,
                invalidationBus,
                runtimeSources,
                featureCommands,
                _catalogNavigation,
                MarkUiMaintenanceDue,
                MarkNavigationMaintenanceDue,
                out _uiShell,
                out var reason))
        {
            _uiFailureAttempts++;
            if (!_uiFailureLogged)
            {
                _uiFailureLogged = true;
                Logger.LogInfo("Mod Config UI is not ready; installation will retry: " + reason);
            }
            if (_uiFailureAttempts < 3)
                _uiSurfaceDiagnostics?.ReportWaiting(SuiteUiSurface.ModsRail, reason);
            else
                _uiSurfaceDiagnostics?.ReportFailure(SuiteUiSurface.ModsRail, reason);
            return;
        }
        _uiFailureLogged = false;
        _uiFailureAttempts = 0;
        _uiSurfaceDiagnostics?.ReportSuccess(SuiteUiSurface.ModsRail);
        _uiIntegritySeconds = UiIntegrityIntervalSeconds;
    }

    private void LogCatalog(ConfigCatalogSnapshot catalog)
    {
        Logger.LogInfo(
            $"Orb Mod Config loaded. UiShell={_modConfigSettings?.EnableUiShell.Value == true}; " +
            $"DiscoveredMods={catalog.Mods.Count}, DiscoveredSettings={catalog.SettingCount}.");
        foreach (var mod in catalog.Mods)
        {
            Logger.LogInfo(
                $"Mod Config catalog: {mod.Name} {mod.Version} ({mod.Guid}); " +
                $"Sections={mod.Sections.Count}, Settings={mod.Sections.Sum(section => section.Settings.Count)}.");
        }
    }

    private void MarkUiMaintenanceDue() => _uiMaintenanceDue = true;

    private void MarkNavigationMaintenanceDue()
    {
        _uiIntegrityDue = true;
        _uiMaintenanceDue = true;
    }

    private void DeactivateUiWork(bool disposeShell)
    {
        _uiWork?.SetState(false, false);
        _uiMaintenanceDue = false;
        _uiIntegrityDue = false;
        if (!disposeShell || _uiShell is null) return;
        _uiShell.Dispose();
        _uiShell = null;
        _uiRetrySeconds = 0f;
        _uiFailureLogged = false;
        _uiFailureAttempts = 0;
    }

    private void ResetSceneState(Scene scene)
    {
        _mainSceneElapsed = 0f;
        _uiRetrySeconds = 0f;
        _uiIntegritySeconds = 0f;
        _uiFailureLogged = false;
        _uiFailureAttempts = 0;
        _quickStripFailureSeconds = 0f;
        _autoBuyUiFailureReason = string.Empty;
        _autoCastUiFailureReason = string.Empty;
        _autoConceptUiFailureReason = string.Empty;
        _autoHarvestUiFailureReason = string.Empty;
        _mentorUiFailureReason = string.Empty;
        _emergencyStopUiFailureReason = string.Empty;
        _uiMaintenanceDue = false;
        _uiIntegrityDue = false;
        _deferInstallUntilFrame = 0;
        _uiSurfaceDiagnostics?.ResetForScene();
        _uiWork?.SetState(scene.name == "Main", false);
    }

    internal static bool AdvanceCadence(ref float remainingSeconds, float elapsedSeconds, float intervalSeconds)
    {
        remainingSeconds -= Math.Max(0.0f, elapsedSeconds);
        if (remainingSeconds > 0.0f) return false;
        remainingSeconds = Math.Max(0.1f, intervalSeconds);
        return true;
    }
}
