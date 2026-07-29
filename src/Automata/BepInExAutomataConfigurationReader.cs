using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal static class BepInExAutomataConfigurationReader
{
    internal static SuiteRuntimeConfiguration Read(BepInExAutomataConfiguration source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        return new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration
            {
                Enabled = source.Enabled.Value,
            },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = source.AutoBuyMode.Value,
                StructureAffordability = source.AutoBuyAffordability.Value,
                UpgradeAffordability = source.UpgradeAffordability.Value,
                IncludeStructures = source.AutoBuyStructures.Value,
                IncludeUpgrades = source.AutoBuyUpgrades.Value,
                AutoLevelSpells = source.AutoLevelSpells.Value,
                PurchaseGrouping = source.PurchaseGrouping.Value,
                EvaluationIntervalSeconds = source.AutoBuyIntervalSeconds.Value,
                LeaveQueueSlots = source.LeaveQueueSlots.Value,
                BatchSizing = source.AutoBuyBatchSizing.Value,
                MaxPurchasesPerBatch = source.MaxPurchasesPerBatch.Value,
                FixedGroupSize = source.FixedGroupSize.Value,
                PrioritizeCostAndQualityStructures =
                    source.PrioritizeCostAndQualityStructures.Value,
                AllowedUuids = source.AllowedAutoBuyUuids.Value,
                BlockedUuids = source.BlockedAutoBuyUuids.Value,
            },
            AutoCast = new AutoCastConfiguration
            {
                Mode = source.AutoCastMode.Value,
                ToggleShortcut = source.AutoCastToggleShortcut.Value.ToString(),
                ShowToggleButton = source.AutoCastShowToggleButton.Value,
                EvaluationIntervalSeconds = source.AutoCastIntervalSeconds.Value,
                StartResourcePercent = source.AutoCastStartResourcePercent.Value,
                ManualPauseSeconds = source.AutoCastManualPauseSeconds.Value,
                FullCharge = source.AutoCastFullCharge.Value,
            },
            AutoConcept = new AutoConceptConfiguration
            {
                Mode = source.AutoConceptMode.Value,
                SlotManagement = source.AutoConceptSlotManagement.Value,
                ShowToggleButton = source.AutoConceptShowToggleButton.Value,
                TrainingPeriodSeconds = source.AutoConceptTrainingPeriodSeconds.Value,
                FallbackEvaluationIntervalSeconds =
                    source.AutoConceptFallbackEvaluationIntervalSeconds.Value,
                QuantityCap = source.AutoConceptQuantityCap.Value,
                RateReservePercent = source.AutoConceptRateReservePercent.Value,
                MinimumResourcePercent = source.AutoConceptMinimumResourcePercent.Value,
                MinimumDrainRatio = source.AutoConceptMinimumDrainRatio.Value,
                AllowedUuids = source.AllowedAutoConceptUuids.Value,
                BlockedUuids = source.BlockedAutoConceptUuids.Value,
            },
            AutoHarvest = new AutoHarvestConfiguration
            {
                Mode = source.AutoHarvestMode.Value,
                CollectFruitTrees = source.AutoHarvestFruitTrees.Value,
                CollectTreasureTrees = source.AutoHarvestTreasureTrees.Value,
                EvaluationInterval = ToDuration(
                    source.AutoHarvestEvaluationIntervalSeconds.Value,
                    nameof(source.AutoHarvestEvaluationIntervalSeconds)),
            },
            Mentor = source.Mentor is null
                ? new OrbMentor.MentorConfiguration()
                : OrbMentor.MentorConfiguration.Read(source.Mentor),
            Safety = new SuiteSafetyConfiguration
            {
                EmergencyDisable = source.EmergencyDisable.Value,
            },
            Reserves = new AutomataReserveConfiguration
            {
                AbsoluteReserve = source.AbsoluteReserve.Value,
                RelativeReserveMultiplier = source.RelativeReserveMultiplier.Value,
            },
        };
    }

    private static MonotonicDuration ToDuration(float seconds, string parameterName)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0 ||
            seconds > TimeSpan.MaxValue.TotalSeconds)
        {
            throw new InvalidOperationException(
                $"{parameterName} requires a finite positive duration.");
        }

        return MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(seconds));
    }
}
