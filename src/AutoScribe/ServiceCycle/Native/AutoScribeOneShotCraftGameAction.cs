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
    private AutoScribeNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;
    private string _quarantineReason = string.Empty;

    internal AutoScribeOneShotCraftGameAction(
        TypedRegistryResolver registry,
        AutoScribeIdentityProfile profile,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
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
            if (HasCompetingSupply(native, action.RecipeId, action.Level, out reason))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.CompetingSupply,
                    reason);
            if (!TryValidateTarget(native, scroll, action.ScrollId, action.Level, out reason))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.TargetUnavailable,
                    reason);

            var level = new BigDouble(action.Level, 0);
            var zero = BigDouble.Zero;
            if (!Invoke<bool>(native.RecipeCanBuyAt, recipe, level))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.Unaffordable,
                    $"CraftingRecipeSO.CanBuyAt({action.Level}) refused recipe {action.RecipeId:D}.");
            var totalCost = InvokeObject(native.RecipeTotalCost, recipe, zero, level);
            if (totalCost.GetType() != native.ResourceCostType)
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.ContractUnavailable,
                    "CraftingRecipeSO.GetTotalCost returned a non-ResourceCostList value.");
            if (!Invoke<bool>(native.CostHasEnough, totalCost))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.Unaffordable,
                    $"GetTotalCost(0,{action.Level}).HasEnough() refused recipe {action.RecipeId:D}.");

            var before = CaptureBefore(native, recipeType, activeQueue, scroll, action.Level, totalCost);
            if (!TryCaptureMutationPermit(out reason))
                return AutoScribeSubmission.Reject(
                    AutoScribePreflight.MutationPermitUnavailable,
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
                level,
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
        BigDouble level,
        in BeforeState before)
    {
        var stage = AutoScribeNativeStage.Payment;
        var nativeCalls = 1;
        var paymentInvoked = false;
        try
        {
            paymentInvoked = true;
            native.RecipePurchase.Invoke(recipe, new object[] { level, BigDouble.Zero });

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
                action.Level,
                totalCost,
                in before,
                paymentInvoked);
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
                    "Auto Scribe verification failed: " + Describe(in receipt));
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
                action.Level,
                totalCost,
                in before,
                paymentInvoked);
            return Quarantine(
                in action,
                AutoScribePreflight.PostPaymentFault,
                stage,
                NativeMutationOutcome.ExecutionThrew,
                nativeCalls,
                in receipt,
                $"Auto Scribe native {stage} failed after payment began: " +
                ex.GetBaseException().Message + "; " + Describe(in receipt));
        }
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

    private bool HasCompetingSupply(
        AutoScribeNativeBindings native,
        Guid recipeId,
        int level,
        out string reason)
    {
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
                    Level(InvokeObject(native.InstanceQuantity, value)) >= level &&
                    !Invoke<bool>(native.InstanceExpired, value))
                {
                    reason =
                        $"{queueIdentity.Uuid:D} already supplies recipe {recipeId:D} at level " +
                        $"{level} or higher.";
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

    private BeforeState CaptureBefore(
        AutoScribeNativeBindings native,
        object recipeType,
        object activeQueue,
        object scroll,
        int level,
        object totalCost)
    {
        return new BeforeState(
            Require<int>(
                native.MaxStartingLevel.GetValue(recipeType),
                "CraftingRecipeTypeSO.maxStartingLevel"),
            CountNonNull(RequireList(
                native.InstanceListValue.GetValue(activeQueue),
                "ActiveScribeInstances.value")),
            StockAt(native, scroll, level),
            CaptureCosts(native, totalCost));
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
        bool paymentInvoked)
    {
        var afterQueue = RequireList(
            native.InstanceListValue.GetValue(activeQueue),
            "ActiveScribeInstances.value");
        var queueCount = CountNonNull(afterQueue);
        var stock = StockAt(native, scroll, level);
        var costs = CaptureCosts(native, totalCost);
        var costMatched = CostsMatch(before.Costs, costs);
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
        bool paymentInvoked)
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
                paymentInvoked);
        }
        catch (Exception) when (paymentInvoked)
        {
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

    private static CostState[] CaptureCosts(
        AutoScribeNativeBindings native,
        object totalCost)
    {
        var values = RequireList(native.Costs.GetValue(totalCost), "ResourceCostList.costs");
        var result = new CostState[values.Count];
        var count = 0;
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
            var quantity = Require<BigDouble>(
                native.ResourceQuantity.Invoke(resource, Array.Empty<object>()),
                "ResourceSO.GetTrueQuantity");
            var found = -1;
            for (var existing = 0; existing < count; existing++)
            {
                if (result[existing].ResourceId == resourceId)
                {
                    found = existing;
                    break;
                }
            }
            if (found >= 0)
                result[found] = new CostState(
                    resourceId,
                    result[found].Expected + expected,
                    quantity);
            else
                result[count++] = new CostState(resourceId, expected, quantity);
        }
        if (count != result.Length) Array.Resize(ref result, count);
        Array.Sort(result, static (left, right) => left.ResourceId.CompareTo(right.ResourceId));
        return result;
    }

    private static bool CostsMatch(CostState[] before, CostState[] after)
    {
        if (before.Length != after.Length) return false;
        for (var index = 0; index < before.Length; index++)
        {
            if (before[index].ResourceId != after[index].ResourceId ||
                before[index].Expected.CompareTo(after[index].Expected) != 0 ||
                (before[index].Quantity - after[index].Quantity)
                    .CompareTo(before[index].Expected) != 0)
                return false;
        }
        return true;
    }

    private static bool AnyCharge(CostState[] before, CostState[] after)
    {
        if (before.Length != after.Length) return false;
        for (var index = 0; index < before.Length; index++)
            if ((before[index].Quantity - after[index].Quantity).CompareTo(BigDouble.Zero) > 0)
                return true;
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

    private static string Describe(in AutoScribeMutationReceipt receipt) =>
        $"evidenceAvailable={receipt.EvidenceAvailable}; paymentInvoked={receipt.PaymentInvoked}; " +
        $"resourcesCharged={receipt.ResourcesCharged}; " +
        $"costMatched={receipt.CostMatched}; ceiling={receipt.CeilingTransitionObserved}; " +
        $"queueAdmitted={receipt.AdmittedToQueue}; instantStock={receipt.AdmittedToInstantStock}; " +
        $"queueDelta={receipt.QueueDelta}; stockDelta={receipt.StockDelta}.";

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
        internal CostState(Guid resourceId, BigDouble expected, BigDouble quantity)
        {
            ResourceId = resourceId;
            Expected = expected;
            Quantity = quantity;
        }

        internal Guid ResourceId { get; }
        internal BigDouble Expected { get; }
        internal BigDouble Quantity { get; }
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
