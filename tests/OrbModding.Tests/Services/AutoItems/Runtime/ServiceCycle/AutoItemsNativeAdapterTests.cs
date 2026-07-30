using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

public sealed class AutoItemsNativeAdapterTests : IDisposable
{
    private readonly Dictionary<Guid, object> _registry = new();

    public AutoItemsNativeAdapterTests()
    {
        Inventory.Preparing = false;
        ConsumableSO.All.Clear();
        GlobalVariables.MultiBuy = new IntVariable { Value = 1 };
        NativeMultiBuyScope.ResetQuarantineForTests();
    }

    public void Dispose()
    {
        Inventory.Preparing = false;
        ConsumableSO.All.Clear();
        NativeMultiBuyScope.ResetQuarantineForTests();
    }

    [Fact]
    public void ScrollSubmissionForcesNativeRandomizationAndVerifiesOneQueuedUse()
    {
        var scroll = Item(KnownEntities.ConsumableScrollType.Uuid, randomizable: true);
        using var adapter = Adapter();
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var result = adapter.Submit(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.True(scroll.IsRandomized());
        Assert.Equal(0, scroll.GetQuantity());
        Assert.Equal(1, scroll.GetQueued());
        Assert.Equal(2, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(1, result.CallOutcome.MutationsCommitted);
    }

    [Fact]
    public void ScrollBatchUsesOneNativeSubmissionAndRestoresPlayerMultiBuy()
    {
        var scroll = Item(KnownEntities.ConsumableScrollType.Uuid, randomizable: true);
        scroll.SetStock(4, 0, 0);
        GlobalVariables.MultiBuy.Value = 5;
        using var adapter = Adapter();
        var action = new AutoItemsCycleAction(
            scroll.GetGuid(),
            AutoItemsConsumableFamily.Scroll,
            collectedAtFrame: 1,
            collectedAtEpoch: 1,
            plannedLevel: 1,
            requestedQuantity: 3);

        var result = adapter.Submit(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(3, scroll.GetQuantity());
        Assert.Equal(3, scroll.GetQueued());
        Assert.Equal(5, GlobalVariables.MultiBuy.AsInt());
        Assert.Equal(2, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(2, result.CallOutcome.MutationAttempts);
    }

    [Fact]
    public void RelicSubmissionCanUseAvailableToxicityHeadroom()
    {
        Toxicity(displayQuantity: 1d);
        var relic = Item(KnownEntities.ConsumableRelicType.Uuid, randomizable: false);
        using var adapter = Adapter();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);

        var result = adapter.Submit(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(0, relic.GetQuantity());
        Assert.Equal(1, relic.GetQueued());
        Assert.Equal(1, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void RelicSubmissionAtZeroUsesExactlyOneItem()
    {
        Toxicity(displayQuantity: 0d);
        var relic = Item(KnownEntities.ConsumableRelicType.Uuid, randomizable: false);
        using var adapter = Adapter();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);

        var result = adapter.Submit(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(0, relic.GetQuantity());
        Assert.Equal(1, relic.GetQueued());
        Assert.Equal(1, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void TemporarySubmissionAtZeroCreatesOnePendingUsage()
    {
        Toxicity(displayQuantity: 0d);
        var fruit = Item(KnownEntities.ConsumableFruitType.Uuid, randomizable: false);
        fruit.hasDuration = true;
        fruit.durationBase = 60d;
        using var adapter = Adapter();
        var action = Action(fruit, AutoItemsConsumableFamily.Fruit);

        var result = adapter.Submit(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.Single(fruit.consumableUsages);
        Assert.False(fruit.consumableUsages[0].en);
    }

    [Fact]
    public void TemporarySubmissionCanUseAvailableToxicityHeadroom()
    {
        Toxicity(displayQuantity: 1d);
        var potion = Item(KnownEntities.ConsumablePotionType.Uuid, randomizable: false);
        potion.hasDuration = true;
        potion.durationBase = 60d;
        using var adapter = Adapter();
        var action = Action(potion, AutoItemsConsumableFamily.Potion);

        var result = adapter.Submit(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(0, potion.GetQuantity());
        Assert.Single(potion.consumableUsages);
    }

    [Fact]
    public void AllowlistedThreadUsesTheGuardedTemporarySubmissionContract()
    {
        Toxicity(displayQuantity: 1d);
        var thread = Item(KnownEntities.ConsumableThreadType.Uuid, randomizable: false);
        thread.hasDuration = true;
        thread.durationBase = 60d;
        using var adapter = Adapter();
        var action = Action(thread, AutoItemsConsumableFamily.Thread);

        var result = adapter.Submit(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(0, thread.GetQuantity());
        Assert.Single(thread.consumableUsages);
    }

    [Fact]
    public void TargetUnavailableIsAnExpectedRejectionRatherThanAnAdapterFault()
    {
        var submission = AutoItemsSubmission.Reject(
            AutoItemsPreflight.TargetUnavailable,
            "No target.");

        var result = AutoItemsCycleActionAdapter.Map(in submission);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoItemsActionResultCodes.TargetUnavailable, result.Code);
    }

    [Theory]
    [InlineData(1, 1111)]
    [InlineData(9, 1112)]
    [InlineData(10, 1113)]
    public void FatalBoundaryFailuresKeepDistinctJournalCodes(
        int preflightValue,
        int expectedCode)
    {
        var preflight = (AutoItemsPreflight)preflightValue;
        var submission = AutoItemsSubmission.Reject(preflight, "bounded diagnostic");

        var result = AutoItemsCycleActionAdapter.Map(in submission);

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(new ServiceActionResultCode(expectedCode), result.Code);
    }

    [Fact]
    public void ExistingTemporaryUsageBlocksAnotherTemporarySubmission()
    {
        Toxicity(displayQuantity: 0d);
        var active = Item(KnownEntities.ConsumableFruitType.Uuid, randomizable: false);
        active.hasDuration = true;
        active.durationBase = 60d;
        active.consumableUsages.Add(new ConsumableUsage { en = true, dr = 30d, maxDr = 60d });
        var potion = Item(KnownEntities.ConsumablePotionType.Uuid, randomizable: false);
        potion.hasDuration = true;
        potion.durationBase = 60d;
        using var adapter = Adapter();
        var action = Action(potion, AutoItemsConsumableFamily.Potion);

        var result = adapter.Submit(in action);

        Assert.Equal(AutoItemsPreflight.TemporaryEffectPresent, result.Preflight);
        Assert.Equal(1, potion.GetQuantity());
    }

    [Fact]
    public void TemporaryUsageStartedAfterPlanningBlocksARelicSubmission()
    {
        Toxicity(displayQuantity: 0d);
        var relic = Item(KnownEntities.ConsumableRelicType.Uuid, randomizable: false);
        var fruit = Item(KnownEntities.ConsumableFruitType.Uuid, randomizable: false);
        fruit.hasDuration = true;
        fruit.durationBase = 60d;
        using var adapter = Adapter();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);
        fruit.consumableUsages.Add(
            new ConsumableUsage { en = true, dr = 30d, maxDr = 60d });

        var result = adapter.Submit(in action);

        Assert.Equal(AutoItemsPreflight.TemporaryEffectPresent, result.Preflight);
        Assert.Equal(1, relic.GetQuantity());
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void ChangedTemporaryCostShapeDoesNotMutate()
    {
        Toxicity(displayQuantity: 0d);
        var fruit = Item(KnownEntities.ConsumableFruitType.Uuid, randomizable: false);
        fruit.hasDuration = true;
        fruit.durationBase = 60d;
        var otherResource = new ResourceSO { uuid = Guid.NewGuid().ToString("D") };
        fruit.consumeCost.costs.Add(new ResourceTuple(otherResource, new BigDouble(1d)));
        using var adapter = Adapter();
        var action = Action(fruit, AutoItemsConsumableFamily.Fruit);

        var result = adapter.Submit(in action);

        Assert.Equal(AutoItemsPreflight.TemporaryCostChanged, result.Preflight);
        Assert.Equal(1, fruit.GetQuantity());
        Assert.Empty(fruit.consumableUsages);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void AmbiguousTemporaryMutationQuarantinesOnlyTheExactItem()
    {
        Toxicity(displayQuantity: 0d);
        var broken = Item(KnownEntities.ConsumableFruitType.Uuid, randomizable: false);
        broken.hasDuration = true;
        broken.durationBase = 60d;
        broken.SelectionNoOp = true;
        var other = Item(KnownEntities.ConsumableFruitType.Uuid, randomizable: false);
        other.hasDuration = true;
        other.durationBase = 60d;
        using var adapter = Adapter();
        var brokenAction = Action(broken, AutoItemsConsumableFamily.Fruit);
        var otherAction = Action(other, AutoItemsConsumableFamily.Fruit);

        var ambiguous = adapter.Submit(in brokenAction);
        var exactBlocked = adapter.Submit(in brokenAction);
        var otherResult = adapter.Submit(in otherAction);

        Assert.Equal(NativeMutationOutcome.PostconditionFailed, ambiguous.Outcome);
        Assert.Equal(AutoItemsPreflight.Quarantined, exactBlocked.Preflight);
        Assert.True(otherResult.Verified, otherResult.Reason);
    }

    [Fact]
    public void NativeBusyRefusalDoesNotMutateTheItem()
    {
        Inventory.Preparing = true;
        var scroll = Item(KnownEntities.ConsumableScrollType.Uuid, randomizable: true);
        using var adapter = Adapter();
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var result = adapter.Submit(in action);

        Assert.Equal(AutoItemsPreflight.NativeBusy, result.Preflight);
        Assert.False(scroll.randomized);
        Assert.Equal(1, scroll.GetQuantity());
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void ScrollWithoutLiveRandomizationSupportDoesNotMutate()
    {
        var scroll = Item(KnownEntities.ConsumableScrollType.Uuid, randomizable: false);
        using var adapter = Adapter();
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var result = adapter.Submit(in action);

        Assert.Equal(AutoItemsPreflight.RandomizationUnavailable, result.Preflight);
        Assert.Equal(1, scroll.GetQuantity());
        Assert.Equal(0, scroll.GetQueued());
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void ChangedFamilyDoesNotMutate()
    {
        var item = Item(KnownEntities.ConsumablePotionType.Uuid, randomizable: true);
        using var adapter = Adapter();
        var action = Action(item, AutoItemsConsumableFamily.Scroll);

        var result = adapter.Submit(in action);

        Assert.Equal(AutoItemsPreflight.FamilyChanged, result.Preflight);
        Assert.Equal(1, item.GetQuantity());
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void LostMutationPermitDoesNotMutate()
    {
        var scroll = Item(KnownEntities.ConsumableScrollType.Uuid, randomizable: true);
        using var adapter = Adapter(static () => false);
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var result = adapter.Submit(in action);

        Assert.Equal(AutoItemsPreflight.MutationPermitUnavailable, result.Preflight);
        Assert.False(scroll.randomized);
        Assert.Equal(1, scroll.GetQuantity());
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void ManualStockChangeBeforeSubmissionIsRevalidated()
    {
        var scroll = Item(KnownEntities.ConsumableScrollType.Uuid, randomizable: true);
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);
        scroll.SetStock(0, 0, 0);
        using var adapter = Adapter();

        var result = adapter.Submit(in action);

        Assert.Equal(AutoItemsPreflight.NotAdmissible, result.Preflight);
        Assert.False(scroll.randomized);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void AmbiguousAttemptQuarantinesFurtherUsesUntilLifecycleInvalidation()
    {
        var scroll = Item(KnownEntities.ConsumableScrollType.Uuid, randomizable: true);
        scroll.SelectionNoOp = true;
        using var adapter = Adapter();
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var ambiguous = adapter.Submit(in action);
        var blocked = adapter.Submit(in action);

        Assert.Equal(NativeMutationOutcome.PostconditionFailed, ambiguous.Outcome);
        Assert.Equal(AutoItemsPreflight.Quarantined, blocked.Preflight);
        Assert.Equal(0, blocked.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void LifecycleInvalidationClearsAmbiguousMutationQuarantine()
    {
        var scroll = Item(KnownEntities.ConsumableScrollType.Uuid, randomizable: true);
        scroll.SelectionNoOp = true;
        using var adapter = Adapter();
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);
        Assert.False(adapter.Submit(in action).Verified);

        scroll.SelectionNoOp = false;
        adapter.InvalidateLifecycle();
        var retried = adapter.Submit(in action);

        Assert.True(retried.Verified, retried.Reason);
    }

    private AutoItemsNativeAdapter Adapter(Func<bool>? mutationPermit = null)
    {
        IDictionary dictionary = _registry;
        var resolver = new TypedRegistryResolver(
            static () => 1,
            () => TypedRegistrySourceSnapshot.Ready(dictionary),
            value => value switch
            {
                IdScriptableObject entity => entity.GetGuid(),
                UpgradeableObject upgradeable => upgradeable.GetGuid(),
                _ => null,
            });
        return new AutoItemsNativeAdapter(
            resolver,
            mutationPermit ?? (static () => true));
    }

    private ConsumableSO Item(Guid family, bool randomizable)
    {
        var familyType = new ConsumableTypeSO();
        familyType.SetGuid(family);
        var item = new ConsumableSO
        {
            visible = true,
            canBeRandomized = randomizable,
        };
        item.SetGuid(Guid.NewGuid());
        item.SetStock(1, 0, 0);
        item.consumableTypes.Add(familyType);
        var toxicity = new ResourceSO
        {
            uuid = KnownEntities.PotionToxicity.Uuid.ToString("D"),
        };
        item.consumeCost.costs.Add(new ResourceTuple(toxicity, new BigDouble(1d)));
        ConsumableSO.All.Add(item);
        _registry.Add(item.GetGuid(), item);
        return item;
    }

    private void Toxicity(double displayQuantity)
    {
        var resource = new ResourceSO
        {
            invertedResource = true,
            maxQuantity = new ValueModifierRecord(new BigDouble(100d)),
            quantity = new BigDouble(100d - displayQuantity),
        };
        resource.uuid = KnownEntities.PotionToxicity.Uuid.ToString("D");
        _registry.Add(resource.GetGuid(), resource);
    }

    private static AutoItemsCycleAction Action(
        ConsumableSO item,
        AutoItemsConsumableFamily family) =>
        new(item.GetGuid(), family, 1, 1);
}
