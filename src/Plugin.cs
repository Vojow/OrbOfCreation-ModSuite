using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using OrbAutomata;
using OrbMentor;
using OrbModConfig;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Control;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace.Control;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Control;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbModding;

/// <summary>
/// The suite's single BepInEx entry point. One DLL, one loader identity, one configuration file:
/// automation, mastery catch-up and the configuration browser load, refuse and unload together.
/// </summary>
[BepInPlugin(PluginIds.SuiteGuid, PluginIds.SuiteName, PluginIds.Version)]
public sealed class Plugin : BaseUnityPlugin
{
#if SERVICE_CYCLE_PROFILE
    private const bool AutoStartServiceCycleDiagnostics = true;
#else
    private const bool AutoStartServiceCycleDiagnostics = false;
#endif
    private const float UiRetryIntervalSeconds = 5.0f;
    private const float UiInstallDelaySeconds = 2.0f;
    private const float UiIntegrityIntervalSeconds = 5.0f;
    private const string AlchemyRecipeTypeName = "AlchemyRecipeSO";

    /// <summary>
    /// Every patch class this plugin installs, named one by one. Scanning an assembly for
    /// <see cref="HarmonyPatch"/> classes would silently adopt whatever else ends up compiled
    /// alongside it — which is the whole suite now that it ships as one DLL. An explicit list can
    /// only widen in a diff.
    /// </summary>
    internal static readonly Type[] HarmonyPatchTypes =
    {
        typeof(AutoConceptActiveListPatch),
        typeof(AutoConceptActiveListBroadPatch),
        typeof(AutoConceptProgressionPatch),
        typeof(SpellFirePatch),
    };

    /// <summary>
    /// The native completions Spell Leveling still has to be told about by hand. Auto Buy reads
    /// structure and upgrade levels off the world snapshot and needs no signal, but Spell Leveling is
    /// unmigrated and has no generation to gate on, so a finished build or purchase still has to nudge
    /// it directly. Named here rather than written inline so that losing one of the two is a diff
    /// rather than an omission; both retire when Spell Leveling moves onto the service cycle.
    /// </summary>
    internal static readonly string[] NativeCompletionHookTargets =
    {
        "StructureSO:CompleteAction",
        "UpgradeSO:CompleteAction",
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
    private bool _rebindSupersededVerificationChord;
    private AutomataFeatureStatuses? _featureStatuses;
    private readonly AutomataServiceRegistry _services = new();
    private AutoCastEngine? _autoCastEngine;
    private AutoConceptController? _autoConceptController;
    private AutoSpellLevelController? _autoSpellLevelController;
    private AutomataActionFamilyOwnership? _automataActionFamilyOwnership;
    private AutomataServiceCycleActivation? _serviceCycleActivation;
    private AutoCastToggleControl? _autoCastToggleControl;
    private AutoCastToggleButton? _autoCastToggleButton;
    private AutoBuyToggleControl? _autoBuyToggleControl;
    private AutoBuyToggleButton? _autoBuyToggleButton;
    private AutoConceptToggleControl? _autoConceptToggleControl;
    private AutoConceptToggleButton? _autoConceptToggleButton;
    private AutomataDifferentialVerificationControl? _mathVerification;
    private float _autoCastUiRetrySeconds;
    private float _autoBuyUiRetrySeconds;
    private float _autoConceptUiRetrySeconds;
    private float _autoCastUiFailureSeconds;
    private float _autoConceptUiFailureSeconds;
    private bool _autoCastUiFailureLogged;
    private bool _autoConceptUiFailureLogged;
    private string _autoCastUiFailureReason = string.Empty;
    private string _autoConceptUiFailureReason = string.Empty;
    private IDisposable? _conceptInventorySubscription;
    private IDisposable? _conceptProgressionSubscription;
    private bool _knownOwnershipWarningLogged;
    private bool _nativeContractsAvailable = true;

    private MentorConfig? _mentorConfig;
    private MentorRuntime? _mentorRuntime;
    private MentorActionFamilyOwnership? _mentorActionFamilyOwnership;
    private MentorGameplayInvalidationBridge? _invalidationBridge;
    private MentorToggleButton? _mentorButton;
    private float _mentorUiRetrySeconds;
    private bool _mentorWasActive;

    private ModConfigSettings? _modConfigSettings;
    private float _mainSceneElapsed;
    private float _uiRetrySeconds;
    private float _uiIntegritySeconds;
    private bool _uiFailureLogged;
    private bool _uiMaintenanceDue;
    private bool _uiIntegrityDue;
    private int _deferInstallUntilFrame;
    private ModConfigUiShell? _uiShell;
    private ConfigCatalogSnapshot? _catalog;
    private ModConfigCoordinatorWork? _uiWork;
    private ModConfigRuntimeSources? _runtimeSources;
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

        // First, before configuration or anything else. The suite computes the game's economy math
        // itself and patches game methods, so an unaudited build is one whose numbers and methods we
        // cannot vouch for; refusing here leaves the game entirely untouched.
        var loadDecision = SuiteLoadGate.Evaluate(Paths.GameRootPath);
        if (!loadDecision.ShouldLoad)
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
        _rebindSupersededVerificationChord =
            AutomataDifferentialVerificationControl.ShouldRebindSupersededDefault(configuration.Status);
        _automataConfig = suite.Automata;
        _mentorConfig = suite.Mentor;
        _modConfigSettings = suite.ModConfig;
        foreach (var diagnostic in configuration.Diagnostics)
            Logger.LogInfo($"Configuration migration {diagnostic.Kind}: {diagnostic.Source}; {diagnostic.Detail}");

        _lifecycleGeneration = GameLifecycleMonitor.Shared.Current.Generation;
        _featureStatuses = new AutomataFeatureStatuses(_automataConfig.Current, _lifecycleGeneration);

        // The load gate above already refused any build that does not match an audited baseline, so
        // reaching here means the native contracts are the ones the suite was built against.
        Log.LogAutomataInfo(loadDecision.Message);
        GameLifecycleMonitor.Shared.Transitioned += OnLifecycleTransition;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        ObserveLifecycle(GameLifecycleTransitionKind.SceneEntered, SceneManager.GetActiveScene().name);
        _lifecycleLease = GameLifecycleMonitor.Shared.CaptureLease();

        if (!_automataConfig.Current.General.Enabled)
        {
            Log.LogAutomataInfo(
                "Orb Of Creation ModSuite is disabled by General/Enabled; no patches and no features were installed.");
            return;
        }

        _harmony = new Harmony(PluginIds.SuiteGuid);
        foreach (var patchType in HarmonyPatchTypes)
            _harmony.CreateClassProcessor(patchType).Patch();

        ComposeAutomata();
        ComposeMentor();
        ComposeModConfig();
    }

    private void ComposeAutomata()
    {
        var config = _automataConfig!;
        var featureStatuses = _featureStatuses!;

        // Diagnostic only: checks the suite's ported cost math against the game's own results on
        // demand. It reads and compares; it never mutates.
        _mathVerification = new AutomataDifferentialVerificationControl(
            Config,
            message => Log.LogAutomataInfo(message),
            _rebindSupersededVerificationChord);

        var reservePolicy = new ReservePolicy(config);
        _automataActionFamilyOwnership = new AutomataActionFamilyOwnership();
        Log.LogAutomataWarning(
            "Action-family ownership is best-effort: exact known conflicts and cooperative suite owners are isolated, but unknown plugins that invoke native actions without registering cannot be proven absent and are not disabled.");
        _automataActionFamilyOwnership.RefreshLoadedPluginInventory(
            Chainloader.PluginInfos.Count,
            guid => Chainloader.PluginInfos.ContainsKey(guid));
        _autoCastToggleControl = new AutoCastToggleControl(
            config,
            () => featureStatuses.AutoCast.Current);
        _autoBuyToggleControl = new AutoBuyToggleControl(
            config,
            () => _autoSpellLevelController?.Capability ?? AutoSpellLevelCapability.Locked,
            () => featureStatuses.AutoBuy.Current,
            () => featureStatuses.SpellLevel.Current);
        Func<long> readAutoHarvestLifecycleEpoch =
            () => GameLifecycleMonitor.Shared.Current.Generation;
        var autoHarvestRegistryResolver = TypedRegistryResolver.Shared;
        AutomataProductionComposition.Register(
            _services,
            tryCreateAutoHarvest: () => _serviceCycleActivation =
                new AutomataServiceCycleActivation(
                    IsLifecycleReady,
                    () =>
                    {
                        // One frame counter shared by every feature below. Resolving it per feature
                        // would let two services disagree about what frame it is — a wiring mistake
                        // that compiles and looks like a quiet game. The world publication is not
                        // here at all: the registry owns it, because there is one game.
                        Func<long> readFrameIdentity = static () => Time.frameCount;
                        return AutomataServiceCycleProductionComposition.TryCreate(
                            config.Current,
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
                                    }),
                                new AutoHarvestServiceCycleFeature(
                                    new AutoHarvestFeatureDependencies(
                                        autoHarvestRegistryResolver,
                                        ownsActionFamily: () => _automataActionFamilyOwnership?.OwnsHarvest == true,
                                        tryCaptureMutationPermit: () =>
                                            _automataActionFamilyOwnership?.TryCaptureHarvestMutationPermit() == true,
                                        runtimeDiagnostics: RuntimeDiagnosticsRegistry.Shared,
                                        featureStatus: featureStatuses.AutoHarvest)),
                                new AutoBuyServiceCycleFeature(
                                    new AutoBuyFeatureDependencies(
                                        readAutoHarvestLifecycleEpoch,
                                        ownershipMask: () =>
                                        {
                                            var autoBuyOwnership = AutoBuyCandidateKinds.None;
                                            if (_automataActionFamilyOwnership?.OwnsAutoBuy(AutoBuyCandidateKind.Structure) == true)
                                                autoBuyOwnership |= AutoBuyCandidateKinds.Structures;
                                            if (_automataActionFamilyOwnership?.OwnsAutoBuy(AutoBuyCandidateKind.Upgrade) == true)
                                                autoBuyOwnership |= AutoBuyCandidateKinds.Upgrades;
                                            return autoBuyOwnership;
                                        },
                                        runtimeDiagnostics: RuntimeDiagnosticsRegistry.Shared,
                                        featureStatus: featureStatuses.AutoBuy,
                                        // A purchase the game refuses is a planner bug, so Auto Buy
                                        // writes down both halves of the disagreement and turns its
                                        // own setting off rather than retrying into a livelock.
                                        refusalResponse: new AutoBuyRefusalResponder(
                                            config,
                                            new AutoBuyRefusalBundleWriter(
                                                () => AutomataTraceRunRoot.Stable("diagnostics")),
                                            message => Log.LogAutomataError(message),
                                            featureStatuses.AutoBuy))),
                            },
                            Log);
                    }),
            createAutoCast: () => _autoCastEngine = new AutoCastEngine(
                config,
                new ReflectionAutoCastCatalog(),
                reservePolicy,
                new ResourceFullnessPolicy(),
                Log,
                coordinator: SuitePerformanceCoordinator.Shared,
                readFrameIdentity: () => Time.frameCount,
                featureStatus: featureStatuses.AutoCast,
                ownsActionFamily: () => _automataActionFamilyOwnership?.OwnsCast == true),
            createAutoConcept: () => _autoConceptController = new AutoConceptController(
                config,
                new ReflectionConceptRuntime(new AlchemyGameplayDomainClassifier()),
                Log,
                SuitePerformanceCoordinator.Shared,
                () => Time.frameCount,
                featureStatuses.AutoConcept,
                () => _automataActionFamilyOwnership?.OwnsConcept == true),
            createSpellLevel: () => _autoSpellLevelController = new AutoSpellLevelController(
                config,
                new ReflectionSpellLevelRuntime(),
                Log,
                SuitePerformanceCoordinator.Shared,
                () => Time.frameCount,
                featureStatuses.SpellLevel,
                () => _automataActionFamilyOwnership?.OwnsSpellLevel == true));
        _autoConceptToggleControl = new AutoConceptToggleControl(
            config,
            () => featureStatuses.AutoConcept.Current);
        foreach (var target in NativeCompletionHookTargets)
            PatchOptional(target, nameof(AfterNativeCompletion), postfix: true);
        foreach (var hook in LifecycleObservationHooks)
            PatchOptional(hook.Target, hook.Handler, hook.Postfix);
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
        var runtimeConfig = config.Current;
        Log.LogAutomataInfo(
            $"Automata loaded. AutoBuyMode={runtimeConfig.AutoBuy.Mode}, " +
            $"StructureAffordability={runtimeConfig.AutoBuy.StructureAffordability}, " +
            $"UpgradeAffordability={runtimeConfig.AutoBuy.UpgradeAffordability}, " +
            $"AutoBuyAllowedUuidCount={CountConfiguredUuids(runtimeConfig.AutoBuy.AllowedUuids)}, " +
            $"AutoBuyCandidateCap={runtimeConfig.AutoBuy.MaxCandidatesPerScan}, " +
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
            $"PrioritizeCostAndQualityStructures={runtimeConfig.AutoBuy.PrioritizeCostAndQualityStructures}, " +
            $"OperationalLogging={runtimeConfig.Diagnostics.IsOperationalLoggingEnabled}, " +
            $"DecisionLogLevel={runtimeConfig.Diagnostics.DecisionLogLevel}.");
    }

    /// <summary>
    /// Mentor installs its hooks imperatively, and the helpers report every failure into the runtime
    /// they guard, so its patching cannot be hoisted out of its composition: the ownership record,
    /// then the runtime, then the one load-bearing mastery hook, then everything else. A missing
    /// mastery hook skips the rest of Mentor and nothing else — what is left behind that early
    /// return is Mentor's own, the spell-loadout hooks included. The suite-wide hooks that used to
    /// sit here, native completion and lifecycle observation alike, are installed with Automata.
    /// </summary>
    private void ComposeMentor()
    {
        var config = _mentorConfig!;
        _mentorActionFamilyOwnership = new MentorActionFamilyOwnership();
        Logger.LogWarning(
            "Mentor action-family ownership is best-effort; unknown unregistered automation cannot be proven absent and is not disabled.");
        _mentorRuntime = new MentorRuntime(
            config,
            Logger,
            SuitePerformanceCoordinator.Shared,
            () => Time.frameCount,
            featureStatusRegistry: FeatureStatusRegistry.Shared,
            ownsActionFamily: domain => _mentorActionFamilyOwnership?.IsHeld(domain) == true,
            captureActionFamilyMutation: domain =>
                _mentorActionFamilyOwnership?.TryCaptureMutationPermit(domain) == true);
        _invalidationBridge = new MentorGameplayInvalidationBridge(GameplayInvalidationBus.Shared);
        _mentorWasActive = config.Active;
        var target = AccessTools.Method("SpellRecipeSO:GainMasteryExp");
        if (target is null) { _mentorRuntime.BlockPermanent("native GainMasteryExp hook unavailable"); return; }
        try { _harmony!.Patch(target, postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterMasteryGain))); }
        catch (Exception ex)
        {
            _mentorRuntime.BlockPermanent($"native GainMasteryExp hook failed: {ex.GetBaseException().Message}");
            return;
        }
        PatchDomainRequired("AlchemyRecipeSO:GainMasteryXp", nameof(AfterAlchemyMasteryGain), MentorDomain.Alchemy, postfix: true);
        PatchDomainRequired("EquipmentSO:IncrementActive", nameof(BeforeArtifactTick), MentorDomain.Artifacts, postfix: false);
        PatchDomainRequired("EquipmentSO:IncrementActive", nameof(FinalizeArtifactTick), MentorDomain.Artifacts, postfix: false, finalizer: true);
        PatchDomainRequired("ExperienceContainer:GainExperience", nameof(BeforeContainerGain), MentorDomain.Artifacts, postfix: false);
        PatchRequired("SpellRecipeSO:Discover", nameof(AfterSpellProgression));
        PatchRequired("SpellRecipeSO:PurchaseLevel", nameof(AfterSpellProgression));
        PatchDomainRequired("AlchemyRecipeSO:Discover", nameof(AfterAlchemyProgression), MentorDomain.Alchemy, postfix: true);
        PatchDomainRequired("AlchemyRecipeSO:ApplyMastery", nameof(AfterAlchemyProgression), MentorDomain.Alchemy, postfix: true);
        PatchDomainRequired("EquipmentSO:Discover", nameof(AfterArtifactProgression), MentorDomain.Artifacts, postfix: true);
        PatchDomainRequired("EquipmentSO:Create", nameof(AfterArtifactProgression), MentorDomain.Artifacts, postfix: true);
        PatchDomainRequired("EquipmentSO:GainMasteryLevels", nameof(AfterArtifactProgression), MentorDomain.Artifacts, postfix: true);
        PatchRequired("SpellRecipeSO:ResetData", nameof(AfterNativeProgressionReset));
        PatchDomainRequired("AlchemyRecipeSO:ResetData", nameof(AfterAlchemyNativeReset), MentorDomain.Alchemy, postfix: true);
        PatchDomainRequired("EquipmentSO:ResetData", nameof(AfterArtifactNativeReset), MentorDomain.Artifacts, postfix: true);
        PatchOptional("SpellManager:AddSpell", nameof(AfterSpellLoadoutChanged), postfix: true);
        PatchOptional("SpellManager:RemoveSpell", nameof(AfterSpellLoadoutChanged), postfix: true);
        PatchOptional("SpellManager:MoveSpell", nameof(AfterSpellLoadoutChanged), postfix: true);
        Logger.LogInfo($"Orb Mentor loaded. Mode={config.Mode.Value}, Sources={config.SpellSourcePolicy.Value}, Economy={config.EconomyMode.Value}, Share={config.SharePercent.Value:0.##}%.");
    }

    /// <summary>
    /// Composed last so the catalog the browser later discovers already sees the automation and
    /// mastery feature statuses published above it.
    /// </summary>
    private void ComposeModConfig()
    {
        _invalidationBus ??= GameplayInvalidationBus.Shared;
        _runtimeSources = new ModConfigRuntimeSources(
            ConfigurationSchemaStatusRegistry.Shared,
            FeatureStatusRegistry.Shared,
            RuntimeDiagnosticsRegistry.Shared,
            ServiceCyclePumpTimingRegistry.Shared,
            ManualFullTraceControlRegistry.Shared,
            HostTraceDumpRegistry.Shared,
            DecisionJournalStatusSources.Shared
#if SERVICE_CYCLE_PROFILE
            , PerformanceProfileControlRegistry.Shared
#endif
            );
        _runUiMaintenance = RunUiMaintenance;
        _uiWork = new ModConfigCoordinatorWork(
            SuitePerformanceCoordinator.Shared,
            () => Time.frameCount);
        ResetSceneState(SceneManager.GetActiveScene());
    }

    private void Update()
    {
        if (_automataConfig is null) return;

        // Not the only pump. Mentor's bridge pumps the same shared bus again from LateUpdate, so a
        // frame with Mod Config enabled and Mentor active hands out the per-frame operation budget
        // twice. Unifying the two needs Mentor off its own tick, so it waits for that migration.
        if (_modConfigSettings?.Enabled.Value == true)
        {
            _invalidationBus?.Pump(
                Time.frameCount,
                GameplayInvalidationBus.DefaultMaxOperationsPerFrame);
        }

        UpdateAutomata();
        UpdateMentor();
        UpdateModConfig();
    }

    private void UpdateAutomata()
    {
        var config = _automataConfig!;
        PublishChangedConfiguration();
        if (!_nativeContractsAvailable)
        {
            _featureStatuses?.ObserveContractUnavailable(
                config.Current,
                _lifecycleGeneration,
                "Installed game assemblies do not match Automata's audited native contracts.");
            return;
        }
        if (!config.Current.General.Enabled)
        {
            CancelPreparedAutomationForOwnershipRelease();
            _automataActionFamilyOwnership?.Refresh(config.Current, lifecycleReady: false, Time.frameCount);
            return;
        }
        var deltaTime = Time.unscaledDeltaTime;
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
        _automataActionFamilyOwnership?.Refresh(config.Current, lifecycleReady, Time.frameCount);
        _mathVerification?.Tick();
        UpdateAutoCastControls(deltaTime);
        UpdateAutoBuyControl(deltaTime);
        UpdateAutoConceptControl(deltaTime);
        if (lifecycleReady)
        {
            _services.Tick(deltaTime);
        }
        else
        {
            _featureStatuses?.ObserveLifecycleNotReady(config.Current, _lifecycleGeneration);
        }
    }

    private void UpdateMentor()
    {
        if (_mentorConfig is null || _mentorRuntime is null) return;
        _mentorActionFamilyOwnership?.Refresh(_mentorConfig, IsGameplayScene(), Time.frameCount);
        if (SceneManager.GetActiveScene().name == "Main" && _mentorConfig.ToggleShortcut.Value.IsDown())
        {
            _mentorConfig.Mode.Value = _mentorConfig.Mode.Value == MentorOperationMode.Active ? MentorOperationMode.Disabled : MentorOperationMode.Active;
            _mentorRuntime.Cancel(MentorDropReason.Disabled);
            _mentorRuntime.RefreshFeatureStatus();
            Logger.LogInfo($"Orb Mentor is now {_mentorConfig.Mode.Value}.");
        }
        var active = _mentorConfig.Active;
        if (_mentorWasActive && !active) _mentorRuntime.Cancel(MentorDropReason.Disabled);
        _mentorWasActive = active;
        if (SceneManager.GetActiveScene().name != "Main") { _mentorButton?.Dispose(); _mentorButton = null; return; }
        if (_mentorButton is not null && !_mentorButton.IsAlive) { _mentorButton.Dispose(); _mentorButton = null; }
        if (_mentorButton is not null) { _mentorButton.Render(); return; }
        _mentorUiRetrySeconds -= Time.unscaledDeltaTime;
        if (_mentorUiRetrySeconds <= 0) { _mentorUiRetrySeconds = UiRetryIntervalSeconds; MentorToggleButton.TryCreate(_mentorConfig, _mentorRuntime, out _mentorButton); }
    }

    private void UpdateModConfig()
    {
        if (_modConfigSettings?.Enabled.Value != true)
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
        if (_mentorConfig?.Active == true && _mentorRuntime?.IsBlocked != true)
            _invalidationBridge?.Pump(Time.frameCount);
        if (IsGameplayScene()) _mentorRuntime?.LateTick();
    }

    private void OnDisable()
    {
        CancelPreparedAutomationForOwnershipRelease();
        _automataActionFamilyOwnership?.ReleaseLifecycleClaims();
        _mentorRuntime?.Cancel(MentorDropReason.Disabled);
        _mentorActionFamilyOwnership?.ReleaseLifecycleClaims();
    }

    private void OnDestroy()
    {
        _uiShell?.Dispose();
        _uiShell = null;
        _uiWork?.Dispose();
        _uiWork = null;
        _runUiMaintenance = null;
        _runtimeSources = null;

        _mentorButton?.Dispose();
        _mentorButton = null;
        _mentorRuntime?.Dispose();
        _mentorRuntime = null;
        _mentorActionFamilyOwnership?.Dispose();
        _mentorActionFamilyOwnership = null;
        _invalidationBridge = null;

        AutoConceptLifecycleSignal.InventoryChanged -= OnAutoConceptInventoryChanged;
        AutoConceptLifecycleSignal.ProgressionChanged -= OnAutoConceptProgressionChanged;
        _conceptInventorySubscription?.Dispose();
        _conceptInventorySubscription = null;
        _conceptProgressionSubscription?.Dispose();
        _conceptProgressionSubscription = null;
        _invalidationBus = null;
        GameLifecycleMonitor.Shared.Transitioned -= OnLifecycleTransition;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        _services.Dispose();
        _autoCastEngine = null;
        _autoConceptController = null;
        _autoSpellLevelController = null;
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
        _mathVerification = null;
        _featureStatuses?.Dispose();
        _featureStatuses = null;

        _harmony?.UnpatchSelf();
        _harmony = null;
        Instance = null;
    }

    private void CancelPreparedAutomationForOwnershipRelease()
    {
        _services.CancelPreparedWork();
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
        _services.InvalidateLifecycle();
        _automataActionFamilyOwnership?.ReleaseLifecycleClaims();
        if (_automataConfig is not null)
            _featureStatuses?.ObserveLifecycleNotReady(_automataConfig.Current, _lifecycleGeneration);
        _mentorRuntime?.ResetLifecycle();
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

    /// <summary>
    /// Publishes the settings, once per frame, if anything changed them.
    /// </summary>
    /// <remarks>
    /// Every source counts. This used to hang off the invalidation the suite's own settings panel
    /// raises, so a setting changed through BepInEx's configuration manager or by editing the file
    /// updated what the panel showed and never advanced a generation: the services kept deciding
    /// against the previous reading until something unrelated republished.
    /// </remarks>
    private void PublishChangedConfiguration()
    {
        if (_automataConfig is null) return;
        if (!_automataConfig.TryTakeUnpublishedChange(out var configuration)) return;
        _serviceCycleActivation?.PublishSavedConfiguration(configuration);
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
                Time.frameCount,
                GameplayInvalidationDomains.AutomataConcepts,
                entityId,
                AlchemyRecipeTypeName,
                source);
            return;
        }

        _invalidationBus.Publish(
            kind,
            Time.frameCount,
            GameplayInvalidationDomains.AutomataConcepts,
            source: source);
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

        if (!inGameplay || !_automataConfig.Current.AutoCast.ShowToggleButton)
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
        if (_automataConfig is null || _autoConceptToggleControl is null) return;
        var inGameplay = SceneManager.GetActiveScene().name == "Main";
        if (!inGameplay || !_automataConfig.Current.AutoConcept.ShowToggleButton)
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

    private static void AfterMasteryGain(SpellRecipeSO __instance, BigDouble exp)
    {
        if (IsGameplayScene()) Instance?._mentorRuntime?.Observe(__instance, exp);
    }
    private static void AfterAlchemyMasteryGain(object __instance, BigDouble exp)
    {
        if (IsGameplayScene()) Instance?._mentorRuntime?.ObserveAlchemy(__instance, exp);
    }
    private static void BeforeArtifactTick(object __instance)
    {
        if (IsGameplayScene()) Instance?._mentorRuntime?.BeginArtifactTick(__instance);
    }
    private static Exception? FinalizeArtifactTick(Exception? __exception) { Instance?._mentorRuntime?.EndArtifactTick(__exception is null); return __exception; }
    private static void BeforeContainerGain(object __instance, BigDouble __0)
    {
        if (IsGameplayScene()) Instance?._mentorRuntime?.ObserveExperienceContainer(__instance, __0);
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

    /// <summary>
    /// A structure finished building or an upgrade finished purchasing, so the spells it may have
    /// unlocked are worth another look right away rather than at the next idle interval.
    /// </summary>
    private static void AfterNativeCompletion()
    {
        if (!IsGameplayScene()) return;
        Instance?._autoSpellLevelController?.NotifyNativeChange();
    }

    private static void AfterSpellLoadoutChanged()
    {
        var plugin = Instance;
        plugin?._mentorRuntime?.NotifyEquippedLoadoutChanged();
        plugin?._invalidationBridge?.PublishSpellLoadout(Time.frameCount);
    }
    private static void AfterSpellProgression(object __instance) =>
        PublishProgression(MentorDomain.Spells, __instance);
    private static void AfterAlchemyProgression(object __instance) =>
        PublishProgression(MentorDomain.Alchemy, __instance);
    private static void AfterArtifactProgression(object __instance) =>
        PublishProgression(MentorDomain.Artifacts, __instance);
    private static void AfterNativeProgressionReset()
    {
        var plugin = Instance;
        plugin?._mentorRuntime?.RequestLifecycleReset();
        plugin?._invalidationBridge?.PublishProgression(MentorDomain.Spells, Time.frameCount, null);
    }
    private static void AfterAlchemyNativeReset()
    {
        var plugin = Instance;
        plugin?._mentorRuntime?.RequestDomainReset(MentorDomain.Alchemy);
        plugin?._invalidationBridge?.PublishProgression(MentorDomain.Alchemy, Time.frameCount, null);
    }
    private static void AfterArtifactNativeReset()
    {
        var plugin = Instance;
        plugin?._mentorRuntime?.RequestDomainReset(MentorDomain.Artifacts);
        plugin?._invalidationBridge?.PublishProgression(MentorDomain.Artifacts, Time.frameCount, null);
    }

    private static void PublishProgression(MentorDomain domain, object changedSource)
    {
        var plugin = Instance;
        plugin?._mentorRuntime?.MarkRelationshipDirty(domain, changedSource);
        var entityId = plugin?._mentorRuntime?.TryGetStableProgressionEntityId(domain, changedSource, out var stableId) == true
            ? stableId
            : null;
        plugin?._invalidationBridge?.PublishProgression(domain, Time.frameCount, entityId);
    }

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

    private void PatchRequired(string targetName, string patchName)
    {
        var target = AccessTools.Method(targetName);
        if (target is null)
        {
            _mentorRuntime?.BlockPermanent($"required lifecycle hook unavailable: {targetName}");
            return;
        }
        try { _harmony!.Patch(target, postfix: new HarmonyMethod(typeof(Plugin), patchName)); }
        catch (Exception ex)
        {
            _mentorRuntime?.BlockPermanent($"required lifecycle hook failed: {targetName}: {ex.GetBaseException().Message}");
        }
    }

    private void PatchDomainRequired(
        string targetName,
        string patchName,
        MentorDomain domain,
        bool postfix,
        bool finalizer = false)
    {
        var target = AccessTools.Method(targetName);
        if (target is null)
        {
            _mentorRuntime?.QuarantineDomain(domain, $"required {domain} hook unavailable: {targetName}");
            return;
        }
        var patch = new HarmonyMethod(typeof(Plugin), patchName);
        try
        {
            if (finalizer) _harmony!.Patch(target, finalizer: patch);
            else if (postfix) _harmony!.Patch(target, postfix: patch);
            else _harmony!.Patch(target, prefix: patch);
        }
        catch (Exception ex)
        {
            _mentorRuntime?.QuarantineDomain(domain, $"required {domain} hook failed: {targetName}: {ex.GetBaseException().Message}");
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
        var catalog = ModConfigCatalogSession.GetOrDiscover(
            ref _catalog,
            () => ConfigCatalog.DiscoverLoaded(runtimeSources.SchemaStatuses),
            LogCatalog);
        if (!ModConfigUiShell.TryCreate(
                Logger,
                catalog,
                invalidationBus,
                runtimeSources,
                MarkUiMaintenanceDue,
                MarkNavigationMaintenanceDue,
                out _uiShell,
                out var reason))
        {
            if (!_uiFailureLogged)
            {
                _uiFailureLogged = true;
                Logger.LogWarning("Mod Config UI is not ready; installation will retry: " + reason);
            }
            return;
        }
        _uiFailureLogged = false;
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
    }

    private void ResetSceneState(Scene scene)
    {
        _mainSceneElapsed = 0f;
        _uiRetrySeconds = 0f;
        _uiIntegritySeconds = 0f;
        _uiFailureLogged = false;
        _uiMaintenanceDue = false;
        _uiIntegrityDue = false;
        _deferInstallUntilFrame = 0;
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
