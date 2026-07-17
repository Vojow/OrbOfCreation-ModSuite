using System;
using BepInEx.Configuration;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutomataConfig
{
    private AutomataConfig(
        ConfigEntry<bool> enabled,
        ConfigEntry<AutoBuyOperationMode> autoBuyMode,
        ConfigEntry<AutoBuyAffordabilityMode> autoBuyAffordabilityMode,
        ConfigEntry<AutoBuyAffordabilityMode> upgradeAffordabilityMode,
        ConfigEntry<bool> autoBuyStructures,
        ConfigEntry<bool> autoBuyUpgrades,
        ConfigEntry<bool> respectActionMultiplier,
        ConfigEntry<float> autoBuyIntervalSeconds,
        ConfigEntry<int> leaveQueueSlots,
        ConfigEntry<int> autoBuyMaxCandidatesPerScan,
        ConfigEntry<AutoBuyBatchSizingMode> autoBuyBatchSizingMode,
        ConfigEntry<int> maxPurchasesPerBatch,
        ConfigEntry<AutoBuyStructureRepeatMode> structureRepeatMode,
        ConfigEntry<int> fixedStructureLevelsPerCandidate,
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
        ConfigEntry<int> autoConceptRebalanceIntervalSeconds,
        ConfigEntry<int> autoConceptQuantityCap,
        ConfigEntry<float> autoConceptRateReservePercent,
        ConfigEntry<float> autoConceptMinimumResourcePercent,
        ConfigEntry<float> autoConceptMinimumDrainRatio,
        ConfigEntry<string> allowedAutoConceptUuids,
        ConfigEntry<string> blockedAutoConceptUuids,
        ConfigEntry<bool> emergencyDisable,
        ConfigEntry<float> cpuBudgetMilliseconds,
        ConfigEntry<bool> enableOperationalLogging,
        ConfigEntry<int> maxLoggedRejections,
        ConfigEntry<AutomataDecisionLogLevel> decisionLogLevel,
        ConfigEntry<string> absoluteReserve,
        ConfigEntry<float> relativeReserveMultiplier)
    {
        Enabled = enabled;
        AutoBuyMode = autoBuyMode;
        AutoBuyAffordability = autoBuyAffordabilityMode;
        UpgradeAffordability = upgradeAffordabilityMode;
        AutoBuyStructures = autoBuyStructures;
        AutoBuyUpgrades = autoBuyUpgrades;
        RespectActionMultiplier = respectActionMultiplier;
        AutoBuyIntervalSeconds = autoBuyIntervalSeconds;
        LeaveQueueSlots = leaveQueueSlots;
        AutoBuyMaxCandidatesPerScan = autoBuyMaxCandidatesPerScan;
        AutoBuyBatchSizing = autoBuyBatchSizingMode;
        MaxPurchasesPerBatch = maxPurchasesPerBatch;
        StructureRepeatMode = structureRepeatMode;
        FixedStructureLevelsPerCandidate = fixedStructureLevelsPerCandidate;
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
        AutoConceptRebalanceIntervalSeconds = autoConceptRebalanceIntervalSeconds;
        AutoConceptQuantityCap = autoConceptQuantityCap;
        AutoConceptRateReservePercent = autoConceptRateReservePercent;
        AutoConceptMinimumResourcePercent = autoConceptMinimumResourcePercent;
        AutoConceptMinimumDrainRatio = autoConceptMinimumDrainRatio;
        AllowedAutoConceptUuids = allowedAutoConceptUuids;
        BlockedAutoConceptUuids = blockedAutoConceptUuids;
        EmergencyDisable = emergencyDisable;
        CpuBudgetMilliseconds = cpuBudgetMilliseconds;
        EnableOperationalLogging = enableOperationalLogging;
        MaxLoggedRejections = maxLoggedRejections;
        DecisionLogLevel = decisionLogLevel;
        AbsoluteReserve = absoluteReserve;
        RelativeReserveMultiplier = relativeReserveMultiplier;
    }

    public ConfigEntry<bool> Enabled { get; }

    public ConfigEntry<AutoBuyOperationMode> AutoBuyMode { get; }

    public ConfigEntry<AutoBuyAffordabilityMode> AutoBuyAffordability { get; }

    public ConfigEntry<AutoBuyAffordabilityMode> UpgradeAffordability { get; }

    public ConfigEntry<bool> AutoBuyStructures { get; }

    public ConfigEntry<bool> AutoBuyUpgrades { get; }

    public ConfigEntry<bool> RespectActionMultiplier { get; }

    public ConfigEntry<float> AutoBuyIntervalSeconds { get; }

    public ConfigEntry<int> LeaveQueueSlots { get; }

    public ConfigEntry<int> AutoBuyMaxCandidatesPerScan { get; }

    public ConfigEntry<AutoBuyBatchSizingMode> AutoBuyBatchSizing { get; }

    public ConfigEntry<int> MaxPurchasesPerBatch { get; }

    public ConfigEntry<AutoBuyStructureRepeatMode> StructureRepeatMode { get; }

    public ConfigEntry<int> FixedStructureLevelsPerCandidate { get; }

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
    public ConfigEntry<int> AutoConceptRebalanceIntervalSeconds { get; }
    public ConfigEntry<int> AutoConceptQuantityCap { get; }
    public ConfigEntry<float> AutoConceptRateReservePercent { get; }
    public ConfigEntry<float> AutoConceptMinimumResourcePercent { get; }
    public ConfigEntry<float> AutoConceptMinimumDrainRatio { get; }
    public ConfigEntry<string> AllowedAutoConceptUuids { get; }
    public ConfigEntry<string> BlockedAutoConceptUuids { get; }

    public ConfigEntry<bool> EmergencyDisable { get; }

    public ConfigEntry<float> CpuBudgetMilliseconds { get; }

    public ConfigEntry<bool> EnableOperationalLogging { get; }

    public ConfigEntry<int> MaxLoggedRejections { get; }

    public ConfigEntry<AutomataDecisionLogLevel> DecisionLogLevel { get; }

    public ConfigEntry<string> AbsoluteReserve { get; }

    public ConfigEntry<float> RelativeReserveMultiplier { get; }

    public bool IsOperationalLoggingEnabled =>
        EnableOperationalLogging.Value &&
        DecisionLogLevel.Value != AutomataDecisionLogLevel.Off;

    public bool CanStartAutoBuyActively =>
        AutoBuyMode.Value == AutoBuyOperationMode.Active &&
        !EmergencyDisable.Value;

    public bool CanStartAutoCastActively =>
        AutoCastMode.Value == AutoCastOperationMode.Active &&
        !EmergencyDisable.Value;

    public bool CanStartAutoConceptActively =>
        AutoConceptMode.Value == AutoConceptOperationMode.Active &&
        !EmergencyDisable.Value;

    public static AutomataConfig Bind(ConfigFile config)
    {
        var saveOnConfigSet = config.SaveOnConfigSet;
        config.SaveOnConfigSet = false;
        try
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

            var autoConceptMode = BindAutoConceptMode(config);

            var autoConceptRebalanceIntervalSeconds = BindAutoConceptRebalanceIntervalSeconds(config);

            var result = new AutomataConfig(
                Bind(config, "General", "Enabled", true, "Enable Automata.", 0, 0),
                autoBuyMode,
                Bind(config, "AutoBuy", "AffordabilityMode", AutoBuyAffordabilityMode.Excess100, "Affordability policy for structures. BuyAll accepts any affordable purchase; excess modes require current resources to be at least 10x, 100x, or 1000x the true cost.", 10, 30),
                Bind(config, "AutoBuy", "UpgradeAffordabilityMode", AutoBuyAffordabilityMode.Excess100, "Independent affordability policy for upgrades. BuyAll accepts any affordable upgrade; excess modes require current resources to be at least 10x, 100x, or 1000x the true cost.", 10, 31),
                Bind(config, "AutoBuy", "IncludeStructures", true, "Include native StructureSO attributes and levels.", 10, 10),
                Bind(config, "AutoBuy", "IncludeUpgrades", true, "Include native UpgradeSO purchases.", 10, 20),
                Bind(config, "AutoBuy", "RespectActionMultiplier", false, "When enabled, repeat the selected purchase up to the current native action multiplier, capped to free queue room. Every level is still submitted and revalidated separately.", 10, 62),
                Bind(config, "AutoBuy", "EvaluationIntervalSeconds", 0.5f, "Unscaled seconds between idle scans when no eligible purchase is pending. In-progress scans and active queue feeding continue every frame.", 10, 90),
                Bind(config, "AutoBuy", "LeaveQueueSlots", 1, "Minimum native action-queue slots Automata leaves free for manual actions.", 10, 70),
                Bind(config, "AutoBuy", "MaxCandidatesPerScan", 1024, "Safety cap for the combined StructureSO and UpgradeSO registry. CPU-limited scans resume on the next frame.", 10, 110),
                Bind(config, "AutoBuy", "BatchSizingMode", AutoBuyBatchSizingMode.FillAvailableQueue, "FillAvailableQueue continues through ranked candidates until only LeaveQueueSlots remain. Fixed queues up to MaxPurchasesPerBatch levels.", 10, 40),
                Bind(config, "AutoBuy", "MaxPurchasesPerBatch", 8, "Maximum queued levels from one completed scan when BatchSizingMode is Fixed.", 10, 50),
                Bind(config, "AutoBuy", "StructureRepeatMode", AutoBuyStructureRepeatMode.BulkDevelopment, "Used when RespectActionMultiplier is disabled. BulkDevelopment follows the live Player Bulk Development value; Fixed uses FixedStructureLevelsPerCandidate; Single queues each ranked structure once.", 10, 60),
                Bind(config, "AutoBuy", "FixedStructureLevelsPerCandidate", 2, "Maximum consecutive one-level structure purchases only when StructureRepeatMode is Fixed. Ignored by BulkDevelopment and Single.", 10, 61, new AcceptableValueRange<int>(1, 100)),
                Bind(config, "AutoBuy", "PrioritizeCostAndQualityStructures", false, "When enabled, unlocked and affordable Structures with native effects proven to reduce costs or increase resource quality rank before ordinary candidates. Unknown effects receive no priority.", 10, 65),
                Bind(config, "AutoBuy", "AllowedUuids", string.Empty, "Optional comma-separated allowlist. When non-empty, only these StructureSO or UpgradeSO UUIDs may be purchased.", 10, 120),
                Bind(config, "AutoBuy", "BlockedUuids", string.Empty, "Comma-separated StructureSO or UpgradeSO UUIDs Automata must never buy.", 10, 130),
                autoCastMode,
                Bind(config, "AutoCast", "ToggleShortcut", new KeyboardShortcut(UnityEngine.KeyCode.X, UnityEngine.KeyCode.LeftAlt), "Toggle Auto Cast between Disabled and Active. Default: Left Alt + X.", 15, 5),
                Bind(config, "AutoCast", "ShowToggleButton", true, "Show the Auto Cast state button immediately left of the native Auto Buy queue switch.", 15, 6),
                Bind(config, "AutoCast", "EvaluationIntervalSeconds", 0.25f, "Unscaled seconds between Auto Cast evaluations.", 15, 10, new AcceptableValueRange<float>(0.1f, 10.0f)),
                Bind(config, "AutoCast", "StartResourcePercent", 0.0f, "Minimum fullness for every finite-cap resource used by a spell's immediate or drain cost. Fresh installs default to 0%.", 15, 20, new AcceptableValueRange<float>(0.0f, 100.0f)),
                Bind(config, "AutoCast", "ManualPauseSeconds", 2.0f, "Unscaled pause after a manual spell fire before Auto Cast resumes.", 15, 30, new AcceptableValueRange<float>(0.0f, 60.0f)),
                Bind(config, "AutoCast", "FullCharge", true, "When enabled, Auto Cast holds charge-capable spells until the native full-charge point. When disabled, it fires them immediately without charging.", 15, 1),
                autoConceptMode,
                Bind(config, "AutoConcept", "SlotManagementMode", AutoConceptSlotManagementMode.RotateAll, "RotateAll replaces active concepts when a compatible discovered concept has strictly lower mastery. PreserveManual fills empty slots and rotates only quantities added by Automata.", 17, 5),
                autoConceptRebalanceIntervalSeconds,
                Bind(config, "AutoConcept", "PerConceptQuantityCap", 0, "Optional maximum automated quantity per concept. Zero uses the native mastery maximum.", 17, 20, new AcceptableValueRange<int>(0, 1000000)),
                Bind(config, "AutoConcept", "RateReservePercent", 10.0f, "Minimum percentage of each drained resource's current gross positive rate to preserve after an automated quantity change.", 17, 30, new AcceptableValueRange<float>(0.0f, 100.0f)),
                Bind(config, "AutoConcept", "MinimumResourcePercent", 10.0f, "Finite-cap drained resources must be at least this full before Auto Concept adds quantity.", 17, 40, new AcceptableValueRange<float>(0.0f, 100.0f)),
                Bind(config, "AutoConcept", "MinimumDrainRatio", 0.95f, "Native post-settlement drain ratio floor. Falling below it rolls back only Automata-owned quantity.", 17, 50, new AcceptableValueRange<float>(0.0f, 1.0f)),
                Bind(config, "AutoConcept", "AllowedUuids", string.Empty, "Optional comma-separated concept allowlist. Empty allows every validated recipe in ConceptRecipes.", 17, 80),
                Bind(config, "AutoConcept", "BlockedUuids", string.Empty, "Comma-separated concept UUIDs Auto Concept must never train.", 17, 90),
                Bind(config, "Safety", "EmergencyDisable", false, "Stops new Automata purchases and casts immediately.", 40, 0),
                Bind(config, "Performance", "CpuBudgetMilliseconds", 1.0f, "Soft CPU budget for each scan or purchase slice, capped at 1 ms for frame-time safety.", 30, 0, new AcceptableValueRange<float>(0.1f, 1.0f)),
                Bind(config, "Diagnostics", "EnableOperationalLogging", false, "Write normal automation decisions to the BepInEx log. Startup, catalog initialization, warnings, and errors are always logged.", 50, 0),
                Bind(config, "Diagnostics", "MaxLoggedRejections", 12, "Maximum rejected candidates written per verbose evaluation when operational logging is enabled.", 50, 20),
                Bind(config, "Diagnostics", "DecisionLogLevel", AutomataDecisionLogLevel.Summary, "Recommendation detail when operational logging is enabled.", 50, 10),
                Bind(config, "Reserves", "AbsoluteReserve", "0", "Absolute amount of every resource to leave after each automated purchase or cast.", 20, 0),
                Bind(config, "Reserves", "RelativeReserveMultiplier", 0.0f, "Additional amount to leave after each action, expressed as a multiple of that action's cost. Affordability modes remain separate.", 20, 10));

            RemoveLegacySettings(config);
            config.Save();
            return result;
        }
        finally
        {
            config.SaveOnConfigSet = saveOnConfigSet;
        }
    }

    private static void RemoveLegacySettings(ConfigFile config)
    {
        RemoveLegacy(config, "AutoBuy", "ActivePurchaseLimitPerSession", 0);
        RemoveLegacy(config, "AutoBuy", "RuntimeProbeConfirmed", true);
        RemoveLegacy(config, "AutoCast", "RuntimeProbeConfirmed", true);
        RemoveLegacy(config, "Safety", "RuntimeProbeConfirmed", true);
        RemoveLegacy(config, "Safety", "AllowUnvalidatedActiveMode", false);
        RemoveLegacy(config, "Research", "Mode", LegacyResearchAutomationMode.Disabled);
        RemoveLegacy(config, "Research", "EvaluationIntervalSeconds", 0.5f);
        RemoveLegacy(config, "Research", "MaxActionsPerEvaluation", 1);
        RemoveLegacy(config, "Performance", "MaxCandidatesPerEvaluation", 256);
        RemoveLegacy(config, "Research", "AllowUnflaggedResearch", false);
        RemoveLegacy(config, "Research", "PinnedResearchUuids", string.Empty);
        RemoveLegacy(config, "Research", "BlockedResearchUuids", string.Empty);
        RemoveLegacy(config, "Research", "CategoryPriority", string.Empty);
        RemoveLegacy(config, "Reserves", "MaxCostToQuantityRatio", 1.0f);
        RemoveLegacy(config, "ActiveMode", "StartMethod", "Develop");
    }

    private static void RemoveLegacy<T>(ConfigFile config, string section, string key, T defaultValue)
    {
        var definition = new ConfigDefinition(section, key);
        config.Bind(section, key, defaultValue, "Removed legacy setting.");
        config.Remove(definition);
    }

    private static ConfigEntry<AutoConceptOperationMode> BindAutoConceptMode(ConfigFile config)
    {
        const string section = "AutoConcept";
        const string key = "Mode";
        var definition = new ConfigDefinition(section, key);
        var serializedMode = config.Bind(section, key, AutoConceptOperationMode.Disabled.ToString(), "Auto Concept mode migration.").Value;
        config.Remove(definition);

        var result = Bind(
            config,
            section,
            key,
            AutoConceptOperationMode.Disabled,
            "Disabled performs no concept work. Active trains the lowest-mastery discovered Scholar concepts through the native Active Concepts list.",
            17,
            0);
        if (string.Equals(serializedMode, "Active", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(serializedMode, "BalanceMastery", StringComparison.OrdinalIgnoreCase))
        {
            result.Value = AutoConceptOperationMode.Active;
        }

        return result;
    }

    private static ConfigEntry<int> BindAutoConceptRebalanceIntervalSeconds(ConfigFile config)
    {
        var result = Bind(
            config,
            "AutoConcept",
            "RebalanceIntervalSeconds",
            300,
            "Seconds between ordinary mastery rebalances. Lifecycle, mastery, slot, and safety changes can trigger an earlier pass.",
            17,
            10,
            new AcceptableValueRange<int>(10, 1800));

        var legacyDefinition = new ConfigDefinition("AutoConcept", "RebalanceIntervalMinutes");
        var legacyMinutes = config.Bind(
            "AutoConcept",
            "RebalanceIntervalMinutes",
            -1.0f,
            new ConfigDescription("Legacy Auto Concept interval migration.")).Value;
        config.Remove(legacyDefinition);
        if (float.IsFinite(legacyMinutes) && legacyMinutes >= 0.0f)
        {
            result.Value = Math.Clamp(
                (int)Math.Round(legacyMinutes * 60.0f, MidpointRounding.AwayFromZero),
                10,
                1800);
        }

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
        AcceptableValueBase? acceptableValues = null)
    {
        var hidden = section == "General" && key == "Enabled";
        var advancedAutoBuy = section == "AutoBuy" && (key == "AllowedUuids" || key == "BlockedUuids" || key == "MaxCandidatesPerScan");
        var displaySection = section switch
        {
            "AutoBuy" when !advancedAutoBuy => "Auto Buy",
            "AutoCast" => "Auto Cast",
            "AutoConcept" => "Auto Concept",
            _ => "Advanced",
        };
        var displayName = key switch
        {
            "Mode" when section == "AutoBuy" => "Auto Buy",
            "Mode" when section == "AutoCast" => "Auto Cast",
            "Mode" when section == "AutoConcept" => "Auto Concept",
            "SlotManagementMode" => "Slot management",
            "AffordabilityMode" => "Structure affordability",
            "UpgradeAffordabilityMode" => "Upgrade affordability",
            "IncludeStructures" => "Buy structures",
            "IncludeUpgrades" => "Buy upgrades",
            "StartResourcePercent" => "Minimum resource percent",
            "CpuBudgetMilliseconds" => "CPU budget (ms)",
            "AllowedUuids" => "Allowed UUIDs",
            "BlockedUuids" => "Blocked UUIDs",
            _ => null,
        };
        var presentationOrder = displaySection == "Auto Buy" ? 0 : displaySection == "Auto Cast" ? 10 : displaySection == "Auto Concept" ? 15 : 20;
        return config.Bind(
            section,
            key,
            defaultValue,
            new ConfigDescription(
                description,
                acceptableValues,
                new ModConfigMetadata(presentationOrder, settingOrder, hidden, displaySection, displayName)));
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

internal enum AutoBuyStructureRepeatMode
{
    Single,
    Fixed,
    BulkDevelopment,
}

internal enum AutomataDecisionLogLevel
{
    Off,
    Summary,
    Verbose
}
