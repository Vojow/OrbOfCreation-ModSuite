using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoScribeOneShotCraftGameActionTests : IDisposable
{
    private readonly Dictionary<Guid, object> _registry = new();
    private readonly AutoScribeIdentityProfile _profile = AutoScribeIdentityCatalog.Audited;

    public AutoScribeOneShotCraftGameActionTests()
    {
        IdScriptableObject.RuntimeLookup.Clear();
        StructureSO.All.Clear();
        ConsumableSO.All.Clear();
        CraftingRecipeSO.All.Clear();
    }

    public void Dispose()
    {
        IdScriptableObject.RuntimeLookup.Clear();
        StructureSO.All.Clear();
        ConsumableSO.All.Clear();
        CraftingRecipeSO.All.Clear();
    }

    [Fact]
    public void VerifiedQueueAdmissionProvesPaymentCeilingAndExactOutcome()
    {
        var fixture = Fixture();
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(new BigDouble(95, 0), fixture.Resource.GetTrueQuantity());
        Assert.Equal(3, fixture.RecipeType.maxStartingLevel);
        Assert.Single(fixture.Active.value);
        Assert.True(result.Receipt.PaymentInvoked);
        Assert.True(result.Receipt.EvidenceAvailable);
        Assert.True(result.Receipt.ResourcesCharged);
        Assert.True(result.Receipt.CostMatched);
        Assert.True(result.Receipt.CeilingTransitionObserved);
        Assert.True(result.Receipt.AdmittedToQueue);
        Assert.False(result.Receipt.AdmittedToInstantStock);
        Assert.Equal(new NativeMutationCallOutcome(4, 1, 1), result.CallOutcome);
    }

    [Fact]
    public void MoreExpensiveRecipeUsesItsOwnAffordableFrontierBelowSharedCeiling()
    {
        var fixture = Fixture();
        fixture.RecipeType.maxStartingLevel = 67;
        fixture.Recipe.MaximumAffordableLevel = 24;
        var request = AtLevel(fixture.Action, level: 1);
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, request);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(24d, Assert.Single(fixture.Active.value).Quantity.ToDouble());
        Assert.Equal(67, fixture.RecipeType.maxStartingLevel);
        Assert.Equal(1, fixture.Recipe.PurchaseCalls);
    }

    [Fact]
    public void ProgressionRequestWaitsUntilItsOwnNextLevelIsAffordable()
    {
        var fixture = Fixture();
        fixture.RecipeType.maxStartingLevel = 67;
        fixture.Recipe.MaximumAffordableLevel = 24;
        var request = AtLevel(fixture.Action, level: 25);
        using var actionBoundary = GameAction();

        var waiting = Submit(actionBoundary, request);

        Assert.Equal(AutoScribePreflight.Unaffordable, waiting.Preflight);
        Assert.Empty(fixture.Active.value);
        Assert.Equal(0, fixture.Recipe.PurchaseCalls);

        fixture.Recipe.MaximumAffordableLevel = 25;
        var advanced = Submit(actionBoundary, request);

        Assert.True(advanced.Verified, advanced.Reason);
        Assert.Equal(25d, Assert.Single(fixture.Active.value).Quantity.ToDouble());
        Assert.Equal(67, fixture.RecipeType.maxStartingLevel);
    }

    [Fact]
    public void VerifiedInstantAdmissionProvesPaymentCeilingAndExactStockOutcome()
    {
        var fixture = Fixture();
        fixture.Recipe.InstantCraftEnabled = true;
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(new BigDouble(95, 0), fixture.Resource.GetTrueQuantity());
        Assert.Equal(3, fixture.RecipeType.maxStartingLevel);
        Assert.Empty(fixture.Active.value);
        Assert.True(result.Receipt.CostMatched);
        Assert.True(result.Receipt.CeilingTransitionObserved);
        Assert.False(result.Receipt.AdmittedToQueue);
        Assert.True(result.Receipt.AdmittedToInstantStock);
        Assert.Equal(1, result.Receipt.StockDelta);
        Assert.Equal(new NativeMutationCallOutcome(4, 1, 1), result.CallOutcome);
    }

    [Fact]
    public void FailureAfterPurchaseCarriesPaidPartialCommitAndQuarantines()
    {
        var fixture = Fixture();
        fixture.Recipe.ThrowAfterPurchase = true;
        using var actionBoundary = GameAction();

        var failed = Submit(actionBoundary, fixture.Action);
        var blocked = Submit(actionBoundary, fixture.Action);
        var health = Observe(failed);

        Assert.Equal(AutoScribeNativeStage.Payment, failed.Stage);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 0), failed.CallOutcome);
        Assert.True(failed.Receipt.ResourcesCharged);
        Assert.True(failed.Receipt.CostMatched);
        Assert.True(failed.Receipt.CeilingTransitionObserved);
        Assert.False(failed.Receipt.AdmittedToQueue);
        Assert.Equal(AutoScribePreflight.Quarantined, blocked.Preflight);
        Assert.Equal(AutoScribeNativeStage.Payment, health.Stage);
        Assert.Contains("after purchase", health.Reason);
    }

    [Fact]
    public void DuplicateCostRowsAreVerifiedAsOneExactResourceCharge()
    {
        var fixture = Fixture();
        fixture.Recipe.TotalCost.costs.Add(
            new ResourceTuple(fixture.Resource, new BigDouble(2, 0)));
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(new BigDouble(93, 0), fixture.Resource.GetTrueQuantity());
        Assert.True(result.Receipt.ResourcesCharged);
        Assert.True(result.Receipt.CostMatched);
    }

    [Fact]
    public void FailureDuringConstructionPreservesPaymentAndNamesConstruction()
    {
        var fixture = Fixture();
        fixture.Recipe.ThrowDuringConstruction = true;
        using var actionBoundary = GameAction();

        var failed = Submit(actionBoundary, fixture.Action);
        var health = Observe(failed);

        Assert.Equal(AutoScribeNativeStage.Construction, failed.Stage);
        Assert.Equal(new NativeMutationCallOutcome(2, 1, 0), failed.CallOutcome);
        Assert.True(failed.Receipt.CostMatched);
        Assert.False(failed.Receipt.AdmittedToQueue);
        Assert.True(actionBoundary.IsQuarantined);
        Assert.Equal(AutoScribeNativeStage.Construction, health.Stage);
        Assert.Contains("construction", health.Reason);
    }

    [Fact]
    public void FailureAfterInitiationPreservesPaymentAndNamesInitiation()
    {
        var fixture = Fixture();
        fixture.Recipe.ThrowAfterInitiation = true;
        using var actionBoundary = GameAction();

        var failed = Submit(actionBoundary, fixture.Action);
        var health = Observe(failed);

        Assert.Equal(AutoScribeNativeStage.Initiation, failed.Stage);
        Assert.Equal(new NativeMutationCallOutcome(3, 1, 0), failed.CallOutcome);
        Assert.True(failed.Receipt.CostMatched);
        Assert.False(failed.Receipt.AdmittedToQueue);
        Assert.True(actionBoundary.IsQuarantined);
        Assert.Equal(AutoScribeNativeStage.Initiation, health.Stage);
        Assert.Contains("initiation", health.Reason);
    }

    [Fact]
    public void FailureAfterFinalAdmissionRecordsAdmittedPartialCommit()
    {
        var fixture = Fixture();
        fixture.Active.ThrowAfterAdd = true;
        using var actionBoundary = GameAction();

        var failed = Submit(actionBoundary, fixture.Action);
        var health = Observe(failed);

        Assert.Equal(AutoScribeNativeStage.Admission, failed.Stage);
        Assert.Equal(new NativeMutationCallOutcome(4, 1, 0), failed.CallOutcome);
        Assert.True(failed.Receipt.CostMatched);
        Assert.True(failed.Receipt.AdmittedToQueue);
        Assert.Equal(1, failed.Receipt.QueueDelta);
        Assert.True(actionBoundary.IsQuarantined);
        Assert.Equal(AutoScribeNativeStage.Admission, health.Stage);
        Assert.Contains("admission", health.Reason);

        var reconciled = Submit(actionBoundary, fixture.Action);

        Assert.Equal(AutoScribePreflight.CompetingSupply, reconciled.Preflight);
        Assert.Contains("No second payment", reconciled.Reason);
        Assert.False(actionBoundary.IsQuarantined);
        Assert.Equal(1, fixture.Recipe.PurchaseCalls);
    }

    [Fact]
    public void DelayedAdmissionReconciliationTracksTheActuallyCraftedStrongerLevel()
    {
        var fixture = Fixture();
        fixture.Recipe.MaximumAffordableLevel = 24;
        fixture.Active.ThrowAfterAdd = true;
        var request = AtLevel(fixture.Action, level: 1);
        using var actionBoundary = GameAction();

        var failed = Submit(actionBoundary, request);
        var reconciled = Submit(actionBoundary, request);

        Assert.Equal(AutoScribePreflight.PostPaymentFault, failed.Preflight);
        Assert.Contains("attempted level 24", failed.Reason);
        Assert.Equal(AutoScribePreflight.CompetingSupply, reconciled.Preflight);
        Assert.Contains("level 24", reconciled.Reason);
        Assert.Equal(1, fixture.Recipe.PurchaseCalls);
    }

    [Fact]
    public void OneContainedScrollDoesNotBlockASiblingRole()
    {
        var fixture = Fixture();
        fixture.Recipe.ThrowDuringConstruction = true;
        using var actionBoundary = GameAction();
        var contained = Submit(actionBoundary, fixture.Action);
        var siblingRole = _profile.Roles[1];
        var siblingRecipe = Assert.IsType<CraftingRecipeSO>(_registry[siblingRole.Recipe!.Value.Uuid]);
        var siblingScroll = Assert.IsType<ConsumableSO>(_registry[siblingRole.Scroll.Uuid]);
        var siblingAction = new AutoScribeCycleAction(
            siblingRecipe.GetGuid(),
            siblingScroll.GetGuid(),
            level: 3,
            collectedAtEpoch: 1);

        var sibling = Submit(actionBoundary, siblingAction);

        Assert.Equal(AutoScribePreflight.PostPaymentFault, contained.Preflight);
        Assert.True(sibling.Verified, sibling.Reason);
        Assert.True(actionBoundary.IsQuarantined);
        Assert.Contains("contained only", actionBoundary.QuarantineReason);
    }

    [Fact]
    public void LifecycleInvalidationClearsQuarantineAndRebindsTheCompleteSet()
    {
        var fixture = Fixture();
        fixture.Recipe.ThrowDuringConstruction = true;
        using var actionBoundary = GameAction();
        var failed = Submit(actionBoundary, fixture.Action);
        Assert.Equal(AutoScribeNativeStage.Construction, failed.Stage);
        Assert.True(actionBoundary.IsQuarantined);

        fixture.Recipe.ThrowDuringConstruction = false;
        actionBoundary.InvalidateLifecycle();
        var retried = Submit(actionBoundary, fixture.Action);

        Assert.False(actionBoundary.IsQuarantined);
        Assert.True(retried.Verified, retried.Reason);
        Assert.Equal(new BigDouble(90, 0), fixture.Resource.GetTrueQuantity());
    }

    [Fact]
    public void QueueFullIsRejectedBeforePayment()
    {
        var fixture = Fixture();
        fixture.Active.Maximum = 0;
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action);

        Assert.Equal(AutoScribePreflight.QueueFull, result.Preflight);
        Assert.Equal(0, fixture.Recipe.PurchaseCalls);
        Assert.Equal(new BigDouble(100, 0), fixture.Resource.GetTrueQuantity());
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void AutomaticCompetingSupplyIsRejectedBeforePayment()
    {
        var fixture = Fixture();
        fixture.Automatic.value.Add(
            new CraftingInstance(fixture.Recipe, new BigDouble(3, 0))
            {
                Automatic = true,
            });
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action);

        Assert.Equal(AutoScribePreflight.CompetingSupply, result.Preflight);
        Assert.Contains(KnownEntities.AutoScribeInstances.Uuid.ToString("D"), result.Reason);
        Assert.Equal(0, fixture.Recipe.PurchaseCalls);
    }

    [Fact]
    public void RecipeScrollMismatchIsRejectedBeforeAnyNativeCall()
    {
        var fixture = Fixture();
        var other = _profile.Roles[1];
        var mismatched = new AutoScribeCycleAction(
            fixture.Action.RecipeId,
            other.Scroll.Uuid,
            fixture.Action.Level,
            fixture.Action.CollectedAtEpoch);
        using var actionBoundary = GameAction();

        var result = actionBoundary.Submit(in mismatched);

        Assert.Equal(AutoScribePreflight.RelationshipMismatch, result.Preflight);
        Assert.Contains("one audited Auto Scribe role", result.Reason);
        Assert.Equal(0, fixture.Recipe.PurchaseCalls);
    }

    [Fact]
    public void MissingLiveTargetReasonIsNotDiscarded()
    {
        var fixture = Fixture(withTarget: false);
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action);

        Assert.Equal(AutoScribePreflight.TargetUnavailable, result.Preflight);
        Assert.Contains("no valid live target", result.Reason);
        Assert.Equal(0, fixture.Recipe.PurchaseCalls);
    }

    private AutoScribeOneShotCraftGameAction GameAction()
    {
        IDictionary dictionary = _registry;
        var resolver = new TypedRegistryResolver(
            static () => 1,
            () => TypedRegistrySourceSnapshot.Ready(dictionary),
            value => value is IdScriptableObject item ? item.GetGuid() : null);
        return new AutoScribeOneShotCraftGameAction(
            resolver,
            _profile,
            static () => true,
            static () => string.Empty);
    }

    private static AutoScribeSubmission Submit(
        AutoScribeOneShotCraftGameAction boundary,
        AutoScribeCycleAction action) =>
        boundary.Submit(in action);

    private static AutoScribeCycleAction AtLevel(
        in AutoScribeCycleAction action,
        int level) =>
        new(
            action.RecipeId,
            action.ScrollId,
            level,
            action.CollectedAtEpoch);

    private FixtureState Fixture(bool withTarget = true)
    {
        var recipeType = Register(
            new CraftingRecipeTypeSO
            {
                maxStartingLevel = 1,
                isLevelType = true,
            },
            _profile.RecipeType.Uuid);
        var recipeRegistry = Register(
            new CraftingRecipeListVariable { Maximum = 6 },
            _profile.RecipeRegistry.Uuid);
        var active = Register(
            new CraftingInstanceListVariable { Maximum = 4 },
            _profile.ActiveInstances.Uuid);
        var automatic = Register(
            new CraftingInstanceListVariable { Maximum = 4, isAutoList = true },
            _profile.AutomaticInstances.Uuid);

        CraftingRecipeSO? selectedRecipe = null;
        ConsumableSO? selectedScroll = null;
        ResourceSO? selectedResource = null;
        AutoScribeCycleAction selectedAction = default;
        for (var roleIndex = 0; roleIndex < _profile.Roles.Count; roleIndex++)
        {
            var role = _profile.Roles[roleIndex];
            var enchantment = Register(new EnchantmentSO(), role.Enchantment.Uuid);
            var scroll = Register(new ConsumableSO { visible = true }, role.Scroll.Uuid);
            var targeting = new Targeting.TargetStructure();
            if (withTarget || roleIndex != 0)
                targeting.Candidates.Add(new StructureSO());
            var onUse = new InstantEffectBlock();
            onUse.effectScripts.Add(new RequestTargetEffectScript
            {
                targetOptions = new Targeting.TargetSelectOptions { Targeting = targeting },
            });
            onUse.effectScripts.Add(new EnchantmentSO.EnchantItemScript
            {
                enchantment = enchantment,
            });
            scroll.onUseEffects.Add(onUse);
            if (!role.IsProducible) continue;

            var recipe = Register(new CraftingRecipeSO
            {
                visible = true,
                useQuantityAsLevel = true,
                MaximumAffordableLevel = 3,
                MainType = recipeType,
                InstantOutput = scroll,
            }, role.Recipe!.Value.Uuid);
            recipe.craftingTypes.Add(recipeType);
            var output = new InstantEffectBlock();
            output.effectScripts.Add(new ConsumableSO.ConsumableGainEffect
            {
                consumable = scroll,
            });
            recipe.completeEffects.Add(output);
            recipeRegistry.value.Add(recipe);
            CraftingRecipeSO.All.Add(recipe);
            if (roleIndex != 0) continue;

            var resource = new ResourceSO
            {
                quantity = new BigDouble(100, 0),
                quality = new ValueModifierRecord(new BigDouble(100, 0)),
            };
            recipe.TotalCost.costs.Add(new ResourceTuple(resource, new BigDouble(5, 0)));
            selectedRecipe = recipe;
            selectedScroll = scroll;
            selectedResource = resource;
            selectedAction = new AutoScribeCycleAction(
                recipe.GetGuid(),
                scroll.GetGuid(),
                level: 3,
                collectedAtEpoch: 1);
        }

        return new FixtureState(
            selectedRecipe!,
            selectedScroll!,
            selectedResource!,
            recipeType,
            active,
            automatic,
            selectedAction);
    }

    private T Register<T>(T value, Guid id) where T : IdScriptableObject
    {
        value.SetGuid(id);
        _registry.Add(id, value);
        IdScriptableObject.RuntimeLookup[id] = value;
        return value;
    }

    private static AutoScribeActionHealth Observe(in AutoScribeSubmission submission)
    {
        var health = new AutoScribeActionHealth();
        health.Observe(in submission);
        return health;
    }

    private readonly record struct FixtureState(
        CraftingRecipeSO Recipe,
        ConsumableSO Scroll,
        ResourceSO Resource,
        CraftingRecipeTypeSO RecipeType,
        CraftingInstanceListVariable Active,
        CraftingInstanceListVariable Automatic,
        AutoScribeCycleAction Action);
}
