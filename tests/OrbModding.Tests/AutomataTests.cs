using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using UnityEngine;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataTests
{
    [Fact]
    public void SpellCostReaderUsesTrueMaxQuantityInsteadOfEffectSoftCap()
    {
        var resource = new TestResourceSO
        {
            uuid = "mana",
            name = "Mana",
            quantity = new TestBigDouble(8.0, 5),
            maxQuantity = new TestValueModifierRecord(new TestBigDouble(5.0, 5)),
            trueAmountMultiplier = 2.0,
        };
        var result = Assert.Single(ReflectionCostReader.Read(new[]
        {
            new TestCostEntry(resource, new TestBigDouble(1.0, 3)),
        }));

        Assert.Equal("8e5", result.CurrentQuantity.ToString());
        Assert.Equal("1e6", result.Capacity?.ToString());
    }

    [Fact]
    public void DecisionLogGate_ThrottlesRepeatedStateButLogsTransitions()
    {
        var gate = new DecisionLogGate(TimeSpan.FromSeconds(30));

        Assert.True(gate.ShouldLog("none", TimeSpan.Zero));
        Assert.False(gate.ShouldLog("none", TimeSpan.FromSeconds(1)));
        Assert.True(gate.ShouldLog("candidate-a", TimeSpan.FromSeconds(2)));
        Assert.True(gate.ShouldLog("candidate-a", TimeSpan.FromSeconds(32)));
    }

    [Fact]
    public void DefaultConfiguration_IsReadyForReleaseUse()
    {
        var config = AutomataConfig.Bind(new ConfigFile());

        Assert.Equal(AutoBuyOperationMode.Active, config.AutoBuyMode.Value);
        Assert.Equal(AutoBuyAffordabilityMode.Excess100, config.AutoBuyAffordability.Value);
        Assert.Equal(AutoBuyAffordabilityMode.Excess100, config.UpgradeAffordability.Value);
        Assert.Equal(1024, config.AutoBuyMaxCandidatesPerScan.Value);
        Assert.Equal(AutoBuyBatchSizingMode.FillAvailableQueue, config.AutoBuyBatchSizing.Value);
        Assert.Equal(8, config.MaxPurchasesPerBatch.Value);
        Assert.Equal(AutoBuyStructureRepeatMode.BulkDevelopment, config.StructureRepeatMode.Value);
        Assert.Equal(2, config.FixedStructureLevelsPerCandidate.Value);
        Assert.False(config.RespectActionMultiplier.Value);
        Assert.Equal("0", config.AbsoluteReserve.Value);
        Assert.Equal(0.0f, config.RelativeReserveMultiplier.Value);
        Assert.Equal(AutoCastOperationMode.Disabled, config.AutoCastMode.Value);
        Assert.Equal(AutoConceptOperationMode.Disabled, config.AutoConceptMode.Value);
        Assert.Equal(AutoConceptSlotManagementMode.TimedCycle, config.AutoConceptSlotManagement.Value);
        Assert.True(config.AutoConceptShowToggleButton.Value);
        Assert.True(config.AutoLevelSpells.Value);
        Assert.Equal(300, config.AutoConceptTrainingPeriodSeconds.Value);
        Assert.Equal(300, config.AutoConceptFallbackEvaluationIntervalSeconds.Value);
        Assert.False(config.EnableOperationalLogging.Value);
        Assert.Equal(1.0f, config.CpuBudgetMilliseconds.Value);
        Assert.True(config.CanStartAutoBuyActively);
        Assert.False(config.CanStartAutoCastActively);
        Assert.False(config.CanStartAutoConceptActively);
    }

    [Fact]
    public void AutoConceptConfiguration_MigratesLegacyBalanceMasteryModeToActive()
    {
        var configFile = new ConfigFile();
        configFile.Bind("AutoConcept", "Mode", "BalanceMastery", "Legacy Auto Concept mode.");

        var config = AutomataConfig.Bind(configFile);

        Assert.Equal(AutoConceptOperationMode.Active, config.AutoConceptMode.Value);
        Assert.True(config.CanStartAutoConceptActively);
    }

    [Theory]
    [InlineData(0.1f, 10)]
    [InlineData(1.0f, 60)]
    [InlineData(2.5f, 150)]
    [InlineData(30.0f, 1800)]
    public void AutoConceptConfiguration_MigratesLegacyMinutesToSeconds(float legacyMinutes, int expectedSeconds)
    {
        var configFile = new ConfigFile();
        configFile.Bind("AutoConcept", "RebalanceIntervalMinutes", legacyMinutes, "Legacy interval.");

        var config = AutomataConfig.Bind(configFile);

        Assert.Equal(expectedSeconds, config.AutoConceptFallbackEvaluationIntervalSeconds.Value);
        Assert.DoesNotContain(
            configFile,
            pair => pair.Key.Section == "AutoConcept" && pair.Key.Key == "RebalanceIntervalMinutes");
    }

    [Fact]
    public void AutoConceptConfiguration_MigratesExistingRebalanceSecondsSetting()
    {
        var configFile = new ConfigFile();
        configFile.Bind("AutoConcept", "RebalanceIntervalSeconds", 10, "Current interval.");

        var config = AutomataConfig.Bind(configFile);

        Assert.Equal(10, config.AutoConceptFallbackEvaluationIntervalSeconds.Value);
        Assert.DoesNotContain(
            configFile,
            pair => pair.Key.Section == "AutoConcept" && pair.Key.Key == "RebalanceIntervalSeconds");
    }

    [Fact]
    public void AutoConceptConfiguration_PreservesNewFallbackSettingOverLegacyValues()
    {
        var configFile = new ConfigFile();
        configFile.Bind("AutoConcept", "FallbackEvaluationIntervalSeconds", 45, "Current interval.");
        configFile.Bind("AutoConcept", "RebalanceIntervalSeconds", 10, "Previous interval.");
        configFile.Bind("AutoConcept", "RebalanceIntervalMinutes", 2.0f, "Legacy interval.");

        var config = AutomataConfig.Bind(configFile);

        Assert.Equal(45, config.AutoConceptFallbackEvaluationIntervalSeconds.Value);
        Assert.DoesNotContain(
            configFile,
            pair => pair.Key.Section == "AutoConcept" &&
                    (pair.Key.Key == "RebalanceIntervalSeconds" || pair.Key.Key == "RebalanceIntervalMinutes"));
    }

    [Fact]
    public void AutoConceptToggleSwitchesModeAndShowsEmergencyBlock()
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        var toggle = new AutoConceptToggleControl(config);

        Assert.Equal(AutoCastToggleVisualState.Off, toggle.State);
        toggle.Toggle();
        Assert.Equal(AutoConceptOperationMode.Active, config.AutoConceptMode.Value);
        Assert.Equal(AutoCastToggleVisualState.On, toggle.State);
        config.EmergencyDisable.Value = true;
        Assert.Equal(AutoCastToggleVisualState.Blocked, toggle.State);
        toggle.Toggle();
        Assert.Equal(AutoConceptOperationMode.Disabled, config.AutoConceptMode.Value);
        Assert.Equal(AutoCastToggleVisualState.Off, toggle.State);
    }

    [Theory]
    [InlineData(0, "CN OFF")]
    [InlineData(1, "CN ON")]
    [InlineData(2, "CN !")]
    public void AutoConceptButtonUsesDistinctCompactLabels(int state, string expected)
    {
        Assert.Equal(expected, AutoConceptToggleButton.FormatLabel((AutoCastToggleVisualState)state));
    }

    [Fact]
    public void AutomataLogTimestamp_IncludesLocalDateTimeMillisecondsAndOffset()
    {
        var timestamp = new DateTimeOffset(2026, 7, 17, 9, 35, 12, TimeSpan.FromHours(2)).AddMilliseconds(345);

        var message = AutomataLoggingExtensions.WithTimestamp("ConceptRecipes runtime list is empty", timestamp);

        Assert.Equal("[2026-07-17 09:35:12.345 +02:00] ConceptRecipes runtime list is empty", message);
    }

    [Fact]
    public void AutoConceptRanksLowestEffectiveMasteryWithStableTieBreaks()
    {
        var ranked = AutoConceptBalancer.Rank(new[]
        {
            new ConceptProgress("z", 2, 0.1, true),
            new ConceptProgress("b", 1, 0.8, true),
            new ConceptProgress("a", 1, 0.8, true),
            new ConceptProgress("locked", 0, 0.0, false),
            new ConceptProgress("early", 1, 0.2, true),
        });

        Assert.Equal(new[] { "early", "a", "b", "z" }, System.Linq.Enumerable.Select(ranked, item => item.Uuid));
    }

    [Theory]
    [InlineData(0, 0.9, 1, 0.0, true)]
    [InlineData(1, 0.2, 1, 0.8, true)]
    [InlineData(1, 0.8, 1, 0.8, false)]
    [InlineData(2, 0.0, 1, 0.9, false)]
    public void AutoConceptRotatesOnlyForStrictlyLowerMastery(
        int candidateLevel,
        double candidateProgress,
        int activeLevel,
        double activeProgress,
        bool expected)
    {
        var candidate = new ConceptProgress("candidate", candidateLevel, candidateProgress, true);
        var active = new ConceptProgress("active", activeLevel, activeProgress, true);

        Assert.Equal(expected, AutoConceptBalancer.HasStrictlyLowerMastery(candidate, active));
    }

    [Theory]
    [InlineData(4, 0.9, 5, 0.0, false)]
    [InlineData(5, 0.4, 5, 0.5, false)]
    [InlineData(5, 0.5, 5, 0.5, true)]
    [InlineData(6, 0.0, 5, 0.9, true)]
    public void AutoConceptCatchUpUsesCapturedEffectiveMastery(
        int currentLevel,
        double currentProgress,
        int targetLevel,
        double targetProgress,
        bool expected)
    {
        var current = new ConceptProgress("current", currentLevel, currentProgress, true);
        var target = new ConceptProgress("target", targetLevel, targetProgress, true);

        Assert.Equal(expected, AutoConceptBalancer.HasReached(current, target));
    }

    [Theory]
    [InlineData(100.0, 109.9, 10, false)]
    [InlineData(100.0, 110.0, 10, true)]
    [InlineData(100.0, 99.0, 10, false)]
    [InlineData(0.0, 9.9, 1, false)]
    [InlineData(0.0, 3600.0, 9999, true)]
    public void AutoConceptTrainingPeriodStartsAtSettledAssignment(
        double startedAt,
        double current,
        int configuredPeriod,
        bool expected)
    {
        Assert.Equal(
            expected,
            AutoConceptBalancer.HasTrainingPeriodElapsed(startedAt, current, configuredPeriod));
    }

    [Fact]
    public void TimedConceptCycleDoesNotEndWhenMasteryCatchesUpEarly()
    {
        var current = new ConceptProgress("current", 5, 0.9, true);
        var target = new ConceptProgress("target", 2, 0.0, true);

        Assert.False(AutoConceptBalancer.HasTrainingSessionCompleted(
            AutoConceptSlotManagementMode.TimedCycle,
            current,
            target,
            100.0,
            109.9,
            10));
        Assert.True(AutoConceptBalancer.HasTrainingSessionCompleted(
            AutoConceptSlotManagementMode.TimedCycle,
            current,
            target,
            100.0,
            110.0,
            10));
        Assert.True(AutoConceptBalancer.HasTrainingSessionCompleted(
            AutoConceptSlotManagementMode.RotateAll,
            current,
            target,
            100.0,
            100.1,
            10));
    }

    [Fact]
    public void TimedConceptCycleUsesFullRotationWithoutMasteryOrdering()
    {
        Assert.True(AutoConceptBalancer.UsesFullRotation(AutoConceptSlotManagementMode.TimedCycle));
        Assert.False(AutoConceptBalancer.RequiresLowerMastery(AutoConceptSlotManagementMode.TimedCycle));
        Assert.False(AutoConceptBalancer.UsesFullRotation(AutoConceptSlotManagementMode.PreserveManual));
        Assert.True(AutoConceptBalancer.RequiresLowerMastery(AutoConceptSlotManagementMode.RotateAll));
    }

    [Fact]
    public void TimedConceptCyclePrioritizesNeverAndLeastRecentlyAssignedConcepts()
    {
        Assert.True(AutoConceptBalancer.CompareTimedCycleOrder(null, "new", 1, "old") < 0);
        Assert.True(AutoConceptBalancer.CompareTimedCycleOrder(2, "older", 3, "newer") < 0);
        Assert.True(AutoConceptBalancer.CompareTimedCycleOrder(null, "a", null, "b") < 0);
    }

    [Fact]
    public void TimedConceptCycleDoesNotLetResourceBlockedCandidateStarveSafeCandidate()
    {
        Assert.False(AutoConceptBalancer.ResourceSafeTimedCandidatePrecedes(
            false,
            null,
            "resource-at-zero",
            4,
            "resource-safe"));
        Assert.True(AutoConceptBalancer.ResourceSafeTimedCandidatePrecedes(
            true,
            null,
            "resource-restored",
            4,
            "current"));
    }

    [Theory]
    [InlineData(null, false, "zero-quantity state is unavailable")]
    [InlineData(true, false, "is at zero")]
    [InlineData(false, true, "")]
    public void AutoConceptPositiveDrainRequiresAuthoritativeNonzeroResource(
        bool? nativeIsAtZero,
        bool expected,
        string expectedReason)
    {
        Assert.Equal(expected, AutoConceptResourcePolicy.TryAcceptPositiveDrain(nativeIsAtZero, out var reason));
        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void AutoSpellLevelRuntimeTransitionsFromLockedToSingleToAll()
    {
        var upgrade = new UpgradeSO();
        IdScriptableObject.RuntimeLookup.Clear();
        IdScriptableObject.RuntimeLookup[new Guid(ReflectionSpellLevelRuntime.UnlockLevelAllSpellsUuid)] = upgrade;
        var recipe = new SpellRecipeSO { discovered = true, readyToLevel = true };
        SpellManager.instance = new SpellManager();
        SpellManager.instance.availableSpellRecipes.value.Add(recipe);
        using var runtime = new ReflectionSpellLevelRuntime();

        Assert.Equal(AutoSpellLevelCapability.Locked, runtime.ReadSnapshot(out var reason).Capability);
        Assert.Equal(string.Empty, reason);

        recipe.levelingPrerequisites.unlocked = true;
        upgrade.queuedPurchaseLevel = 1;
        recipe.levelCost.affordable = false;
        var single = runtime.ReadSnapshot(out reason);
        Assert.Equal(AutoSpellLevelCapability.Single, single.Capability);
        Assert.Null(single.Candidate);
        Assert.Equal(0, recipe.levelCost.PerformCalls);

        recipe.levelCost.affordable = true;
        single = runtime.ReadSnapshot(out reason);
        Assert.NotNull(single.Candidate);
        Assert.True(runtime.TryLevelSingle(single.Candidate!, out reason));
        Assert.Equal(1, recipe.masteryLevel);
        Assert.Equal(1, recipe.levelCost.PerformCalls);

        recipe.readyToLevel = true;
        upgrade.purchaseLevel = 1;
        var all = runtime.ReadSnapshot(out reason);
        Assert.Equal(AutoSpellLevelCapability.All, all.Capability);
        Assert.True(runtime.TryLevelAll(out reason));
        Assert.Equal(2, recipe.masteryLevel);
        Assert.Equal(2, recipe.levelCost.PerformCalls);
    }

    [Fact]
    public void AutoConceptOwnershipNeverClaimsUnexpectedManualQuantity()
    {
        var ledger = new ConceptOwnershipLedger();
        ledger.ObserveBaseline("concept", 3);
        ledger.RecordAutomatedDelta("concept", 5, 2);

        Assert.True(ledger.TryGet("concept", out var owned));
        Assert.Equal(3, owned.ManualBaseline);
        Assert.Equal(2, owned.AutomatedDelta);
        Assert.True(ledger.RebaselineIfUnexpected("concept", 7));
        Assert.True(ledger.TryGet("concept", out owned));
        Assert.Equal(7, owned.ManualBaseline);
        Assert.Equal(0, owned.AutomatedDelta);
    }

    [Theory]
    [InlineData(-1.0, 0.1)]
    [InlineData(0.5, 0.5)]
    [InlineData(4.0, 1.0)]
    public void AutoBuyCpuBudgetIsHardCappedForFrameSafety(double configured, double expected)
    {
        Assert.Equal(expected, AutoBuyEngine.EffectiveCpuBudget(configured), 6);
    }

    [Fact]
    public void ReflectionAutoBuyCatalogCachesTheStaticRegistry()
    {
        using var catalog = new ReflectionAutoBuyCatalog();

        var first = catalog.Discover();
        var second = catalog.Discover();

        Assert.Same(first, second);
    }

    [Fact]
    public void ReservePolicy_RequiresCostPlusTheLargestReserveFloor()
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "100";
        config.RelativeReserveMultiplier.Value = 2.0f;
        var policy = new ReservePolicy(config);

        var accepted = policy.Evaluate(new[]
        {
            new ResourceAdmissionCost("mana", "Mana", new BigAmount(1.0, 1), new BigAmount(1.1, 2)),
        });
        var rejected = policy.Evaluate(new[]
        {
            new ResourceAdmissionCost("mana", "Mana", new BigAmount(1.0, 1), new BigAmount(1.09, 2)),
        });

        Assert.True(accepted.Passed);
        Assert.False(rejected.Passed);
    }

    private sealed class TestCostEntry
    {
        public TestCostEntry(TestResourceSO resource, TestBigDouble amount)
        {
            this.resource = resource;
            this.amount = amount;
        }

        public readonly TestResourceSO resource;
        public readonly TestBigDouble amount;
    }

    private sealed class TestResourceSO : ScriptableObject
    {
        public string uuid = string.Empty;
        public TestBigDouble quantity = new TestBigDouble(0.0, 0);
        public TestValueModifierRecord maxQuantity = new TestValueModifierRecord(new TestBigDouble(-1.0, 0));
        public double trueAmountMultiplier = 1.0;

        public TestBigDouble GetTrueQuantity() => quantity;

        public TestBigDouble GetTrueAmount(TestBigDouble amount) =>
            new TestBigDouble(amount.mantissa * trueAmountMultiplier, amount.exponent);
    }

    private sealed class TestValueModifierRecord
    {
        private readonly TestBigDouble _value;

        public TestValueModifierRecord(TestBigDouble value)
        {
            _value = value;
        }

        public TestBigDouble GetValue() => _value;
    }

    private sealed class TestBigDouble
    {
        public TestBigDouble(double mantissa, long exponent)
        {
            this.mantissa = mantissa;
            this.exponent = exponent;
        }

        public readonly double mantissa;
        public readonly long exponent;
    }
}
