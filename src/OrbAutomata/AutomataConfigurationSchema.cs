using System;
using System.Collections.Generic;
using System.Globalization;
using OrbModding.Common;

namespace OrbAutomata;

internal static class AutomataConfigurationSchema
{
    internal static readonly ConfigurationKey AutoConceptMode = new("AutoConcept", "Mode");
    internal static readonly ConfigurationKey FallbackInterval = new("AutoConcept", "FallbackEvaluationIntervalSeconds");
    internal static readonly ConfigurationKey PreviousIntervalSeconds = new("AutoConcept", "RebalanceIntervalSeconds");
    internal static readonly ConfigurationKey LegacyIntervalMinutes = new("AutoConcept", "RebalanceIntervalMinutes");
    internal static readonly ConfigurationKey PurchaseGrouping = new("AutoBuy", "PurchaseGrouping");
    internal static readonly ConfigurationKey FixedGroupSize = new("AutoBuy", "FixedGroupSize");
    internal static readonly ConfigurationKey LegacyRespectActionMultiplier = new("AutoBuy", "RespectActionMultiplier");
    internal static readonly ConfigurationKey LegacyRepeatWhileAffordable = new("AutoBuy", "RepeatWhileAffordable");
    internal static readonly ConfigurationKey LegacyStructureRepeatMode = new("AutoBuy", "StructureRepeatMode");
    internal static readonly ConfigurationKey LegacyFixedStructureLevels = new("AutoBuy", "FixedStructureLevelsPerCandidate");
    internal static readonly IReadOnlyList<ConfigurationKey> DiscardedObsoleteKeys = new[]
    {
        new ConfigurationKey("AutoBuy", "ActivePurchaseLimitPerSession"),
        new ConfigurationKey("AutoBuy", "RuntimeProbeConfirmed"),
        new ConfigurationKey("AutoCast", "RuntimeProbeConfirmed"),
        new ConfigurationKey("Safety", "RuntimeProbeConfirmed"),
        new ConfigurationKey("Safety", "AllowUnvalidatedActiveMode"),
        new ConfigurationKey("Research", "Mode"),
        new ConfigurationKey("Research", "EvaluationIntervalSeconds"),
        new ConfigurationKey("Research", "MaxActionsPerEvaluation"),
        new ConfigurationKey("Performance", "MaxCandidatesPerEvaluation"),
        new ConfigurationKey("Research", "AllowUnflaggedResearch"),
        new ConfigurationKey("Research", "PinnedResearchUuids"),
        new ConfigurationKey("Research", "BlockedResearchUuids"),
        new ConfigurationKey("Research", "CategoryPriority"),
        new ConfigurationKey("Reserves", "MaxCostToQuantityRatio"),
        new ConfigurationKey("ActiveMode", "StartMethod"),
        new ConfigurationKey("AutoConcept", "AutoLevelSpells"),
    };

    internal static ConfigurationSchemaPlan Plan { get; } = CreatePlan();

    private static ConfigurationSchemaPlan CreatePlan()
    {
        var knownKeys = new List<ConfigurationKey>
        {
            AutoConceptMode,
            FallbackInterval,
            PreviousIntervalSeconds,
            LegacyIntervalMinutes,
        };
        knownKeys.AddRange(DiscardedObsoleteKeys);
        return new ConfigurationSchemaPlan(3, new[]
        {
            new ConfigurationMigrationStep(0, 1, knownKeys, MigrateVersionZero),
            new ConfigurationMigrationStep(
                1,
                2,
                Array.Empty<ConfigurationKey>(),
                static _ => { }),
            new ConfigurationMigrationStep(
                2,
                3,
                new[]
                {
                    PurchaseGrouping,
                    FixedGroupSize,
                    LegacyRespectActionMultiplier,
                    LegacyRepeatWhileAffordable,
                    LegacyStructureRepeatMode,
                    LegacyFixedStructureLevels,
                },
                MigratePurchaseGrouping),
        });
    }

    private static void MigratePurchaseGrouping(ConfigurationMigrationContext context)
    {
        var grouping = AutoBuyPurchaseGroupingMode.BulkDevelopment;
        var groupingSource = PurchaseGrouping;
        if (context.TryGet(PurchaseGrouping, out var currentGrouping))
        {
            grouping = ParseGrouping(currentGrouping, allowActionMultiplier: true);
            groupingSource = PurchaseGrouping;
        }
        else if (context.TryGet(LegacyRespectActionMultiplier, out var respectMultiplier))
        {
            if (!bool.TryParse(respectMultiplier, out var enabled))
                throw new ConfigurationMigrationException(ConfigurationMigrationFailureCode.InvalidKnownMode);
            if (enabled)
            {
                grouping = AutoBuyPurchaseGroupingMode.ActionMultiplier;
                groupingSource = LegacyRespectActionMultiplier;
            }
            else if (context.TryGet(LegacyStructureRepeatMode, out var legacyGrouping))
            {
                grouping = ParseGrouping(legacyGrouping, allowActionMultiplier: false);
                groupingSource = LegacyStructureRepeatMode;
            }
        }
        else if (context.TryGet(LegacyStructureRepeatMode, out var legacyGrouping))
        {
            grouping = ParseGrouping(legacyGrouping, allowActionMultiplier: false);
            groupingSource = LegacyStructureRepeatMode;
        }

        context.Map(
            groupingSource,
            PurchaseGrouping,
            grouping.ToString(),
            "Collapsed legacy repeat precedence into one purchase-grouping mode.");

        if (context.TryGet(FixedGroupSize, out var currentFixedSize))
        {
            context.Preserve(FixedGroupSize, ParseFixedGroupSize(currentFixedSize));
        }
        else if (context.TryGet(LegacyFixedStructureLevels, out var legacyFixedSize))
        {
            context.Map(
                LegacyFixedStructureLevels,
                FixedGroupSize,
                ParseFixedGroupSize(legacyFixedSize),
                "Renamed the fixed Structure group-size setting.");
        }

        context.DiscardObsolete(
            LegacyRespectActionMultiplier,
            "Replaced by PurchaseGrouping=ActionMultiplier.");
        context.DiscardObsolete(
            LegacyRepeatWhileAffordable,
            "Auto Buy now always advances through ranked groups while its queue quota has room.");
        context.DiscardObsolete(
            LegacyStructureRepeatMode,
            "Replaced by PurchaseGrouping.");
        context.DiscardObsolete(
            LegacyFixedStructureLevels,
            "Replaced by FixedGroupSize.");
    }

    private static AutoBuyPurchaseGroupingMode ParseGrouping(
        string serialized,
        bool allowActionMultiplier)
    {
        if (!Enum.TryParse<AutoBuyPurchaseGroupingMode>(serialized, true, out var grouping) ||
            !Enum.IsDefined(typeof(AutoBuyPurchaseGroupingMode), grouping) ||
            (!allowActionMultiplier && grouping == AutoBuyPurchaseGroupingMode.ActionMultiplier))
        {
            throw new ConfigurationMigrationException(ConfigurationMigrationFailureCode.InvalidKnownMode);
        }

        return grouping;
    }

    private static string ParseFixedGroupSize(string serialized)
    {
        if (!int.TryParse(serialized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ||
            size < 1 ||
            size > 100)
        {
            throw new ConfigurationMigrationException(ConfigurationMigrationFailureCode.InvalidKnownMode);
        }

        return size.ToString(CultureInfo.InvariantCulture);
    }

    private static void MigrateVersionZero(ConfigurationMigrationContext context)
    {
        if (context.TryGet(AutoConceptMode, out var mode))
        {
            if (string.Equals(mode, "BalanceMastery", StringComparison.OrdinalIgnoreCase))
            {
                context.Map(
                    AutoConceptMode,
                    AutoConceptMode,
                    AutoConceptOperationMode.Active.ToString(),
                    "Converted legacy BalanceMastery mode to Active.");
            }
            else if (string.Equals(mode, "Active", StringComparison.OrdinalIgnoreCase))
            {
                context.Preserve(AutoConceptMode, AutoConceptOperationMode.Active.ToString());
            }
            else if (string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                context.Preserve(AutoConceptMode, AutoConceptOperationMode.Disabled.ToString());
            }
            else
            {
                throw new ConfigurationMigrationException(ConfigurationMigrationFailureCode.InvalidKnownMode);
            }
        }

        MigrateFallbackInterval(context);
        foreach (var key in DiscardedObsoleteKeys)
        {
            context.DiscardObsolete(key, "Removed obsolete setting without speculative remapping.");
        }
    }

    private static void MigrateFallbackInterval(ConfigurationMigrationContext context)
    {
        if (context.TryGet(FallbackInterval, out var current))
        {
            var seconds = ParseNonNegativeSeconds(current);
            context.Map(
                FallbackInterval,
                FallbackInterval,
                Math.Clamp(seconds, 10, 1800).ToString(CultureInfo.InvariantCulture),
                "Preserved the current fallback interval with destination precedence.");
            DiscardSupersededIntervals(context);
            return;
        }

        if (context.TryGet(PreviousIntervalSeconds, out var previous))
        {
            var seconds = ParseNonNegativeSeconds(previous);
            context.Map(
                PreviousIntervalSeconds,
                FallbackInterval,
                Math.Clamp(seconds, 10, 1800).ToString(CultureInfo.InvariantCulture),
                "Converted legacy interval seconds to fallback seconds.");
            context.DiscardObsolete(LegacyIntervalMinutes, "Discarded because legacy seconds had precedence.");
            return;
        }

        if (!context.TryGet(LegacyIntervalMinutes, out var legacyMinutes)) return;
        if (!double.TryParse(
                legacyMinutes,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var minutes) ||
            !double.IsFinite(minutes) ||
            minutes < 0d)
        {
            throw new ConfigurationMigrationException(ConfigurationMigrationFailureCode.InvalidKnownIntervalMinutes);
        }

        var roundedSeconds = Math.Round(minutes * 60d, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(roundedSeconds))
            throw new ConfigurationMigrationException(ConfigurationMigrationFailureCode.KnownIntervalOutsideFiniteRange);
        var clampedSeconds = (int)Math.Clamp(roundedSeconds, 10d, 1800d);
        context.Map(
            LegacyIntervalMinutes,
            FallbackInterval,
            clampedSeconds.ToString(CultureInfo.InvariantCulture),
            "Converted legacy interval minutes to rounded fallback seconds.");
    }

    private static int ParseNonNegativeSeconds(string serialized)
    {
        if (!int.TryParse(serialized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds < 0)
            throw new ConfigurationMigrationException(ConfigurationMigrationFailureCode.InvalidKnownIntervalSeconds);
        return seconds;
    }

    private static void DiscardSupersededIntervals(ConfigurationMigrationContext context)
    {
        context.DiscardObsolete(PreviousIntervalSeconds, "Discarded because the current destination had precedence.");
        context.DiscardObsolete(LegacyIntervalMinutes, "Discarded because the current destination had precedence.");
    }
}
