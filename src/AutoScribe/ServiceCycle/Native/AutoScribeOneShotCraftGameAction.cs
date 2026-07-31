using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Doctrine-shaped re-drive of the native Scribe UI composite. All fallible suite-owned reads and
/// decisions finish before payment; after payment every stage is receipted and any ambiguity
/// quarantines this GameAction for the lifecycle.
/// </summary>
internal sealed class AutoScribeOneShotCraftGameAction : IDisposable
{
    private readonly TypedRegistryResolver _registry;
    private readonly AutoScribeIdentityProfile _profile;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<long> _readFrameIdentity;
    private readonly Action<long, long> _observeMutationAttempt;
    private AutoScribeNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;
    private string _quarantineReason = string.Empty;

    internal AutoScribeOneShotCraftGameAction(
        TypedRegistryResolver registry,
        AutoScribeIdentityProfile profile,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<long> readFrameIdentity,
        Action<long, long> observeMutationAttempt)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
        _readFrameIdentity = readFrameIdentity ??
            throw new ArgumentNullException(nameof(readFrameIdentity));
        _observeMutationAttempt = observeMutationAttempt ??
            throw new ArgumentNullException(nameof(observeMutationAttempt));
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;
    internal bool IsQuarantined => _quarantineReason.Length != 0;
    internal string QuarantineReason => _quarantineReason;

    internal AutoScribeSubmission Submit(in AutoScribeCycleAction action)
    {
        if (_quarantineReason.Length != 0)
            return AutoScribeSubmission.Reject(
                AutoScribePreflight.Quarantined,
                _quarantineReason);
        if (_bindings is not { } native)
            return AutoScribeSubmission.Reject(
                AutoScribePreflight.ContractUnavailable,
                _bindingFailure.Length == 0
                    ? "The lifecycle-scoped Auto Scribe binding set is unavailable."
                    : _bindingFailure);

        try
        {
            if (!TryResolveRelation(
                    in action,
                    native,
                    out var recipe,
                    out var scroll,
                    out var recipeType,
                    out var activeQueue,
                    out var reason,
                    out var rejection))
                return AutoScribeSubmission.Reject(rejection, reason);
            if (!Invoke<bool>(native.RecipeVisible, recipe))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.RecipeUnavailable,
                    $"CraftingRecipeSO.IsVisible() refused recipe {action.RecipeId:D}.");
            if (!Invoke<bool>(native.QueueHasRoom, activeQueue))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.QueueFull,
                    "ActiveScribeInstances.HasEmptySpot() refused before payment.");

            var craftLevel = FindHighestAffordableLevel(
                native,
                recipe,
                recipeType,
                action.Level);
            if (craftLevel < action.Level)
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.Unaffordable,
                    $"Recipe {action.RecipeId:D} could not afford requested level " +
                    $"{action.Level} or any stronger level.");
            if (HasCapacityReplacement(native, scroll, action.RecipeId, out reason))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.CompetingSupply,
                    reason);
            if (!TryValidateTarget(native, scroll, action.ScrollId, craftLevel, out reason))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.TargetUnavailable,
                    reason);

            var level = new BigDouble(craftLevel, 0);
            var zero = BigDouble.Zero;
            var totalCost = InvokeObject(native.RecipeTotalCost, recipe, zero, level);
            if (totalCost.GetType() != native.ResourceCostType)
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.ContractUnavailable,
                    "CraftingRecipeSO.GetTotalCost returned a non-ResourceCostList value.");
            if (!Invoke<bool>(native.CostHasEnough, totalCost))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.Unaffordable,
                    $"GetTotalCost(0,{craftLevel}).HasEnough() refused recipe {action.RecipeId:D}.");

            if (!TryCaptureBeforeCosts(
                    native,
                    totalCost,
                    out var costs,
                    out reason,
                    out rejection))
                return AutoScribeSubmission.Reject(rejection, reason);
            var before = CaptureBefore(
                native,
                recipeType,
                activeQueue,
                scroll,
                craftLevel,
                costs);
            if (!TryCaptureMutationPermit(out reason))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.MutationPermitUnavailable,
                    reason);

            // Every mutable native admission signal is repeated after the mutation permit and
            // immediately before payment. The world snapshot selects work; these live reads retain
            // native authority over capacity, targets, queue room, and affordability.
            if (!Invoke<bool>(native.RecipeVisible, recipe))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.RecipeUnavailable,
                    $"CraftingRecipeSO.IsVisible() refused recipe {action.RecipeId:D} immediately before payment.");
            if (!Invoke<bool>(native.QueueHasRoom, activeQueue))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.QueueFull,
                    "ActiveScribeInstances.HasEmptySpot() refused immediately before payment.");
            if (!CanBuyAt(native, recipe, craftLevel))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.Unaffordable,
                    $"CraftingRecipeSO.CanBuyAt({craftLevel}) refused immediately before payment.");
            if (HasCapacityReplacement(native, scroll, action.RecipeId, out reason))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.CompetingSupply,
                    reason);
            if (!TryValidateTarget(native, scroll, action.ScrollId, craftLevel, out reason))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.TargetUnavailable,
                    reason);

            totalCost = InvokeObject(native.RecipeTotalCost, recipe, zero, level);
            if (totalCost.GetType() != native.ResourceCostType)
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.ContractUnavailable,
                    "CraftingRecipeSO.GetTotalCost returned a non-ResourceCostList value immediately before payment.");
            if (!Invoke<bool>(native.CostHasEnough, totalCost))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.Unaffordable,
                    $"GetTotalCost(0,{craftLevel}).HasEnough() refused immediately before payment.");
            if (!TryCaptureBeforeCosts(
                    native,
                    totalCost,
                    out costs,
                    out reason,
                    out rejection))
                return AutoScribeSubmission.Reject(rejection, reason);
            before = CaptureBefore(
                native,
                recipeType,
                activeQueue,
                scroll,
                craftLevel,
                costs);

            if (!TryReadMutationFrame(out var mutationFrame, out reason))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.ContractUnavailable,
                    reason);

            // Payment is deliberately the final pre-native risk. From here onward no metadata is
            // discovered and no policy/configuration decision is made.
            return Execute(
                in action,
                native,
                recipe,
                recipeType,
                activeQueue,
                scroll,
                totalCost,
                craftLevel,
                level,
                mutationFrame,
                in before);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return AutoScribeSubmission.Reject(
                AutoScribePreflight.ContractUnavailable,
                "Auto Scribe preflight failed before payment: " +
                ex.GetBaseException().Message);
        }
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        _quarantineReason = string.Empty;
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        _quarantineReason = string.Empty;
    }

    private AutoScribeSubmission Execute(
        in AutoScribeCycleAction action,
        AutoScribeNativeBindings native,
        object recipe,
        object recipeType,
        object activeQueue,
        object scroll,
        object totalCost,
        int craftLevel,
        BigDouble level,
        long mutationFrame,
        in BeforeState before)
    {
        var stage = AutoScribeNativeStage.Payment;
        var nativeCalls = 1;
        var paymentInvoked = false;
        try
        {
            paymentInvoked = true;
            try
            {
                native.RecipePurchase.Invoke(recipe, new object[] { level, BigDouble.Zero });
            }
            finally
            {
                // Payment is Auto Scribe's first irreversible native call. The gap opens for both
                // success and throw paths before any later construction/admission work continues.
                _observeMutationAttempt(action.CollectedAtEpoch, mutationFrame);
            }

            stage = AutoScribeNativeStage.Construction;
            nativeCalls = 2;
            var instance = native.ConstructInstance.Invoke(new object[] { recipe, level }) ??
                throw new InvalidOperationException("CraftingInstance construction returned null.");
            if (instance.GetType() != native.InstanceType)
                throw new InvalidOperationException(
                    "CraftingInstance construction returned the wrong native type.");

            stage = AutoScribeNativeStage.Initiation;
            nativeCalls = 3;
            native.InstanceInitiate.Invoke(instance, Array.Empty<object>());

            stage = AutoScribeNativeStage.Admission;
            var instant = Invoke<bool>(native.InstanceInstantCheck, instance);
            nativeCalls = 4;
            if (instant)
                native.InstanceInstant.Invoke(instance, Array.Empty<object>());
            else
                native.QueueAdd.Invoke(activeQueue, new[] { instance });

            stage = AutoScribeNativeStage.Verification;
            var receipt = CaptureReceipt(
                native,
                recipeType,
                activeQueue,
                scroll,
                action.RecipeId,
                craftLevel,
                totalCost,
                in before,
                paymentInvoked,
                out var costMismatch);
            var verified =
                receipt.CostMatched &&
                receipt.CeilingTransitionObserved &&
                (receipt.AdmittedToQueue ^ receipt.AdmittedToInstantStock);
            if (!verified)
                return Quarantine(
                    in action,
                    AutoScribePreflight.VerificationFailed,
                    stage,
                    NativeMutationOutcome.PostconditionFailed,
                    nativeCalls,
                    in receipt,
                    "Auto Scribe verification failed: " +
                    Describe(in receipt, in costMismatch));
            return new AutoScribeSubmission(
                AutoScribePreflight.Proceeded,
                stage,
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(nativeCalls, 1, 1),
                in receipt,
                "Verified exact Scribe payment, ceiling transition, and one native admission.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var receipt = CaptureReceiptBestEffort(
                native,
                recipeType,
                activeQueue,
                scroll,
                action.RecipeId,
                craftLevel,
                totalCost,
                in before,
                paymentInvoked,
                out var costMismatch);
            return Quarantine(
                in action,
                AutoScribePreflight.PostPaymentFault,
                stage,
                NativeMutationOutcome.ExecutionThrew,
                nativeCalls,
                in receipt,
                $"Auto Scribe native {stage} failed after payment began: " +
                ex.GetBaseException().Message + "; " +
                Describe(in receipt, in costMismatch));
        }
    }

    private bool TryReadMutationFrame(out long frame, out string reason)
    {
        try
        {
            frame = _readFrameIdentity();
            if (frame >= 0)
            {
                reason = string.Empty;
                return true;
            }
            reason = "The shared frame identity was negative immediately before Scribe payment.";
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            frame = 0;
            reason =
                "The shared frame identity was unavailable immediately before Scribe payment: " +
                ex.GetBaseException().Message;
        }
        return false;
    }

    private AutoScribeSubmission Quarantine(
        in AutoScribeCycleAction action,
        AutoScribePreflight preflight,
        AutoScribeNativeStage stage,
        NativeMutationOutcome outcome,
        int nativeCalls,
        in AutoScribeMutationReceipt receipt,
        string reason)
    {
        _quarantineReason =
            $"Auto Scribe is quarantined for this lifecycle after {stage} on " +
            $"{action.RecipeId:D}: {reason}";
        return new AutoScribeSubmission(
            preflight,
            stage,
            outcome,
            new NativeMutationCallOutcome(nativeCalls, 1, 0),
            in receipt,
            _quarantineReason);
    }

    private bool TryResolveRelation(
        in AutoScribeCycleAction action,
        AutoScribeNativeBindings native,
        out object recipe,
        out object scroll,
        out object recipeType,
        out object activeQueue,
        out string reason,
        out AutoScribePreflight rejection)
    {
        recipe = null!;
        scroll = null!;
        recipeType = null!;
        activeQueue = null!;
        if (!_profile.TryFindByRecipe(action.RecipeId, out var recipeRole) ||
            !_profile.TryFindByScroll(action.ScrollId, out var scrollRole) ||
            recipeRole.Ordinal != scrollRole.Ordinal ||
            recipeRole.Recipe?.Uuid != action.RecipeId ||
            recipeRole.Scroll.Uuid != action.ScrollId)
        {
            reason =
                $"Action recipe {action.RecipeId:D} and Scroll {action.ScrollId:D} do not identify " +
                "one audited Auto Scribe role.";
            rejection = AutoScribePreflight.RelationshipMismatch;
            return false;
        }

        var recipeResolution = _registry.Resolve(action.RecipeId, native.RecipeType);
        var scrollResolution = _registry.Resolve(action.ScrollId, native.ConsumableType);
        var enchantmentResolution = _registry.Resolve(
            recipeRole.Enchantment.Uuid,
            native.EnchantmentType);
        var typeResolution = _registry.Resolve(_profile.RecipeType.Uuid, native.RecipeTypeType);
        var registryResolution = _registry.Resolve(
            _profile.RecipeRegistry.Uuid,
            native.RecipeListType);
        var activeResolution = _registry.Resolve(
            _profile.ActiveInstances.Uuid,
            native.InstanceListType);
        var automaticResolution = _registry.Resolve(
            _profile.AutomaticInstances.Uuid,
            native.InstanceListType);
        if (!recipeResolution.IsResolved || !scrollResolution.IsResolved ||
            !enchantmentResolution.IsResolved || !typeResolution.IsResolved ||
            !registryResolution.IsResolved || !activeResolution.IsResolved ||
            !automaticResolution.IsResolved)
        {
            reason = FirstFailure(
                recipeResolution,
                scrollResolution,
                enchantmentResolution,
                typeResolution,
                registryResolution,
                activeResolution,
                automaticResolution);
            rejection = AutoScribePreflight.IdentityUnavailable;
            return false;
        }

        recipe = recipeResolution.Value!;
        scroll = scrollResolution.Value!;
        recipeType = typeResolution.Value!;
        activeQueue = activeResolution.Value!;
        if (Invoke<Guid>(native.Identity, recipe) != action.RecipeId ||
            Invoke<Guid>(native.ConsumableIdentity, scroll) != action.ScrollId ||
            Invoke<Guid>(native.EnchantmentIdentity, enchantmentResolution.Value!) !=
                recipeRole.Enchantment.Uuid)
        {
            reason =
                "The live recipe, Scroll, or enchantment identity did not equal the action relation.";
            rejection = AutoScribePreflight.RelationshipMismatch;
            return false;
        }
        if (!ValidateRecipeRegistry(native, registryResolution.Value!, out reason) ||
            !ValidateRecipeRelation(
                native,
                recipe,
                recipeType,
                scroll,
                recipeRole.Enchantment.Uuid,
                out reason))
        {
            rejection = AutoScribePreflight.RelationshipMismatch;
            return false;
        }

        reason = string.Empty;
        rejection = AutoScribePreflight.Proceeded;
        return true;
    }

    private bool ValidateRecipeRegistry(
        AutoScribeNativeBindings native,
        object registry,
        out string reason)
    {
        if (native.RecipeListValue.GetValue(registry) is not IList values)
        {
            reason = "ScribeCraftingRecipes.value was not a live native list.";
            return false;
        }
        var expected = 0;
        for (var roleIndex = 0; roleIndex < _profile.Roles.Count; roleIndex++)
            if (_profile.Roles[roleIndex].IsProducible) expected++;
        if (values.Count != expected)
        {
            reason =
                $"ScribeCraftingRecipes contained {values.Count} recipes; exactly {expected} are audited.";
            return false;
        }
        for (var roleIndex = 0; roleIndex < _profile.Roles.Count; roleIndex++)
        {
            var role = _profile.Roles[roleIndex];
            if (!role.Recipe.HasValue) continue;
            var found = false;
            foreach (var value in values)
            {
                if (value is null || value.GetType() != native.RecipeType)
                {
                    reason = "ScribeCraftingRecipes contained a non-CraftingRecipeSO value.";
                    return false;
                }
                if (Invoke<Guid>(native.Identity, value) == role.Recipe.Value.Uuid)
                    found = true;
            }
            if (!found)
            {
                reason =
                    $"ScribeCraftingRecipes omitted audited recipe {role.Recipe.Value.Uuid:D}.";
                return false;
            }
        }
        reason = string.Empty;
        return true;
    }

    private static bool ValidateRecipeRelation(
        AutoScribeNativeBindings native,
        object recipe,
        object recipeType,
        object scroll,
        Guid expectedEnchantment,
        out string reason)
    {
        if (native.CraftingTypes.GetValue(recipe) is not IEnumerable types)
        {
            reason = "CraftingRecipeSO.craftingTypes was unavailable.";
            return false;
        }
        var typeCount = 0;
        foreach (var value in types)
        {
            if (!ReferenceEquals(value, recipeType))
            {
                reason = "The live recipe referenced a non-Scribe crafting type.";
                return false;
            }
            typeCount++;
        }
        if (typeCount != 1 ||
            native.UseQuantityAsLevel.GetValue(recipe) is not true ||
            native.IsLevelType.GetValue(recipeType) is not true ||
            !ReferenceEquals(InvokeObject(native.RecipeMainType, recipe), recipeType))
        {
            reason =
                "The live recipe did not prove exactly one levelled Scribe crafting-type relation.";
            return false;
        }

        var outputs = 0;
        foreach (var blockValue in RequireEnumerable(
                     native.CompleteEffects.GetValue(recipe),
                     "CraftingRecipeSO.completeEffects"))
        {
            if (blockValue is null || blockValue.GetType() != native.InstantBlockType ||
                native.EffectScripts.GetValue(blockValue) is not IEnumerable scripts)
            {
                reason = "The live recipe complete-effect graph changed type.";
                return false;
            }
            foreach (var script in scripts)
            {
                if (script is null || !native.InstantScriptType.IsInstanceOfType(script))
                {
                    reason = "The live recipe output graph contained an unknown script.";
                    return false;
                }
                if (script.GetType() != native.GainType) continue;
                outputs++;
                if (!ReferenceEquals(native.GainConsumable.GetValue(script), scroll))
                {
                    reason = "The live Scribe recipe output was not the action Scroll.";
                    return false;
                }
            }
        }
        if (outputs != 1)
        {
            reason =
                $"The live Scribe recipe had {outputs} ConsumableGainEffect outputs; exactly one is required.";
            return false;
        }

        var requestCount = 0;
        var enchantCount = 0;
        foreach (var blockValue in RequireEnumerable(
                     native.OnUseEffects.GetValue(scroll),
                     "ConsumableSO.onUseEffects"))
        {
            if (blockValue is null || blockValue.GetType() != native.InstantBlockType ||
                native.EffectScripts.GetValue(blockValue) is not IEnumerable scripts)
            {
                reason = "The live Scroll on-use graph changed type.";
                return false;
            }
            foreach (var script in scripts)
            {
                if (script is null || !native.InstantScriptType.IsInstanceOfType(script))
                {
                    reason = "The live Scroll on-use graph contained an unknown script.";
                    return false;
                }
                if (script.GetType() == native.RequestType) requestCount++;
                if (script.GetType() != native.EnchantScriptType) continue;
                enchantCount++;
                var enchantment = native.EnchantScriptEnchantment.GetValue(script);
                if (enchantment is null || enchantment.GetType() != native.EnchantmentType ||
                    Invoke<Guid>(native.EnchantmentIdentity, enchantment) != expectedEnchantment)
                {
                    reason =
                        $"The live Scroll enchantment did not equal audited {expectedEnchantment:D}.";
                    return false;
                }
            }
        }
        if (requestCount != 1 || enchantCount != 1)
        {
            reason =
                $"The live Scroll had {requestCount} target requests and {enchantCount} enchant effects; " +
                "exactly one of each is required.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private bool HasCapacityReplacement(
        AutoScribeNativeBindings native,
        object scroll,
        Guid recipeId,
        out string reason)
    {
        var queued = Invoke<int>(native.ConsumableQueued, scroll);
        if (queued > 0)
        {
            reason = $"The live Scroll already has {queued} queued consumable production.";
            return true;
        }
        var prep = Require<BigDouble>(
            native.ConsumableCurrentPrepTime.GetValue(scroll),
            "ConsumableSO.currentPrepTime");
        if (prep.CompareTo(BigDouble.Zero) > 0)
        {
            reason = $"The live Scroll has active preparation time {prep}.";
            return true;
        }
        foreach (var value in RequireEnumerable(
                     native.ConsumableUsages.GetValue(scroll),
                     "ConsumableSO.consumableUsages"))
        {
            if (value is null || value.GetType() != native.ConsumableUsageType)
            {
                reason = "ConsumableSO.consumableUsages contained a non-ConsumableUsage value.";
                return true;
            }
            var engaged = Require<bool>(
                native.UsageEngaged.GetValue(value),
                "ConsumableUsage.en");
            var remaining = Require<BigDouble>(
                native.UsageRemainingDuration.GetValue(value),
                "ConsumableUsage.dr");
            if (!engaged || remaining.CompareTo(BigDouble.Zero) > 0)
            {
                reason = !engaged
                    ? "The live Scroll has a pending consumable usage."
                    : $"The live Scroll has an active consumable usage with {remaining} duration remaining.";
                return true;
            }
        }

        foreach (var queueIdentity in new[]
        {
            _profile.ActiveInstances,
            _profile.AutomaticInstances,
        })
        {
            var resolution = _registry.Resolve(queueIdentity.Uuid, native.InstanceListType);
            if (!resolution.IsResolved)
            {
                reason = resolution.Format();
                return true;
            }
            if (native.InstanceListValue.GetValue(resolution.Value!) is not IList work)
            {
                reason = $"{queueIdentity.Uuid:D} did not expose its exact CraftingInstance list.";
                return true;
            }
            foreach (var value in work)
            {
                if (value is null) continue;
                if (value.GetType() != native.InstanceType)
                {
                    reason = $"{queueIdentity.Uuid:D} contained a non-CraftingInstance value.";
                    return true;
                }
                if (Invoke<Guid>(native.InstanceRecipe, value) == recipeId &&
                    !Invoke<bool>(native.InstanceExpired, value))
                {
                    var level = Level(InvokeObject(native.InstanceQuantity, value));
                    reason =
                        $"{queueIdentity.Uuid:D} already supplies recipe {recipeId:D} at level {level}.";
                    return true;
                }
            }
        }
        reason = string.Empty;
        return false;
    }

    private static bool TryValidateTarget(
        AutoScribeNativeBindings native,
        object scroll,
        Guid scrollId,
        int level,
        out string reason)
    {
        object? options = null;
        var requests = 0;
        foreach (var blockValue in RequireEnumerable(
                     native.OnUseEffects.GetValue(scroll),
                     "ConsumableSO.onUseEffects"))
        {
            if (blockValue is null || blockValue.GetType() != native.InstantBlockType ||
                native.EffectScripts.GetValue(blockValue) is not IEnumerable scripts)
            {
                reason = "The live Scroll target graph changed type.";
                return false;
            }
            foreach (var script in scripts)
            {
                if (script?.GetType() != native.RequestType) continue;
                requests++;
                options = native.TargetOptions.GetValue(script);
            }
        }
        if (requests != 1 || options is null || options.GetType() != native.OptionsType)
        {
            reason =
                $"Scroll {scrollId:D} did not expose exactly one exact TargetSelectOptions.";
            return false;
        }
        var targeting = InvokeObject(native.GetTargeting, options);
        if (targeting.GetType() != native.TargetType)
        {
            reason = $"Scroll {scrollId:D} did not resolve the exact TargetStructure selector.";
            return false;
        }
        var scaling = InvokeObject(
            native.ScalingBasic,
            target: null,
            new BigDouble(level, 0));
        if (scaling.GetType() != native.ScalingType ||
            native.GetRandomList.Invoke(targeting, new[] { scaling }) is not ICollection candidates)
        {
            reason = $"Scroll {scrollId:D} target selection changed contract.";
            return false;
        }
        if (candidates.Count == 0)
        {
            reason = $"Scroll {scrollId:D} has no valid live target at level {level}.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static int FindHighestAffordableLevel(
        AutoScribeNativeBindings native,
        object recipe,
        object recipeType,
        int minimumLevel)
    {
        if (minimumLevel <= 0 ||
            !CanBuyAt(native, recipe, minimumLevel))
            return 0;

        var sharedMaximum = Math.Max(
            1,
            Require<int>(
                native.MaxStartingLevel.GetValue(recipeType),
                "CraftingRecipeTypeSO.maxStartingLevel"));
        var affordable = minimumLevel;
        if (sharedMaximum > affordable)
        {
            if (!CanBuyAt(native, recipe, sharedMaximum))
                return FindHighestAffordableBetween(
                    native,
                    recipe,
                    affordable,
                    sharedMaximum - 1);
            affordable = sharedMaximum;
        }
        if (affordable == int.MaxValue)
            return affordable;

        // Audited levelled Scribe costs grow monotonically. Bracket the first unaffordable level,
        // then refine the frontier without an unbounded linear scan.
        while (affordable < int.MaxValue)
        {
            var candidate = affordable > int.MaxValue / 2
                ? int.MaxValue
                : Math.Max(affordable + 1, affordable * 2);
            if (!CanBuyAt(native, recipe, candidate))
                return FindHighestAffordableBetween(
                    native,
                    recipe,
                    affordable,
                    candidate - 1);
            affordable = candidate;
        }
        return affordable;
    }

    private static int FindHighestAffordableBetween(
        AutoScribeNativeBindings native,
        object recipe,
        int affordable,
        int maximum)
    {
        var low = affordable + 1;
        var high = maximum;
        var found = affordable;
        while (low <= high)
        {
            var candidate = low + ((high - low) / 2);
            if (CanBuyAt(native, recipe, candidate))
            {
                found = candidate;
                low = candidate + 1;
            }
            else
            {
                high = candidate - 1;
            }
        }
        return found;
    }

    private static bool CanBuyAt(
        AutoScribeNativeBindings native,
        object recipe,
        int level) =>
        Invoke<bool>(
            native.RecipeCanBuyAt,
            recipe,
            new BigDouble(level, 0));

    private BeforeState CaptureBefore(
        AutoScribeNativeBindings native,
        object recipeType,
        object activeQueue,
        object scroll,
        int level,
        CostState[] costs)
    {
        return new BeforeState(
            Require<int>(
                native.MaxStartingLevel.GetValue(recipeType),
                "CraftingRecipeTypeSO.maxStartingLevel"),
            CountNonNull(RequireList(
                native.InstanceListValue.GetValue(activeQueue),
                "ActiveScribeInstances.value")),
            StockAt(native, scroll, level),
            costs);
    }

    private static AutoScribeMutationReceipt CaptureReceipt(
        AutoScribeNativeBindings native,
        object recipeType,
        object activeQueue,
        object scroll,
        Guid recipeId,
        int level,
        object totalCost,
        in BeforeState before,
        bool paymentInvoked,
        out CostMismatch costMismatch)
    {
        var afterQueue = RequireList(
            native.InstanceListValue.GetValue(activeQueue),
            "ActiveScribeInstances.value");
        var queueCount = CountNonNull(afterQueue);
        var stock = StockAt(native, scroll, level);
        var costs = CaptureObservedCosts(native, totalCost);
        var costMatched = CostsMatch(before.Costs, costs, out costMismatch);
        var resourcesCharged = AnyCharge(before.Costs, costs);
        var ceiling = Require<int>(
            native.MaxStartingLevel.GetValue(recipeType),
            "CraftingRecipeTypeSO.maxStartingLevel");
        var expectedCeiling = Math.Max(before.MaximumStartingLevel, level);
        var queueMatch = queueCount == before.QueueCount + 1 &&
            ContainsWork(native, afterQueue, recipeId, level);
        var stockMatch = stock == before.StockAtLevel + 1 &&
            queueCount == before.QueueCount;
        return new AutoScribeMutationReceipt(
            evidenceAvailable: true,
            paymentInvoked,
            resourcesCharged,
            costMatched,
            ceiling == expectedCeiling,
            queueMatch,
            stockMatch,
            queueCount - before.QueueCount,
            stock - before.StockAtLevel);
    }

    private static AutoScribeMutationReceipt CaptureReceiptBestEffort(
        AutoScribeNativeBindings native,
        object recipeType,
        object activeQueue,
        object scroll,
        Guid recipeId,
        int level,
        object totalCost,
        in BeforeState before,
        bool paymentInvoked,
        out CostMismatch costMismatch)
    {
        try
        {
            return CaptureReceipt(
                native,
                recipeType,
                activeQueue,
                scroll,
                recipeId,
                level,
                totalCost,
                in before,
                paymentInvoked,
                out costMismatch);
        }
        catch (Exception) when (paymentInvoked)
        {
            costMismatch = CostMismatch.EvidenceUnavailable;
            return new AutoScribeMutationReceipt(
                evidenceAvailable: false,
                paymentInvoked,
                resourcesCharged: false,
                costMatched: false,
                ceilingTransitionObserved: false,
                admittedToQueue: false,
                admittedToInstantStock: false,
                queueDelta: int.MinValue,
                stockDelta: int.MinValue);
        }
    }

    private static bool TryCaptureBeforeCosts(
        AutoScribeNativeBindings native,
        object totalCost,
        out CostState[] costs,
        out string reason,
        out AutoScribePreflight rejection)
    {
        var values = RequireList(native.Costs.GetValue(totalCost), "ResourceCostList.costs");
        var result = new CostState[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var tuple = values[index];
            if (tuple is null || tuple.GetType() != native.ResourceTupleType)
                throw new InvalidOperationException(
                    "ResourceCostList.costs contained a non-ResourceTuple value.");
            var resource = native.TupleResource.GetValue(tuple);
            if (resource is null || resource.GetType() != native.ResourceType)
                throw new InvalidOperationException("ResourceTuple.resource changed type.");
            var resourceId = Invoke<Guid>(native.ResourceIdentity, resource);
            var expected = Require<BigDouble>(
                native.TupleValue.Invoke(tuple, Array.Empty<object>()),
                "ResourceTuple.GetValue");
            if (expected.CompareTo(BigDouble.Zero) < 0)
                throw new InvalidOperationException(
                    $"ResourceTuple.GetValue returned a negative cost for {resourceId:D}.");
            if (Invoke<bool>(native.ResourceIsBandwidth, resource))
            {
                costs = Array.Empty<CostState>();
                reason =
                    $"Recipe cost resource {resourceId:D} is a bandwidth resource and cannot " +
                    "provide an exact debit receipt.";
                rejection = AutoScribePreflight.ContractUnavailable;
                return false;
            }
            var quantity = Invoke<BigDouble>(native.ResourceRawQuantity, resource);
            var rawSpend = Invoke<BigDouble>(native.ResourceTrueSpend, resource, expected);
            if (rawSpend.CompareTo(BigDouble.Zero) < 0)
                throw new InvalidOperationException(
                    $"ResourceSO.GetTrueSpend returned a negative debit for {resourceId:D}.");
            var decayApplied = Invoke<bool>(native.ResourceHasDecay, resource);
            var decayPercent = decayApplied
                ? Invoke<BigDouble>(native.ResourceDecayPercent, resource)
                : BigDouble.Zero;
            if (decayPercent.CompareTo(BigDouble.Zero) < 0 ||
                decayPercent.CompareTo(BigDouble.One) > 0)
                throw new InvalidOperationException(
                    $"ResourceSO.GetDecayPercent returned an invalid value for {resourceId:D}.");
            var expectedRawDebit = rawSpend * (BigDouble.One - decayPercent);
            for (var existing = 0; existing < index; existing++)
            {
                if (result[existing].ResourceId != resourceId) continue;
                if (!ReferenceEquals(result[existing].Resource, resource) ||
                    result[existing].DecayApplied != decayApplied ||
                    result[existing].DecayPercent.CompareTo(decayPercent) != 0 ||
                    result[existing].RawQuantity.CompareTo(quantity) != 0)
                    throw new InvalidOperationException(
                        $"Duplicate recipe cost rows disagreed for resource {resourceId:D}.");
            }
            result[index] = new CostState(
                resourceId,
                resource,
                expected,
                expectedRawDebit,
                quantity,
                decayApplied,
                decayPercent);
        }
        for (var index = 0; index < result.Length; index++)
        {
            if (HasEarlierResource(result, index)) continue;
            var aggregateDebit = BigDouble.Zero;
            for (var row = index; row < result.Length; row++)
                if (result[row].ResourceId == result[index].ResourceId)
                    aggregateDebit += result[row].ExpectedRawDebit;
            if (result[index].RawQuantity.CompareTo(aggregateDebit) >= 0)
                continue;
            costs = Array.Empty<CostState>();
            reason =
                $"Aggregated native debit for cost resource {result[index].ResourceId:D} " +
                $"requires {aggregateDebit} raw units but only " +
                $"{result[index].RawQuantity} are available.";
            rejection = AutoScribePreflight.Unaffordable;
            return false;
        }
        costs = result;
        reason = string.Empty;
        rejection = AutoScribePreflight.Proceeded;
        return true;
    }

    private static CostState[] CaptureObservedCosts(
        AutoScribeNativeBindings native,
        object totalCost)
    {
        var values = RequireList(native.Costs.GetValue(totalCost), "ResourceCostList.costs");
        var result = new CostState[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var tuple = values[index];
            if (tuple is null || tuple.GetType() != native.ResourceTupleType)
                throw new InvalidOperationException(
                    "ResourceCostList.costs contained a non-ResourceTuple value.");
            var resource = native.TupleResource.GetValue(tuple);
            if (resource is null || resource.GetType() != native.ResourceType)
                throw new InvalidOperationException("ResourceTuple.resource changed type.");
            var resourceId = Invoke<Guid>(native.ResourceIdentity, resource);
            var expected = Require<BigDouble>(
                native.TupleValue.Invoke(tuple, Array.Empty<object>()),
                "ResourceTuple.GetValue");
            var quantity = Invoke<BigDouble>(native.ResourceRawQuantity, resource);
            for (var existing = 0; existing < index; existing++)
            {
                if (result[existing].ResourceId != resourceId) continue;
                if (!ReferenceEquals(result[existing].Resource, resource) ||
                    result[existing].RawQuantity.CompareTo(quantity) != 0)
                    throw new InvalidOperationException(
                        $"Duplicate recipe cost rows disagreed for resource {resourceId:D}.");
            }
            result[index] = new CostState(
                resourceId,
                resource,
                expected,
                BigDouble.Zero,
                quantity,
                decayApplied: false,
                BigDouble.Zero);
        }
        return result;
    }

    private static bool CostsMatch(
        CostState[] before,
        CostState[] after,
        out CostMismatch mismatch)
    {
        if (before.Length != after.Length)
        {
            mismatch = CostMismatch.ShapeChanged;
            return false;
        }
        for (var index = 0; index < before.Length; index++)
        {
            if (before[index].ResourceId != after[index].ResourceId ||
                !ReferenceEquals(before[index].Resource, after[index].Resource))
            {
                mismatch = CostMismatch.ShapeChanged;
                return false;
            }
            if (before[index].Expected.CompareTo(after[index].Expected) != 0)
            {
                mismatch = CostMismatch.NominalChanged(
                    in before[index],
                    after[index].RawQuantity);
                return false;
            }
        }

        var matchedKind = CostMismatchKind.None;
        for (var index = 0; index < before.Length; index++)
        {
            if (HasEarlierResource(before, index)) continue;
            var expectedAfter = before[index].RawQuantity;
            var aggregateNominal = BigDouble.Zero;
            var aggregateDebit = BigDouble.Zero;
            for (var row = 0; row < before.Length; row++)
            {
                if (before[row].ResourceId != before[index].ResourceId) continue;
                aggregateNominal += before[row].Expected;
                aggregateDebit += before[row].ExpectedRawDebit;
                // ResourceCostList.PerformCost invokes ResourceSO.Spend once per authored row.
                // BigDouble subtraction is rounded and non-associative, so reproduce that exact
                // row order instead of subtracting one mathematically aggregated debit.
                expectedAfter = BigDouble.Max(
                    expectedAfter - before[row].ExpectedRawDebit,
                    BigDouble.Zero);
            }
            var observedAfter = after[index].RawQuantity;
            if (observedAfter.CompareTo(expectedAfter) != 0)
            {
                mismatch = CostMismatch.DebitChanged(
                    in before[index],
                    aggregateNominal,
                    aggregateDebit,
                    expectedAfter,
                    observedAfter);
                return false;
            }
            for (var row = index + 1; row < after.Length; row++)
                if (after[row].ResourceId == before[index].ResourceId &&
                    after[row].RawQuantity.CompareTo(observedAfter) != 0)
                {
                    mismatch = CostMismatch.ShapeChanged;
                    return false;
                }

            var resourceKind = expectedAfter.CompareTo(before[index].RawQuantity) == 0
                ? CostMismatchKind.MatchedBelowResolution
                : expectedAfter.CompareTo(BigDouble.Zero) == 0
                    ? CostMismatchKind.MatchedClampedToZero
                    : CostMismatchKind.MatchedRepresentable;
            matchedKind = MergeMatchedKind(matchedKind, resourceKind);
        }
        mismatch = CostMismatch.Matched(matchedKind);
        return true;
    }

    private static bool HasEarlierResource(CostState[] rows, int index)
    {
        for (var earlier = 0; earlier < index; earlier++)
            if (rows[earlier].ResourceId == rows[index].ResourceId)
                return true;
        return false;
    }

    private static CostMismatchKind MergeMatchedKind(
        CostMismatchKind current,
        CostMismatchKind next)
    {
        if (current == CostMismatchKind.None) return next;
        if (current == next) return current;
        return CostMismatchKind.MatchedMixed;
    }

    private static bool AnyCharge(CostState[] before, CostState[] after)
    {
        if (before.Length != after.Length) return false;
        for (var index = 0; index < before.Length; index++)
        {
            if (HasEarlierResource(before, index)) continue;
            if ((before[index].RawQuantity - after[index].RawQuantity)
                    .CompareTo(BigDouble.Zero) > 0)
                return true;
        }
        return before.Length == 0;
    }

    private static bool ContainsWork(
        AutoScribeNativeBindings native,
        IList work,
        Guid recipeId,
        int level)
    {
        foreach (var value in work)
        {
            if (value is null || value.GetType() != native.InstanceType) continue;
            if (Invoke<Guid>(native.InstanceRecipe, value) == recipeId &&
                Level(InvokeObject(native.InstanceQuantity, value)) == level &&
                !Invoke<bool>(native.InstanceExpired, value))
                return true;
        }
        return false;
    }

    private static int StockAt(
        AutoScribeNativeBindings native,
        object scroll,
        int level)
    {
        var total = 0;
        foreach (var value in RequireEnumerable(
                     native.ConsumableCounts.GetValue(scroll),
                     "ConsumableSO.consumableCounts"))
        {
            if (value is null || value.GetType() != native.ConsumableCountType)
                throw new InvalidOperationException(
                    "ConsumableSO.consumableCounts contained the wrong native type.");
            if (Invoke<int>(native.CountLevel, value) == level)
                total = checked(total + Invoke<int>(native.CountQuantity, value));
        }
        return total;
    }

    private bool TryCaptureMutationPermit(out string reason)
    {
        try
        {
            if (_tryCaptureMutationPermit())
            {
                reason = string.Empty;
                return true;
            }
            reason = _readOwnershipFailure();
            if (string.IsNullOrWhiteSpace(reason))
                reason = "Auto Scribe no longer owns CraftingQueueSubmission.";
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            reason =
                "Auto Scribe could not capture its CraftingQueueSubmission permit: " +
                ex.GetBaseException().Message;
            return false;
        }
    }

    private void BindLifecycle()
    {
        if (AutoScribeNativeBindings.TryCreate(out var bindings, out var reason))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

    private static string FirstFailure(params TypedRegistryResolution[] resolutions)
    {
        for (var index = 0; index < resolutions.Length; index++)
            if (!resolutions[index].IsResolved) return resolutions[index].Format();
        return "An Auto Scribe live identity was unavailable.";
    }

    private static string Describe(
        in AutoScribeMutationReceipt receipt,
        in CostMismatch costMismatch) =>
        $"evidenceAvailable={receipt.EvidenceAvailable}; paymentInvoked={receipt.PaymentInvoked}; " +
        $"resourcesCharged={receipt.ResourcesCharged}; " +
        $"costMatched={receipt.CostMatched}; ceiling={receipt.CeilingTransitionObserved}; " +
        $"queueAdmitted={receipt.AdmittedToQueue}; instantStock={receipt.AdmittedToInstantStock}; " +
        $"queueDelta={receipt.QueueDelta}; stockDelta={receipt.StockDelta}; " +
        costMismatch.Format();

    private static int CountNonNull(IList values)
    {
        var count = 0;
        foreach (var value in values)
            if (value is not null) count++;
        return count;
    }

    private static IList RequireList(object? value, string contract) =>
        value as IList ??
        throw new InvalidOperationException(contract + " was not a native list.");

    private static IEnumerable RequireEnumerable(object? value, string contract) =>
        value as IEnumerable ??
        throw new InvalidOperationException(contract + " was not enumerable.");

    private static T Require<T>(object? value, string contract) =>
        value is T typed
            ? typed
            : throw new InvalidOperationException(contract + " changed type.");

    private static object InvokeObject(
        MethodInfo method,
        object? target,
        params object[] arguments) =>
        method.Invoke(target, arguments) ??
        throw new InvalidOperationException(
            $"{method.DeclaringType?.Name}.{method.Name} returned null.");

    private static T Invoke<T>(
        MethodInfo method,
        object? target,
        params object[] arguments) =>
        method.Invoke(target, arguments) is T value
            ? value
            : throw new InvalidOperationException(
                $"{method.DeclaringType?.Name}.{method.Name} changed return type.");

    private static int Level(object value)
    {
        if (value is not BigDouble number)
            throw new InvalidOperationException("CraftingInstance.GetQuantity changed type.");
        var scalar = number.ToDouble();
        if (!double.IsFinite(scalar) || scalar < 1d || scalar > int.MaxValue)
            throw new InvalidOperationException("CraftingInstance quantity was not a valid level.");
        return (int)Math.Floor(scalar);
    }

    private static bool IsExpected(Exception exception) => exception is
        TargetInvocationException or
        ArgumentException or
        InvalidOperationException or
        InvalidCastException or
        FormatException or
        OverflowException or
        NullReferenceException or
        TargetException or
        TargetParameterCountException or
        MemberAccessException or
        TypeLoadException;

    private readonly struct CostState
    {
        internal CostState(
            Guid resourceId,
            object resource,
            BigDouble expected,
            BigDouble expectedRawDebit,
            BigDouble rawQuantity,
            bool decayApplied,
            BigDouble decayPercent)
        {
            ResourceId = resourceId;
            Resource = resource;
            Expected = expected;
            ExpectedRawDebit = expectedRawDebit;
            RawQuantity = rawQuantity;
            DecayApplied = decayApplied;
            DecayPercent = decayPercent;
        }

        internal Guid ResourceId { get; }
        internal object Resource { get; }
        internal BigDouble Expected { get; }
        internal BigDouble ExpectedRawDebit { get; }
        internal BigDouble RawQuantity { get; }
        internal bool DecayApplied { get; }
        internal BigDouble DecayPercent { get; }
    }

    private enum CostMismatchKind
    {
        None = 0,
        MatchedRepresentable = 1,
        MatchedBelowResolution = 2,
        MatchedClampedToZero = 3,
        MatchedMixed = 4,
        ShapeChanged = 5,
        NominalChanged = 6,
        DebitChanged = 7,
        EvidenceUnavailable = 8,
    }

    private readonly struct CostMismatch
    {
        private CostMismatch(
            CostMismatchKind kind,
            Guid resourceId,
            BigDouble nominalCost,
            BigDouble expectedRawDebit,
            BigDouble rawQuantityBefore,
            BigDouble expectedRawQuantityAfter,
            BigDouble observedRawQuantityAfter,
            bool decayApplied,
            BigDouble decayPercent)
        {
            Kind = kind;
            ResourceId = resourceId;
            NominalCost = nominalCost;
            ExpectedRawDebit = expectedRawDebit;
            RawQuantityBefore = rawQuantityBefore;
            ExpectedRawQuantityAfter = expectedRawQuantityAfter;
            ObservedRawQuantityAfter = observedRawQuantityAfter;
            DecayApplied = decayApplied;
            DecayPercent = decayPercent;
        }

        internal static CostMismatch ShapeChanged => new(
            CostMismatchKind.ShapeChanged,
            Guid.Empty,
            default,
            default,
            default,
            default,
            default,
            decayApplied: false,
            default);

        internal static CostMismatch EvidenceUnavailable => new(
            CostMismatchKind.EvidenceUnavailable,
            Guid.Empty,
            default,
            default,
            default,
            default,
            default,
            decayApplied: false,
            default);

        internal static CostMismatch Matched(CostMismatchKind kind) => new(
            kind,
            Guid.Empty,
            default,
            default,
            default,
            default,
            default,
            decayApplied: false,
            default);

        internal static CostMismatch NominalChanged(
            in CostState before,
            BigDouble observedRawQuantityAfter) =>
            new(
                CostMismatchKind.NominalChanged,
                before.ResourceId,
                before.Expected,
                before.ExpectedRawDebit,
                before.RawQuantity,
                before.RawQuantity,
                observedRawQuantityAfter,
                before.DecayApplied,
                before.DecayPercent);

        internal static CostMismatch DebitChanged(
            in CostState before,
            BigDouble aggregateNominal,
            BigDouble aggregateRawDebit,
            BigDouble expectedRawQuantityAfter,
            BigDouble observedRawQuantityAfter) =>
            new(
                CostMismatchKind.DebitChanged,
                before.ResourceId,
                aggregateNominal,
                aggregateRawDebit,
                before.RawQuantity,
                expectedRawQuantityAfter,
                observedRawQuantityAfter,
                before.DecayApplied,
                before.DecayPercent);

        private CostMismatchKind Kind { get; }
        private Guid ResourceId { get; }
        private BigDouble NominalCost { get; }
        private BigDouble ExpectedRawDebit { get; }
        private BigDouble RawQuantityBefore { get; }
        private BigDouble ExpectedRawQuantityAfter { get; }
        private BigDouble ObservedRawQuantityAfter { get; }
        private bool DecayApplied { get; }
        private BigDouble DecayPercent { get; }

        internal string Format() => Kind switch
        {
            CostMismatchKind.None => "costEvidence=matchedNoCosts.",
            CostMismatchKind.MatchedRepresentable =>
                "costEvidence=matchedRepresentableDebit.",
            CostMismatchKind.MatchedBelowResolution =>
                "costEvidence=matchedBelowBigDoubleResolution.",
            CostMismatchKind.MatchedClampedToZero =>
                "costEvidence=matchedClampedToZero.",
            CostMismatchKind.MatchedMixed => "costEvidence=matchedMixedNativeArithmetic.",
            CostMismatchKind.ShapeChanged => "costEvidence=shapeChanged.",
            CostMismatchKind.EvidenceUnavailable => "costEvidence=unavailable.",
            CostMismatchKind.NominalChanged or CostMismatchKind.DebitChanged =>
                $"costEvidence={Kind}; costResource={ResourceId:D}; " +
                $"nominalCost={NominalCost}; expectedRawDebit={ExpectedRawDebit}; " +
                $"rawQuantityBefore={RawQuantityBefore}; " +
                $"expectedRawQuantityAfter={ExpectedRawQuantityAfter}; " +
                $"observedRawQuantityAfter={ObservedRawQuantityAfter}; " +
                $"observedRawDebit={RawQuantityBefore - ObservedRawQuantityAfter}; " +
                $"decayApplied={DecayApplied}; " +
                $"decayPercent={DecayPercent}.",
            _ => "costEvidence=unknown.",
        };
    }

    private readonly struct BeforeState
    {
        internal BeforeState(
            int maximumStartingLevel,
            int queueCount,
            int stockAtLevel,
            CostState[] costs)
        {
            MaximumStartingLevel = maximumStartingLevel;
            QueueCount = queueCount;
            StockAtLevel = stockAtLevel;
            Costs = costs;
        }

        internal int MaximumStartingLevel { get; }
        internal int QueueCount { get; }
        internal int StockAtLevel { get; }
        internal CostState[] Costs { get; }
    }
}
