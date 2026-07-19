using System;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoConceptControllerHeadlessTests : IDisposable
{
    public AutoConceptControllerHeadlessTests()
    {
        IdScriptableObject.RuntimeLookup.Clear();
    }

    public void Dispose()
    {
        IdScriptableObject.RuntimeLookup.Clear();
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void AutoConceptController_TimedRotationStartsOnlyAfterSettlementAndAddsReplacementAfterRemoval()
    {
        var first = Concept("00000000-0000-0000-0000-000000000001");
        var second = Concept("00000000-0000-0000-0000-000000000002");
        var active = InstallNativeLists(first, second);
        var config = Config(AutoConceptSlotManagementMode.TimedCycle);
        var coordinator = new SuitePerformanceCoordinator(new ZeroClock(), 10.0, 10.0, 32);
        long frame = 0;
        using var controller = new AutoConceptController(
            config,
            new ReflectionConceptRuntime(new AlchemyGameplayDomainClassifier()),
            new ManualLogSource(),
            coordinator,
            () => frame);

        Tick(controller, ref frame, 0.1f);
        var firstInstance = Assert.Single(active.value);
        Assert.Same(first, firstInstance.reference);
        Assert.Equal(0, firstInstance.quantity);
        Assert.Equal(1, firstInstance.queuedQuantity);

        Tick(controller, ref frame, 20.0f);
        Assert.Single(active.value);
        Assert.Equal(1, firstInstance.queuedQuantity);

        active.RebuildCounts();
        controller.NotifyNativeChange();
        Tick(controller, ref frame, 0.1f);
        Tick(controller, ref frame, 9.0f);
        Assert.Equal(1, firstInstance.queuedQuantity);

        Tick(controller, ref frame, 1.1f);
        Assert.Equal(1, firstInstance.quantity);
        Assert.Equal(0, firstInstance.queuedQuantity);
        Assert.DoesNotContain(active.value, item => ReferenceEquals(item.reference, second));

        active.RebuildCounts();
        controller.NotifyNativeChange();
        Tick(controller, ref frame, 0.3f);
        var replacement = active.value.Single(item => ReferenceEquals(item.reference, second));
        Assert.Equal(0, replacement.quantity);
        Assert.Equal(1, replacement.queuedQuantity);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void AutoConceptController_DisableLeavesSettledNativeQuantityUnchanged()
    {
        var recipe = Concept("owned-concept");
        var active = InstallNativeLists(recipe);
        var config = Config(AutoConceptSlotManagementMode.PreserveManual);
        var coordinator = new SuitePerformanceCoordinator(new ZeroClock(), 10.0, 10.0, 16);
        long frame = 0;
        using var controller = new AutoConceptController(
            config,
            new ReflectionConceptRuntime(new AlchemyGameplayDomainClassifier()),
            new ManualLogSource(),
            coordinator,
            () => frame);

        Tick(controller, ref frame, 0.1f);
        active.RebuildCounts();
        controller.NotifyNativeChange();
        Tick(controller, ref frame, 0.1f);
        var instance = Assert.Single(active.value);
        Assert.Equal(1, instance.quantity);

        config.AutoConceptMode.Value = AutoConceptOperationMode.Disabled;
        Tick(controller, ref frame, 10.0f);

        Assert.Equal(1, instance.quantity);
        Assert.Equal(1, instance.queuedQuantity);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void AutoConceptController_LifecycleInvalidationDiscardsPendingMutation()
    {
        var recipe = Concept("pending-concept");
        var active = InstallNativeLists(recipe);
        var config = Config(AutoConceptSlotManagementMode.PreserveManual);
        var coordinator = new SuitePerformanceCoordinator(new ZeroClock(), 10.0, 10.0, 16);
        using var blocker = coordinator.Register(
            "test",
            "occupy native mutation",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        blocker.SetPending(true);
        long frame = 1;
        using var controller = new AutoConceptController(
            config,
            new ReflectionConceptRuntime(new AlchemyGameplayDomainClassifier()),
            new ManualLogSource(),
            coordinator,
            () => frame);

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(blocker, frame, out var lease));
        lease.Complete();
        controller.Tick(0.1f);
        Assert.Empty(active.value);

        controller.InvalidateLifecycle();
        recipe.discovered = false;
        blocker.SetPending(false);
        frame++;
        controller.Tick(0.1f);

        Assert.Empty(active.value);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void AutoConceptController_OwnershipLossDiscardsPendingMutation()
    {
        var recipe = Concept("ownership-pending-concept");
        var active = InstallNativeLists(recipe);
        var config = Config(AutoConceptSlotManagementMode.PreserveManual);
        var coordinator = new SuitePerformanceCoordinator(new ZeroClock(), 10.0, 10.0, 16);
        using var blocker = coordinator.Register(
            "test", "occupy native mutation", SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        blocker.SetPending(true);
        var owned = true;
        long frame = 1;
        using var controller = new AutoConceptController(
            config,
            new ReflectionConceptRuntime(new AlchemyGameplayDomainClassifier()),
            new ManualLogSource(), coordinator, () => frame,
            ownsActionFamily: () => owned);

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(blocker, frame, out var lease));
        lease.Complete();
        controller.Tick(0.1f);
        owned = false;
        blocker.SetPending(false);
        frame++;
        controller.Tick(0.1f);

        Assert.Empty(active.value);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void AutoConceptHealthKeepsMutationFaultUntilLifecycleRecovery()
    {
        var recipe = Concept("faulted-concept");
        var active = InstallNativeLists(recipe);
        active.SuppressAddMutation = true;
        var config = Config(AutoConceptSlotManagementMode.PreserveManual);
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config, 1, registry);
        var coordinator = new SuitePerformanceCoordinator(new ZeroClock(), 10.0, 10.0, 16);
        long frame = 0;
        using var controller = new AutoConceptController(
            config,
            new ReflectionConceptRuntime(new AlchemyGameplayDomainClassifier()),
            new ManualLogSource(),
            coordinator,
            () => frame,
            statuses.AutoConcept);

        Tick(controller, ref frame, 0.1f);
        Assert.Equal(FeatureStatusState.Faulted, statuses.AutoConcept.Current.State);
        Assert.Equal(FeatureStatusReasonCode.PostconditionFailed, statuses.AutoConcept.Current.Reason.Code);

        config.AutoConceptMode.Value = AutoConceptOperationMode.Disabled;
        Tick(controller, ref frame, 0.1f);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoConcept.Current.State);

        config.AutoConceptMode.Value = AutoConceptOperationMode.Active;
        controller.NotifyNativeChange();
        Tick(controller, ref frame, 0.1f);
        Assert.Equal(FeatureStatusState.Faulted, statuses.AutoConcept.Current.State);
        Assert.Equal(FeatureStatusReasonCode.PostconditionFailed, statuses.AutoConcept.Current.Reason.Code);

        controller.InvalidateLifecycle();
        statuses.ObserveLifecycleNotReady(config, 2);
        active.SuppressAddMutation = false;
        controller.NotifyNativeChange();
        Tick(controller, ref frame, 0.1f);

        Assert.Equal(FeatureStatusState.Operational, statuses.AutoConcept.Current.State);
        Assert.Equal(2, statuses.AutoConcept.Current.LifecycleGeneration);
        Assert.Single(active.value);
    }

    private static AutomataConfig Config(AutoConceptSlotManagementMode mode)
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AutoConceptMode.Value = AutoConceptOperationMode.Active;
        config.AutoConceptSlotManagement.Value = mode;
        config.AutoConceptTrainingPeriodSeconds.Value = 10;
        config.AutoConceptQuantityCap.Value = 1;
        config.AutoConceptRateReservePercent.Value = 0.0f;
        config.AutoConceptMinimumResourcePercent.Value = 0.0f;
        config.EnableOperationalLogging.Value = false;
        return config;
    }

    private static AlchemyRecipeSO Concept(string uuid)
    {
        var resource = new ConceptResource();
        return new AlchemyRecipeSO(
            uuid,
            uuid,
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = 1,
            drainCost = new ConceptCostVector(
                new ConceptCostEntry(resource, new BigDouble(1.0, 0))),
        };
    }

    private static AlchemyInstanceListVariable InstallNativeLists(params AlchemyRecipeSO[] recipes)
    {
        var active = new AlchemyInstanceListVariable();
        active.SetGuid(new Guid(ReflectionConceptRuntime.ActiveConceptsUuid));
        var recipeList = new AlchemyRecipeListVariable { value = recipes.ToList() };
        recipeList.SetGuid(AlchemyGameplayDomainClassifier.ConceptRecipesUuid);
        IdScriptableObject.RuntimeLookup[new Guid(ReflectionConceptRuntime.ActiveConceptsUuid)] = active;
        IdScriptableObject.RuntimeLookup[AlchemyGameplayDomainClassifier.ConceptRecipesUuid] = recipeList;
        return active;
    }

    private static void Tick(AutoConceptController controller, ref long frame, float deltaSeconds)
    {
        frame++;
        controller.Tick(deltaSeconds);
    }

    private sealed class ZeroClock : IPerformanceClock
    {
        public long GetTimestamp() => 0;

        public double GetElapsedMilliseconds(long startTimestamp, long endTimestamp) => 0.0;
    }
}
