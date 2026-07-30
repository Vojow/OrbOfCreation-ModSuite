using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using OrbAutomata;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.GameMcp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine.UI;
#endif
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
    private const int StartStatusFailureLogFrameThreshold = 120;
#if SERVICE_CYCLE_PROFILE
    private const bool AutoStartServiceCycleDiagnostics = true;
    private const float GameMcpCaptureIntervalSeconds = 0.1f;
    private GameMcpStateStore? _gameMcpState;
    private GameMcpCommandBus? _gameMcpCommands;
    private GameMcpHttpServer? _gameMcpServer;
    private float _gameMcpCaptureElapsed;
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
    private ModConfigStartStatusView? _startStatusView;
    private string _startStatusFailure = string.Empty;
    private int _startStatusFailureFrames;
    private int _processId;
    private string _controlPlaneFailure = string.Empty;

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
            _controlPlaneFailure = loadDecision.Message;
            Logger.LogError(loadDecision.Message);
            return;
        }

        var configuration = SuiteConfiguration.TryBind(Config);
        if (!configuration.Success)
        {
            _controlPlaneFailure = configuration.Status.Reason;
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
#if SERVICE_CYCLE_PROFILE
        _gameMcpState = new GameMcpStateStore();
        _gameMcpCommands = new GameMcpCommandBus();
        CaptureGameMcpState();
        _gameMcpServer = GameMcpHttpServer.TryStart(
            _gameMcpState,
            _gameMcpCommands,
            message => Logger.LogInfo(message),
            message => Logger.LogError(message));
#endif
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
                        new AutoItemsServiceCycleFeature(
                            new AutoItemsFeatureDependencies(
                                autoHarvestRegistryResolver,
                                readAutoHarvestLifecycleEpoch,
                                ownsActionFamily: () =>
                                    _automataActionFamilyOwnership!.OwnsItems,
                                tryCaptureMutationPermit: () =>
                                    _automataActionFamilyOwnership!
                                        .TryCaptureItemMutationPermit(),
                                readOwnershipFailure: () =>
                                    _automataActionFamilyOwnership!
                                        .ItemsOwnershipFailure,
                                featureStatus: featureStatuses.AutoItems)),
                        new AutoHarvestServiceCycleFeature(
                            new AutoHarvestFeatureDependencies(
                                autoHarvestRegistryResolver,
                                ownsActionFamily: () => _automataActionFamilyOwnership!.OwnsHarvest,
                                tryCaptureMutationPermit: () =>
                                    _automataActionFamilyOwnership!.TryCaptureHarvestMutationPermit(),
                                runtimeDiagnostics: RuntimeDiagnosticsRegistry.Shared,
                                featureStatus: featureStatuses.AutoHarvest)),
                        new AutoBuyServiceCycleFeature(
                            new AutoBuyFeatureDependencies(
                                readAutoHarvestLifecycleEpoch,
                                ownershipMask: () =>
                                    _automataActionFamilyOwnership!.EffectiveAutoBuyOwnership(
                                        _configurationStore!.Current.AutoBuy),
                                runtimeDiagnostics: RuntimeDiagnosticsRegistry.Shared,
                                featureStatus: featureStatuses.AutoBuy,
                                // Every refusal writes both halves. Affordability-only drift skips
                                // and re-plans; structural contradictions stand the feature down.
                                refusalResponse: new AutoBuyRefusalResponder(
                                    () => _configurationStore!.Current.AutoBuy.Mode ==
                                        AutoBuyOperationMode.Active,
                                    StandDownAutoBuy,
                                    new AutoBuyRefusalBundleWriter(
                                        () => AutomataTraceRunRoot.Stable("diagnostics")),
                                    message => Log.LogAutomataError(message))
#if SERVICE_CYCLE_PROFILE
                                , gameMcpOwnership: kind =>
                                    _automataActionFamilyOwnership!.OwnsAutoBuy(kind)
#endif
                                )),
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
            $"AutoCastMode={runtimeConfig.AutoCast.Mode}, " +
            $"AutoCastFullCharge={runtimeConfig.AutoCast.FullCharge}, " +
            $"AutoCastStartResourcePercent={runtimeConfig.AutoCast.StartResourcePercent}, " +
            $"AutoConceptMode={runtimeConfig.AutoConcept.Mode}, " +
            $"AutoConceptSlotManagement={runtimeConfig.AutoConcept.SlotManagement}, " +
            $"AutoHarvestMode={runtimeConfig.AutoHarvest.Mode}, " +
            $"AutoHarvestFruitTrees={runtimeConfig.AutoHarvest.CollectFruitTrees}, " +
            $"AutoHarvestTreasureTrees={runtimeConfig.AutoHarvest.CollectTreasureTrees}, " +
            $"AutoItemsMode={runtimeConfig.AutoItems.Mode}, " +
            $"AutoItemsUseScrolls={runtimeConfig.AutoItems.UseScrolls}, " +
            $"AutoItemsUseRelics={runtimeConfig.AutoItems.UseRelics}, " +
            $"AutoLevelSpells={runtimeConfig.AutoBuy.AutoLevelSpells}, " +
            "Auto Buy fills the available queue and groups structures by live Bulk Development.");
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
#if SERVICE_CYCLE_PROFILE
        _gameMcpCommands?.ObserveEmergencyStop(
            _configurationStore!.Current.Safety.EmergencyDisable);
        DrainGameMcpCommands();
#endif
        UpdateMentor();
        UpdateQuickStripSurface(Time.unscaledDeltaTime);
        UpdateModConfig();
#if SERVICE_CYCLE_PROFILE
        _gameMcpCaptureElapsed += Time.unscaledDeltaTime;
        if (_gameMcpCaptureElapsed >= GameMcpCaptureIntervalSeconds)
        {
            CaptureGameMcpState();
            _gameMcpCaptureElapsed = 0f;
        }
#endif
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

    private void UpdateStartStatusView()
    {
        if (!string.Equals(
                SceneManager.GetActiveScene().name,
                "Start",
                StringComparison.Ordinal))
        {
            _startStatusView?.Dispose();
            _startStatusView = null;
            _startStatusFailure = string.Empty;
            _startStatusFailureFrames = 0;
            return;
        }
        if (_processId == 0)
            _processId = ModConfigProcessIdentity.CaptureCurrentProcessId();

        var controlPlaneReady = _automataConfig is not null &&
            _controlPlaneFailure.Length == 0;
#if SERVICE_CYCLE_PROFILE
        const string mode = "Performance-debug build";
        var endpoint = _gameMcpServer is null
            ? "Agent endpoint unavailable · see log"
            : "Agent: 127.0.0.1:19106/mcp";
        var mcpStatus = !controlPlaneReady
            ? "MCP unavailable"
            : _gameMcpServer is null
                ? "MCP starting"
                : "MCP ready";
#else
        const string mode = "Release build";
        const string endpoint = "Agent unavailable · install performance-debug";
        const string mcpStatus = "MCP unavailable";
#endif
        var compatibility = !controlPlaneReady
            ? "Control-plane error · see log"
            : _auditedBuild
                ? "Audited game verified"
                : _runtimeActivationAllowed
                ? "Unverified game accepted"
                : "Unverified game · actions blocked";
        var tone = !controlPlaneReady
            ? ModConfigStartStatusTone.Failure
            : _auditedBuild && _runtimeActivationAllowed
#if SERVICE_CYCLE_PROFILE
                && _gameMcpServer is not null
#endif
                ? ModConfigStartStatusTone.Ready
                : ModConfigStartStatusTone.Attention;
        _startStatusView ??= new ModConfigStartStatusView();
        if (_startStatusView.TryRender(
                new ModConfigStartStatusPresentation(
                    "Orb ModSuite  ·  v" + PluginIds.Version,
                    mode,
                    mcpStatus + "  ·  " + compatibility,
                    endpoint,
                    "PID " + _processId + "  ·  Localhost only",
                    tone),
                out var reason))
        {
            _startStatusFailure = string.Empty;
            _startStatusFailureFrames = 0;
            return;
        }
        if (string.Equals(_startStatusFailure, reason, StringComparison.Ordinal))
            _startStatusFailureFrames++;
        else
        {
            _startStatusFailure = reason;
            _startStatusFailureFrames = 1;
        }
        if (_startStatusFailureFrames != StartStatusFailureLogFrameThreshold) return;
        Logger.LogError("Start status panel unavailable: " + reason);
    }

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
#if SERVICE_CYCLE_PROFILE
        if (_gameMcpServer?.IsListening == true)
            _automataActionFamilyOwnership?.RefreshForGameMcp(
                configuration, lifecycleReady, Time.frameCount);
        else
            _automataActionFamilyOwnership?.Refresh(
                configuration, lifecycleReady, Time.frameCount);
#else
        _automataActionFamilyOwnership?.Refresh(configuration, lifecycleReady, Time.frameCount);
#endif
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
        UpdateStartStatusView();
    }

    private void OnDisable()
    {
        CancelPreparedAutomationForOwnershipRelease();
        _automataActionFamilyOwnership?.ReleaseLifecycleClaims();
        _mentorActionFamilyOwnership?.ReleaseLifecycleClaims();
    }

    private void OnDestroy()
    {
#if SERVICE_CYCLE_PROFILE
        _gameMcpCommands?.Close(
            "suite_shutdown",
            "the suite is shutting down; pending MCP commands cannot mutate native state");
        _gameMcpServer?.Dispose();
        _gameMcpServer = null;
        _gameMcpCommands = null;
        _gameMcpState = null;
#endif
        _startStatusView?.Dispose();
        _startStatusView = null;
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
            if (config.AutoItems.Mode == AutoItemsOperationMode.Active &&
                AutoItemsConfigurationPolicy.HasEnabledFamily(config.AutoItems))
                result.Add("Auto Items");
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

#if SERVICE_CYCLE_PROFILE
    private void DrainGameMcpCommands()
    {
        if (_gameMcpCommands is null) return;
        const int maximumCommandsPerFrame = 4;
        for (var index = 0;
             index < maximumCommandsPerFrame &&
             _gameMcpCommands.TryDequeue(out var command);
             index++)
        {
            GameMcpCommandResult result;
            var completeNow = true;
            try
            {
                if (command.Kind is GameMcpCommandKind.ConfigurationSet or
                    GameMcpCommandKind.EmergencyStop)
                {
                    result = ExecuteAdministrativeGameMcp(command);
                }
                else if (command.Kind is >= GameMcpCommandKind.Screenshot and
                         <= GameMcpCommandKind.ContinueRun)
                {
                    completeNow = TryExecuteGameMcpGadget(command, out result);
                }
                else if (_serviceCycleActivation is null ||
                         !_serviceCycleActivation.TryExecuteGameMcp(command, out result))
                {
                    result = GameMcpCommandResult.Rejected(
                        "runtime_not_available",
                        "the ServiceCycle runtime is not active in this scene");
                }
            }
            catch (Exception exception)
            {
                result = GameMcpCommandResult.Faulted(
                    "command_dispatch_fault",
                    exception.GetBaseException().Message);
            }
            if (completeNow) CompleteGameMcpCommand(command, result);
        }
    }

    private void CompleteGameMcpCommand(
        GameMcpCommand command,
        GameMcpCommandResult result)
    {
        _gameMcpCommands?.Complete(command, result);
        Logger.LogInfo(
            "Game MCP command " + command.Sequence + " completed " +
            result.Status + " (" + result.Code + "): " + result.Reason);
    }

    private GameMcpCommandResult ExecuteAdministrativeGameMcp(GameMcpCommand command)
    {
        if (_configurationStore is null)
            return GameMcpCommandResult.Rejected(
                "configuration_not_available",
                "the committed suite configuration store is not composed");
        var expected = command.ExpectedConfigurationGeneration;
        var before = _configurationStore.CurrentGeneration;
        if (expected != before.Value)
        {
            return GameMcpCommandResult.Rejected(
                "stale_configuration_generation",
                "command expected configuration generation " + expected +
                " but the main thread now has generation " + before.Value,
                observedConfigurationGeneration: before.Value);
        }

        if (command.Kind == GameMcpCommandKind.ConfigurationSet)
        {
            if (!_configurationStore.TrySetGameMcp(
                    command.Mode,
                    command.PayloadKey,
                    command.PayloadValue,
                    before,
                    out var reason))
            {
                return GameMcpCommandResult.Rejected(
                    "configuration_write_rejected",
                    reason,
                    observedConfigurationGeneration:
                        _configurationStore.CurrentGeneration.Value);
            }
            return GameMcpCommandResult.Committed(
                "configuration_committed",
                "the BepInEx entry was committed through configuration generation " +
                _configurationStore.CurrentGeneration.Value,
                observedWorldGeneration: 0,
                observedLifecycleGeneration: _lifecycleGeneration,
                observedConfigurationGeneration:
                    _configurationStore.CurrentGeneration.Value);
        }

        var engage = command.Mode == "engage";
        if (!engage && !_runtimeActivationAllowed)
        {
            return GameMcpCommandResult.Rejected(
                "runtime_activation_blocked",
                "the emergency stop cannot resume while exact-build compatibility blocks runtime activation",
                observedLifecycleGeneration: _lifecycleGeneration,
                observedConfigurationGeneration: before.Value);
        }
        if (_configurationStore.Current.Safety.EmergencyDisable == engage)
        {
            return GameMcpCommandResult.Rejected(
                "already_in_requested_state",
                engage
                    ? "the suite emergency stop is already engaged"
                    : "the suite emergency stop is already clear",
                observedLifecycleGeneration: _lifecycleGeneration,
                observedConfigurationGeneration: before.Value);
        }

        // Match the in-game control's order. Engaging synchronously cancels the host before another
        // queued command can run in this frame; resume is published and the host clears only through
        // its ordinary configured-stop pump and fresh-world gate.
        OnEmergencyStopChanged(engage);
        _configurationStore.SetEmergencyStop(engage);
        return GameMcpCommandResult.Committed(
            engage ? "emergency_stop_engaged" : "emergency_stop_resume_committed",
            engage
                ? "the committed safety setting is true and prepared native actions were cancelled"
                : "the committed safety setting is false; dispatch resumes only after the host accepts a fresh world",
            observedWorldGeneration: 0,
            observedLifecycleGeneration: _lifecycleGeneration,
            observedConfigurationGeneration:
                _configurationStore.CurrentGeneration.Value);
    }

    private bool TryExecuteGameMcpGadget(
        GameMcpCommand command,
        out GameMcpCommandResult result)
    {
        if (command.Kind == GameMcpCommandKind.Screenshot)
        {
            StartCoroutine(CaptureGameMcpAtEndOfFrame(
                command,
                GadgetCommitted(
                    "screenshot_captured",
                    "the game framebuffer was captured after the current frame completed",
                    new JObject { ["operation"] = "screenshot" })));
            result = null!;
            return false;
        }
        if (command.Kind == GameMcpCommandKind.Navigation)
        {
            StartCoroutine(NavigateGameMcpAcrossFrames(command));
            result = null!;
            return false;
        }

        result = command.Kind switch
        {
            GameMcpCommandKind.Probe => ProbeGameMcp(command),
            GameMcpCommandKind.ScreenCatalog => CaptureScreenCatalogGameMcp(),
            GameMcpCommandKind.TooltipCatalog => CaptureTooltipCatalogGameMcp(command),
            GameMcpCommandKind.TooltipRead => ReadTooltipGameMcp(command),
            GameMcpCommandKind.ContinueRun => ContinueRunGameMcp(),
            _ => GameMcpCommandResult.Rejected(
                "unsupported_gadget",
                "the requested gadget is not allowlisted",
                observedLifecycleGeneration: _lifecycleGeneration,
                observedConfigurationGeneration:
                    _configurationStore?.CurrentGeneration.Value ?? 0),
        };
        if (command.Capture && string.Equals(result.Status, "committed", StringComparison.Ordinal))
        {
            StartCoroutine(CaptureGameMcpAtEndOfFrame(command, result));
            return false;
        }
        return true;
    }

    private GameMcpCommandResult ContinueRunGameMcp()
    {
        if (!string.Equals(
                SceneManager.GetActiveScene().name,
                "Start",
                StringComparison.Ordinal))
        {
            return GameMcpCommandResult.Rejected(
                "continue_wrong_scene",
                "the audited Continue action exists only on the Start scene",
                observedLifecycleGeneration: _lifecycleGeneration,
                observedConfigurationGeneration:
                    _configurationStore?.CurrentGeneration.Value ?? 0);
        }

        var managerType = AccessTools.TypeByName("SaveStateManager");
        var manager = managerType is null
            ? null
            : Resources.FindObjectsOfTypeAll(managerType).FirstOrDefault();
        var startGame = AccessTools.Method("SaveStateManager:StartGame");
        if (manager is null || startGame is null)
        {
            return GameMcpCommandResult.Rejected(
                "continue_contract_unavailable",
                "the audited SaveStateManager.StartGame contract could not be resolved",
                observedLifecycleGeneration: _lifecycleGeneration,
                observedConfigurationGeneration:
                    _configurationStore?.CurrentGeneration.Value ?? 0);
        }

        startGame.Invoke(manager, Array.Empty<object>());
        return GadgetCommitted(
            "continue_invoked",
            "the game's audited native Continue action was invoked for the selected save",
            new JObject
            {
                ["sceneBefore"] = "Start",
                ["nativeType"] = "SaveStateManager",
                ["nativeMethod"] = "StartGame",
            });
    }

    private IEnumerator CaptureGameMcpAtEndOfFrame(
        GameMcpCommand command,
        GameMcpCommandResult baseResult)
    {
        yield return new WaitForEndOfFrame();
        Texture2D? texture = null;
        try
        {
            texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture is null)
                throw new InvalidOperationException(
                    "ScreenCapture.CaptureScreenshotAsTexture returned null");
            var png = texture.EncodeToPNG();
            if (png is null || png.Length == 0)
                throw new InvalidOperationException("Texture2D.EncodeToPNG returned no bytes");
            var details = string.IsNullOrWhiteSpace(baseResult.DetailsJson)
                ? new JObject()
                : JObject.Parse(baseResult.DetailsJson);
            details["captureFrame"] = Time.frameCount;
            details["mimeType"] = "image/png";
            details["inlineBytes"] = png.Length;
            if (command.SaveCapture)
            {
                var directory = AutomataTraceRunRoot.Child("mcp-screenshots");
                System.IO.Directory.CreateDirectory(directory);
                var name = "mcp-" +
                    DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") +
                    "-" + command.Sequence + ".png";
                var path = System.IO.Path.Combine(directory, name);
                using (var stream = new System.IO.FileStream(
                           path,
                           System.IO.FileMode.CreateNew,
                           System.IO.FileAccess.Write,
                           System.IO.FileShare.Read))
                {
                    stream.Write(png, 0, png.Length);
                }
                details["savedPath"] = path;
                details["savedRelativePath"] =
                    AutomataTraceRunRoot.FormatRelativePath("mcp-screenshots/" + name);
            }
            CompleteGameMcpCommand(
                command,
                baseResult.WithInlinePng(details.ToString(Formatting.None), png));
        }
        catch (Exception exception)
        {
            CompleteGameMcpCommand(
                command,
                GameMcpCommandResult.Faulted(
                    "inline_screenshot_failed",
                    "server defect: the end-of-frame screenshot did not finish: " +
                    exception.GetBaseException().Message,
                    observedLifecycleGeneration: _lifecycleGeneration,
                    observedConfigurationGeneration:
                        _configurationStore?.CurrentGeneration.Value ?? 0));
        }
        finally
        {
            if (texture is not null) Destroy(texture);
        }
    }

    private GameMcpCommandResult CaptureScreenCatalogGameMcp()
    {
        if (!TryCaptureScreenCatalog(out var tabs, out var subtabs, out var reason))
            return GadgetRejected("screen_catalog_unavailable", reason);
        return GadgetCommitted(
            "screen_catalog_read",
            "the live closed-world screen catalog was read on Unity's main thread",
            new JObject
            {
                ["scene"] = SceneManager.GetActiveScene().name,
                ["tabs"] = tabs,
                ["subtabs"] = subtabs,
            });
    }

    private bool TryBeginNavigateGameMcp(
        GameMcpCommand command,
        out JObject request,
        out JObject details,
        out GameMcpCommandResult failure)
    {
        request = new JObject();
        details = new JObject();
        failure = null!;
        var scene = SceneManager.GetActiveScene().name;
        if (scene != "Main" || _uiShell is null || !_uiShell.IsAlive)
        {
            failure = GadgetRejected(
                "native_navigation_unavailable",
                "the live native navigation catalog is available only while the Main scene shell is alive");
            return false;
        }

        try { request = JObject.Parse(command.PayloadValue); }
        catch (JsonException exception)
        {
            failure = GadgetRejected(
                "navigation_request_invalid",
                "the immutable navigation request could not be decoded: " + exception.Message);
            return false;
        }
        var tabs = _uiShell.CaptureNativeTabsForGameMcp();
        if (!TryResolveTabSelector(request["tab"] as JObject, tabs, out var tab, out var tabReason))
        {
            failure = GadgetRejected("tab_match_failed", tabReason);
            return false;
        }
        if (!_uiShell.TrySelectNativeTabForGameMcp(tab.Index, out var selectReason))
        {
            failure = GadgetRejected("native_tab_rejected", selectReason);
            return false;
        }

        details = new JObject
        {
            ["operation"] = "navigate",
            ["sceneBefore"] = scene,
            ["tab"] = ProjectNavigationEntry(tab.Index, tab.Label, tab.Path),
        };
        return true;
    }

    private IEnumerator NavigateGameMcpAcrossFrames(GameMcpCommand command)
    {
        if (!TryBeginNavigateGameMcp(command, out var request, out var details, out var failure))
        {
            CompleteGameMcpCommand(command, failure);
            yield break;
        }

        // Native tab selection changes the active content hierarchy during the next Unity frame.
        // Waiting here makes a compound tab/subtab/plot request one real navigation operation instead
        // of requiring the caller to retry after the first control becomes active.
        yield return null;

        if (request["subtab"] is JObject subtabSelector)
        {
            var subtabs = CaptureSubtabs();
            if (!TryResolveSubtabSelector(
                    subtabSelector,
                    subtabs,
                    out var subtab,
                    out var subtabReason))
            {
                CompleteGameMcpCommand(
                    command,
                    GadgetRejected("subtab_match_failed", subtabReason));
                yield break;
            }
            if (!subtab.TrySelect(out var selectionReason))
            {
                CompleteGameMcpCommand(
                    command,
                    GadgetRejected("subtab_selection_failed", selectionReason));
                yield break;
            }
            details["subtab"] =
                ProjectNavigationEntry(subtab.Index, subtab.Label, subtab.Path);
            yield return null;
        }
        if (command.TargetId != Guid.Empty)
        {
            var plotResult = NavigateExactPlot(
                command.TargetId,
                SceneManager.GetActiveScene().name);
            if (!string.Equals(plotResult.Status, "committed", StringComparison.Ordinal))
            {
                CompleteGameMcpCommand(command, plotResult);
                yield break;
            }
            details["plotNodeUuid"] = command.TargetId.ToString("D");
        }
        var result = GadgetCommitted(
            "navigation_arrived",
            "the requested catalog destination was invoked through native UI controls",
            details);
        if (command.Capture)
        {
            yield return CaptureGameMcpAtEndOfFrame(command, result);
            yield break;
        }
        CompleteGameMcpCommand(command, result);
    }

    private GameMcpCommandResult NavigateExactPlot(
        Guid stableUuid,
        string scene)
    {
        if (scene != "Main")
        {
            return GadgetRejected(
                "wrong_scene",
                "plot selection is available only in the Main scene, not " + scene);
        }

        const string expectedNativeType = "PlotNodeSO";
        var plotType = AccessTools.TypeByName(expectedNativeType);
        var listType = AccessTools.TypeByName("UIPlotNodeList");
        if (plotType is null || listType is null)
        {
            return GadgetRejected(
                "native_plot_navigation_unavailable",
                "required native types are unavailable: expected " +
                expectedNativeType + " and UIPlotNodeList");
        }

        var plot = TypedRegistryResolver.Shared.Resolve(stableUuid, plotType);
        if (!plot.IsResolved)
        {
            return GadgetRejected(
                "native_plot_not_resolved",
                "stable plot " + stableUuid.ToString("D") + " as " +
                expectedNativeType + " was not resolved: " + plot.Reason);
        }

        var activeLists = Resources.FindObjectsOfTypeAll(listType)
            .OfType<MonoBehaviour>()
            .Where(list => list.enabled && list.gameObject.activeInHierarchy)
            .ToArray();
        if (activeLists.Length != 1)
        {
            return GadgetRejected(
                "native_plot_list_unavailable",
                "expected exactly one active UIPlotNodeList but found " +
                activeLists.Length);
        }

        var onNodeClick = listType.GetMethod(
            "OnNodeClick",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { plotType },
            modifiers: null);
        if (onNodeClick is null || onNodeClick.ReturnType != typeof(void))
        {
            return GadgetRejected(
                "native_plot_navigation_unavailable",
                "UIPlotNodeList.OnNodeClick(" + expectedNativeType +
                ") -> System.Void could not be resolved");
        }

        onNodeClick.Invoke(activeLists[0], new[] { plot.Value });
        return GadgetCommitted(
            "navigation_invoked",
            "the exact audited plot was selected through the native plot-list click path",
            new JObject
            {
                ["operation"] = "select_plot_node",
                ["plotUuid"] = stableUuid.ToString("D"),
                ["expectedNativeType"] = expectedNativeType,
                ["nativeMethod"] = "UIPlotNodeList.OnNodeClick",
                ["sceneBefore"] = scene,
            });
    }

    private bool TryCaptureScreenCatalog(
        out JArray tabs,
        out JArray subtabs,
        out string reason)
    {
        tabs = new JArray();
        subtabs = new JArray();
        if (SceneManager.GetActiveScene().name != "Main" ||
            _uiShell is null ||
            !_uiShell.IsAlive)
        {
            reason = "the Main scene native navigation shell is not alive";
            return false;
        }
        var nativeTabs = _uiShell.CaptureNativeTabsForGameMcp();
        for (var index = 0; index < nativeTabs.Count; index++)
        {
            var tab = nativeTabs[index];
            tabs.Add(ProjectNavigationEntry(tab.Index, tab.Label, tab.Path));
        }
        var nativeSubtabs = CaptureSubtabs();
        for (var index = 0; index < nativeSubtabs.Count; index++)
        {
            var subtab = nativeSubtabs[index];
            subtabs.Add(ProjectNavigationEntry(subtab.Index, subtab.Label, subtab.Path));
        }
        reason = string.Empty;
        return true;
    }

    private IReadOnlyList<GameMcpSubtab> CaptureSubtabs()
    {
        if (_uiShell is null || !_uiShell.IsAlive)
            return Array.Empty<GameMcpSubtab>();
        if (_uiShell.IsOpenForGameMcp)
        {
            var pages = _uiShell.CapturePagesForGameMcp();
            var suiteResult = new GameMcpSubtab[pages.Count];
            for (var index = 0; index < pages.Count; index++)
            {
                var pageIndex = index;
                suiteResult[index] = new GameMcpSubtab(
                    index,
                    pages[index],
                    "Mods/Page[" + index + "]",
                    () => _uiShell.TrySelectPageForGameMcp(pageIndex, out var reason)
                        ? string.Empty
                        : reason);
            }
            return suiteResult;
        }
        var viewRadioType = AccessTools.TypeByName("UIViewRadioButton");
        if (viewRadioType is null) return Array.Empty<GameMcpSubtab>();
        var candidates = Resources.FindObjectsOfTypeAll(viewRadioType)
            .OfType<Component>()
            .Where(component =>
                component.gameObject.activeInHierarchy &&
                !_uiShell.IsNativeTabForGameMcp(component))
            .Select(component => new
            {
                Component = component,
                Button = component.GetComponent<Button>(),
                Label = component.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true),
                Path = NativeObjectPath.BuildIndexed(component),
            })
            .Where(candidate =>
                candidate.Button is not null &&
                candidate.Button.enabled &&
                candidate.Button.interactable &&
                candidate.Label is not null &&
                GameMcpGadgetPolicy.IsCurrentContentSubtabPath(candidate.Path))
            .OrderBy(candidate => candidate.Path, StringComparer.Ordinal)
            .ToArray();
        var result = new GameMcpSubtab[candidates.Length];
        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            result[index] = new GameMcpSubtab(
                index,
                candidate.Label!.text?.Trim() ?? string.Empty,
                candidate.Path,
                () =>
                {
                    candidate.Button!.onClick.Invoke();
                    return string.Empty;
                });
        }
        return result;
    }

    private static bool TryResolveTabSelector(
        JObject? selector,
        IReadOnlyList<GameMcpNativeTab> entries,
        out GameMcpNativeTab selected,
        out string reason)
    {
        if (selector is null)
        {
            selected = default;
            reason = "tab selector is absent";
            return false;
        }
        var kind = (string?)selector["kind"];
        if (kind == "index")
        {
            var requested = (int?)selector["value"] ?? -1;
            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index].Index != requested) continue;
                selected = entries[index];
                reason = string.Empty;
                return true;
            }
            selected = default;
            reason = "tab index " + requested + " matched zero live catalog entries";
            return false;
        }
        if (kind == "name")
        {
            var requested = (string?)selector["value"] ?? string.Empty;
            var matches = new List<GameMcpNativeTab>();
            for (var index = 0; index < entries.Count; index++)
                if (string.Equals(entries[index].Label, requested, StringComparison.Ordinal))
                    matches.Add(entries[index]);
            if (matches.Count == 1)
            {
                selected = matches[0];
                reason = string.Empty;
                return true;
            }
            selected = default;
            reason = "exact tab name '" + requested + "' matched " +
                matches.Count + " live catalog entries";
            return false;
        }
        selected = default;
        reason = "tab selector kind is not name or index";
        return false;
    }

    private static bool TryResolveSubtabSelector(
        JObject selector,
        IReadOnlyList<GameMcpSubtab> entries,
        out GameMcpSubtab selected,
        out string reason)
    {
        var kind = (string?)selector["kind"];
        var matches = new List<GameMcpSubtab>();
        if (kind == "index")
        {
            var requested = (int?)selector["value"] ?? -1;
            for (var index = 0; index < entries.Count; index++)
                if (entries[index].Index == requested) matches.Add(entries[index]);
            reason = "subtab index " + requested;
        }
        else if (kind == "name")
        {
            var requested = (string?)selector["value"] ?? string.Empty;
            for (var index = 0; index < entries.Count; index++)
                if (string.Equals(entries[index].Label, requested, StringComparison.Ordinal))
                    matches.Add(entries[index]);
            reason = "exact subtab name '" + requested + "'";
        }
        else
        {
            selected = null!;
            reason = "subtab selector kind is not name or index";
            return false;
        }
        if (matches.Count == 1)
        {
            selected = matches[0];
            reason = string.Empty;
            return true;
        }
        selected = null!;
        reason += " matched " + matches.Count + " live catalog entries";
        return false;
    }

    private static JObject ProjectNavigationEntry(int index, string label, string path) =>
        new()
        {
            ["index"] = index,
            ["name"] = label,
            ["path"] = path,
            ["selectableByName"] = label.Length > 0,
        };

    private GameMcpCommandResult CaptureTooltipCatalogGameMcp(GameMcpCommand command)
    {
        var entries = CaptureActiveHoverTooltips();
        if (!int.TryParse(
                command.PayloadValue,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var offset))
        {
            return GadgetRejected(
                "tooltip_offset_invalid",
                "the immutable tooltip catalog offset could not be decoded");
        }
        var projected = new JArray();
        var subTooltipsField = typeof(HoverTooltip).GetField(
            "subTooltips",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (subTooltipsField is null)
        {
            return GadgetRejected(
                "tooltip_contract_unavailable",
                "audited HoverTooltip.subTooltips could not be resolved");
        }
        var end = (int)Math.Min(entries.Count, (long)offset + command.Amount);
        for (var index = offset; index < end; index++)
        {
            var hover = entries[index];
            var item = hover.tooltipItem;
            if (item is null) continue;
            var children = subTooltipsField.GetValue(hover) as ICollection<ITooltipable>;
            projected.Add(new JObject
            {
                ["index"] = index,
                ["path"] = NativeObjectPath.BuildIndexed(hover),
                ["name"] = item.GetName(),
                ["displayType"] = item.GetDisplayType(),
                ["hasAltTooltips"] = item.HasAltTooltips(),
                ["nestedTooltipCount"] = children?.Count ?? 0,
            });
        }
        return GadgetCommitted(
            "tooltip_catalog_read",
            "active tooltip-bearing elements were enumerated from the current native screen",
            new JObject
            {
                ["scene"] = SceneManager.GetActiveScene().name,
                ["tooltips"] = projected,
                ["identity"] = "exact current-screen sibling-indexed native hierarchy path",
                ["total"] = entries.Count,
                ["offset"] = offset,
                ["limit"] = command.Amount,
                ["hasMore"] = end < entries.Count,
            });
    }

    private GameMcpCommandResult ReadTooltipGameMcp(GameMcpCommand command)
    {
        var requestedPath = command.PayloadValue;
        var matches = CaptureActiveHoverTooltips()
            .Where(hover => string.Equals(
                NativeObjectPath.BuildIndexed(hover),
                requestedPath,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            return GadgetRejected(
                "tooltip_match_failed",
                "exact tooltip path '" + requestedPath + "' matched " +
                matches.Length + " active current-screen elements");
        }
        var hover = matches[0];
        if (hover.tooltipItem is null)
        {
            return GadgetRejected(
                "tooltip_content_unavailable",
                "the exact HoverTooltip has no assigned ITooltipable");
        }
        var subTooltipsField = typeof(HoverTooltip).GetField(
            "subTooltips",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (subTooltipsField is null)
        {
            return GadgetRejected(
                "tooltip_contract_unavailable",
                "audited HoverTooltip.subTooltips could not be resolved");
        }
        var children = subTooltipsField.GetValue(hover) as ICollection<ITooltipable>;
        var nested = new JArray();
        if (children is not null)
        {
            foreach (var child in children)
                nested.Add(ProjectTooltipText(child));
        }
        if (command.Capture) hover.OpenTooltip();
        var details = ProjectTooltipText(hover.tooltipItem);
        details["path"] = requestedPath;
        details["nestedTooltips"] = nested;
        details["structuralDepth"] = 1;
        details["contentLimit"] =
            "core authored text is included; rendered TooltipNode value rows are a follow-up";
        return GadgetCommitted(
            "tooltip_read",
            "core tooltip text and authored nested-tooltip links were read from the native element",
            details);
    }

    private static JObject ProjectTooltipText(ITooltipable item) =>
        new()
        {
            ["name"] = item.GetName(),
            ["displayType"] = item.GetDisplayType(),
            ["description"] = item.GetDescription(),
            ["hasAltTooltips"] = item.HasAltTooltips(),
        };

    private static IReadOnlyList<HoverTooltip> CaptureActiveHoverTooltips() =>
        Resources.FindObjectsOfTypeAll(typeof(HoverTooltip))
            .OfType<HoverTooltip>()
            .Where(hover =>
                hover.enabled &&
                hover.gameObject.activeInHierarchy)
            .OrderBy(hover => NativeObjectPath.BuildIndexed(hover), StringComparer.Ordinal)
            .ToArray();

    private GameMcpCommandResult ProbeGameMcp(GameMcpCommand command)
    {
        JObject details;
        switch (command.Mode)
        {
            case "runtime":
                var lifecycle = GameLifecycleMonitor.Shared.Current;
                details = new JObject
                {
                    ["probe"] = "runtime",
                    ["scene"] = SceneManager.GetActiveScene().name,
                    ["frame"] = Time.frameCount,
                    ["timeScale"] = Time.timeScale,
                    ["lifecycleGeneration"] = lifecycle.Generation,
                    ["lifecycleState"] = lifecycle.State.ToString(),
                    ["gameplayReady"] = lifecycle.IsGameplayReady,
                    ["modsShellAlive"] = _uiShell?.IsAlive == true,
                };
                break;
            case "action_queue_room":
                var queue = new AutoBuyNativeQueueRoomAdapter();
                if (!queue.TryReadRemainingRoom(out var remaining))
                    return GadgetRejected(
                        "native_probe_unavailable",
                        "ActionManager.GetRemainingRoom could not be resolved or returned an invalid value");
                details = new JObject
                {
                    ["probe"] = "action_queue_room",
                    ["remainingRoom"] = remaining,
                    ["nativeContract"] = "ActionManager.GetRemainingRoom()",
                };
                break;
            case "navigation":
                var tabs = _uiShell is not null && _uiShell.IsAlive
                    ? _uiShell.CaptureNativeTabsForGameMcp()
                    : Array.Empty<GameMcpNativeTab>();
                details = new JObject
                {
                    ["probe"] = "navigation",
                    ["scene"] = SceneManager.GetActiveScene().name,
                    ["nativeTabCount"] = tabs.Count,
                    ["activeNativeSubtabCount"] = CaptureSubtabs().Count,
                    ["catalogTool"] = "game_screen_catalog",
                };
                break;
            default:
                return GadgetRejected(
                    "unsupported_probe",
                    "probe '" + command.Mode +
                    "' is not allowlisted; supported probes are runtime, " +
                    "action_queue_room, and navigation");
        }
        return GadgetCommitted(
            "probe_read",
            "the allowlisted read-only probe completed on Unity's main thread",
            details);
    }

    private GameMcpCommandResult GadgetCommitted(
        string code,
        string reason,
        JObject details) =>
        GameMcpCommandResult.Committed(
            code,
            reason,
            observedWorldGeneration: 0,
            observedLifecycleGeneration: _lifecycleGeneration,
            observedConfigurationGeneration:
                _configurationStore?.CurrentGeneration.Value ?? 0,
            details.ToString(Formatting.None));

    private GameMcpCommandResult GadgetRejected(string code, string reason) =>
        GameMcpCommandResult.Rejected(
            code,
            reason,
            observedLifecycleGeneration: _lifecycleGeneration,
            observedConfigurationGeneration:
                _configurationStore?.CurrentGeneration.Value ?? 0);

    private sealed class GameMcpSubtab
    {
        private readonly Func<string> _select;

        internal GameMcpSubtab(
            int index,
            string label,
            string path,
            Func<string> select)
        {
            Index = index;
            Label = label ?? string.Empty;
            Path = path ?? string.Empty;
            _select = select ?? throw new ArgumentNullException(nameof(select));
        }

        internal int Index { get; }
        internal string Label { get; }
        internal string Path { get; }
        internal bool TrySelect(out string reason)
        {
            reason = _select();
            return reason.Length == 0;
        }
    }

    private void CaptureGameMcpState()
    {
        if (_gameMcpState is null || _configurationStore is null) return;
        GameMcpRuntimeState? runtime = null;
        if (_serviceCycleActivation is not null &&
            _serviceCycleActivation.TryCaptureGameMcpState(out var captured))
        {
            runtime = captured;
        }
        _gameMcpState.Capture(
            _configurationStore.Current,
            _configurationStore.CurrentGeneration,
            _automataConfig?.CaptureGameMcpWritableSettings() ?? "[]",
            _lifecycleGeneration,
            SceneManager.GetActiveScene().name,
            _nativeContractsAvailable,
            FeatureStatusRegistry.Shared.GetSnapshot(),
            DecisionJournalStatusRegistry.Shared.Status,
            DecisionJournalStatusRegistry.Shared.Revision,
            runtime);
    }
#endif

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
