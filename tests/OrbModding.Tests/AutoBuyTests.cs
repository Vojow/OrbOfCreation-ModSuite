using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoBuyTests
{
    [Fact]
    public void ActiveMode_PurchasesLowestCostRatioFirst()
    {
        var expensive = Candidate("expensive", AutoBuyCandidateKind.Upgrade, 50, 1_000);
        var efficient = Candidate("efficient", AutoBuyCandidateKind.Structure, 5, 1_000);
        var log = Run(config => config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll,
            new FakeCatalog(4, expensive, efficient));

        Assert.Equal(0, expensive.PurchaseCalls);
        Assert.Equal(1, efficient.PurchaseCalls);
        Assert.Contains(log.Entries, entry => entry?.ToString()?.Contains("efficient", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void EmergencyDisable_BlocksActivePurchases()
    {
        var candidate = Candidate("guarded", AutoBuyCandidateKind.Upgrade, 1, 1_000);
        var log = Run(config =>
        {
            config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.EmergencyDisable.Value = true;
        }, new FakeCatalog(4, candidate));

        Assert.Equal(0, candidate.PurchaseCalls);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void ActiveMode_PurchasesExactlyOneBestCandidate()
    {
        var lessEfficient = Candidate("less-efficient", AutoBuyCandidateKind.Structure, 20, 1_000);
        var best = Candidate("best", AutoBuyCandidateKind.Upgrade, 1, 1_000);
        Run(config =>
        {
            config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.MaxPurchasesPerBatch.Value = 1;
            config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.Fixed;
        }, new FakeCatalog(4, lessEfficient, best));

        Assert.Equal(0, lessEfficient.PurchaseCalls);
        Assert.Equal(1, best.PurchaseCalls);
    }

    [Fact]
    public void AffordabilityModes_AreIndependentForStructuresAndUpgrades()
    {
        var structure = Candidate("structure", AutoBuyCandidateKind.Structure, 20, 1_000);
        var upgrade = Candidate("upgrade", AutoBuyCandidateKind.Upgrade, 20, 1_000);

        Run(config =>
        {
            config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.Excess100;
            config.UpgradeAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        }, new FakeCatalog(4, structure, upgrade));

        Assert.Equal(0, structure.PurchaseCalls);
        Assert.Equal(1, upgrade.PurchaseCalls);
    }

    [Fact]
    public void OperationalLoggingOff_SuppressesSuccessfulPurchaseChatter()
    {
        var candidate = Candidate("quiet", AutoBuyCandidateKind.Upgrade, 1, 1_000);
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.EnableOperationalLogging.Value = false;
        config.RepeatWhileAffordable.Value = false;
        var log = new ManualLogSource();
        using var engine = new AutoBuyEngine(
            config,
            new FakeCatalog(4, candidate),
            new ReservePolicy(config),
            log);

        engine.Tick(config.AutoBuyIntervalSeconds.Value);

        Assert.Equal(1, candidate.PurchaseCalls);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void ActiveMode_PurchasesRankedDistinctBatchFromOneCompletedScan()
    {
        var first = Candidate("first", AutoBuyCandidateKind.Upgrade, 1, 1_000);
        var second = Candidate("second", AutoBuyCandidateKind.Structure, 2, 1_000);
        var third = Candidate("third", AutoBuyCandidateKind.Upgrade, 3, 1_000);
        var fourth = Candidate("fourth", AutoBuyCandidateKind.Structure, 4, 1_000);
        var catalog = new FakeCatalog(10, fourth, second, first, third);

        var log = Run(config =>
        {
            config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.Fixed;
            config.MaxPurchasesPerBatch.Value = 3;
        }, catalog);

        Assert.Equal(1, catalog.DiscoverCalls);
        Assert.Equal(1, first.PurchaseCalls);
        Assert.Equal(1, second.PurchaseCalls);
        Assert.Equal(1, third.PurchaseCalls);
        Assert.Equal(0, fourth.PurchaseCalls);
        Assert.Contains(log.Entries, entry => entry?.ToString()?.Contains("Purchased=3", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ActiveBatch_RechecksQueueRoomAndPreservesConfiguredFreeSlot()
    {
        var candidates = new[]
        {
            Candidate("one", AutoBuyCandidateKind.Structure, 1, 1_000),
            Candidate("two", AutoBuyCandidateKind.Structure, 2, 1_000),
            Candidate("three", AutoBuyCandidateKind.Structure, 3, 1_000),
            Candidate("four", AutoBuyCandidateKind.Structure, 4, 1_000),
        };
        var catalog = new FakeCatalog(1, candidates)
        {
            QueueRooms = new Queue<int>(new[] { 4, 4, 3, 2, 1 }),
        };

        Run(config =>
        {
            config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.MaxPurchasesPerBatch.Value = 8;
            config.LeaveQueueSlots.Value = 1;
        }, catalog);

        Assert.Equal(3, candidates[0].PurchaseCalls + candidates[1].PurchaseCalls + candidates[2].PurchaseCalls + candidates[3].PurchaseCalls);
        Assert.Equal(0, candidates[3].PurchaseCalls);
    }

    [Fact]
    public void FillAvailableQueue_IgnoresFixedBatchCountAndStopsAtQueueReserve()
    {
        var candidates = new[]
        {
            Candidate("one", AutoBuyCandidateKind.Structure, 1, 1_000),
            Candidate("two", AutoBuyCandidateKind.Structure, 2, 1_000),
            Candidate("three", AutoBuyCandidateKind.Structure, 3, 1_000),
            Candidate("four", AutoBuyCandidateKind.Structure, 4, 1_000),
        };
        var catalog = new FakeCatalog(1, candidates)
        {
            QueueRooms = new Queue<int>(new[] { 4, 4, 3, 2, 1 }),
        };

        Run(config =>
        {
            config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.FillAvailableQueue;
            config.MaxPurchasesPerBatch.Value = 1;
            config.LeaveQueueSlots.Value = 1;
        }, catalog);

        Assert.Equal(3, candidates.Sum(candidate => candidate.PurchaseCalls));
        Assert.Equal(0, candidates[3].PurchaseCalls);
    }

    [Fact]
    public void BulkDevelopmentRepeat_QueuesMatchingStructureLevelsWithPerLevelPurchases()
    {
        var structure = Candidate("bulk-structure", AutoBuyCandidateKind.Structure, 1, 1_000);
        var catalog = new FakeCatalog(10, structure)
        {
            BulkDevelopment = 3,
        };

        Run(config =>
        {
            config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.RepeatWhileAffordable.Value = false;
            config.StructureRepeatMode.Value = AutoBuyStructureRepeatMode.BulkDevelopment;
            config.MaxPurchasesPerBatch.Value = 8;
        }, catalog);

        Assert.Equal(3, structure.PurchaseCalls);
    }

    [Fact]
    public void BulkDevelopmentRepeat_UsesUpdatedLiveValueForFollowingBatch()
    {
        var structure = Candidate("live-bulk-structure", AutoBuyCandidateKind.Structure, 1, 1_000);
        var catalog = new FakeCatalog(10, structure)
        {
            BulkDevelopment = 2,
        };
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.RepeatWhileAffordable.Value = false;
        config.StructureRepeatMode.Value = AutoBuyStructureRepeatMode.BulkDevelopment;
        config.MaxPurchasesPerBatch.Value = 8;
        var log = new ManualLogSource();
        using var engine = new AutoBuyEngine(
            config,
            catalog,
            new ReservePolicy(config),
            log,
            _ => 0.0,
            _ => 0.0);

        engine.Tick(config.AutoBuyIntervalSeconds.Value);
        Assert.Equal(2, structure.PurchaseCalls);

        catalog.BulkDevelopment = 4;
        engine.Tick(0.0f);

        Assert.Equal(6, structure.PurchaseCalls);
        Assert.Equal(2, catalog.DiscoverCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatWhileAffordable_FillsUsableQueueBeyondLegacyGroupLimits(bool isUpgrade)
    {
        var kind = isUpgrade ? AutoBuyCandidateKind.Upgrade : AutoBuyCandidateKind.Structure;
        var candidate = Candidate("abundant", kind, 1, 10_000);
        var catalog = new FakeCatalog(12, candidate)
        {
            BulkDevelopment = 2,
            ActionMultiplier = 2,
        };

        Run(config =>
        {
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.UpgradeAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.RepeatWhileAffordable.Value = true;
            config.RespectActionMultiplier.Value = false;
            config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.FillAvailableQueue;
            config.LeaveQueueSlots.Value = 2;
        }, catalog);

        Assert.Equal(10, candidate.PurchaseCalls);
        Assert.Equal(1, catalog.DiscoverCalls);
    }

    [Fact]
    public void RepeatWhileAffordable_StopsAtTheExactLiveResourceBoundary()
    {
        var candidate = Candidate("bounded", AutoBuyCandidateKind.Structure, 10, 35);

        Run(config =>
        {
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.RepeatWhileAffordable.Value = true;
            config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.FillAvailableQueue;
            config.LeaveQueueSlots.Value = 0;
        }, new FakeCatalog(10, candidate));

        Assert.Equal(3, candidate.PurchaseCalls);
    }

    [Fact]
    public void RepeatWhileAffordable_VisitsLowerRankedCandidateBeforeRepeating()
    {
        var selected = Candidate("selected", AutoBuyCandidateKind.Structure, 10, 25);
        var lowerRanked = Candidate("lower-ranked", AutoBuyCandidateKind.Structure, 500, 1_000);

        Run(config =>
        {
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.RepeatWhileAffordable.Value = true;
            config.LeaveQueueSlots.Value = 0;
        }, new FakeCatalog(5, selected, lowerRanked));

        Assert.Equal(1, selected.PurchaseCalls);
        Assert.Equal(1, lowerRanked.PurchaseCalls);
    }

    [Fact]
    public void RepeatWhileAffordable_RechecksIncreasingLevelCostBeforeEveryPurchase()
    {
        var candidate = Candidate(
            "scaling-cost",
            AutoBuyCandidateKind.Structure,
            cost: 10,
            quantity: 100,
            costMultiplier: 2);

        Run(config =>
        {
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.RepeatWhileAffordable.Value = true;
            config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.FillAvailableQueue;
            config.LeaveQueueSlots.Value = 0;
        }, new FakeCatalog(10, candidate));

        Assert.Equal(3, candidate.PurchaseCalls);
    }

    [Fact]
    public void RepeatWhileAffordable_StillHonorsTheFixedBatchCap()
    {
        var candidate = Candidate("fixed-cap", AutoBuyCandidateKind.Upgrade, 1, 10_000);

        Run(config =>
        {
            config.UpgradeAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.RepeatWhileAffordable.Value = true;
            config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.Fixed;
            config.MaxPurchasesPerBatch.Value = 4;
            config.LeaveQueueSlots.Value = 0;
        }, new FakeCatalog(20, candidate));

        Assert.Equal(4, candidate.PurchaseCalls);
    }

    [Fact]
    public void ActiveBatch_ContinuesToNextRankedCandidateAfterOnePurchaseFails()
    {
        var failing = Candidate("failing", AutoBuyCandidateKind.Upgrade, 1, 1_000, purchaseSucceeds: false);
        var fallback = Candidate("fallback", AutoBuyCandidateKind.Upgrade, 2, 1_000);

        Run(config =>
        {
            config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.MaxPurchasesPerBatch.Value = 1;
        }, new FakeCatalog(4, failing, fallback));

        Assert.Equal(1, failing.PurchaseCalls);
        Assert.Equal(1, fallback.PurchaseCalls);
    }

    [Fact]
    public void RepeatedNativeFailureWarningsAreRateLimitedPerCandidate()
    {
        var failing = Candidate("persistent-failure", AutoBuyCandidateKind.Structure, 1, 1_000, purchaseSucceeds: false);
        var catalog = new FakeCatalog(4, failing);
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.RepeatWhileAffordable.Value = false;
        var log = new ManualLogSource();
        using var engine = new AutoBuyEngine(config, catalog, new ReservePolicy(config), log);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            engine.Tick(config.AutoBuyIntervalSeconds.Value);
        }

        Assert.Equal(20, failing.PurchaseCalls);
        Assert.Single(log.Entries, entry =>
            entry?.ToString()?.Contains("Auto Buy could not purchase", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void CpuLimitedPurchaseBatch_ContinuesOnFollowingFramesWithoutRescanning()
    {
        var first = Candidate("first", AutoBuyCandidateKind.Upgrade, 1, 1_000);
        var second = Candidate("second", AutoBuyCandidateKind.Structure, 2, 1_000);
        var third = Candidate("third", AutoBuyCandidateKind.Upgrade, 3, 1_000);
        var catalog = new FakeCatalog(10, first, second, third);
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.MaxPurchasesPerBatch.Value = 3;
        config.EnableOperationalLogging.Value = true;
        config.RepeatWhileAffordable.Value = false;
        var log = new ManualLogSource();
        using var engine = new AutoBuyEngine(
            config,
            catalog,
            new ReservePolicy(config),
            log,
            _ => 0.0,
            _ => double.PositiveInfinity);

        engine.Tick(config.AutoBuyIntervalSeconds.Value);
        Assert.Equal(1, first.PurchaseCalls + second.PurchaseCalls + third.PurchaseCalls);

        engine.Tick(0.0f);
        engine.Tick(0.0f);

        Assert.Equal(1, catalog.DiscoverCalls);
        Assert.Equal(1, first.PurchaseCalls);
        Assert.Equal(1, second.PurchaseCalls);
        Assert.Equal(1, third.PurchaseCalls);
        Assert.Contains(log.Entries, entry =>
            entry?.ToString()?.Contains("Purchased=3", StringComparison.Ordinal) == true &&
            entry.ToString()?.Contains("CpuSliced=True", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void DecisionLogLevelOffSuppressesOperationalPurchaseAndBatchLogs()
    {
        var candidate = Candidate("quiet", AutoBuyCandidateKind.Structure, 1, 1_000);
        var catalog = new FakeCatalog(4, candidate);
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.MaxPurchasesPerBatch.Value = 1;
        config.EnableOperationalLogging.Value = true;
        config.DecisionLogLevel.Value = AutomataDecisionLogLevel.Off;
        config.RepeatWhileAffordable.Value = false;
        var log = new ManualLogSource();
        using var engine = new AutoBuyEngine(config, catalog, new ReservePolicy(config), log);

        engine.Tick(config.AutoBuyIntervalSeconds.Value);

        Assert.Equal(1, candidate.PurchaseCalls);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void CompletedActiveBatch_PipelinesNextScanOnFollowingFrame()
    {
        var first = Candidate("first", AutoBuyCandidateKind.Upgrade, 1, 1_000);
        var second = Candidate("second", AutoBuyCandidateKind.Upgrade, 2, 1_000);
        var catalog = new FakeCatalog(10, first, second);
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.MaxPurchasesPerBatch.Value = 2;
        config.RepeatWhileAffordable.Value = false;
        var log = new ManualLogSource();
        using var engine = new AutoBuyEngine(config, catalog, new ReservePolicy(config), log);

        engine.Tick(config.AutoBuyIntervalSeconds.Value);
        engine.Tick(0.0f);

        Assert.Equal(2, catalog.DiscoverCalls);
        Assert.Equal(4, first.PurchaseCalls + second.PurchaseCalls);
    }

    [Fact]
    public void PreparedActiveBatch_WaitsForQueueRoomAndFeedsSlotWithoutRescanning()
    {
        var candidate = Candidate("prepared", AutoBuyCandidateKind.Upgrade, 1, 1_000);
        var catalog = new FakeCatalog(1, candidate)
        {
            QueueRooms = new Queue<int>(new[] { 1, 1, 2 }),
        };
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.MaxPurchasesPerBatch.Value = 1;
        config.LeaveQueueSlots.Value = 1;
        config.EnableOperationalLogging.Value = true;
        var log = new ManualLogSource();
        using var engine = new AutoBuyEngine(config, catalog, new ReservePolicy(config), log);

        engine.Tick(config.AutoBuyIntervalSeconds.Value);
        Assert.Equal(0, candidate.PurchaseCalls);

        engine.Tick(0.0f);

        Assert.Equal(1, catalog.DiscoverCalls);
        Assert.Equal(1, candidate.PurchaseCalls);
        Assert.Contains(log.Entries, entry =>
            entry?.ToString()?.Contains("waiting for native queue room", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void QueueReserve_PreparesRecommendationWithoutPurchasing()
    {
        var candidate = Candidate("queued", AutoBuyCandidateKind.Upgrade, 1, 1_000);
        var log = Run(config =>
        {
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.DecisionLogLevel.Value = AutomataDecisionLogLevel.Verbose;
            config.LeaveQueueSlots.Value = 1;
        }, new FakeCatalog(1, candidate));

        Assert.Equal(0, candidate.PurchaseCalls);
        Assert.Contains(log.Entries, entry => entry?.ToString()?.Contains("waiting for native queue room", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Allowlist_ExcludesEveryCandidateNotExplicitlySelected()
    {
        var selected = Candidate("selected", AutoBuyCandidateKind.Upgrade, 10, 1_000);
        var cheaperButExcluded = Candidate("excluded", AutoBuyCandidateKind.Upgrade, 1, 1_000);
        var log = Run(config =>
        {
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.AllowedAutoBuyUuids.Value = "selected";
        }, new FakeCatalog(4, cheaperButExcluded, selected));

        Assert.Contains(log.Entries, entry => entry?.ToString()?.Contains("selected", StringComparison.Ordinal) == true);
        Assert.Equal(0, cheaperButExcluded.PurchaseCalls);
    }

    [Fact]
    public void ActiveMode_HasNoHiddenPerSessionPurchaseLimit()
    {
        var candidate = Candidate("single-probe", AutoBuyCandidateKind.Upgrade, 1, 1_000);
        var catalog = new FakeCatalog(4, candidate);
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.AutoBuyIntervalSeconds.Value = 0.1f;
        config.EnableOperationalLogging.Value = true;
        config.RepeatWhileAffordable.Value = false;
        var log = new ManualLogSource();
        using var engine = new AutoBuyEngine(config, catalog, new ReservePolicy(config), log);

        engine.Tick(0.1f);
        engine.Tick(0.1f);
        engine.Tick(0.1f);

        Assert.Equal(3, candidate.PurchaseCalls);
        Assert.Equal(3, catalog.DiscoverCalls);
        Assert.DoesNotContain(log.Entries, entry => entry?.ToString()?.Contains("per-session purchase limit", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void RespectActionMultiplier_RepeatsUpgradeUpToCurrentMultiplier()
    {
        var candidate = Candidate("multiplied", AutoBuyCandidateKind.Upgrade, 1, 1_000);
        var catalog = new FakeCatalog(10, candidate) { ActionMultiplier = 5 };

        Run(config =>
        {
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.RespectActionMultiplier.Value = true;
            config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.FillAvailableQueue;
        }, catalog);

        Assert.Equal(5, candidate.PurchaseCalls);
    }

    [Fact]
    public void IgnoredActionMultiplier_KeepsUpgradeAtOneLevelPerCandidate()
    {
        var candidate = Candidate("single", AutoBuyCandidateKind.Upgrade, 1, 1_000);
        var catalog = new FakeCatalog(10, candidate) { ActionMultiplier = 5 };

        Run(config =>
        {
            config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
            config.RespectActionMultiplier.Value = false;
            config.MaxPurchasesPerBatch.Value = 1;
        }, catalog);

        Assert.Equal(1, candidate.PurchaseCalls);
    }

    [Fact]
    public void MultipliedPurchase_RechecksReserveBeforeEveryLevel()
    {
        var candidate = Candidate("reserved", AutoBuyCandidateKind.Upgrade, 10, 100);
        var catalog = new FakeCatalog(10, candidate) { ActionMultiplier = 10 };
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "50";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.UpgradeAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.RespectActionMultiplier.Value = true;
        config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.FillAvailableQueue;
        using var engine = new AutoBuyEngine(config, catalog, new ReservePolicy(config), new ManualLogSource());

        engine.Tick(config.AutoBuyIntervalSeconds.Value);

        Assert.Equal(5, candidate.PurchaseCalls);
    }

    [Fact]
    public void BudgetLimitedScan_ResumesInsteadOfRestarting()
    {
        var first = Candidate("first", AutoBuyCandidateKind.Upgrade, 1, 1_000, available: false);
        var second = Candidate("second", AutoBuyCandidateKind.Upgrade, 1, 1_000, available: false);
        var catalog = new FakeCatalog(4, first, second);
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AutoBuyIntervalSeconds.Value = 0.1f;
        var log = new ManualLogSource();
        using var engine = new AutoBuyEngine(config, catalog, new ReservePolicy(config), log, _ => double.PositiveInfinity);

        engine.Tick(0.1f);
        engine.Tick(0.0f);

        Assert.Equal(1, catalog.DiscoverCalls);
        Assert.Equal(1, first.AvailabilityChecks);
        Assert.Equal(1, second.AvailabilityChecks);
    }

    private static ManualLogSource Run(Action<AutomataConfig> configure, FakeCatalog catalog)
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.EnableOperationalLogging.Value = true;
        config.RepeatWhileAffordable.Value = false;
        configure(config);
        var log = new ManualLogSource();
        using var engine = new AutoBuyEngine(
            config,
            catalog,
            new ReservePolicy(config),
            log,
            _ => 0.0,
            _ => 0.0);
        engine.Tick(config.AutoBuyIntervalSeconds.Value);
        return log;
    }

    private static FakeCandidate Candidate(
        string uuid,
        AutoBuyCandidateKind kind,
        double cost,
        double quantity,
        bool available = true,
        bool purchaseSucceeds = true,
        double costMultiplier = 1.0)
    {
        return new FakeCandidate(uuid, kind, cost, quantity, available, purchaseSucceeds, costMultiplier);
    }

    private sealed class FakeCatalog : IAutoBuyCatalog
    {
        private readonly IReadOnlyList<IAutoBuyCandidate> _candidates;
        private readonly int _queueRoom;

        public FakeCatalog(int queueRoom, params IAutoBuyCandidate[] candidates)
        {
            _queueRoom = queueRoom;
            _candidates = candidates;
        }

        public int DiscoverCalls { get; private set; }

        public Queue<int>? QueueRooms { get; set; }

        public int BulkDevelopment { get; set; } = 1;

        public int ActionMultiplier { get; set; } = 1;

        public IEnumerable<IAutoBuyCandidate> Discover()
        {
            DiscoverCalls++;
            return _candidates;
        }

        public bool TryGetRemainingQueueRoom(out int room)
        {
            room = QueueRooms is { Count: > 0 } ? QueueRooms.Dequeue() : _queueRoom;
            return true;
        }

        public bool TryGetBulkDevelopment(out int levels)
        {
            levels = BulkDevelopment;
            return true;
        }

        public bool TryGetActionMultiplier(out int multiplier)
        {
            multiplier = ActionMultiplier;
            return true;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeCandidate : IAutoBuyCandidate
    {
        private readonly AutoBuyCandidateSnapshot _snapshot;
        private double _cost;
        private double _quantity;
        private readonly bool _available;
        private readonly bool _purchaseSucceeds;
        private readonly double _costMultiplier;

        public FakeCandidate(
            string uuid,
            AutoBuyCandidateKind kind,
            double cost,
            double quantity,
            bool available,
            bool purchaseSucceeds,
            double costMultiplier)
        {
            _snapshot = new AutoBuyCandidateSnapshot(this, uuid, uuid, kind, GetType().Name);
            _cost = cost;
            _quantity = quantity;
            _available = available;
            _purchaseSucceeds = purchaseSucceeds;
            _costMultiplier = costMultiplier;
        }

        public int PurchaseCalls { get; private set; }

        public int AvailabilityChecks { get; private set; }

        public AutoBuyCandidateSnapshot Snapshot() => _snapshot;

        public bool IsAvailable()
        {
            AvailabilityChecks++;
            return _available;
        }

        public bool CanPurchase(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public IReadOnlyList<ResourceAdmissionCost> GetCosts() => new[]
        {
            new ResourceAdmissionCost("resource", "Resource", new BigAmount(_cost, 0), new BigAmount(_quantity, 0)),
        };

        public bool TryPurchaseOne(out string reason)
        {
            PurchaseCalls++;
            reason = _purchaseSucceeds ? string.Empty : "simulated native failure";
            if (_purchaseSucceeds)
            {
                _quantity -= _cost;
                _cost *= _costMultiplier;
            }

            return _purchaseSucceeds;
        }
    }
}
