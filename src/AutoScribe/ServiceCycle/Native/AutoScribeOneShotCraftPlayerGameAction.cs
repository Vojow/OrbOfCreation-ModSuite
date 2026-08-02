using System;
using System.Collections;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed partial class AutoScribeOneShotCraftGameAction
{
    internal CraftingPlayerSubmission Submit(in CraftingPlayerAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.WrongThread,
                "Crafting actions are bound to Unity thread " + _mainThreadId + ".");
        if (_playerBindings is not { } native)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.ContractUnavailable,
                _playerBindingFailure.Length == 0
                    ? "The lifecycle-scoped player crafting binding set is unavailable."
                    : _playerBindingFailure);

        long liveLifecycle;
        try
        {
            liveLifecycle = _readLifecycleEpoch();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.LifecycleReplaced,
                "The live lifecycle could not be read: " + ex.GetBaseException().Message);
        }
        if (liveLifecycle != action.LifecycleEpoch)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.LifecycleReplaced,
                "Action lifecycle " + action.LifecycleEpoch +
                " is stale; live lifecycle is " + liveLifecycle + ".");

        var resolution = _registry.Resolve(action.RecipeId, native.RecipeType);
        if (!resolution.IsResolved)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.RecipeUnavailable,
                resolution.Format());
        var recipe = resolution.Value!;
        try
        {
            if (!native.RecipeVisible(recipe))
                return CraftingPlayerSubmission.Reject(
                    in action,
                    CraftingPlayerPreflight.NotVisible,
                    "CraftingRecipeSO.IsVisible() refused the exact UUID-resolved recipe.");
            if (!TryFindPage(native, recipe, out var page, out var pageReason))
                return CraftingPlayerSubmission.Reject(
                    in action,
                    CraftingPlayerPreflight.PageRelationAmbiguous,
                    pageReason);
            return page is null
                ? SubmitDirect(in action, native, recipe)
                : SubmitQueued(in action, native, recipe, page);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.ContractUnavailable,
                "Crafting preflight failed before mutation: " + ex.GetBaseException().Message);
        }
    }

    private CraftingPlayerSubmission SubmitDirect(
        in CraftingPlayerAction action,
        CraftingPlayerNativeBindings native,
        object recipe)
    {
        if (native.RecipeTime(recipe) > 0d)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.PageRelationAmbiguous,
                "A timed recipe had no stable authored UICraftingPage relation; direct execution was refused.");
        var purchase = native.RecipePurchaseAmount(recipe, BigDouble.One);
        if (purchase <= BigDouble.Zero)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.InvalidPurchaseAmount,
                "CraftingRecipeSO.GetPurchaseQuantity(1) returned a non-positive amount.");
        if (!native.RecipeCanBuy(recipe))
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.Unaffordable,
                "CraftingRecipeSO.CanBuy() refused the exact direct-craft recipe.");
        var beforeState = new CraftingPlayerState(
            CraftingPlayerPipeline.Direct,
            purchase,
            BigDouble.Zero,
            0,
            0);
        if (!TryPlayerCraftingPermit(in action, out var permitFailure)) return permitFailure;

        try
        {
            native.RecipeExecute(recipe);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return PlayerCraftingFault(
                in action,
                CraftingPlayerPreflight.PostCommitFault,
                CraftingPlayerNativeStage.DirectExecute,
                NativeMutationOutcome.ExecutionThrew,
                1,
                in beforeState,
                in beforeState,
                "CraftingRecipeSO.Execute threw after the direct composite began: " +
                ex.GetBaseException().Message);
        }

        var evidence = new CraftingPlayerEvidence(true, in beforeState, in beforeState);
        return new CraftingPlayerSubmission(
            action.RecipeId,
            CraftingPlayerPreflight.Proceeded,
            CraftingPlayerNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1),
            in evidence,
            "The exact recipe completed its native direct Execute composite.");
    }

    private CraftingPlayerSubmission SubmitQueued(
        in CraftingPlayerAction action,
        CraftingPlayerNativeBindings native,
        object recipe,
        object page)
    {
        var queue = native.PageQueue(page);
        var mainType = native.PageMainType(page);
        if (!ReferenceEquals(mainType, native.RecipeMainType(recipe)))
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.PageRelationAmbiguous,
                "The authored page main type does not match CraftingRecipeSO.GetMainType().");
        var mode = native.PageCraftMode(page);
        if (mode is not 0 and not 1)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.PageRelationAmbiguous,
                "UICraftingPage craftMode was neither native stack mode 0 nor new-instance mode 1.");
        var previous = native.QueueQuantity(queue, recipe);
        var purchase = native.RecipePurchaseAmount(recipe, previous);
        if (purchase <= BigDouble.Zero)
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.InvalidPurchaseAmount,
                "CraftingRecipeSO.GetPurchaseQuantity(previous) returned a non-positive amount.");
        var requestedTotal = previous + (purchase < BigDouble.One ? BigDouble.One : purchase);
        if (!native.RecipeCanBuyAt(recipe, requestedTotal))
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.Unaffordable,
                "CraftingRecipeSO.CanBuyAt(previous + purchase) refused the exact queued recipe.");
        var totalCost = native.RecipeTotalCost(recipe, previous, purchase);
        if (!native.CostHasEnough(totalCost))
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.Unaffordable,
                "GetTotalCost(previous,purchase).HasEnough() refused the exact queued recipe.");

        var existing = mode == 0 ? FindInstance(native, queue, action.RecipeId) : null;
        if (existing is null && !native.QueueHasRoom(queue))
            return CraftingPlayerSubmission.Reject(
                in action,
                CraftingPlayerPreflight.QueueFull,
                "The authored manual crafting queue has no empty spot for a new instance.");
        var planned = existing is null
            ? CraftingPlayerPipeline.QueueNew
            : CraftingPlayerPipeline.QueueStack;
        var before = CapturePlayerState(native, queue, recipe, planned, purchase);
        if (!TryPlayerCraftingPermit(in action, out var permitFailure)) return permitFailure;

        var stage = CraftingPlayerNativeStage.Payment;
        var calls = 1;
        try
        {
            native.RecipePurchase(recipe, purchase, previous);
            if (existing is not null)
            {
                stage = CraftingPlayerNativeStage.Admission;
                calls = 2;
                native.InstanceAddQuantity(existing, purchase);
                return VerifyQueued(
                    in action,
                    native,
                    queue,
                    recipe,
                    existing,
                    planned,
                    purchase,
                    in before,
                    calls);
            }

            stage = CraftingPlayerNativeStage.Construction;
            calls = 2;
            var instance = native.ConstructInstance(recipe, purchase);
            calls = 3;
            if (native.InstanceIsInstant(instance))
            {
                stage = CraftingPlayerNativeStage.Admission;
                calls = 4;
                native.InstanceInstant(instance);
                var after = CapturePlayerState(
                    native,
                    queue,
                    recipe,
                    CraftingPlayerPipeline.QueueInstant,
                    purchase);
                var evidence = new CraftingPlayerEvidence(true, in before, in after);
                return new CraftingPlayerSubmission(
                    action.RecipeId,
                    CraftingPlayerPreflight.Proceeded,
                    CraftingPlayerNativeStage.Verification,
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(calls, 1, 1),
                    in evidence,
                    "The exact recipe completed through native instant admission.");
            }

            stage = CraftingPlayerNativeStage.Initiation;
            calls = 4;
            native.InstanceInitiate(instance);
            stage = CraftingPlayerNativeStage.Admission;
            calls = 5;
            native.QueueAdd(queue, instance);
            return VerifyQueued(
                in action,
                native,
                queue,
                recipe,
                instance,
                planned,
                purchase,
                in before,
                calls);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var after = CapturePlayerStateBestEffort(native, queue, recipe, planned, purchase, in before);
            if (OutcomeObserved(
                    native,
                    queue,
                    recipe,
                    action.RecipeId,
                    existing,
                    previous,
                    purchase))
            {
                var evidence = new CraftingPlayerEvidence(true, in before, in after);
                return new CraftingPlayerSubmission(
                    action.RecipeId,
                    CraftingPlayerPreflight.Proceeded,
                    CraftingPlayerNativeStage.Verification,
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(calls, 1, 1),
                    in evidence,
                    "The exact queued outcome was observed after native code threw: " +
                    ex.GetBaseException().Message);
            }
            return PlayerCraftingFault(
                in action,
                CraftingPlayerPreflight.PostCommitFault,
                stage,
                NativeMutationOutcome.ExecutionThrew,
                calls,
                in before,
                in after,
                "Queued crafting failed after payment began: " + ex.GetBaseException().Message);
        }
    }

    private CraftingPlayerSubmission VerifyQueued(
        in CraftingPlayerAction action,
        CraftingPlayerNativeBindings native,
        object queue,
        object recipe,
        object instance,
        CraftingPlayerPipeline pipeline,
        BigDouble purchase,
        in CraftingPlayerState before,
        int calls)
    {
        var after = CapturePlayerState(native, queue, recipe, pipeline, purchase);
        var expected = before.QueuedAmount + purchase;
        var verified = after.QueuedAmount == expected &&
            ContainsExactInstance(native, queue, instance, action.RecipeId);
        if (!verified)
            return PlayerCraftingFault(
                in action,
                CraftingPlayerPreflight.VerificationFailed,
                CraftingPlayerNativeStage.Verification,
                NativeMutationOutcome.PostconditionFailed,
                calls,
                in before,
                in after,
                "The exact recipe did not reach its requested queued amount on the authored queue.");
        var evidence = new CraftingPlayerEvidence(true, in before, in after);
        return new CraftingPlayerSubmission(
            action.RecipeId,
            CraftingPlayerPreflight.Proceeded,
            CraftingPlayerNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(calls, 1, 1),
            in evidence,
            "The exact recipe reached its requested native queue outcome.");
    }

    private static bool TryFindPage(
        CraftingPlayerNativeBindings native,
        object recipe,
        out object? page,
        out string reason)
    {
        page = null;
        var pages = native.Pages();
        for (var pageIndex = 0; pageIndex < pages.Length; pageIndex++)
        {
            var candidate = pages.GetValue(pageIndex);
            if (candidate is null || candidate.GetType() != native.PageType) continue;
            var recipes = native.PageRecipes(candidate);
            var contains = false;
            for (var recipeIndex = 0; recipeIndex < recipes.Count; recipeIndex++)
                if (ReferenceEquals(recipes[recipeIndex], recipe))
                {
                    contains = true;
                    break;
                }
            if (!contains) continue;
            if (page is not null)
            {
                reason = "The exact recipe appears on more than one authored UICraftingPage.";
                return false;
            }
            page = candidate;
        }
        reason = string.Empty;
        return true;
    }

    private static object? FindInstance(
        CraftingPlayerNativeBindings native,
        object queue,
        Guid recipeId)
    {
        var values = native.QueueValues(queue);
        for (var index = 0; index < values.Count; index++)
        {
            var candidate = values[index];
            if (candidate is not null && candidate.GetType() == native.InstanceType &&
                native.InstanceRecipe(candidate) == recipeId)
                return candidate;
        }
        return null;
    }

    private static bool ContainsExactInstance(
        CraftingPlayerNativeBindings native,
        object queue,
        object instance,
        Guid recipeId)
    {
        var values = native.QueueValues(queue);
        for (var index = 0; index < values.Count; index++)
            if (ReferenceEquals(values[index], instance) &&
                native.InstanceRecipe(instance) == recipeId)
                return true;
        return false;
    }

    private static bool OutcomeObserved(
        CraftingPlayerNativeBindings native,
        object queue,
        object recipe,
        Guid recipeId,
        object? existing,
        BigDouble previous,
        BigDouble purchase)
    {
        if (native.QueueQuantity(queue, recipe) != previous + purchase)
            return false;
        if (existing is not null)
            return ContainsExactInstance(native, queue, existing, recipeId);
        return FindInstance(native, queue, recipeId) is not null;
    }

    private static CraftingPlayerState CapturePlayerState(
        CraftingPlayerNativeBindings native,
        object queue,
        object recipe,
        CraftingPlayerPipeline pipeline,
        BigDouble purchase) =>
        new(
            pipeline,
            purchase,
            native.QueueQuantity(queue, recipe),
            CountNonNull(native.QueueValues(queue)),
            native.QueueMaximum(queue));

    private static CraftingPlayerState CapturePlayerStateBestEffort(
        CraftingPlayerNativeBindings native,
        object queue,
        object recipe,
        CraftingPlayerPipeline pipeline,
        BigDouble purchase,
        in CraftingPlayerState fallback)
    {
        try
        {
            return CapturePlayerState(native, queue, recipe, pipeline, purchase);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return fallback;
        }
    }

    private bool TryPlayerCraftingPermit(
        in CraftingPlayerAction action,
        out CraftingPlayerSubmission failure)
    {
        if (TryCaptureMutationPermit(out var reason))
        {
            failure = default;
            return true;
        }
        failure = CraftingPlayerSubmission.Reject(
            in action,
            CraftingPlayerPreflight.MutationPermitUnavailable,
            reason);
        return false;
    }

    private static CraftingPlayerSubmission PlayerCraftingFault(
        in CraftingPlayerAction action,
        CraftingPlayerPreflight preflight,
        CraftingPlayerNativeStage stage,
        NativeMutationOutcome outcome,
        int calls,
        in CraftingPlayerState before,
        in CraftingPlayerState after,
        string reason)
    {
        var exactReason = "One-shot crafting " + stage + " failed on " +
            EntityIdentityFormatter.Format(action.RecipeId) + ": " + reason;
        var evidence = new CraftingPlayerEvidence(true, in before, in after);
        return new CraftingPlayerSubmission(
            action.RecipeId,
            preflight,
            stage,
            outcome,
            new NativeMutationCallOutcome(calls, 1, 0),
            in evidence,
            exactReason);
    }
}
