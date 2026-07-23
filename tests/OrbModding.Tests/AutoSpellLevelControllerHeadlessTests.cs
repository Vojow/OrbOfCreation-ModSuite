using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoSpellLevelControllerHeadlessTests : IDisposable
{
    public AutoSpellLevelControllerHeadlessTests()
    {
        IdScriptableObject.RuntimeLookup.Clear();
        SpellRecipeSO.All.Clear();
        SpellManager.instance = new SpellManager();
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void LockedLevelAllUpgrade_UsesAuditedSingleSpellMutation()
    {
        InstallLevelAllUpgrade(purchaseLevel: 0);
        var lower = AddReadySpell("00000000-0000-0000-0000-000000000001", mastery: 1);
        var higher = AddReadySpell("00000000-0000-0000-0000-000000000002", mastery: 4);
        var config = ActiveConfig();
        var coordinator = new SuitePerformanceCoordinator(new ZeroClock(), 10.0, 10.0, 16);
        long frame = 0;
        using var controller = new AutoSpellLevelController(
            config,
            new ReflectionSpellLevelRuntime(),
            new ManualLogSource(),
            coordinator,
            () => frame);

        frame++;
        controller.Tick(0.1f);

        Assert.Equal(AutoSpellLevelCapability.Single, controller.Capability);
        Assert.Equal(2, lower.masteryLevel);
        Assert.Equal(1, lower.levelCost.PerformCalls);
        Assert.Equal(4, higher.masteryLevel);
        Assert.Equal(0, higher.levelCost.PerformCalls);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void UnlockedLevelAllUpgrade_DelegatesToNativeLevelAllAction()
    {
        InstallLevelAllUpgrade(purchaseLevel: 1);
        var first = AddReadySpell("00000000-0000-0000-0000-000000000011", mastery: 2);
        var second = AddReadySpell("00000000-0000-0000-0000-000000000012", mastery: 6);
        var config = ActiveConfig();
        var coordinator = new SuitePerformanceCoordinator(new ZeroClock(), 10.0, 10.0, 16);
        long frame = 0;
        using var controller = new AutoSpellLevelController(
            config,
            new ReflectionSpellLevelRuntime(),
            new ManualLogSource(),
            coordinator,
            () => frame);

        frame++;
        controller.Tick(0.1f);

        Assert.Equal(AutoSpellLevelCapability.All, controller.Capability);
        Assert.Equal(3, first.masteryLevel);
        Assert.Equal(7, second.masteryLevel);
        Assert.Equal(1, first.levelCost.PerformCalls);
        Assert.Equal(1, second.levelCost.PerformCalls);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void NoOpSingleLevelBlocksFurtherCostsUntilLifecycleRecovery()
    {
        InstallLevelAllUpgrade(purchaseLevel: 0);
        var recipe = AddReadySpell("00000000-0000-0000-0000-000000000013", mastery: 2);
        recipe.SuppressLevelMutation = true;
        using var runtime = new ReflectionSpellLevelRuntime();
        var candidate = runtime.ReadSnapshot(out _).Candidate;
        Assert.NotNull(candidate);

        Assert.False(runtime.TryLevelSingle(candidate!, out var failedReason));
        Assert.Contains("PostconditionFailed", failedReason);
        Assert.Equal(2, runtime.LastNativeMutationOutcome.NativeCallsAttempted);
        Assert.Equal(0, runtime.LastNativeMutationOutcome.MutationsCommitted);
        Assert.Equal(1, recipe.levelCost.PerformCalls);
        Assert.False(runtime.TryLevelSingle(candidate, out var blockedReason));
        Assert.Contains("blocked until the next lifecycle", blockedReason);
        Assert.Equal(0, runtime.LastNativeMutationOutcome.NativeCallsAttempted);
        Assert.Equal(1, recipe.levelCost.PerformCalls);

        recipe.SuppressLevelMutation = false;
        runtime.InvalidateLifecycle();

        Assert.True(runtime.TryLevelSingle(candidate, out var recoveredReason), recoveredReason);
        Assert.Equal(3, recipe.masteryLevel);
        Assert.Equal(2, recipe.levelCost.PerformCalls);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void LifecycleInvalidation_DiscardsAQueuedSpellMutation()
    {
        InstallLevelAllUpgrade(purchaseLevel: 0);
        var recipe = AddReadySpell("00000000-0000-0000-0000-000000000021", mastery: 3);
        var config = ActiveConfig();
        var coordinator = new SuitePerformanceCoordinator(new ZeroClock(), 10.0, 10.0, 16);
        using var blocker = coordinator.Register(
            "test",
            "occupy native mutation",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        blocker.SetPending(true);
        long frame = 1;
        using var controller = new AutoSpellLevelController(
            config,
            new ReflectionSpellLevelRuntime(),
            new ManualLogSource(),
            coordinator,
            () => frame);

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(blocker, frame, out var lease));
        lease.Complete();
        controller.Tick(0.1f);
        Assert.Equal(3, recipe.masteryLevel);

        controller.InvalidateLifecycle();
        config.AutoLevelSpells.Value = false;
        blocker.SetPending(false);
        frame++;
        controller.Tick(0.1f);

        Assert.Equal(3, recipe.masteryLevel);
        Assert.Equal(0, recipe.levelCost.PerformCalls);
        Assert.Equal(AutoSpellLevelCapability.Locked, controller.Capability);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void OwnershipLossDiscardsAQueuedSpellMutation()
    {
        InstallLevelAllUpgrade(purchaseLevel: 0);
        var recipe = AddReadySpell("00000000-0000-0000-0000-000000000031", mastery: 3);
        var config = ActiveConfig();
        var coordinator = new SuitePerformanceCoordinator(new ZeroClock(), 10.0, 10.0, 16);
        using var blocker = coordinator.Register(
            "test", "occupy native mutation", SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        blocker.SetPending(true);
        var owned = true;
        long frame = 1;
        using var controller = new AutoSpellLevelController(
            config, new ReflectionSpellLevelRuntime(), new ManualLogSource(), coordinator,
            () => frame, ownsActionFamily: () => owned);

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(blocker, frame, out var lease));
        lease.Complete();
        controller.Tick(0.1f);
        owned = false;
        blocker.SetPending(false);
        frame++;
        controller.Tick(0.1f);

        Assert.Equal(3, recipe.masteryLevel);
        Assert.Equal(0, recipe.levelCost.PerformCalls);
    }

    public void Dispose()
    {
        IdScriptableObject.RuntimeLookup.Clear();
        SpellRecipeSO.All.Clear();
        SpellManager.instance = null;
    }

    private static BepInExAutomataConfiguration ActiveConfig()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoLevelSpells.Value = true;
        config.EnableOperationalLogging.Value = false;
        return config;
    }

    private static void InstallLevelAllUpgrade(int purchaseLevel)
    {
        IdScriptableObject.RuntimeLookup.Add(
            new Guid(ReflectionSpellLevelRuntime.UnlockLevelAllSpellsUuid),
            new UpgradeSO
            {
                uuid = ReflectionSpellLevelRuntime.UnlockLevelAllSpellsUuid,
                purchaseLevel = purchaseLevel,
            });
    }

    private static SpellRecipeSO AddReadySpell(string uuid, int mastery)
    {
        var recipe = new SpellRecipeSO
        {
            uuid = uuid,
            masteryLevel = mastery,
            discovered = true,
            readyToLevel = true,
        };
        recipe.levelingPrerequisites.unlocked = true;
        SpellManager.instance!.availableSpellRecipes.value.Add(recipe);
        return recipe;
    }

    private sealed class ZeroClock : IPerformanceClock
    {
        public long GetTimestamp() => 0;

        public double GetElapsedMilliseconds(long startTimestamp, long endTimestamp) => 0;
    }
}
