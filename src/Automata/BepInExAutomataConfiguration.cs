using System;
using System.Collections.Generic;
using System.Threading;
using BepInEx.Configuration;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbMentor;

namespace OrbAutomata;

internal sealed class BepInExAutomataConfiguration
{
    private SuiteRuntimeConfiguration _current = null!;
    private MentorConfig? _mentor;
    private int _unpublishedChange;
    private int _emergencyClearRequested;
    private static readonly IReadOnlyList<ModConfigDependency> AutoBuyActiveDependencies = new[]
    {
        new ModConfigDependency("AutoBuy", "Mode", "Active"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> AutoBuyStructuresActiveDependencies = new[]
    {
        new ModConfigDependency("AutoBuy", "Mode", "Active"),
        new ModConfigDependency("AutoBuy", "IncludeStructures"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> AutoBuyUpgradesActiveDependencies = new[]
    {
        new ModConfigDependency("AutoBuy", "Mode", "Active"),
        new ModConfigDependency("AutoBuy", "IncludeUpgrades"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> AutoBuyFixedBatchDependencies = new[]
    {
        new ModConfigDependency("AutoBuy", "Mode", "Active"),
        new ModConfigDependency("AutoBuy", "BatchSizingMode", "Fixed"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> AutoBuyPurchaseGroupingDependencies = new[]
    {
        new ModConfigDependency("AutoBuy", "Mode", "Active"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> AutoBuyFixedGroupingDependencies = new[]
    {
        new ModConfigDependency("AutoBuy", "Mode", "Active"),
        new ModConfigDependency("AutoBuy", "IncludeStructures"),
        new ModConfigDependency("AutoBuy", "PurchaseGrouping", "Fixed"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> AutoCastActiveDependencies = new[]
    {
        new ModConfigDependency("AutoCast", "Mode", "Active"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> AutoConceptActiveDependencies = new[]
    {
        new ModConfigDependency("AutoConcept", "Mode", "Active"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> AutoHarvestActiveDependencies = new[]
    {
        new ModConfigDependency("AutoHarvest", "Mode", "Active"),
    };

    private BepInExAutomataConfiguration(
        ConfigFile config,
        ConfigEntry<bool> enabled,
        ConfigEntry<AutoBuyOperationMode> autoBuyMode,
        ConfigEntry<AutoBuyAffordabilityMode> autoBuyAffordabilityMode,
        ConfigEntry<AutoBuyAffordabilityMode> upgradeAffordabilityMode,
        ConfigEntry<bool> autoBuyStructures,
        ConfigEntry<bool> autoBuyUpgrades,
        ConfigEntry<bool> autoLevelSpells,
        ConfigEntry<AutoBuyPurchaseGroupingMode> purchaseGrouping,
        ConfigEntry<float> autoBuyIntervalSeconds,
        ConfigEntry<int> leaveQueueSlots,
        ConfigEntry<AutoBuyBatchSizingMode> autoBuyBatchSizingMode,
        ConfigEntry<int> maxPurchasesPerBatch,
        ConfigEntry<int> fixedGroupSize,
        ConfigEntry<bool> prioritizeCostAndQualityStructures,
        ConfigEntry<string> allowedAutoBuyUuids,
        ConfigEntry<string> blockedAutoBuyUuids,
        ConfigEntry<AutoCastOperationMode> autoCastMode,
        ConfigEntry<KeyboardShortcut> autoCastToggleShortcut,
        ConfigEntry<bool> autoCastShowToggleButton,
        ConfigEntry<float> autoCastIntervalSeconds,
        ConfigEntry<float> autoCastStartResourcePercent,
        ConfigEntry<float> autoCastManualPauseSeconds,
        ConfigEntry<bool> autoCastFullCharge,
        ConfigEntry<AutoConceptOperationMode> autoConceptMode,
        ConfigEntry<AutoConceptSlotManagementMode> autoConceptSlotManagementMode,
        ConfigEntry<bool> autoConceptShowToggleButton,
        ConfigEntry<int> autoConceptTrainingPeriodSeconds,
        ConfigEntry<int> autoConceptFallbackEvaluationIntervalSeconds,
        ConfigEntry<int> autoConceptQuantityCap,
        ConfigEntry<float> autoConceptRateReservePercent,
        ConfigEntry<float> autoConceptMinimumResourcePercent,
        ConfigEntry<float> autoConceptMinimumDrainRatio,
        ConfigEntry<string> allowedAutoConceptUuids,
        ConfigEntry<string> blockedAutoConceptUuids,
        ConfigEntry<AutoHarvestOperationMode> autoHarvestMode,
        ConfigEntry<bool> autoHarvestFruitTrees,
        ConfigEntry<bool> autoHarvestTreasureTrees,
        ConfigEntry<float> autoHarvestEvaluationIntervalSeconds,
        ConfigEntry<AutoAgromancyOperationMode> autoAgromancyMode,
        ConfigEntry<bool> allowUnverifiedGameBuild,
        ConfigEntry<string> acceptedUnverifiedBuildFingerprint,
        ConfigEntry<bool> emergencyDisable,
        ConfigEntry<string> absoluteReserve,
        ConfigEntry<float> relativeReserveMultiplier)
    {
        Enabled = enabled;
        AutoBuyMode = autoBuyMode;
        AutoBuyAffordability = autoBuyAffordabilityMode;
        UpgradeAffordability = upgradeAffordabilityMode;
        AutoBuyStructures = autoBuyStructures;
        AutoBuyUpgrades = autoBuyUpgrades;
        AutoLevelSpells = autoLevelSpells;
        PurchaseGrouping = purchaseGrouping;
        AutoBuyIntervalSeconds = autoBuyIntervalSeconds;
        LeaveQueueSlots = leaveQueueSlots;
        AutoBuyBatchSizing = autoBuyBatchSizingMode;
        MaxPurchasesPerBatch = maxPurchasesPerBatch;
        FixedGroupSize = fixedGroupSize;
        PrioritizeCostAndQualityStructures = prioritizeCostAndQualityStructures;
        AllowedAutoBuyUuids = allowedAutoBuyUuids;
        BlockedAutoBuyUuids = blockedAutoBuyUuids;
        AutoCastMode = autoCastMode;
        AutoCastToggleShortcut = autoCastToggleShortcut;
        AutoCastShowToggleButton = autoCastShowToggleButton;
        AutoCastIntervalSeconds = autoCastIntervalSeconds;
        AutoCastStartResourcePercent = autoCastStartResourcePercent;
        AutoCastManualPauseSeconds = autoCastManualPauseSeconds;
        AutoCastFullCharge = autoCastFullCharge;
        AutoConceptMode = autoConceptMode;
        AutoConceptSlotManagement = autoConceptSlotManagementMode;
        AutoConceptShowToggleButton = autoConceptShowToggleButton;
        AutoConceptTrainingPeriodSeconds = autoConceptTrainingPeriodSeconds;
        AutoConceptFallbackEvaluationIntervalSeconds = autoConceptFallbackEvaluationIntervalSeconds;
        AutoConceptQuantityCap = autoConceptQuantityCap;
        AutoConceptRateReservePercent = autoConceptRateReservePercent;
        AutoConceptMinimumResourcePercent = autoConceptMinimumResourcePercent;
        AutoConceptMinimumDrainRatio = autoConceptMinimumDrainRatio;
        AllowedAutoConceptUuids = allowedAutoConceptUuids;
        BlockedAutoConceptUuids = blockedAutoConceptUuids;
        AutoHarvestMode = autoHarvestMode;
        AutoHarvestFruitTrees = autoHarvestFruitTrees;
        AutoHarvestTreasureTrees = autoHarvestTreasureTrees;
        AutoHarvestEvaluationIntervalSeconds = autoHarvestEvaluationIntervalSeconds;
        AutoAgromancyMode = autoAgromancyMode;
        AllowUnverifiedGameBuild = allowUnverifiedGameBuild;
        AcceptedUnverifiedBuildFingerprint = acceptedUnverifiedBuildFingerprint;
        EmergencyDisable = emergencyDisable;
        AbsoluteReserve = absoluteReserve;
        RelativeReserveMultiplier = relativeReserveMultiplier;
        RefreshCurrent();
        config.SettingChanged += (_, _) =>
        {
            RefreshCurrent();
            Volatile.Write(ref _unpublishedChange, 1);
        };
        emergencyDisable.SettingChanged += (_, _) =>
        {
            if (!emergencyDisable.Value)
                Volatile.Write(ref _emergencyClearRequested, 1);
        };
    }

    public ConfigEntry<bool> Enabled { get; }

    public ConfigEntry<AutoBuyOperationMode> AutoBuyMode { get; }

    public ConfigEntry<AutoBuyAffordabilityMode> AutoBuyAffordability { get; }

    public ConfigEntry<AutoBuyAffordabilityMode> UpgradeAffordability { get; }

    public ConfigEntry<bool> AutoBuyStructures { get; }

    public ConfigEntry<bool> AutoBuyUpgrades { get; }

    public ConfigEntry<AutoBuyPurchaseGroupingMode> PurchaseGrouping { get; }

    public ConfigEntry<float> AutoBuyIntervalSeconds { get; }

    public ConfigEntry<int> LeaveQueueSlots { get; }

    public ConfigEntry<AutoBuyBatchSizingMode> AutoBuyBatchSizing { get; }

    public ConfigEntry<int> MaxPurchasesPerBatch { get; }

    public ConfigEntry<int> FixedGroupSize { get; }

    public ConfigEntry<bool> PrioritizeCostAndQualityStructures { get; }

    public ConfigEntry<string> AllowedAutoBuyUuids { get; }

    public ConfigEntry<string> BlockedAutoBuyUuids { get; }

    public ConfigEntry<AutoCastOperationMode> AutoCastMode { get; }

    public ConfigEntry<KeyboardShortcut> AutoCastToggleShortcut { get; }

    public ConfigEntry<bool> AutoCastShowToggleButton { get; }

    public ConfigEntry<float> AutoCastIntervalSeconds { get; }

    public ConfigEntry<float> AutoCastStartResourcePercent { get; }

    public ConfigEntry<float> AutoCastManualPauseSeconds { get; }

    public ConfigEntry<bool> AutoCastFullCharge { get; }

    public ConfigEntry<AutoConceptOperationMode> AutoConceptMode { get; }
    public ConfigEntry<AutoConceptSlotManagementMode> AutoConceptSlotManagement { get; }
    public ConfigEntry<bool> AutoConceptShowToggleButton { get; }
    public ConfigEntry<bool> AutoLevelSpells { get; }
    public ConfigEntry<int> AutoConceptTrainingPeriodSeconds { get; }
    public ConfigEntry<int> AutoConceptFallbackEvaluationIntervalSeconds { get; }
    public ConfigEntry<int> AutoConceptQuantityCap { get; }
    public ConfigEntry<float> AutoConceptRateReservePercent { get; }
    public ConfigEntry<float> AutoConceptMinimumResourcePercent { get; }
    public ConfigEntry<float> AutoConceptMinimumDrainRatio { get; }
    public ConfigEntry<string> AllowedAutoConceptUuids { get; }
    public ConfigEntry<string> BlockedAutoConceptUuids { get; }

    public ConfigEntry<AutoHarvestOperationMode> AutoHarvestMode { get; }
    public ConfigEntry<bool> AutoHarvestFruitTrees { get; }
    public ConfigEntry<bool> AutoHarvestTreasureTrees { get; }
    public ConfigEntry<float> AutoHarvestEvaluationIntervalSeconds { get; }
    public ConfigEntry<AutoAgromancyOperationMode> AutoAgromancyMode { get; }

    public ConfigEntry<bool> AllowUnverifiedGameBuild { get; }

    internal ConfigEntry<string> AcceptedUnverifiedBuildFingerprint { get; }

    public ConfigEntry<bool> EmergencyDisable { get; }

    public ConfigEntry<string> AbsoluteReserve { get; }

    public ConfigEntry<float> RelativeReserveMultiplier { get; }

    public SuiteRuntimeConfiguration Current => Volatile.Read(ref _current);

    internal void SetAutoBuyMode(AutoBuyOperationMode mode) => AutoBuyMode.Value = mode;

    internal void SetAutoCastMode(AutoCastOperationMode mode) => AutoCastMode.Value = mode;

    internal void SetAutoConceptMode(AutoConceptOperationMode mode) => AutoConceptMode.Value = mode;

    internal void SetAutoHarvestMode(AutoHarvestOperationMode mode) => AutoHarvestMode.Value = mode;

    internal void SetMentorMode(MentorOperationMode mode)
    {
        if (_mentor is null) throw new InvalidOperationException("Mentor configuration is not attached.");
        _mentor.Mode.Value = mode;
    }

    internal void SetEmergencyStop(bool stopped) => EmergencyDisable.Value = stopped;

    internal void SetAllowUnverifiedGameBuild(bool allowed) =>
        AllowUnverifiedGameBuild.Value = allowed;

    internal void AcceptUnverifiedBuild(string fingerprint) =>
        AcceptedUnverifiedBuildFingerprint.Value = fingerprint ?? string.Empty;

    internal bool IsAutoCastTogglePressed() => AutoCastToggleShortcut.Value.IsDown();

    internal void RefreshCurrent() =>
        Volatile.Write(ref _current, BepInExAutomataConfigurationReader.Read(this));

    internal void AttachMentor(MentorConfig mentor)
    {
        _mentor = mentor ?? throw new ArgumentNullException(nameof(mentor));
        RefreshCurrent();
    }

    internal MentorConfig? Mentor => _mentor;

    /// <summary>
    /// The reading to publish, if the settings have changed since this was last asked.
    /// </summary>
    /// <remarks>
    /// Every source of change lands here — the suite's own panel, BepInEx's configuration manager, an
    /// edited file reloaded from disk — because BepInEx raises one event for all of them. The caller
    /// takes the change on the main thread rather than being handed it from the event, since the file
    /// watcher raises that event on a thread of its own and the suite's publications are replaced by
    /// the main thread only.
    /// </remarks>
    internal bool TryTakeUnpublishedChange(out SuiteRuntimeConfiguration configuration)
    {
        if (Interlocked.Exchange(ref _unpublishedChange, 0) == 0)
        {
            configuration = null!;
            return false;
        }

        configuration = Current;
        return true;
    }

    /// <summary>
    /// Consumes an explicit post-bind request to clear the saved emergency stop.
    /// </summary>
    /// <remarks>
    /// Initial config binding does not raise this signal. On an unverified build, that distinction
    /// lets the General emergency switch act as explicit consent without treating a persisted
    /// false default as consent after the game assemblies change.
    /// </remarks>
    internal bool TryTakeEmergencyClearRequest() =>
        Interlocked.Exchange(ref _emergencyClearRequested, 0) != 0;

    public static BepInExAutomataConfiguration Bind(ConfigFile config)
    {
        var result = TryBind(config);
        if (!result.Success)
            throw new InvalidOperationException(result.Status.Reason);
        return result.Config!;
    }

    public static ConfigurationSchemaBindResult<BepInExAutomataConfiguration> TryBind(
        ConfigFile config,
        IConfigurationFileOperations? fileOperations = null,
        ConfigurationSchemaStatusRegistry? statuses = null) =>
        ConfigurationSchemaTransaction.Bind(
            PluginIds.SuiteGuid,
            config,
            SuiteConfigurationSchema.Plan,
            BindCurrent,
            fileOperations,
            statuses);

    internal static BepInExAutomataConfiguration BindCurrent(ConfigFile config)
    {
            var autoBuyMode = Bind(
                config,
                "AutoBuy",
                "Mode",
                AutoBuyOperationMode.Active,
                "Disabled stops Auto Buy. Active purchases through the native queue after every action is revalidated.",
                10,
                0);

            var autoCastMode = Bind(
                config,
                "AutoCast",
                "Mode",
                AutoCastOperationMode.Disabled,
                "Disabled stops Auto Cast. Active casts through the native spell manager.",
                15,
                0);

            var autoConceptMode = Bind(
                config,
                "AutoConcept",
                "Mode",
                AutoConceptOperationMode.Disabled,
                "Disabled performs no concept work. Active trains the lowest-mastery discovered Scholar concepts through the native Active Concepts list.",
                17,
                0);

            var autoConceptFallbackEvaluationIntervalSeconds = Bind(
                config,
                "AutoConcept",
                "FallbackEvaluationIntervalSeconds",
                300,
                "Maximum idle seconds before a fallback Auto Concept evaluation. Published lifecycle, mastery, slot, quantity, and safety changes can wake it earlier.",
                17,
                10,
                new AcceptableValueRange<int>(10, 1800),
                AutoConceptActiveDependencies);

            var result = new BepInExAutomataConfiguration(
                config,
                Bind(config, "General", "Enabled", true, "Master switch for automation and mastery catch-up. The in-game configuration and safety controls remain available while this is off.", 0, 0),
                autoBuyMode,
                Bind(config, "AutoBuy", "AffordabilityMode", AutoBuyAffordabilityMode.Excess100, "Affordability policy for structures. BuyAll accepts any affordable purchase; excess modes require current resources to be at least 10x, 100x, or 1000x the true cost.", 10, 30, dependencies: AutoBuyStructuresActiveDependencies),
                Bind(config, "AutoBuy", "UpgradeAffordabilityMode", AutoBuyAffordabilityMode.Excess100, "Independent affordability policy for upgrades. BuyAll accepts any affordable upgrade; excess modes require current resources to be at least 10x, 100x, or 1000x the true cost.", 10, 31, dependencies: AutoBuyUpgradesActiveDependencies),
                Bind(config, "AutoBuy", "IncludeStructures", true, "Include native StructureSO attributes and levels.", 10, 10, dependencies: AutoBuyActiveDependencies),
                Bind(config, "AutoBuy", "IncludeUpgrades", true, "Include native UpgradeSO purchases.", 10, 20, dependencies: AutoBuyActiveDependencies),
                Bind(config, "AutoBuy", "AutoLevelSpells", true, "Automatically level ready spells while Auto Buy is active. Capability follows native progression automatically: locked, one spell per action, then the native level-all action after its upgrade completes.", 10, 25, dependencies: AutoBuyActiveDependencies),
                Bind(config, "AutoBuy", "PurchaseGrouping", AutoBuyPurchaseGroupingMode.BulkDevelopment, "Group size for each ranked candidate before Auto Buy advances and later repeats the ranked pass. Single buys one level; Fixed groups Structures by FixedGroupSize; BulkDevelopment follows the live Player value for Structures; ActionMultiplier follows the live native multiplier for either purchase family. Upgrades otherwise remain one level.", 10, 55, dependencies: AutoBuyPurchaseGroupingDependencies),
                Bind(config, "AutoBuy", "EvaluationIntervalSeconds", 0.5f, "Minimum unscaled seconds between Auto Buy and Spell Level planning cycles when no earlier service wake is due.", 10, 90, new AcceptableValueRange<float>(0.25f, 10.0f), AutoBuyActiveDependencies),
                Bind(config, "AutoBuy", "LeaveQueueSlots", 1, "Minimum native action-queue slots Automata leaves free for manual actions.", 10, 70, dependencies: AutoBuyActiveDependencies),
                Bind(config, "AutoBuy", "BatchSizingMode", AutoBuyBatchSizingMode.FillAvailableQueue, "FillAvailableQueue continues through ranked candidates until only LeaveQueueSlots remain. Fixed queues up to MaxPurchasesPerBatch levels.", 10, 40, dependencies: AutoBuyActiveDependencies),
                Bind(config, "AutoBuy", "MaxPurchasesPerBatch", 8, "Maximum actions proposed by one decision when BatchSizingMode is Fixed.", 10, 50, dependencies: AutoBuyFixedBatchDependencies),
                Bind(config, "AutoBuy", "FixedGroupSize", 2, "Maximum consecutive one-level Structure purchases when PurchaseGrouping is Fixed. Upgrades remain one level.", 10, 56, new AcceptableValueRange<int>(1, 100), AutoBuyFixedGroupingDependencies),
                Bind(config, "AutoBuy", "PrioritizeCostAndQualityStructures", false, "When enabled, unlocked and affordable Structures with native effects proven to reduce costs or increase resource quality rank before ordinary candidates. Unknown effects receive no priority.", 10, 65, dependencies: AutoBuyStructuresActiveDependencies),
                Bind(config, "AutoBuy", "AllowedUuids", string.Empty, "Optional comma-separated allowlist. When non-empty, only these StructureSO or UpgradeSO UUIDs may be purchased.", 10, 120, dependencies: AutoBuyActiveDependencies),
                Bind(config, "AutoBuy", "BlockedUuids", string.Empty, "Comma-separated StructureSO or UpgradeSO UUIDs Automata must never buy.", 10, 130, dependencies: AutoBuyActiveDependencies),
                autoCastMode,
                Bind(config, "AutoCast", "ToggleShortcut", new KeyboardShortcut(UnityEngine.KeyCode.F8), "Toggle Auto Cast between Disabled and Active. Default: F8.", 15, 5),
                Bind(config, "AutoCast", "ShowToggleButton", true, "Show the Auto Cast state button immediately left of the native Auto Buy queue switch.", 15, 6),
                Bind(config, "AutoCast", "EvaluationIntervalSeconds", 0.25f, "Unscaled seconds between Auto Cast evaluations.", 15, 10, new AcceptableValueRange<float>(0.1f, 10.0f), AutoCastActiveDependencies),
                Bind(config, "AutoCast", "StartResourcePercent", 0.0f, "Minimum fullness for every finite-cap resource used by a spell's immediate or drain cost. Fresh installs default to 0%.", 15, 20, new AcceptableValueRange<float>(0.0f, 100.0f), AutoCastActiveDependencies),
                Bind(config, "AutoCast", "ManualPauseSeconds", 2.0f, "Unscaled pause after a manual spell fire before Auto Cast resumes.", 15, 30, new AcceptableValueRange<float>(0.0f, 60.0f), AutoCastActiveDependencies),
                Bind(config, "AutoCast", "FullCharge", true, "When enabled, Auto Cast holds charge-capable spells until the native full-charge point. When disabled, it fires them immediately without charging.", 15, 1, dependencies: AutoCastActiveDependencies),
                autoConceptMode,
                Bind(config, "AutoConcept", "SlotManagementMode", AutoConceptSlotManagementMode.TimedCycle, "RotateAll replaces active concepts when a compatible discovered concept has strictly lower mastery. PreserveManual fills empty slots and rotates only quantities added by Automata. TimedCycle rotates compatible concepts only after their full settled training period, even if they already caught up.", 17, 5, dependencies: AutoConceptActiveDependencies),
                Bind(config, "AutoConcept", "ShowToggleButton", true, "Show the Auto Concept state button in the native Auto Buy-anchored control strip.", 17, 6),
                Bind(config, "AutoConcept", "TrainingPeriodSeconds", 300, "RotateAll and PreserveManual protect a newly assigned concept until it catches the captured highest mastery or this settled time elapses. TimedCycle always uses the full settled period.", 17, 7, new AcceptableValueRange<int>(10, 3600), AutoConceptActiveDependencies),
                autoConceptFallbackEvaluationIntervalSeconds,
                Bind(config, "AutoConcept", "PerConceptQuantityCap", 0, "Optional maximum automated quantity per concept. Zero uses the native mastery maximum.", 17, 20, new AcceptableValueRange<int>(0, 1000000), AutoConceptActiveDependencies),
                Bind(config, "AutoConcept", "RateReservePercent", 10.0f, "Minimum percentage of each drained resource's current gross positive rate to preserve after an automated quantity change.", 17, 30, new AcceptableValueRange<float>(0.0f, 100.0f), AutoConceptActiveDependencies),
                Bind(config, "AutoConcept", "MinimumResourcePercent", 10.0f, "Finite-cap drained resources must be at least this full before Auto Concept adds quantity.", 17, 40, new AcceptableValueRange<float>(0.0f, 100.0f), AutoConceptActiveDependencies),
                Bind(config, "AutoConcept", "MinimumDrainRatio", 0.95f, "Native post-settlement drain ratio floor. Falling below it rolls back only Automata-owned quantity.", 17, 50, new AcceptableValueRange<float>(0.0f, 1.0f), AutoConceptActiveDependencies),
                Bind(config, "AutoConcept", "AllowedUuids", string.Empty, "Optional comma-separated concept allowlist. Empty allows every validated recipe in ConceptRecipes.", 17, 80, dependencies: AutoConceptActiveDependencies),
                Bind(config, "AutoConcept", "BlockedUuids", string.Empty, "Comma-separated concept UUIDs Auto Concept must never train.", 17, 90, dependencies: AutoConceptActiveDependencies),
                Bind(config, "AutoHarvest", "Mode", AutoHarvestOperationMode.Disabled, "Disabled performs no harvest work. Active queues one audited native fruit-tree or treasure-tree collect action at a time.", 18, 0),
                Bind(config, "AutoHarvest", "CollectFruitTrees", true, "Collect ready fruit trees through their native plot action.", 18, 10, dependencies: AutoHarvestActiveDependencies),
                Bind(config, "AutoHarvest", "CollectTreasureTrees", true, "Collect ready treasure trees through their native plot action.", 18, 20, dependencies: AutoHarvestActiveDependencies),
                Bind(config, "AutoHarvest", "EvaluationIntervalSeconds", 1.0f, "Unscaled seconds between exact Auto Harvest readiness checks.", 18, 30, new AcceptableValueRange<float>(0.25f, 10.0f), AutoHarvestActiveDependencies),
                Bind(config, "AutoAgromancy", "Mode", AutoAgromancyOperationMode.Disabled, "Disabled performs no Druidry level balancing. Active rebalances an increased pair or all active pairs after accepted plot and verified Auto Harvest submissions.", 19, 0),
                Bind(config, "Compatibility", "AllowUnverifiedGameBuild", false, "Advanced risk acknowledgement. Allows gameplay patches and services on the exact unaudited assembly pair observed when this is enabled. A later game update automatically returns the suite to quarantine.", 50, 0),
                Bind(config, "Compatibility", "AcceptedUnverifiedBuildFingerprint", string.Empty, "Exact assembly-pair fingerprint accepted by the player. Managed by the suite.", 50, 10, hidden: true),
                Bind(config, "Safety", "EmergencyDisable", false, "Suite-wide emergency stop: halts new purchases, casts, concepts, spell levels, harvest submissions, Druidry level adjustments, and mastery sharing immediately.", 40, 0),
                Bind(config, "Reserves", "AbsoluteReserve", "0", "Absolute amount of every resource to leave after each automated purchase or cast.", 20, 0),
                Bind(config, "Reserves", "RelativeReserveMultiplier", 0.0f, "Additional amount to leave after each action, expressed as a multiple of that action's cost. Affordability modes remain separate.", 20, 10));

            return result;
    }

    private static ConfigEntry<T> Bind<T>(
        ConfigFile config,
        string section,
        string key,
        T defaultValue,
        string description,
        int sectionOrder,
        int settingOrder,
        AcceptableValueBase? acceptableValues = null,
        IReadOnlyList<ModConfigDependency>? dependencies = null,
        bool restartRequired = false,
        bool hidden = false)
    {
        var advancedAutoBuy = section == "AutoBuy" && (key == "AllowedUuids" || key == "BlockedUuids");
        var advancedAutoConcept = section == "AutoConcept" && key == "FallbackEvaluationIntervalSeconds";
        var displaySection = section switch
        {
            "General" when key == "Enabled" => "General",
            "Safety" when key == "EmergencyDisable" => "General",
            "AutoBuy" when !advancedAutoBuy => "Auto Buy",
            "AutoCast" => "Auto Cast",
            "AutoConcept" when !advancedAutoConcept => "Auto Concept",
            "AutoHarvest" => "Auto Harvest",
            "AutoAgromancy" => "Auto Agromancy",
            _ => "Advanced",
        };
        var displayName = key switch
        {
            "Enabled" when section == "General" => "Automation enabled",
            "Mode" when section == "AutoBuy" => "Auto Buy",
            "Mode" when section == "AutoCast" => "Auto Cast",
            "Mode" when section == "AutoConcept" => "Auto Concept",
            "Mode" when section == "AutoHarvest" => "Auto Harvest",
            "Mode" when section == "AutoAgromancy" => "Auto Agromancy",
            "CollectFruitTrees" => "Collect fruit trees",
            "CollectTreasureTrees" => "Collect treasure trees",
            "EvaluationIntervalSeconds" when section == "AutoHarvest" => "Evaluation interval (seconds)",
            "SlotManagementMode" => "Slot management",
            "TrainingPeriodSeconds" => "Training period (seconds)",
            "AutoLevelSpells" => "Auto-level spells",
            "FallbackEvaluationIntervalSeconds" => "Auto Concept fallback evaluation (seconds)",
            "AllowUnverifiedGameBuild" => "Allow this unverified game build",
            "AffordabilityMode" => "Structure affordability",
            "UpgradeAffordabilityMode" => "Upgrade affordability",
            "IncludeStructures" => "Buy structures",
            "IncludeUpgrades" => "Buy upgrades",
            "StartResourcePercent" => "Minimum resource percent",
            "AllowedUuids" => "Allowed UUIDs",
            "BlockedUuids" => "Blocked UUIDs",
            _ => null,
        };
        var presentationOrder = displaySection == "General" ? -10 : displaySection == "Auto Buy" ? 0 : displaySection == "Auto Cast" ? 10 : displaySection == "Auto Concept" ? 15 : displaySection == "Auto Harvest" ? 17 : displaySection == "Auto Agromancy" ? 18 : 20;
        var metadata = dependencies is null
            ? new ModConfigMetadata(presentationOrder, settingOrder, hidden, displaySection, displayName, restartRequired)
            : new ModConfigMetadata(presentationOrder, settingOrder, dependencies, hidden, displaySection, displayName, restartRequired);
        return config.Bind(
            section,
            key,
            defaultValue,
            new ConfigDescription(
                description,
                acceptableValues,
                metadata));
    }

}

internal enum AutoBuyOperationMode
{
    Disabled,
    Active
}

internal enum AutoCastOperationMode
{
    Disabled,
    Active,
}

internal enum AutoHarvestOperationMode
{
    Disabled,
    Active,
}

internal enum LegacyResearchAutomationMode
{
    Disabled,
    DryRun,
    Active,
}

internal enum AutoBuyAffordabilityMode
{
    BuyAll,
    Excess10,
    Excess100,
    Excess1000
}

internal enum AutoBuyBatchSizingMode
{
    Fixed,
    FillAvailableQueue,
}

internal enum AutoBuyPurchaseGroupingMode
{
    Single,
    Fixed,
    BulkDevelopment,
    ActionMultiplier,
}
