using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Doctrine-shaped re-drive of the native Scribe UI composite. All fallible suite-owned reads and
/// decisions finish before payment; the exact work admission is the one postcondition sentinel.
/// </summary>
internal sealed partial class AutoScribeOneShotCraftGameAction : IDisposable
{
    private readonly TypedRegistryResolver _registry;
    private readonly AutoScribeIdentityProfile _profile;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly int _mainThreadId;
    private AutoScribeNativeBindings? _bindings;
    private CraftingPlayerNativeBindings? _playerBindings;
    private string _bindingFailure = string.Empty;
    private string _playerBindingFailure = string.Empty;
    private string _quarantineReason = string.Empty;

    internal AutoScribeOneShotCraftGameAction(
        TypedRegistryResolver registry,
        AutoScribeIdentityProfile profile,
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _readLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
        _mainThreadId = Environment.CurrentManagedThreadId;
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
                    $"CraftingRecipeSO.IsVisible() refused recipe {EntityIdentityFormatter.Format(action.RecipeId)}.");
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
                    $"Recipe {EntityIdentityFormatter.Format(action.RecipeId)} could not afford requested level " +
                    $"{action.Level} or any stronger level.");
            if (HasCompetingSupply(native, action.RecipeId, craftLevel, out reason))
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
                    $"GetTotalCost(0,{craftLevel}).HasEnough() refused recipe {EntityIdentityFormatter.Format(action.RecipeId)}.");

            var stockBefore = StockAt(native, scroll, craftLevel);
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
                activeQueue,
                scroll,
                craftLevel,
                level,
                stockBefore);
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
        _playerBindings = null;
        _bindingFailure = string.Empty;
        _playerBindingFailure = string.Empty;
        _quarantineReason = string.Empty;
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _playerBindings = null;
        _bindingFailure = string.Empty;
        _playerBindingFailure = string.Empty;
        _quarantineReason = string.Empty;
    }

    private AutoScribeSubmission Execute(
        in AutoScribeCycleAction action,
        AutoScribeNativeBindings native,
        object recipe,
        object activeQueue,
        object scroll,
        int craftLevel,
        BigDouble level,
        int stockBefore)
    {
        var stage = AutoScribeNativeStage.Payment;
        var nativeCalls = 1;
        try
        {
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
            var verified = instant
                ? StockAt(native, scroll, craftLevel) == stockBefore + 1
                : ContainsWork(native, RequireList(
                    native.InstanceListValue.GetValue(activeQueue),
                    "ActiveScribeInstances.value"), action.RecipeId, craftLevel);
            if (!verified)
                return Quarantine(
                    in action,
                    AutoScribePreflight.VerificationFailed,
                    stage,
                    NativeMutationOutcome.PostconditionFailed,
                    nativeCalls,
                    "The exact crafted work was not observable after native admission.");
            return new AutoScribeSubmission(
                AutoScribePreflight.Proceeded,
                stage,
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(nativeCalls, 1, 1),
                "The exact crafted work reached its native destination.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var landed = WorkObservedBestEffort(
                native, activeQueue, scroll, action.RecipeId, craftLevel, stockBefore);
            if (landed)
                return new AutoScribeSubmission(
                    AutoScribePreflight.Proceeded,
                    AutoScribeNativeStage.Verification,
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(nativeCalls, 1, 1),
                    "The exact crafted work was observable after native code threw.");
            return Quarantine(
                in action,
                AutoScribePreflight.PostPaymentFault,
                stage,
                NativeMutationOutcome.ExecutionThrew,
                nativeCalls,
                $"Auto Scribe native {stage} failed after payment began: " +
                ex.GetBaseException().Message);
        }
    }

    private AutoScribeSubmission Quarantine(
        in AutoScribeCycleAction action,
        AutoScribePreflight preflight,
        AutoScribeNativeStage stage,
        NativeMutationOutcome outcome,
        int nativeCalls,
        string reason)
    {
        _quarantineReason =
            $"Auto Scribe is quarantined for this lifecycle after {stage} on " +
            $"{EntityIdentityFormatter.Format(action.RecipeId)}: {reason}";
        return new AutoScribeSubmission(
            preflight,
            stage,
            outcome,
            new NativeMutationCallOutcome(nativeCalls, 1, 0),
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
                $"Action recipe {EntityIdentityFormatter.Format(action.RecipeId)} and Scroll {EntityIdentityFormatter.Format(action.ScrollId)} do not identify " +
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
                    $"ScribeCraftingRecipes omitted audited recipe {EntityIdentityFormatter.Format(role.Recipe.Value.Uuid)}.";
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
                        $"The live Scroll enchantment did not equal audited {EntityIdentityFormatter.Format(expectedEnchantment)}.";
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
                reason = $"{EntityIdentityFormatter.Format(queueIdentity.Uuid)} did not expose its exact CraftingInstance list.";
                return true;
            }
            foreach (var value in work)
            {
                if (value is null) continue;
                if (value.GetType() != native.InstanceType)
                {
                    reason = $"{EntityIdentityFormatter.Format(queueIdentity.Uuid)} contained a non-CraftingInstance value.";
                    return true;
                }
                if (Invoke<Guid>(native.InstanceRecipe, value) == recipeId &&
                    Level(InvokeObject(native.InstanceQuantity, value)) >= level &&
                    !Invoke<bool>(native.InstanceExpired, value))
                {
                    reason =
                        $"{EntityIdentityFormatter.Format(queueIdentity.Uuid)} already supplies recipe {EntityIdentityFormatter.Format(recipeId)} at level " +
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
                $"Scroll {EntityIdentityFormatter.Format(scrollId)} did not expose exactly one exact TargetSelectOptions.";
            return false;
        }
        var targeting = InvokeObject(native.GetTargeting, options);
        if (targeting.GetType() != native.TargetType)
        {
            reason = $"Scroll {EntityIdentityFormatter.Format(scrollId)} did not resolve the exact TargetStructure selector.";
            return false;
        }
        var scaling = InvokeObject(
            native.ScalingBasic,
            target: null,
            new BigDouble(level, 0));
        if (scaling.GetType() != native.ScalingType ||
            native.GetRandomList.Invoke(targeting, new[] { scaling }) is not ICollection candidates)
        {
            reason = $"Scroll {EntityIdentityFormatter.Format(scrollId)} target selection changed contract.";
            return false;
        }
        if (candidates.Count == 0)
        {
            reason = $"Scroll {EntityIdentityFormatter.Format(scrollId)} has no valid live target at level {level}.";
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

    private static bool WorkObservedBestEffort(
        AutoScribeNativeBindings native,
        object activeQueue,
        object scroll,
        Guid recipeId,
        int level,
        int stockBefore)
    {
        try
        {
            if (StockAt(native, scroll, level) == stockBefore + 1) return true;
            return ContainsWork(native, RequireList(
                native.InstanceListValue.GetValue(activeQueue),
                "ActiveScribeInstances.value"), recipeId, level);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
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
        }
        else
        {
            _bindings = null;
            _bindingFailure = reason;
        }
        if (CraftingPlayerNativeBindings.TryCreate(out var player, out var playerReason))
        {
            _playerBindings = player;
            _playerBindingFailure = string.Empty;
        }
        else
        {
            _playerBindings = null;
            _playerBindingFailure = playerReason;
        }
    }

    private static string FirstFailure(params TypedRegistryResolution[] resolutions)
    {
        for (var index = 0; index < resolutions.Length; index++)
            if (!resolutions[index].IsResolved) return resolutions[index].Format();
        return "An Auto Scribe live identity was unavailable.";
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

}
