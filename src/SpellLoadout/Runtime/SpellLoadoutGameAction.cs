using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// The single mutation boundary for removing an equipped spell or moving it to another loadout
/// position. It re-resolves the runtime UUID and every mutable gate on Unity's main thread.
/// </summary>
internal sealed class SpellLoadoutGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private SpellLoadoutNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;
    private string _quarantineReason = string.Empty;

    internal SpellLoadoutGameAction(
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
    internal bool IsQuarantined => _quarantineReason.Length != 0;

    internal SpellLoadoutSubmission Submit(in SpellLoadoutAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.WrongThread,
                "Spell loadout actions are bound to Unity thread " + _mainThreadId +
                ", not thread " + Environment.CurrentManagedThreadId + ".");
        if (_quarantineReason.Length != 0)
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.Quarantined,
                _quarantineReason);
        if (_bindings is not { } native)
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.ContractUnavailable,
                _bindingFailure.Length == 0
                    ? "The lifecycle-scoped spell loadout binding set is unavailable."
                    : _bindingFailure);

        long currentEpoch;
        try { currentEpoch = _readLifecycleEpoch(); }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.LifecycleReplaced,
                "The current lifecycle epoch could not be read: " + ex.GetBaseException().Message);
        }
        if (currentEpoch != action.LifecycleEpoch)
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.LifecycleReplaced,
                "Action lifecycle " + action.LifecycleEpoch +
                " is stale; the live lifecycle is " + currentEpoch + ".");

        try
        {
            return action.Kind switch
            {
                SpellLoadoutActionKind.Remove => Remove(in action, native),
                SpellLoadoutActionKind.Move => Move(in action, native),
                _ => SpellLoadoutSubmission.Reject(
                    SpellLoadoutPreflight.ContractUnavailable,
                    "Unknown spell loadout action kind " + (int)action.Kind + "."),
            };
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.ContractUnavailable,
                "Spell loadout preflight failed before mutation: " + ex.GetBaseException().Message);
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

    private SpellLoadoutSubmission Remove(
        in SpellLoadoutAction action,
        SpellLoadoutNativeBindings native)
    {
        if (!TryResolve(native, action.SpellInstanceId, out var manager, out _, out var spell,
                out var sourceSlot, out var before, out var reason))
            return SpellLoadoutSubmission.Reject(SpellLoadoutPreflight.IdentityUnavailable, reason);
        if (!native.CanRemove(spell))
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.NativeRemoveRefused,
                "Native Spell.CanRemove() refused runtime spell " +
                EntityIdentityFormatter.Format(action.SpellInstanceId) + ".");
        if (!TryCapturePermit(out reason))
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.MutationPermitUnavailable,
                reason);

        try
        {
            native.Remove(manager, spell);
            var after = Capture(native);
            return Removed(in before, in after, action.SpellInstanceId)
                ? Verified(
                    sourceSlot,
                    -1,
                    in before,
                    in after,
                    new NativeMutationCallOutcome(1, 1, 1),
                    "The exact runtime spell is absent and every surviving spell remains in order.")
                : Quarantine(
                    in action,
                    SpellLoadoutPreflight.VerificationFailed,
                    SpellLoadoutNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed,
                    sourceSlot,
                    -1,
                    in before,
                    in after,
                    new NativeMutationCallOutcome(1, 1, 0),
                    "Removal did not produce exact target absence with survivor-order preservation.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var after = CaptureBestEffort(native, in before);
            if (Removed(in before, in after, action.SpellInstanceId))
                return Verified(
                    sourceSlot,
                    -1,
                    in before,
                    in after,
                    new NativeMutationCallOutcome(1, 1, 1),
                    "SpellManager.RemoveSpell threw after the requested removal became observable.");
            return Quarantine(
                in action,
                SpellLoadoutPreflight.PostCommitFault,
                SpellLoadoutNativeStage.Remove,
                NativeMutationOutcome.ExecutionThrew,
                sourceSlot,
                -1,
                in before,
                in after,
                new NativeMutationCallOutcome(1, 1, 0),
                "SpellManager.RemoveSpell threw before the requested outcome was observable: " +
                ex.GetBaseException().Message);
        }
    }

    private SpellLoadoutSubmission Move(
        in SpellLoadoutAction action,
        SpellLoadoutNativeBindings native)
    {
        if (!TryResolve(native, action.SpellInstanceId, out _, out var active, out _,
                out var sourceSlot, out var before, out var reason))
            return SpellLoadoutSubmission.Reject(SpellLoadoutPreflight.IdentityUnavailable, reason);
        if (action.DestinationSlot >= before.Slots.Length)
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.DestinationOutOfRange,
                "Destination slot " + action.DestinationSlot +
                " is outside the live native range 0.." + (before.Slots.Length - 1) + ".");
        if (sourceSlot == action.DestinationSlot)
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.AlreadyInRequestedState,
                "Runtime spell " + EntityIdentityFormatter.Format(action.SpellInstanceId) +
                " is already in slot " + sourceSlot + ".");
        if (!TryCapturePermit(out reason))
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.MutationPermitUnavailable,
                reason);

        var nativeCalls = 0;
        var stage = SpellLoadoutNativeStage.Swap;
        try
        {
            nativeCalls = 1;
            native.Swap(active, sourceSlot, action.DestinationSlot);
            stage = SpellLoadoutNativeStage.Notify;
            nativeCalls = 2;
            native.UpdateObservable(active);
            var after = Capture(native);
            return Moved(in before, in after, sourceSlot, action.DestinationSlot)
                ? Verified(
                    sourceSlot,
                    action.DestinationSlot,
                    in before,
                    in after,
                    new NativeMutationCallOutcome(2, 1, 1),
                    "The exact ordered loadout now contains the requested two-position swap.")
                : Quarantine(
                    in action,
                    SpellLoadoutPreflight.VerificationFailed,
                    SpellLoadoutNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed,
                    sourceSlot,
                    action.DestinationSlot,
                    in before,
                    in after,
                    new NativeMutationCallOutcome(2, 1, 0),
                    "Reordering changed more or less than the exact requested two-position swap.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var after = CaptureBestEffort(native, in before);
            if (Moved(in before, in after, sourceSlot, action.DestinationSlot))
                return Verified(
                    sourceSlot,
                    action.DestinationSlot,
                    in before,
                    in after,
                    new NativeMutationCallOutcome(nativeCalls, 1, 1),
                    "The native reorder pipeline threw after the requested slot order became observable.");
            return Quarantine(
                in action,
                SpellLoadoutPreflight.PostCommitFault,
                stage,
                NativeMutationOutcome.ExecutionThrew,
                sourceSlot,
                action.DestinationSlot,
                in before,
                in after,
                new NativeMutationCallOutcome(nativeCalls, 1, 0),
                "The native reorder pipeline threw before the requested outcome was observable: " +
                ex.GetBaseException().Message);
        }
    }

    private static bool TryResolve(
        SpellLoadoutNativeBindings native,
        Guid targetId,
        out object manager,
        out object active,
        out object spell,
        out int slot,
        out SpellLoadoutState state,
        out string reason)
    {
        manager = native.ReadManager()!;
        active = null!;
        spell = null!;
        slot = -1;
        state = default;
        if (manager is null)
        {
            reason = "SpellManager.instance is unavailable in this lifecycle.";
            return false;
        }
        active = native.ReadActive(manager);
        var values = native.ReadActiveValues(active);
        var ids = new Guid[values.Count];
        var names = new string[values.Count];
        var matches = 0;
        for (var index = 0; index < values.Count; index++)
        {
            var candidate = values[index];
            if (candidate is null) continue;
            if (candidate.GetType() != native.SpellType)
                throw new InvalidOperationException("Equipped slot " + index + " did not hold an exact Spell.");
            if (native.IsEmpty(candidate)) continue;
            var id = ReadIdentity(native, candidate, index);
            ids[index] = id;
            names[index] = NativeName(native, candidate);
            if (id != targetId) continue;
            spell = candidate;
            slot = index;
            matches++;
        }
        state = new SpellLoadoutState(ids, names);
        if (matches == 1)
        {
            reason = string.Empty;
            return true;
        }
        reason = matches == 0
            ? "No exact equipped Spell with runtime identity " +
              EntityIdentityFormatter.Format(targetId) + " exists."
            : "Runtime Spell identity " + EntityIdentityFormatter.Format(targetId) +
              " is ambiguous across " + matches + " exact instances.";
        return false;
    }

    private static SpellLoadoutState Capture(SpellLoadoutNativeBindings native)
    {
        var manager = native.ReadManager() ??
            throw new InvalidOperationException("SpellManager.instance was null during verification.");
        var active = native.ReadActive(manager);
        var values = native.ReadActiveValues(active);
        var ids = new Guid[values.Count];
        var names = new string[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var candidate = values[index];
            if (candidate is null) continue;
            if (candidate.GetType() != native.SpellType)
                throw new InvalidOperationException("Equipped slot " + index + " did not hold an exact Spell.");
            if (native.IsEmpty(candidate)) continue;
            ids[index] = ReadIdentity(native, candidate, index);
            names[index] = NativeName(native, candidate);
        }
        return new SpellLoadoutState(ids, names);
    }

    private static string NativeName(SpellLoadoutNativeBindings native, object spell)
    {
        var name = native.GetName(spell)?.Trim() ?? string.Empty;
        return name.Length == 0 ? "Equipped spell" : name;
    }

    private static Guid ReadIdentity(
        SpellLoadoutNativeBindings native,
        object spell,
        int slot)
    {
        var container = native.ReadSpellGuid(spell) ??
            throw new InvalidOperationException("Equipped slot " + slot + " had no GuidContainer.");
        var id = native.ReadGuidValue(container);
        if (id == Guid.Empty)
            throw new InvalidOperationException("Equipped slot " + slot + " had an empty runtime UUID.");
        return id;
    }

    private static SpellLoadoutState CaptureBestEffort(
        SpellLoadoutNativeBindings native,
        in SpellLoadoutState fallback)
    {
        try { return Capture(native); }
        catch (Exception ex) when (IsExpected(ex)) { return fallback; }
    }

    private static bool Removed(
        in SpellLoadoutState before,
        in SpellLoadoutState after,
        Guid target)
    {
        var expected = new List<Guid>();
        for (var index = 0; index < before.Slots.Length; index++)
        {
            var id = before.Slots[index];
            if (id != Guid.Empty && id != target) expected.Add(id);
        }
        var observed = new List<Guid>();
        for (var index = 0; index < after.Slots.Length; index++)
        {
            var id = after.Slots[index];
            if (id == target) return false;
            if (id != Guid.Empty) observed.Add(id);
        }
        if (expected.Count != observed.Count) return false;
        for (var index = 0; index < expected.Count; index++)
            if (expected[index] != observed[index]) return false;
        return true;
    }

    private static bool Moved(
        in SpellLoadoutState before,
        in SpellLoadoutState after,
        int source,
        int destination)
    {
        if (before.Slots.Length != after.Slots.Length) return false;
        for (var index = 0; index < before.Slots.Length; index++)
        {
            var expected = index == source
                ? before.Slots[destination]
                : index == destination
                    ? before.Slots[source]
                    : before.Slots[index];
            if (after.Slots[index] != expected) return false;
        }
        return true;
    }

    private static SpellLoadoutSubmission Verified(
        int sourceSlot,
        int destinationSlot,
        in SpellLoadoutState before,
        in SpellLoadoutState after,
        NativeMutationCallOutcome callOutcome,
        string reason)
    {
        var evidence = new SpellLoadoutEvidence(
            true, sourceSlot, destinationSlot, in before, in after);
        return new SpellLoadoutSubmission(
            SpellLoadoutPreflight.Proceeded,
            SpellLoadoutNativeStage.Verification,
            NativeMutationOutcome.Verified,
            callOutcome,
            in evidence,
            reason);
    }

    private SpellLoadoutSubmission Quarantine(
        in SpellLoadoutAction action,
        SpellLoadoutPreflight preflight,
        SpellLoadoutNativeStage stage,
        NativeMutationOutcome outcome,
        int sourceSlot,
        int destinationSlot,
        in SpellLoadoutState before,
        in SpellLoadoutState after,
        NativeMutationCallOutcome callOutcome,
        string reason)
    {
        _quarantineReason = "Spell loadout is quarantined for this lifecycle after " + stage +
            " on " + EntityIdentityFormatter.Format(action.SpellInstanceId) + ": " + reason;
        var evidence = new SpellLoadoutEvidence(
            true, sourceSlot, destinationSlot, in before, in after);
        return new SpellLoadoutSubmission(
            preflight,
            stage,
            outcome,
            callOutcome,
            in evidence,
            _quarantineReason);
    }

    private bool TryCapturePermit(out string reason)
    {
        if (_tryCaptureMutationPermit())
        {
            reason = string.Empty;
            return true;
        }
        reason = _readOwnershipFailure();
        if (reason.Length == 0)
            reason = "The suite does not own the spell loadout action family.";
        return false;
    }

    private void BindLifecycle()
    {
        var resolve = _resolveType ?? ReflectionUtil.FindLoadedType;
        var include = _includeContract ?? (_ => true);
        if (!SpellLoadoutNativeBindings.TryCreate(
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
