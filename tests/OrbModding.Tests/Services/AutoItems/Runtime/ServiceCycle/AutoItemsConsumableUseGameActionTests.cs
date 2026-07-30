using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

public sealed class AutoItemsConsumableUseGameActionTests : IDisposable
{
    private readonly Dictionary<Guid, object> _registry = new();

    public AutoItemsConsumableUseGameActionTests()
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
    public void ScrollSubmissionUsesScopedTargetRevalidationAndVerifiesExactEvidence()
    {
        var scroll = Item(AutoItemsConsumableFamily.Scroll);
        using var gameAction = GameAction();
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var result = gameAction.Submit(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.True(scroll.IsRandomized());
        Assert.Equal(0, scroll.GetQuantity());
        Assert.Equal(1, scroll.GetQueued());
        Assert.Equal(2, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(1, result.CallOutcome.MutationAttempts);
        Assert.Equal(1, result.CallOutcome.MutationsCommitted);
    }

    [Fact]
    public void RelicSubmissionVerifiesOneNativeTransaction()
    {
        var relic = Item(AutoItemsConsumableFamily.Relic);
        using var gameAction = GameAction();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);

        var result = gameAction.Submit(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(0, relic.GetQuantity());
        Assert.Equal(1, relic.GetQueued());
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.CallOutcome);
    }

    [Fact]
    public void ChangedFamilyDoesNotMutate()
    {
        var item = Item(AutoItemsConsumableFamily.Relic);
        using var gameAction = GameAction();
        var action = Action(item, AutoItemsConsumableFamily.Scroll);

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.FamilyChanged, result.Preflight);
        Assert.Contains("Expected exactly one live Scroll", result.Reason);
        Assert.Equal(1, item.GetQuantity());
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void NativeBusyRefusalNamesInventoryValidatorAndDoesNotMutate()
    {
        Inventory.Preparing = true;
        var scroll = Item(AutoItemsConsumableFamily.Scroll);
        using var gameAction = GameAction();
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.NativeBusy, result.Preflight);
        Assert.Contains("Inventory.CanUseConsumable()", result.Reason);
        Assert.False(scroll.randomized);
        Assert.Equal(1, scroll.GetQuantity());
    }

    [Fact]
    public void LostMutationPermitPreservesTheExactOwnershipReason()
    {
        var scroll = Item(AutoItemsConsumableFamily.Scroll);
        using var gameAction = GameAction(
            static () => false,
            static () =>
                "Automata Auto Items could not claim ConsumableUse; Other Items owns it.");
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.MutationPermitUnavailable, result.Preflight);
        Assert.Equal(
            "Automata Auto Items could not claim ConsumableUse; Other Items owns it.",
            result.Reason);
        Assert.False(scroll.randomized);
        Assert.Equal(1, scroll.GetQuantity());
    }

    [Fact]
    public void ManualStockRaceAfterPreflightIsAttemptedAndFailsExactPostcondition()
    {
        var scroll = Item(AutoItemsConsumableFamily.Scroll);
        using var gameAction = GameAction(
            () =>
            {
                scroll.SetStock(0, 0, 0);
                return true;
            });
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.Quarantined, result.Preflight);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, result.Outcome);
        Assert.Contains("ambiguous consumable mutation", result.Reason);
        Assert.Contains("quarantined", result.Reason);
        Assert.Equal(new NativeMutationCallOutcome(2, 1, 0), result.CallOutcome);
    }

    [Fact]
    public void MissingLiveStockIsRefusedByCanFireBeforeMutation()
    {
        var scroll = Item(AutoItemsConsumableFamily.Scroll);
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);
        scroll.SetStock(0, 0, 0);
        using var gameAction = GameAction();

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.CanFireRefused, result.Preflight);
        Assert.Contains("ConsumableSO.CanFire()", result.Reason);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void TargetUnavailableHasAnExactNonEmptyExplanation()
    {
        var scroll = Item(AutoItemsConsumableFamily.Scroll, withTarget: false);
        using var gameAction = GameAction();
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.TargetUnavailable, result.Preflight);
        Assert.Contains("no valid structure target", result.Reason);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void AmbiguousPostconditionQuarantinesTheWholeGameActionForTheLifecycle()
    {
        var relic = Item(AutoItemsConsumableFamily.Relic);
        relic.SelectionNoOp = true;
        using var gameAction = GameAction();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);

        var ambiguous = gameAction.Submit(in action);
        var blocked = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.Quarantined, ambiguous.Preflight);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, ambiguous.Outcome);
        Assert.Equal(AutoItemsPreflight.Quarantined, blocked.Preflight);
        Assert.Contains(relic.GetGuid().ToString("D"), blocked.Reason);
        Assert.Equal(0, blocked.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void LifecycleResetRebindsTheCompleteContractAndClearsQuarantine()
    {
        var relic = Item(AutoItemsConsumableFamily.Relic);
        relic.SelectionNoOp = true;
        using var gameAction = GameAction();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);
        Assert.False(gameAction.Submit(in action).Verified);

        relic.SelectionNoOp = false;
        gameAction.InvalidateLifecycle();
        var retried = gameAction.Submit(in action);

        Assert.True(gameAction.BindingsAvailable, gameAction.BindingFailure);
        Assert.True(retried.Verified, retried.Reason);
    }

    private AutoItemsConsumableUseGameAction GameAction(
        Func<bool>? permit = null,
        Func<string>? failure = null)
    {
        IDictionary dictionary = _registry;
        var resolver = new TypedRegistryResolver(
            static () => 1,
            () => TypedRegistrySourceSnapshot.Ready(dictionary),
            value => value is IdScriptableObject entity ? entity.GetGuid() : null);
        return new AutoItemsConsumableUseGameAction(
            resolver,
            permit ?? (static () => true),
            failure ?? (static () => string.Empty));
    }

    private ConsumableSO Item(
        AutoItemsConsumableFamily family,
        bool withTarget = true)
    {
        var familyType = new ConsumableTypeSO();
        familyType.SetGuid(
            family == AutoItemsConsumableFamily.Scroll
                ? KnownEntities.ConsumableScrollType.Uuid
                : KnownEntities.ConsumableRelicType.Uuid);
        var item = new ConsumableSO
        {
            visible = true,
            canBeRandomized = family == AutoItemsConsumableFamily.Scroll,
        };
        item.SetGuid(Guid.NewGuid());
        item.SetStock(1, 0, 0);
        item.consumableTypes.Add(familyType);
        if (family == AutoItemsConsumableFamily.Scroll)
        {
            item.consumableCounts.Add(new ConsumableCount { Level = 1, Quantity = 1 });
            var targeting = new Targeting.TargetStructure();
            if (withTarget) targeting.Candidates.Add(new Target());
            var request = new RequestTargetEffectScript
            {
                targetOptions = new Targeting.TargetSelectOptions { Targeting = targeting },
            };
            var block = new InstantEffectBlock();
            block.effectScripts.Add(request);
            item.onUseEffects.Add(block);
        }
        ConsumableSO.All.Add(item);
        _registry.Add(item.GetGuid(), item);
        return item;
    }

    private static AutoItemsCycleAction Action(
        ConsumableSO item,
        AutoItemsConsumableFamily family) =>
        new(item.GetGuid(), family, collectedAtEpoch: 1, plannedLevel: family ==
            AutoItemsConsumableFamily.Scroll ? 1 : 0);

    private sealed class Target : Targeting.ITargetable
    {
    }
}
