using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-bound, fail-closed native spell selection, discovery, and creation boundary.</summary>
internal sealed class SpellWorkbenchGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private SpellWorkbenchNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;
    private string _quarantineReason = string.Empty;

    internal SpellWorkbenchGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;
    internal bool IsQuarantined => _quarantineReason.Length != 0;

    internal SpellWorkbenchSubmission Submit(in SpellWorkbenchAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.WrongThread,
                $"Spell workbench actions are bound to Unity thread {_mainThreadId}, not thread {Environment.CurrentManagedThreadId}.");
        if (_quarantineReason.Length != 0)
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.Quarantined, _quarantineReason);
        if (_bindings is not { } native)
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.ContractUnavailable,
                _bindingFailure.Length == 0 ? "The lifecycle-scoped spell workbench binding set is unavailable." : _bindingFailure);

        long currentEpoch;
        try { currentEpoch = _readLifecycleEpoch(); }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.LifecycleReplaced,
                "The current lifecycle epoch could not be read: " + ex.GetBaseException().Message);
        }
        if (currentEpoch != action.LifecycleEpoch)
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.LifecycleReplaced,
                $"Action lifecycle {action.LifecycleEpoch} is stale; the live lifecycle is {currentEpoch}.");

        try
        {
            var manager = native.ReadManager();
            if (manager is null)
                return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.ContractUnavailable,
                    "SpellManager.instance is not available in the current lifecycle.");
            if (!TryResolveRecipe(native, action.SpellRecipeId, out var recipe, out var reason))
                return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.IdentityUnavailable, reason);
            return action.Kind switch
            {
                SpellWorkbenchActionKind.Select => Select(in action, native, manager, recipe),
                SpellWorkbenchActionKind.Discover => Discover(in action, native, manager, recipe),
                SpellWorkbenchActionKind.Create => Create(in action, native, manager, recipe),
                _ => SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.ContractUnavailable,
                    "Unknown spell workbench action kind " + (int)action.Kind + "."),
            };
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.ContractUnavailable,
                "Spell workbench preflight failed before mutation: " + ex.GetBaseException().Message);
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

    private SpellWorkbenchSubmission Select(in SpellWorkbenchAction action,
        SpellWorkbenchNativeBindings native, object manager, object recipe)
    {
        var glyphs = native.ReadRecipeGlyphs(recipe);
        if (glyphs.Count == 0)
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.RecipeUnavailable,
                "The target recipe has no authored core glyphs.");
        for (var index = 0; index < glyphs.Count; index++)
        {
            var glyph = glyphs[index];
            if (glyph is null || glyph.GetType() != native.GlyphType)
                return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.RecipeUnavailable,
                    $"Authored core glyph {index} was not an exact GlyphSO.");
            if (native.IsGlyphAugment(glyph))
                return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.RecipeUnavailable,
                    $"Authored core glyph {index} is classified as an augment.");
            if (!native.IsGlyphAvailable(glyph))
                return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.SelectionUnavailable,
                    $"Native GlyphSO.IsAvailable() refused authored core glyph {EntityIdentityFormatter.Format(native.ReadIdentity(glyph))}.");
        }

        var before = Capture(native, manager, recipe);
        if (!TryCapturePermit(out var reason))
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.MutationPermitUnavailable, reason);
        var stage = SpellWorkbenchNativeStage.ClearSelection;
        var nativeCalls = 0;
        try
        {
            var core = native.ReadCore(manager);
            var augments = native.ReadAugments(manager);
            native.Empty(core);
            nativeCalls++;
            native.Empty(augments);
            nativeCalls++;
            stage = SpellWorkbenchNativeStage.ApplySelection;
            for (var index = 0; index < glyphs.Count; index++)
            {
                native.Add(core, glyphs[index]!);
                nativeCalls++;
            }
            stage = SpellWorkbenchNativeStage.Verification;
            var after = Capture(native, manager, recipe);
            var matched = after.ResolvedRecipeId == action.SpellRecipeId &&
                after.AugmentGlyphIds.Length == 0 && Same(after.CoreGlyphIds, ReadIds(native, glyphs));
            return matched
                ? Verified(stage, nativeCalls, in before, in after,
                    "The exact authored core glyph sequence now resolves to the requested recipe.")
                : Quarantine(in action, SpellWorkbenchPreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed, nativeCalls, in before, in after,
                    "The native selection did not resolve to the requested recipe.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var after = CaptureBestEffort(native, manager, recipe, in before);
            if (after.ResolvedRecipeId == action.SpellRecipeId && after.AugmentGlyphIds.Length == 0)
                return Verified(SpellWorkbenchNativeStage.Verification, nativeCalls, in before, in after,
                    "Selection threw after the requested recipe became the exact native selection.");
            return Quarantine(in action, SpellWorkbenchPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew, nativeCalls, in before, in after,
                "Selection threw before the requested recipe was observable: " + ex.GetBaseException().Message);
        }
    }

    private SpellWorkbenchSubmission Discover(in SpellWorkbenchAction action,
        SpellWorkbenchNativeBindings native, object manager, object recipe)
    {
        if (!TryRequireSelection(action.SpellRecipeId, native, manager, out var rejection)) return rejection;
        if (native.IsDiscovered(recipe))
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.AlreadyDiscovered,
                "The requested spell recipe is already discovered.");
        if (!native.CanDiscover(recipe))
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.DiscoveryUnavailable,
                "SpellRecipeSO.CanDiscover() refused the requested recipe.");
        if (!native.IsCreatable(recipe))
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.RecipeUnavailable,
                "SpellRecipeSO.IsCreatable() refused the requested recipe.");
        if (!native.HasEnough(native.GetDiscoverCost(recipe)))
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.Unaffordable,
                "GetDiscoverCost().HasEnough() refused the requested recipe.");
        return Execute(in action, native, manager, recipe, SpellWorkbenchNativeStage.Discover,
            () => native.Discover(manager),
            after => after.TargetDiscovered,
            "The requested recipe is now discovered.");
    }

    private SpellWorkbenchSubmission Create(in SpellWorkbenchAction action,
        SpellWorkbenchNativeBindings native, object manager, object recipe)
    {
        if (!TryRequireSelection(action.SpellRecipeId, native, manager, out var rejection)) return rejection;
        if (!native.IsDiscovered(recipe))
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.DiscoveryUnavailable,
                "Create requires an already discovered recipe; use discover first.");
        if (!native.IsCreatable(recipe))
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.RecipeUnavailable,
                "SpellRecipeSO.IsCreatable() refused the requested recipe.");
        var active = native.ReadActive(manager);
        if (!native.HasEmpty(active))
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.LoadoutFull,
                "The native active-spell list has no empty slot.");
        var createCost = native.GetCreateCost(manager, native.ReadGlyphValues(native.ReadCore(manager)));
        if (createCost is null || !native.HasEnough(createCost))
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.Unaffordable,
                "GetSpellCreateCost().HasEnough() refused the current exact selection.");
        var before = Capture(native, manager, recipe);
        return Execute(in action, native, manager, recipe, SpellWorkbenchNativeStage.Create,
            () => native.Create(manager),
            after => HasNewInstance(before.TargetSpellInstanceIds, after.TargetSpellInstanceIds),
            "A new runtime spell instance referencing the requested recipe is equipped.");
    }

    private SpellWorkbenchSubmission Execute(in SpellWorkbenchAction action,
        SpellWorkbenchNativeBindings native, object manager, object recipe,
        SpellWorkbenchNativeStage stage, Action execute, Func<SpellWorkbenchState, bool> verify,
        string success)
    {
        var before = Capture(native, manager, recipe);
        if (!TryCapturePermit(out var reason))
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.MutationPermitUnavailable, reason);
        try
        {
            execute();
            var after = Capture(native, manager, recipe);
            return verify(after)
                ? Verified(SpellWorkbenchNativeStage.Verification, 1, in before, in after, success)
                : Quarantine(in action, SpellWorkbenchPreflight.VerificationFailed,
                    SpellWorkbenchNativeStage.Verification, NativeMutationOutcome.PostconditionFailed,
                    1, in before, in after, "The requested native transition did not happen.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var after = CaptureBestEffort(native, manager, recipe, in before);
            if (verify(after))
                return Verified(SpellWorkbenchNativeStage.Verification, 1, in before, in after,
                    success + " The native call threw after the outcome landed.");
            return Quarantine(in action, SpellWorkbenchPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew, 1, in before, in after,
                "Native execution threw before the requested outcome was observable: " + ex.GetBaseException().Message);
        }
    }

    private static bool TryRequireSelection(Guid target, SpellWorkbenchNativeBindings native,
        object manager, out SpellWorkbenchSubmission rejection)
    {
        var augments = native.ReadGlyphValues(native.ReadAugments(manager));
        if (augments.Count != 0)
        {
            rejection = SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.CompositionUnsupported,
                "B-002 accepts the base recipe only; clear augment composition or use the composition family.");
            return false;
        }
        var resolved = native.ResolveRecipe(manager, native.ReadGlyphValues(native.ReadCore(manager)));
        if (resolved is null || resolved.GetType() != native.RecipeType || native.ReadIdentity(resolved) != target)
        {
            rejection = SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.WrongSelection,
                "The live core-glyph selection does not resolve to the requested recipe.");
            return false;
        }
        rejection = default;
        return true;
    }

    private static SpellWorkbenchState Capture(SpellWorkbenchNativeBindings native,
        object manager, object targetRecipe)
    {
        var core = native.ReadGlyphValues(native.ReadCore(manager));
        var augments = native.ReadGlyphValues(native.ReadAugments(manager));
        var resolved = native.ResolveRecipe(manager, core);
        var resolvedId = resolved is not null && resolved.GetType() == native.RecipeType
            ? native.ReadIdentity(resolved)
            : Guid.Empty;
        var instances = new List<Guid>();
        var active = native.ReadActiveValues(native.ReadActive(manager));
        for (var index = 0; index < active.Count; index++)
        {
            var spell = active[index];
            if (spell is null) continue;
            var reference = native.ReadSpellReference(spell);
            if (reference is null || reference.GetType() != native.RecipeType ||
                native.ReadIdentity(reference) != native.ReadIdentity(targetRecipe)) continue;
            var container = native.ReadSpellGuid(spell);
            instances.Add(container is null ? Guid.Empty : native.ReadGuidValue(container));
        }
        return new SpellWorkbenchState(resolvedId, native.IsDiscovered(targetRecipe),
            ReadIds(native, core), ReadIds(native, augments), instances.ToArray());
    }

    private static SpellWorkbenchState CaptureBestEffort(SpellWorkbenchNativeBindings native,
        object manager, object recipe, in SpellWorkbenchState fallback)
    {
        try { return Capture(native, manager, recipe); }
        catch (Exception ex) when (IsExpected(ex)) { return fallback; }
    }

    private static Guid[] ReadIds(SpellWorkbenchNativeBindings native, IList values)
    {
        var result = new Guid[values.Count];
        for (var index = 0; index < result.Length; index++)
        {
            var value = values[index];
            result[index] = value is null ? Guid.Empty : native.ReadIdentity(value);
        }
        return result;
    }

    private static bool TryResolveRecipe(SpellWorkbenchNativeBindings native, Guid id,
        out object recipe, out string reason)
    {
        recipe = null!;
        var matches = 0;
        foreach (var value in native.ReadRecipes())
        {
            if (value is null || value.GetType() != native.RecipeType || native.ReadIdentity(value) != id) continue;
            recipe = value;
            matches++;
        }
        if (matches == 1) { reason = string.Empty; return true; }
        reason = matches == 0
            ? $"No exact SpellRecipeSO with identity {EntityIdentityFormatter.Format(id)} exists in the live registry."
            : $"SpellRecipeSO identity {EntityIdentityFormatter.Format(id)} is ambiguous across {matches} exact instances.";
        return false;
    }

    private static bool Same(Guid[] left, Guid[] right)
    {
        if (left.Length != right.Length) return false;
        for (var index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
        return true;
    }

    private static bool HasNewInstance(Guid[] before, Guid[] after)
    {
        if (after.Length <= before.Length) return false;
        for (var index = 0; index < after.Length; index++)
        {
            if (after[index] == Guid.Empty) continue;
            var found = false;
            for (var prior = 0; prior < before.Length; prior++)
                if (after[index] == before[prior]) { found = true; break; }
            if (!found) return true;
        }
        return false;
    }

    private static SpellWorkbenchSubmission Verified(SpellWorkbenchNativeStage stage,
        int nativeCalls, in SpellWorkbenchState before, in SpellWorkbenchState after, string reason)
    {
        var evidence = new SpellWorkbenchEvidence(true, in before, in after);
        return new SpellWorkbenchSubmission(SpellWorkbenchPreflight.Proceeded, stage,
            NativeMutationOutcome.Verified, new NativeMutationCallOutcome(nativeCalls, 1, 1),
            in evidence, reason);
    }

    private SpellWorkbenchSubmission Quarantine(in SpellWorkbenchAction action,
        SpellWorkbenchPreflight preflight, SpellWorkbenchNativeStage stage,
        NativeMutationOutcome outcome, int nativeCalls, in SpellWorkbenchState before,
        in SpellWorkbenchState after, string reason)
    {
        _quarantineReason = $"Spell workbench actions are quarantined for this lifecycle after {stage} on " +
            $"recipe {EntityIdentityFormatter.Format(action.SpellRecipeId)}: {reason}";
        var evidence = new SpellWorkbenchEvidence(true, in before, in after);
        return new SpellWorkbenchSubmission(preflight, stage, outcome,
            new NativeMutationCallOutcome(nativeCalls, 1, 0), in evidence, _quarantineReason);
    }

    private bool TryCapturePermit(out string reason)
    {
        if (_tryCaptureMutationPermit()) { reason = string.Empty; return true; }
        reason = _readOwnershipFailure();
        if (reason.Length == 0) reason = "The suite does not own the spell workbench action family.";
        return false;
    }

    private void BindLifecycle()
    {
        var resolveType = _resolveType ?? ReflectionUtil.FindLoadedType;
        Func<string, bool> includeContract = _includeContract ?? (_ => true);
        if (SpellWorkbenchNativeBindings.TryCreate(resolveType, includeContract, out var bindings, out var reason))
        {
            _bindings = bindings;
            return;
        }
        _bindingFailure = reason;
    }

    private static bool IsExpected(Exception ex) =>
        ex is InvalidOperationException or ArgumentException or TargetInvocationException or NullReferenceException;
}
