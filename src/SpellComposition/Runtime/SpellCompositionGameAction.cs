using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-bound mutation boundary for the global spell output level and one spell's augments.</summary>
internal sealed class SpellCompositionGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private SpellCompositionNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal SpellCompositionGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal SpellCompositionSubmission Submit(in SpellCompositionAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.WrongThread,
                "Spell composition is bound to Unity thread " + _mainThreadId +
                ", not thread " + Environment.CurrentManagedThreadId + ".");
        if (_bindings is not { } native)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.ContractUnavailable,
                _bindingFailure.Length == 0
                    ? "The lifecycle-scoped spell composition binding set is unavailable."
                    : _bindingFailure);

        long currentEpoch;
        try { currentEpoch = _readLifecycleEpoch(); }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.LifecycleReplaced,
                "The current lifecycle epoch could not be read: " + ex.GetBaseException().Message);
        }
        if (currentEpoch != action.LifecycleEpoch)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.LifecycleReplaced,
                "Action lifecycle " + action.LifecycleEpoch +
                " is stale; the live lifecycle is " + currentEpoch + ".");

        try
        {
            return action.Kind switch
            {
                SpellCompositionActionKind.SetOutputLevel => SetOutputLevel(in action, native),
                SpellCompositionActionKind.SetAugments => SetAugments(in action, native),
                _ => SpellCompositionSubmission.Reject(
                    SpellCompositionPreflight.ContractUnavailable,
                    "Unknown spell composition action kind " + (int)action.Kind + "."),
            };
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.ContractUnavailable,
                "Spell composition preflight failed before mutation: " +
                ex.GetBaseException().Message);
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

    private SpellCompositionSubmission SetOutputLevel(
        in SpellCompositionAction action,
        SpellCompositionNativeBindings native)
    {
        var player = native.ReadPlayer();
        var output = native.ReadOutputVariable();
        if (player is null || output is null)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.ContractUnavailable,
                "Player output-level state is not initialized in this lifecycle.");
        var maximumVariable = native.ReadMaximumOutputVariable(player);
        var current = native.ReadInt(output);
        var maximum = native.ReadInt(maximumVariable);
        if (action.OutputLevel < 1 || action.OutputLevel > maximum)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.OutputLevelOutOfRange,
                "Requested output level " + action.OutputLevel +
                " is outside the live native range 1.." + maximum + ".");
        if (current == action.OutputLevel)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.AlreadyInRequestedState,
                "The global spell output level is already " + current + ".");
        var before = Capture(native, null);
        if (!TryCapturePermit(out var reason))
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.MutationPermitUnavailable,
                reason);
        try
        {
            native.SetInt(output, action.OutputLevel);
            var after = Capture(native, null);
            return after.OutputLevel == action.OutputLevel
                ? Verified(in before, in after, "The global spell output level is now " + action.OutputLevel + ".")
                : FaultAfterCommit(
                    in action,
                    SpellCompositionPreflight.VerificationFailed,
                    SpellCompositionNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed,
                    in before,
                    in after,
                    "The global output-level variable did not hold the requested value.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var after = CaptureBestEffort(native, null, in before);
            if (after.OutputLevel == action.OutputLevel)
                return Verified(
                    in before,
                    in after,
                    "The output-level setter threw after the requested value became observable.");
            return FaultAfterCommit(
                in action,
                SpellCompositionPreflight.PostCommitFault,
                SpellCompositionNativeStage.OutputLevel,
                NativeMutationOutcome.ExecutionThrew,
                in before,
                in after,
                "The output-level setter threw before the requested outcome was observable: " +
                ex.GetBaseException().Message);
        }
    }

    private SpellCompositionSubmission SetAugments(
        in SpellCompositionAction action,
        SpellCompositionNativeBindings native)
    {
        if (!TryResolveSpell(native, action.SpellInstanceId, out var spell, out var reason))
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.IdentityUnavailable,
                reason);

        var requested = (SpellCompositionGlyphStack[])action.AugmentGlyphs.Clone();
        Array.Sort(requested, static (left, right) => left.GlyphId.CompareTo(right.GlyphId));
        var expanded = native.CreateGlyphList();
        var record = native.CreateRecord();
        Guid previous = Guid.Empty;
        for (var index = 0; index < requested.Length; index++)
        {
            var stack = requested[index];
            if (stack.GlyphId == previous)
                return SpellCompositionSubmission.Reject(
                    SpellCompositionPreflight.DuplicateGlyph,
                    "Augment glyph " + EntityIdentityFormatter.Format(stack.GlyphId) +
                    " appears more than once; combine its count into one row.");
            previous = stack.GlyphId;
            if (!TryResolveGlyph(native, stack.GlyphId, out var glyph, out reason))
                return SpellCompositionSubmission.Reject(
                    SpellCompositionPreflight.GlyphIdentityUnavailable,
                    reason);
            if (!native.IsGlyphAvailable(glyph))
                return SpellCompositionSubmission.Reject(
                    SpellCompositionPreflight.GlyphUnavailable,
                    "Native GlyphSO.IsAvailable() refused " +
                    EntityIdentityFormatter.Format(stack.GlyphId) + ".");
            if (!native.IsGlyphAugment(glyph))
                return SpellCompositionSubmission.Reject(
                    SpellCompositionPreflight.NotAnAugment,
                    "Glyph " + EntityIdentityFormatter.Format(stack.GlyphId) +
                    " is not a spell augment.");
            var maximum = native.GetGlyphMaximumUsages(glyph);
            if (stack.Count > maximum)
                return SpellCompositionSubmission.Reject(
                    SpellCompositionPreflight.UsageLimitExceeded,
                    "Requested " + stack.Count + " uses of " +
                    EntityIdentityFormatter.Format(stack.GlyphId) +
                    ", but the live native maximum is " + maximum + ".");
            native.SetRecord(record, glyph, stack.Count);
            for (var quantity = 0; quantity < stack.Count; quantity++) expanded.Add(glyph);
        }
        if (!native.MeetsNonLevelRequirements(expanded, spell))
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.IncompatibleComposition,
                "GlyphSO.MeetsNonLvRequirements() refused the exact requested composition for this spell.");
        var requiredMastery = native.GetMasteryRequirement(expanded);
        var recipeMastery = native.GetRecipeMastery(spell);
        if (requiredMastery > recipeMastery)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.MasteryRequirementUnmet,
                "The composition requires recipe mastery " + requiredMastery +
                ", but the live spell recipe has mastery " + recipeMastery + ".");
        var before = Capture(native, spell);
        if (Same(before.AugmentGlyphs, requested))
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.AlreadyInRequestedState,
                "The exact requested augment composition is already applied.");
        if (!TryCapturePermit(out reason))
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.MutationPermitUnavailable,
                reason);

        try
        {
            native.SetAugments(spell, record);
            var after = Capture(native, spell);
            return Same(after.AugmentGlyphs, requested) &&
                after.SpellInstanceId == action.SpellInstanceId
                ? Verified(in before, in after, "The exact augment composition is now applied to the requested spell instance.")
                : FaultAfterCommit(
                    in action,
                    SpellCompositionPreflight.VerificationFailed,
                    SpellCompositionNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed,
                    in before,
                    in after,
                    "The requested spell instance did not expose the exact requested augment composition.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var after = CaptureBestEffort(native, spell, in before);
            if (Same(after.AugmentGlyphs, requested) &&
                after.SpellInstanceId == action.SpellInstanceId)
                return Verified(
                    in before,
                    in after,
                    "The augment setter threw after the requested composition became observable.");
            return FaultAfterCommit(
                in action,
                SpellCompositionPreflight.PostCommitFault,
                SpellCompositionNativeStage.AugmentComposition,
                NativeMutationOutcome.ExecutionThrew,
                in before,
                in after,
                "The augment setter threw before the requested outcome was observable: " +
                ex.GetBaseException().Message);
        }
    }

    private static SpellCompositionState Capture(
        SpellCompositionNativeBindings native,
        object? spell)
    {
        var player = native.ReadPlayer() ??
            throw new InvalidOperationException("Player._instance was null.");
        var output = native.ReadOutputVariable() ??
            throw new InvalidOperationException("Player.GetSpellOutputLevel() returned null.");
        var outputLevel = native.ReadInt(output);
        var maximum = native.ReadInt(native.ReadMaximumOutputVariable(player));
        if (spell is null)
            return new SpellCompositionState(
                outputLevel,
                maximum,
                Guid.Empty,
                Guid.Empty,
                Array.Empty<SpellCompositionGlyphStack>());
        var instance = native.ReadSpellGuid(spell);
        var instanceId = instance is null ? Guid.Empty : native.ReadGuidValue(instance);
        var recipe = native.ReadSpellReference(spell);
        var recipeId = recipe is null ? Guid.Empty : native.ReadIdentity(recipe);
        var values = native.ReadSpellAugments(spell);
        var rows = new List<SpellCompositionGlyphStack>(values.Count);
        var seen = new HashSet<Guid>();
        for (var index = 0; index < values.Count; index++)
        {
            var glyph = values[index];
            if (glyph is null) continue;
            var id = native.ReadIdentity(glyph);
            if (id == Guid.Empty || !seen.Add(id)) continue;
            var count = native.GetGlyphQuantity(spell, glyph);
            if (count > 0) rows.Add(new SpellCompositionGlyphStack(id, count));
        }
        rows.Sort(static (left, right) => left.GlyphId.CompareTo(right.GlyphId));
        return new SpellCompositionState(
            outputLevel,
            maximum,
            instanceId,
            recipeId,
            rows.ToArray());
    }

    private static SpellCompositionState CaptureBestEffort(
        SpellCompositionNativeBindings native,
        object? spell,
        in SpellCompositionState fallback)
    {
        try { return Capture(native, spell); }
        catch (Exception ex) when (IsExpected(ex)) { return fallback; }
    }

    private static bool TryResolveSpell(
        SpellCompositionNativeBindings native,
        Guid id,
        out object spell,
        out string reason)
    {
        spell = null!;
        var manager = native.ReadManager();
        if (manager is null)
        {
            reason = "SpellManager.instance is unavailable in this lifecycle.";
            return false;
        }
        var values = native.ReadActiveValues(native.ReadActive(manager));
        var matches = 0;
        for (var index = 0; index < values.Count; index++)
        {
            var candidate = values[index];
            if (candidate is null || candidate.GetType() != native.SpellType) continue;
            var container = native.ReadSpellGuid(candidate);
            if (container is null || native.ReadGuidValue(container) != id) continue;
            spell = candidate;
            matches++;
        }
        if (matches == 1)
        {
            reason = string.Empty;
            return true;
        }
        reason = matches == 0
            ? "No exact equipped Spell with runtime identity " + EntityIdentityFormatter.Format(id) + " exists."
            : "Runtime Spell identity " + EntityIdentityFormatter.Format(id) +
              " is ambiguous across " + matches + " exact instances.";
        return false;
    }

    private static bool TryResolveGlyph(
        SpellCompositionNativeBindings native,
        Guid id,
        out object glyph,
        out string reason)
    {
        glyph = null!;
        var matches = 0;
        foreach (var candidate in native.ReadGlyphs())
        {
            if (candidate is null || candidate.GetType() != native.GlyphType ||
                native.ReadIdentity(candidate) != id) continue;
            glyph = candidate;
            matches++;
        }
        if (matches == 1)
        {
            reason = string.Empty;
            return true;
        }
        reason = matches == 0
            ? "No exact GlyphSO with identity " + EntityIdentityFormatter.Format(id) + " exists."
            : "GlyphSO identity " + EntityIdentityFormatter.Format(id) +
              " is ambiguous across " + matches + " exact instances.";
        return false;
    }

    private static bool Same(
        SpellCompositionGlyphStack[] left,
        SpellCompositionGlyphStack[] right)
    {
        if (left.Length != right.Length) return false;
        for (var index = 0; index < left.Length; index++)
            if (left[index].GlyphId != right[index].GlyphId || left[index].Count != right[index].Count)
                return false;
        return true;
    }

    private static SpellCompositionSubmission Verified(
        in SpellCompositionState before,
        in SpellCompositionState after,
        string reason)
    {
        var evidence = new SpellCompositionEvidence(true, in before, in after);
        return new SpellCompositionSubmission(
            SpellCompositionPreflight.Proceeded,
            SpellCompositionNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1),
            in evidence,
            reason);
    }

    private static SpellCompositionSubmission FaultAfterCommit(
        in SpellCompositionAction action,
        SpellCompositionPreflight preflight,
        SpellCompositionNativeStage stage,
        NativeMutationOutcome outcome,
        in SpellCompositionState before,
        in SpellCompositionState after,
        string reason)
    {
        var target = action.Kind == SpellCompositionActionKind.SetOutputLevel
            ? "the global output level"
            : "spell " + EntityIdentityFormatter.Format(action.SpellInstanceId);
        var exactReason = "Spell composition faulted after " + stage + " on " +
            target + ": " + reason;
        var evidence = new SpellCompositionEvidence(true, in before, in after);
        return new SpellCompositionSubmission(
            preflight,
            stage,
            outcome,
            new NativeMutationCallOutcome(1, 1, 0),
            in evidence,
            exactReason);
    }

    private bool TryCapturePermit(out string reason)
    {
        if (_tryCaptureMutationPermit())
        {
            reason = string.Empty;
            return true;
        }
        reason = _readOwnershipFailure();
        if (reason.Length == 0) reason = "The suite does not own the spell composition action family.";
        return false;
    }

    private void BindLifecycle()
    {
        var resolve = _resolveType ?? ReflectionUtil.FindLoadedType;
        var include = _includeContract ?? (_ => true);
        if (!SpellCompositionNativeBindings.TryCreate(
                resolve,
                include,
                out _bindings,
                out _bindingFailure))
            _bindings = null;
    }

    private static bool IsExpected(Exception ex) =>
        ex is ArgumentException or InvalidOperationException or OverflowException or
            TargetInvocationException or MemberAccessException;
}
