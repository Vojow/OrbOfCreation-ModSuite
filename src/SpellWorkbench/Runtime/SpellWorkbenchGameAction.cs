using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-bound spell discovery and explicit-layout loadout-add boundary.</summary>
internal sealed class SpellWorkbenchGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private SpellWorkbenchNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal SpellWorkbenchGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        var identity = RuntimeIdentityRegistryBinding.Shared;
        _registry = registry ?? new TypedRegistryResolver(
            _readLifecycleEpoch, identity.Read, identity.ReadStableUuid);
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal SpellWorkbenchSubmission Submit(in SpellWorkbenchAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.WrongThread,
                $"Spell workbench actions are bound to Unity thread {_mainThreadId}, not thread {Environment.CurrentManagedThreadId}.");
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
                SpellWorkbenchActionKind.Discover => Discover(in action, native, manager, recipe),
                SpellWorkbenchActionKind.CreateWithLayout => CreateWithLayout(),
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
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
    }

    private SpellWorkbenchSubmission Discover(in SpellWorkbenchAction action,
        SpellWorkbenchNativeBindings native, object manager, object recipe)
    {
        if (action.AugmentGlyphs.Length != 0)
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.CompositionUnsupported,
                "Discovery accepts core components only; augment glyphs are chosen when adding the spell.");
        if (action.CoreGlyphs.Length == 0)
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.SelectionUnavailable,
                "Discovery requires the exact visible component sequence from preview.");
        return DiscoverFromComponents(in action, native, manager, recipe);
    }

    private SpellWorkbenchSubmission DiscoverFromComponents(
        in SpellWorkbenchAction action,
        SpellWorkbenchNativeBindings native,
        object manager,
        object recipe)
    {
        if (!TryResolveGlyphLayout(
                action.CoreGlyphs, expectAugment: false, native,
                out var components, out var componentReason))
            return SpellWorkbenchSubmission.Reject(
                SpellWorkbenchPreflight.SelectionUnavailable, componentReason);
        if (native.IsDiscovered(recipe))
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.AlreadyDiscovered,
                "The resolved spell recipe is already discovered.");
        if (!native.CanDiscover(recipe))
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.DiscoveryUnavailable,
                "SpellRecipeSO.CanDiscover() refused the resolved recipe.");
        if (!native.IsCreatable(recipe))
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.RecipeUnavailable,
                "SpellRecipeSO.IsCreatable() refused the resolved recipe.");
        var nativeComponents = NativeGlyphList(native, components);
        var resolved = native.ResolveRecipe(manager, nativeComponents);
        if (resolved is null || resolved.GetType() != native.RecipeType ||
            native.ReadIdentity(resolved) != action.SpellRecipeId)
            return SpellWorkbenchSubmission.Reject(
                SpellWorkbenchPreflight.WrongSelection,
                "The exact live component sequence did not resolve to the previewed spell recipe.");
        var resolvedCost = native.GetDiscoverCost(resolved);
        if (!native.HasEnough(resolvedCost))
            return SpellWorkbenchSubmission.Reject(
                SpellWorkbenchPreflight.Unaffordable,
                "GetDiscoverCost().HasEnough() refused the resolved recipe.");

        var before = Capture(native, manager, recipe);
        if (!TryCapturePermit(out var permitReason))
            return SpellWorkbenchSubmission.Reject(
                SpellWorkbenchPreflight.MutationPermitUnavailable, permitReason);

        var nativeCalls = 0;
        var stage = SpellWorkbenchNativeStage.ClearSelection;
        var previousCore = Copy(native.ReadGlyphValues(native.ReadCore(manager)));
        var previousAugments = Copy(native.ReadGlyphValues(native.ReadAugments(manager)));
        try
        {
            ApplySelection(native, manager, components, Array.Empty<object>(), ref nativeCalls);
            stage = SpellWorkbenchNativeStage.ApplySelection;
            var selected = native.ResolveRecipe(
                manager, native.ReadGlyphValues(native.ReadCore(manager)));
            if (selected is null || selected.GetType() != native.RecipeType ||
                native.ReadIdentity(selected) != action.SpellRecipeId)
            {
                if (RestoreSelection(
                        native, manager, previousCore, previousAugments, ref nativeCalls))
                    return SpellWorkbenchSubmission.Reject(
                        SpellWorkbenchPreflight.WrongSelection,
                        "The exact live component sequence did not resolve to the previewed spell recipe.");
                var divergent = CaptureBestEffort(native, manager, recipe, in before);
                return FaultAfterCommit(in action, SpellWorkbenchPreflight.PostCommitFault,
                    SpellWorkbenchNativeStage.ApplySelection,
                    NativeMutationOutcome.PostconditionFailed, nativeCalls,
                    in before, in divergent,
                    "The live component selection diverged and its prior UI state could not be restored.");
            }
            if (!native.CanDiscover(selected) || !native.IsCreatable(selected))
                return SpellWorkbenchSubmission.Reject(
                    SpellWorkbenchPreflight.DiscoveryUnavailable,
                    "The resolved recipe stopped being discoverable before payment.");
            stage = SpellWorkbenchNativeStage.Discover;
            native.Discover(manager);
            nativeCalls++;
            var after = Capture(native, manager, recipe);
            return after.TargetDiscovered
                ? Verified(SpellWorkbenchNativeStage.Verification, nativeCalls,
                    in before, in after,
                    "The component-resolved spell recipe is now discovered.")
                : FaultAfterCommit(in action, SpellWorkbenchPreflight.VerificationFailed,
                    SpellWorkbenchNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed, nativeCalls,
                    in before, in after,
                    "The resolved recipe did not become discovered.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var after = CaptureBestEffort(native, manager, recipe, in before);
            if (after.TargetDiscovered)
                return Verified(SpellWorkbenchNativeStage.Verification, nativeCalls,
                    in before, in after,
                    "The component-resolved recipe became discovered before the native fault.");
            RestoreSelection(
                native, manager, previousCore, previousAugments, ref nativeCalls);
            after = CaptureBestEffort(native, manager, recipe, in before);
            return FaultAfterCommit(in action, SpellWorkbenchPreflight.PostCommitFault,
                stage, NativeMutationOutcome.ExecutionThrew, nativeCalls,
                in before, in after,
                "Component discovery faulted before its outcome was observable: " +
                ex.GetBaseException().Message);
        }
    }

    private static SpellWorkbenchSubmission CreateWithLayout() =>
        SpellWorkbenchSubmission.Reject(
            SpellWorkbenchPreflight.ContractUnavailable,
            "Spell loadout add is unavailable because the complete player-visible admission " +
            "contract is not bound: usage requirements, computed usage cost, unique-spell " +
            "compatibility, loadout budget, and non-level glyph requirements must all be " +
            "revalidated before mutation.");

    private bool TryResolveGlyphLayout(
        SpellWorkbenchGlyphStack[] layout,
        bool expectAugment,
        SpellWorkbenchNativeBindings native,
        out List<object> glyphs,
        out string reason)
    {
        glyphs = new List<object>();
        for (var index = 0; index < layout.Length; index++)
        {
            var stack = layout[index];
            var resolution = _registry.Resolve(stack.GlyphId, native.GlyphType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
            {
                reason = resolution.IsResolved
                    ? "The glyph registry resolution became stale."
                    : resolution.Reason;
                return false;
            }
            var glyph = resolution.Value!;
            if (native.IsGlyphAugment(glyph) != expectAugment)
            {
                reason = EntityIdentityFormatter.Format(stack.GlyphId) +
                    (expectAugment
                        ? " is not a spell augment."
                        : " is an augment and cannot be a core discovery component.");
                return false;
            }
            if (!native.IsGlyphAvailable(glyph))
            {
                reason = "GlyphSO.IsAvailable() refused " +
                    EntityIdentityFormatter.Format(stack.GlyphId) + ".";
                return false;
            }
            var maximum = native.GetGlyphMaximumUsages(glyph);
            if (stack.Count > maximum)
            {
                reason = "Requested " + stack.Count + " uses of " +
                    EntityIdentityFormatter.Format(stack.GlyphId) +
                    ", but the live usable count is " + maximum + ".";
                return false;
            }
            for (var count = 0; count < stack.Count; count++) glyphs.Add(glyph);
        }
        reason = string.Empty;
        return true;
    }

    private static void ApplySelection(
        SpellWorkbenchNativeBindings native,
        object manager,
        IList<object> coreGlyphs,
        IList<object> augmentGlyphs,
        ref int nativeCalls)
    {
        var core = native.ReadCore(manager);
        var augments = native.ReadAugments(manager);
        native.Empty(core);
        nativeCalls++;
        native.Empty(augments);
        nativeCalls++;
        for (var index = 0; index < coreGlyphs.Count; index++)
        {
            native.Add(core, coreGlyphs[index]);
            nativeCalls++;
        }
        for (var index = 0; index < augmentGlyphs.Count; index++)
        {
            native.Add(augments, augmentGlyphs[index]);
            nativeCalls++;
        }
    }

    private static object NativeGlyphList(
        SpellWorkbenchNativeBindings native,
        IList<object> glyphs)
    {
        var result = native.CreateGlyphList();
        for (var index = 0; index < glyphs.Count; index++) result.Add(glyphs[index]);
        return result;
    }

    private static List<object> Copy(IList source)
    {
        var result = new List<object>(source.Count);
        for (var index = 0; index < source.Count; index++)
            if (source[index] is { } value) result.Add(value);
        return result;
    }

    private static bool RestoreSelection(
        SpellWorkbenchNativeBindings native,
        object manager,
        IList<object> core,
        IList<object> augments,
        ref int nativeCalls)
    {
        try
        {
            ApplySelection(native, manager, core, augments, ref nativeCalls);
            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
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

    private static SpellWorkbenchSubmission Verified(SpellWorkbenchNativeStage stage,
        int nativeCalls, in SpellWorkbenchState before, in SpellWorkbenchState after, string reason)
    {
        var evidence = new SpellWorkbenchEvidence(true, in before, in after);
        return new SpellWorkbenchSubmission(SpellWorkbenchPreflight.Proceeded, stage,
            NativeMutationOutcome.Verified, new NativeMutationCallOutcome(nativeCalls, 1, 1),
            in evidence, reason);
    }

    private static SpellWorkbenchSubmission FaultAfterCommit(in SpellWorkbenchAction action,
        SpellWorkbenchPreflight preflight, SpellWorkbenchNativeStage stage,
        NativeMutationOutcome outcome, int nativeCalls, in SpellWorkbenchState before,
        in SpellWorkbenchState after, string reason)
    {
        var exactReason = $"Spell workbench action faulted after {stage} on " +
            $"recipe {EntityIdentityFormatter.Format(action.SpellRecipeId)}: {reason}";
        var paymentInvoked = action.Kind == SpellWorkbenchActionKind.Discover &&
            stage >= SpellWorkbenchNativeStage.Discover;
        var evidence = new SpellWorkbenchEvidence(
            true, in before, in after, paymentInvoked);
        return new SpellWorkbenchSubmission(preflight, stage, outcome,
            new NativeMutationCallOutcome(nativeCalls, 1, 0), in evidence, exactReason);
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
