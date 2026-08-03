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
                SpellWorkbenchActionKind.CreateWithLayout => CreateWithLayout(
                    in action, native, manager, recipe),
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
                "The resolved spell cannot be discovered right now.");
        if (!native.IsCreatable(recipe))
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.RecipeUnavailable,
                "The resolved spell is not currently craftable.");
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
                "The resolved spell's discovery cost is not affordable.");

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
                return FaultAfterCommit(in action, SpellWorkbenchPreflight.PostCommitFault,
                    SpellWorkbenchNativeStage.ApplySelection,
                    NativeMutationOutcome.PostconditionFailed, nativeCalls,
                    "The live component selection diverged and its prior UI state could not be restored.");
            }
            if (!native.CanDiscover(selected) || !native.IsCreatable(selected))
            {
                return RestoreSelection(
                        native, manager, previousCore, previousAugments, ref nativeCalls)
                    ? SpellWorkbenchSubmission.Reject(
                        SpellWorkbenchPreflight.DiscoveryUnavailable,
                        "The resolved recipe stopped being discoverable before payment.")
                    : FaultAfterCommit(in action, SpellWorkbenchPreflight.PostCommitFault,
                        SpellWorkbenchNativeStage.ApplySelection,
                        NativeMutationOutcome.PostconditionFailed, nativeCalls,
                        "Live discovery admission changed and the prior workbench selection could not be restored.");
            }
            stage = SpellWorkbenchNativeStage.Discover;
            native.Discover(manager);
            nativeCalls++;
            var restored = RestoreSelection(
                native, manager, previousCore, previousAugments, ref nativeCalls);
            if (!restored)
                return FaultAfterCommit(in action, SpellWorkbenchPreflight.PostCommitFault,
                    SpellWorkbenchNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed, nativeCalls,
                    "The recipe discovery call returned but the prior workbench selection could not be restored.");
            return native.IsDiscovered(recipe)
                ? Verified(SpellWorkbenchNativeStage.Verification, nativeCalls,
                    "The component-resolved spell recipe is now discovered.")
                : FaultAfterCommit(in action, SpellWorkbenchPreflight.VerificationFailed,
                    SpellWorkbenchNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed, nativeCalls,
                    "The resolved recipe did not become discovered.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var restored = RestoreSelection(
                native, manager, previousCore, previousAugments, ref nativeCalls);
            if (restored && IsDiscoveredBestEffort(native, recipe))
                return Verified(SpellWorkbenchNativeStage.Verification, nativeCalls,
                    "The component-resolved recipe became discovered before the native fault.");
            return FaultAfterCommit(in action, SpellWorkbenchPreflight.PostCommitFault,
                stage, NativeMutationOutcome.ExecutionThrew, nativeCalls,
                restored
                    ? "Component discovery faulted before its outcome was observable: " +
                        ex.GetBaseException().Message
                    : "Component discovery faulted and the prior workbench selection could not be restored: " +
                ex.GetBaseException().Message);
        }
    }

    private SpellWorkbenchSubmission CreateWithLayout(
        in SpellWorkbenchAction action,
        SpellWorkbenchNativeBindings native,
        object manager,
        object recipe)
    {
        if (action.CoreGlyphs.Length != 0)
            return SpellWorkbenchSubmission.Reject(
                SpellWorkbenchPreflight.CompositionUnsupported,
                "Loadout add accepts the chosen augment layout only; the discovered recipe owns its core glyphs.");
        if (!TryResolveGlyphLayout(
                action.AugmentGlyphs, expectAugment: true, native,
                out var augments, out var augmentReason))
            return SpellWorkbenchSubmission.Reject(
                SpellWorkbenchPreflight.SelectionUnavailable, augmentReason);
        if (!TryReadUsableCore(native, recipe, out var core, out var coreReason))
            return SpellWorkbenchSubmission.Reject(
                SpellWorkbenchPreflight.RecipeUnavailable, coreReason);
        if (!TryAdmitCreate(native, manager, recipe, core, augments,
                out var refusal, out var refusalReason, out var createCost))
            return SpellWorkbenchSubmission.Reject(refusal, refusalReason);

        var expectedLayout = ReadIds(native, augments);
        var before = ReadMatchingInstanceIds(native, manager, recipe, expectedLayout);
        if (!TryCapturePermit(out var permitReason))
            return SpellWorkbenchSubmission.Reject(
                SpellWorkbenchPreflight.MutationPermitUnavailable, permitReason);

        var nativeCalls = 0;
        var stage = SpellWorkbenchNativeStage.ClearSelection;
        var previousCore = Copy(native.ReadGlyphValues(native.ReadCore(manager)));
        var previousAugments = Copy(native.ReadGlyphValues(native.ReadAugments(manager)));
        try
        {
            ApplySelection(native, manager, core, augments, ref nativeCalls);
            stage = SpellWorkbenchNativeStage.ApplySelection;

            var selectedCore = Copy(native.ReadGlyphValues(native.ReadCore(manager)));
            var selectedAugments = Copy(native.ReadGlyphValues(native.ReadAugments(manager)));
            if (!TryAdmitCreate(native, manager, recipe, selectedCore, selectedAugments,
                    out refusal, out refusalReason, out createCost))
            {
                if (RestoreSelection(
                        native, manager, previousCore, previousAugments, ref nativeCalls))
                    return SpellWorkbenchSubmission.Reject(refusal, refusalReason);
                return FaultAfterCommit(in action, SpellWorkbenchPreflight.PostCommitFault,
                    SpellWorkbenchNativeStage.ApplySelection,
                    NativeMutationOutcome.PostconditionFailed, nativeCalls,
                    "Live admission changed and the prior workbench selection could not be restored.");
            }

            stage = SpellWorkbenchNativeStage.Payment;
            native.PerformCost(createCost!);
            nativeCalls++;
            stage = SpellWorkbenchNativeStage.Create;
            native.Create(manager, recipe);
            nativeCalls++;
            var restored = RestoreSelection(
                native, manager, previousCore, previousAugments, ref nativeCalls);
            if (!restored)
                return FaultAfterCommit(in action, SpellWorkbenchPreflight.PostCommitFault,
                    SpellWorkbenchNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed, nativeCalls,
                    "The spell creation call returned but the prior workbench selection could not be restored.");
            return HasNewMatchingInstance(
                    native, manager, recipe, expectedLayout, before)
                ? Verified(SpellWorkbenchNativeStage.Verification, nativeCalls,
                    "A new spell with the exact requested augment layout is equipped.")
                : FaultAfterCommit(in action, SpellWorkbenchPreflight.VerificationFailed,
                    SpellWorkbenchNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed, nativeCalls,
                    "No new spell with the requested exact layout was added.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var restored = RestoreSelection(
                native, manager, previousCore, previousAugments, ref nativeCalls);
            if (restored && HasNewMatchingInstanceBestEffort(
                    native, manager, recipe, expectedLayout, before))
                return Verified(SpellWorkbenchNativeStage.Verification, nativeCalls,
                    "The exact layout was added before the native fault.");
            return FaultAfterCommit(in action, SpellWorkbenchPreflight.PostCommitFault,
                stage, NativeMutationOutcome.ExecutionThrew, nativeCalls,
                restored
                    ? "Loadout add faulted after its payment boundary without the requested exact layout: " +
                        ex.GetBaseException().Message
                    : "Loadout add faulted and the prior workbench selection could not be restored: " +
                ex.GetBaseException().Message);
        }
    }

    private static bool TryReadUsableCore(
        SpellWorkbenchNativeBindings native,
        object recipe,
        out List<object> core,
        out string reason)
    {
        var authored = native.ReadRecipeGlyphs(recipe);
        core = new List<object>(authored.Count);
        for (var index = 0; index < authored.Count; index++)
        {
            var glyph = authored[index];
            if (glyph is null)
            {
                reason = "The recipe's authored core glyphs are not currently usable.";
                return false;
            }
            if (!TryValidateGlyph(
                    native,
                    glyph,
                    native.ReadIdentity(glyph),
                    expectAugment: false,
                    out reason))
            {
                return false;
            }
            core.Add(glyph);
        }
        if (core.Count == 0)
        {
            reason = "The recipe has no authored core glyph composition.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool TryAdmitCreate(
        SpellWorkbenchNativeBindings native,
        object manager,
        object recipe,
        IList<object> core,
        IList<object> augments,
        out SpellWorkbenchPreflight refusal,
        out string reason,
        out object? createCost)
    {
        createCost = null;
        if (!native.IsDiscovered(recipe))
            return Refuse(SpellWorkbenchPreflight.DiscoveryUnavailable,
                "Loadout add requires an already discovered recipe.",
                out refusal, out reason);
        if (!native.IsCreatable(recipe))
            return Refuse(SpellWorkbenchPreflight.RecipeUnavailable,
                "The requested spell is not currently craftable.",
                out refusal, out reason);

        var candidate = native.CreateEmptySpell(recipe, 0);
        native.SetSpellLevel(candidate, native.GetSelectedSpellLevel(recipe));
        var record = native.CreateStackedRecord();
        SetStackedLayout(native, record, augments);
        native.SetSpellAugments(candidate, record);
        var nativeAugments = NativeGlyphList(native, augments);
        if (!native.MeetsNonLevelRequirements(nativeAugments, candidate))
            return Refuse(SpellWorkbenchPreflight.GlyphRequirementsUnavailable,
                "The selected glyph layout does not meet its duration/toggle requirements.",
                out refusal, out reason);
        if (!native.HasMetUsageRequirements(recipe) && augments.Count == 0)
            return Refuse(SpellWorkbenchPreflight.UsageRequirementsUnavailable,
                "The recipe's usage requirements are unmet; the UI permits its override only with a selected augment.",
                out refusal, out reason);
        var usageCost = native.GetUsageCost(candidate);
        if (!native.HasEnough(usageCost))
            return Refuse(SpellWorkbenchPreflight.UsageUnaffordable,
                "The candidate spell exceeds the live usage budget.",
                out refusal, out reason);
        var active = native.ReadActive(manager);
        if (!native.HasEmpty(active))
            return Refuse(SpellWorkbenchPreflight.LoadoutFull,
                "The native active-spell list has no empty slot.",
                out refusal, out reason);
        if (native.IsUniqueSpell(candidate) && HasActiveRecipe(native, manager, recipe))
            return Refuse(SpellWorkbenchPreflight.UniqueSpellConflict,
                "The candidate is loadout-unique and this recipe is already equipped.",
                out refusal, out reason);

        var combined = new List<object>(core.Count + augments.Count);
        for (var index = 0; index < core.Count; index++) combined.Add(core[index]);
        for (var index = 0; index < augments.Count; index++) combined.Add(augments[index]);
        var nativeLayout = NativeGlyphList(native, combined);
        var resolved = native.ResolveRecipe(manager, nativeLayout);
        if (resolved is null || resolved.GetType() != native.RecipeType ||
            native.ReadIdentity(resolved) != native.ReadIdentity(recipe))
            return Refuse(SpellWorkbenchPreflight.WrongSelection,
                "The exact live glyph layout no longer resolves to the requested spell.",
                out refusal, out reason);
        createCost = native.GetCreationCost(manager, nativeLayout);
        if (createCost is null)
            return Refuse(SpellWorkbenchPreflight.ContractUnavailable,
                "The game did not provide a creation price for this spell layout.",
                out refusal, out reason);
        if (!native.HasEnough(createCost))
            return Refuse(SpellWorkbenchPreflight.Unaffordable,
                UnaffordableReason(native, createCost),
                out refusal, out reason);
        refusal = SpellWorkbenchPreflight.Proceeded;
        reason = string.Empty;
        return true;
    }

    private static bool Refuse(
        SpellWorkbenchPreflight exact,
        string exactReason,
        out SpellWorkbenchPreflight refusal,
        out string reason)
    {
        refusal = exact;
        reason = exactReason;
        return false;
    }

    private static bool HasActiveRecipe(
        SpellWorkbenchNativeBindings native,
        object manager,
        object recipe)
    {
        var recipeId = native.ReadIdentity(recipe);
        var active = native.ReadActiveValues(native.ReadActive(manager));
        for (var index = 0; index < active.Count; index++)
        {
            var spell = active[index];
            if (spell is null) continue;
            var reference = native.ReadSpellReference(spell);
            if (reference is not null && reference.GetType() == native.RecipeType &&
                native.ReadIdentity(reference) == recipeId) return true;
        }
        return false;
    }

    private static void SetStackedLayout(
        SpellWorkbenchNativeBindings native,
        object record,
        IList<object> augments)
    {
        var counts = new Dictionary<Guid, int>();
        var values = new Dictionary<Guid, object>();
        for (var index = 0; index < augments.Count; index++)
        {
            var id = native.ReadIdentity(augments[index]);
            counts.TryGetValue(id, out var count);
            counts[id] = count + 1;
            values[id] = augments[index];
        }
        foreach (var pair in counts)
            native.SetStackedRecord(record, values[pair.Key], pair.Value);
    }

    private bool TryResolveGlyphLayout(
        SpellWorkbenchGlyphStack[] layout,
        bool expectAugment,
        SpellWorkbenchNativeBindings native,
        out List<object> glyphs,
        out string reason)
    {
        glyphs = new List<object>();
        var resolved = new Dictionary<Guid, object>();
        var totals = new Dictionary<Guid, int>();
        var order = new List<Guid>();
        for (var index = 0; index < layout.Length; index++)
        {
            var stack = layout[index];
            if (!totals.ContainsKey(stack.GlyphId)) order.Add(stack.GlyphId);
            totals.TryGetValue(stack.GlyphId, out var prior);
            var total = (long)prior + stack.Count;
            if (total > int.MaxValue)
            {
                reason = "The requested glyph count exceeds the supported native integer range.";
                return false;
            }
            totals[stack.GlyphId] = (int)total;
        }
        for (var index = 0; index < order.Count; index++)
        {
            var glyphId = order[index];
            var resolution = _registry.Resolve(glyphId, native.GlyphType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
            {
                reason = resolution.IsResolved
                    ? "The glyph registry resolution became stale."
                    : resolution.Reason;
                return false;
            }
            var glyph = resolution.Value!;
            if (!TryValidateGlyph(native, glyph, glyphId, expectAugment, out reason))
                return false;
            var maximum = native.GetGlyphMaximumUsages(glyph);
            var requested = totals[glyphId];
            if (requested > maximum)
            {
                reason = "Requested " + requested + " uses of " +
                    EntityIdentityFormatter.Format(glyphId) +
                    ", but the live usable count is " + maximum + ".";
                return false;
            }
            resolved[glyphId] = glyph;
        }
        for (var index = 0; index < order.Count; index++)
        {
            var glyphId = order[index];
            for (var count = 0; count < totals[glyphId]; count++)
                glyphs.Add(resolved[glyphId]);
        }
        reason = string.Empty;
        return true;
    }

    private static bool TryValidateGlyph(
        SpellWorkbenchNativeBindings native,
        object glyph,
        Guid glyphId,
        bool expectAugment,
        out string reason)
    {
        if (glyph.GetType() != native.GlyphType)
        {
            reason = "The resolved spell component has the wrong native type.";
            return false;
        }
        if (native.IsGlyphAugment(glyph) != expectAugment)
        {
            reason = EntityIdentityFormatter.Format(glyphId) +
                (expectAugment
                    ? " is not a spell augment."
                    : " is an augment and cannot be a core discovery component.");
            return false;
        }
        if (!native.IsGlyphAvailable(glyph))
        {
            reason = EntityIdentityFormatter.Format(glyphId) +
                " is not available for this spell layout.";
            return false;
        }
        if (native.ReadGlyphLevel(glyph) <= 0)
        {
            reason = EntityIdentityFormatter.Format(glyphId) +
                " is not owned; spell glyphs require an owned level above zero.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static string UnaffordableReason(
        SpellWorkbenchNativeBindings native,
        object createCost)
    {
        var totals = new Dictionary<Guid, (object Resource, BigDouble Cost)>();
        var rows = native.ReadCostEntries(createCost);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row is null) continue;
            var resource = native.ReadCostResource(row);
            if (resource is null) continue;
            var id = native.ReadIdentity(resource);
            totals.TryGetValue(id, out var current);
            totals[id] = (resource, current.Cost + native.ReadCostValue(row));
        }
        foreach (var pair in totals)
            if (!native.HasResourceAmount(pair.Value.Resource, pair.Value.Cost))
                return EntityIdentityFormatter.Format(pair.Key) +
                    " is short for this spell layout.";
        return "The requested spell layout is not affordable with the current resources.";
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

    private static IList NativeGlyphList(
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

    private static Guid[] ReadMatchingInstanceIds(SpellWorkbenchNativeBindings native,
        object manager, object targetRecipe, Guid[] expectedLayout)
    {
        var matching = new List<Guid>();
        var targetId = native.ReadIdentity(targetRecipe);
        var active = native.ReadActiveValues(native.ReadActive(manager));
        for (var index = 0; index < active.Count; index++)
        {
            var spell = active[index];
            if (spell is null) continue;
            var reference = native.ReadSpellReference(spell);
            if (reference is null || reference.GetType() != native.RecipeType ||
                native.ReadIdentity(reference) != targetId) continue;
            var container = native.ReadSpellGuid(spell);
            var instanceId = container is null ? Guid.Empty : native.ReadGuidValue(container);
            if (SameLayout(native, native.ReadSpellAugments(spell), expectedLayout))
                matching.Add(instanceId);
        }
        return matching.ToArray();
    }

    private static bool HasNewMatchingInstance(
        SpellWorkbenchNativeBindings native,
        object manager,
        object recipe,
        Guid[] expectedLayout,
        Guid[] before) =>
        HasNewInstance(before, ReadMatchingInstanceIds(native, manager, recipe, expectedLayout));

    private static bool HasNewMatchingInstanceBestEffort(
        SpellWorkbenchNativeBindings native,
        object manager,
        object recipe,
        Guid[] expectedLayout,
        Guid[] before)
    {
        try { return HasNewMatchingInstance(native, manager, recipe, expectedLayout, before); }
        catch (Exception ex) when (IsExpected(ex)) { return false; }
    }

    private static bool IsDiscoveredBestEffort(
        SpellWorkbenchNativeBindings native,
        object recipe)
    {
        try { return native.IsDiscovered(recipe); }
        catch (Exception ex) when (IsExpected(ex)) { return false; }
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

    private static bool SameLayout(
        SpellWorkbenchNativeBindings native,
        IList actual,
        Guid[] expected)
    {
        if (actual.Count != expected.Length) return false;
        var counts = new Dictionary<Guid, int>();
        for (var index = 0; index < expected.Length; index++)
        {
            counts.TryGetValue(expected[index], out var count);
            counts[expected[index]] = count + 1;
        }
        for (var index = 0; index < actual.Count; index++)
        {
            var value = actual[index];
            if (value is null) return false;
            var id = native.ReadIdentity(value);
            if (!counts.TryGetValue(id, out var count) || count == 0) return false;
            if (count == 1) counts.Remove(id);
            else counts[id] = count - 1;
        }
        return counts.Count == 0;
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
        int nativeCalls, string reason)
    {
        return new SpellWorkbenchSubmission(SpellWorkbenchPreflight.Proceeded, stage,
            NativeMutationOutcome.Verified, new NativeMutationCallOutcome(nativeCalls, 1, 1),
            reason);
    }

    private static SpellWorkbenchSubmission FaultAfterCommit(in SpellWorkbenchAction action,
        SpellWorkbenchPreflight preflight, SpellWorkbenchNativeStage stage,
        NativeMutationOutcome outcome, int nativeCalls, string reason)
    {
        var exactReason = $"Spell workbench action faulted after {stage} on " +
            $"recipe {EntityIdentityFormatter.Format(action.SpellRecipeId)}: {reason}";
        return new SpellWorkbenchSubmission(preflight, stage, outcome,
            new NativeMutationCallOutcome(nativeCalls, 1, 0), exactReason);
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
