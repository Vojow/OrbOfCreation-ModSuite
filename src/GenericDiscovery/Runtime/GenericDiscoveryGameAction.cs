using System;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Lifecycle-scoped generic discovery transaction. Admission and exact cost reads complete before
/// the mutation permit; the native UI's payment-then-discover order is preserved. Only exact target
/// identity and the requested discovered outcome gate success.
/// </summary>
internal sealed class GenericDiscoveryGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private GenericDiscoveryNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal GenericDiscoveryGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        var identity = RuntimeIdentityRegistryBinding.Shared;
        _registry = registry ?? new TypedRegistryResolver(
            _readLifecycleEpoch,
            identity.Read,
            identity.ReadStableUuid);
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal GenericDiscoverySubmission Submit(in GenericDiscoveryAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.WrongThread,
                "Generic discovery is bound to Unity thread " + _mainThreadId +
                ", not thread " + Environment.CurrentManagedThreadId + ".");
        if (_bindings is not { } native)
            return GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.ContractUnavailable,
                _bindingFailure.Length == 0
                    ? "The lifecycle-scoped generic discovery binding set is unavailable."
                    : _bindingFailure);

        long currentEpoch;
        try
        {
            currentEpoch = _readLifecycleEpoch();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.LifecycleReplaced,
                "The current lifecycle epoch could not be read: " +
                exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != currentEpoch)
            return GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.LifecycleReplaced,
                "Action lifecycle " + action.LifecycleEpoch +
                " is stale; the live lifecycle is " + currentEpoch + ".");

        try
        {
            if (!native.SupportedTypes.TryGetValue(action.ExpectedNativeType, out var expectedType))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.UnsupportedType,
                    "Native type " + action.ExpectedNativeType +
                    " is not in the audited generic discovery family.");
            var resolution = _registry.Resolve(action.TargetId, expectedType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.IdentityUnavailable,
                    resolution.IsResolved
                        ? "The typed registry resolution became stale before discovery admission."
                        : resolution.Reason);
            var target = resolution.Value!;
            if (!native.DiscoverableType.IsInstanceOfType(target))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.IdentityUnavailable,
                    "The exact registered " + action.ExpectedNativeType +
                    " does not implement IDiscoverable at the action boundary.");

            var before = CaptureState(native, target, action.ExpectedNativeType);
            if (before.Discovered)
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.AlreadyDiscovered,
                    EntityIdentityFormatter.Format(action.TargetId) + " is already discovered.");
            if (!before.Visible)
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.NotVisible,
                    "IDiscoverable.IsDiscoverVisible() refused " +
                    EntityIdentityFormatter.Format(action.TargetId) + ".");
            if (!before.CanDiscover)
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.DiscoveryUnavailable,
                    "IDiscoverable.CanDiscover() refused " +
                    EntityIdentityFormatter.Format(action.TargetId) + ".");

            var cost = native.GetCost(target);
            if (cost is null || cost.GetType() != native.CostType)
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.ContractUnavailable,
                    "IDiscoverable.GetDiscoverCost() returned a non-ResourceCostList value.");
            if (!native.HasEnough(cost))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.Unaffordable,
                    "GetDiscoverCost().HasEnough() refused " +
                    EntityIdentityFormatter.Format(action.TargetId) + ".");
            var costs = CaptureCosts(native, cost);
            if (!TryCapturePermit(out var permitReason))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.MutationPermitUnavailable,
                    permitReason);

            return Execute(in action, native, target, cost, in before, costs);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.ContractUnavailable,
                "Generic discovery preflight failed before mutation: " +
                exception.GetBaseException().Message);
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

    private GenericDiscoverySubmission Execute(
        in GenericDiscoveryAction action,
        GenericDiscoveryNativeBindings native,
        object target,
        object cost,
        in GenericDiscoveryState before,
        CostBefore[] costs)
    {
        var stage = GenericDiscoveryNativeStage.Payment;
        var nativeCalls = 0;
        var paymentInvoked = false;
        try
        {
            paymentInvoked = true;
            nativeCalls = 1;
            native.PerformCost(cost);
            stage = GenericDiscoveryNativeStage.Discover;
            nativeCalls = 2;
            native.Discover(target);
            stage = GenericDiscoveryNativeStage.Verification;
            var after = CaptureState(native, target, action.ExpectedNativeType);
            var receipt = BuildReceipt(
                native, in before, in after, costs, paymentInvoked, after.Discovered);
            return after.Discovered
                ? Verified(nativeCalls, in receipt)
                : Fault(
                    in action,
                    GenericDiscoveryPreflight.VerificationFailed,
                    stage,
                    NativeMutationOutcome.PostconditionFailed,
                    nativeCalls,
                    in receipt,
                    "The requested target remained undiscovered after the native callback.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            var receipt = CaptureReceiptBestEffort(
                native,
                target,
                action.ExpectedNativeType,
                in before,
                costs,
                paymentInvoked);
            if (receipt.EvidenceAvailable && receipt.After.Discovered)
                return Verified(nativeCalls, in receipt);
            return Fault(
                in action,
                GenericDiscoveryPreflight.PostCommitFault,
                stage,
                NativeMutationOutcome.ExecutionThrew,
                nativeCalls,
                in receipt,
                "Native generic discovery threw before the requested discovered outcome was observable: " +
                exception.GetBaseException().Message);
        }
    }

    private static GenericDiscoverySubmission Verified(
        int nativeCalls,
        in GenericDiscoveryMutationReceipt receipt) =>
        new(
            GenericDiscoveryPreflight.Proceeded,
            GenericDiscoveryNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(nativeCalls, 1, 1),
            in receipt,
            "Verified that the exact requested UUID became discovered; payment observations are evidence only.");

    private static GenericDiscoverySubmission Fault(
        in GenericDiscoveryAction action,
        GenericDiscoveryPreflight preflight,
        GenericDiscoveryNativeStage stage,
        NativeMutationOutcome outcome,
        int nativeCalls,
        in GenericDiscoveryMutationReceipt receipt,
        string reason)
    {
        var exactReason = "Generic discovery " + stage + " failed on " +
            EntityIdentityFormatter.Format(action.TargetId) + ": " + reason;
        return new GenericDiscoverySubmission(
            preflight,
            stage,
            outcome,
            new NativeMutationCallOutcome(nativeCalls, 1, 0),
            in receipt,
            exactReason);
    }

    private static GenericDiscoveryState CaptureState(
        GenericDiscoveryNativeBindings native,
        object target,
        string nativeType) =>
        new(
            nativeType,
            native.IsVisible(target),
            native.CanDiscover(target),
            native.IsDiscovered(target),
            native.IsRequired(target));

    private static CostBefore[] CaptureCosts(
        GenericDiscoveryNativeBindings native,
        object cost)
    {
        var entries = native.GetEntries(cost);
        var result = new CostBefore[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index] ??
                throw new InvalidOperationException("Discovery cost entry " + index + " was null.");
            var resource = native.ReadResource(entry);
            result[index] = new CostBefore(
                native.ReadResourceIdentity(resource),
                native.ReadCost(entry),
                native.ReadResourceAmount(resource),
                resource);
        }
        return result;
    }

    private static GenericDiscoveryMutationReceipt BuildReceipt(
        GenericDiscoveryNativeBindings native,
        in GenericDiscoveryState before,
        in GenericDiscoveryState after,
        CostBefore[] costs,
        bool paymentInvoked,
        bool postconditionMatched)
    {
        var rows = new GenericDiscoveryCostReceipt[costs.Length];
        var charged = false;
        for (var index = 0; index < rows.Length; index++)
        {
            var current = native.ReadResourceAmount(costs[index].Resource);
            rows[index] = new GenericDiscoveryCostReceipt(
                costs[index].ResourceId,
                costs[index].Expected,
                costs[index].Amount,
                current);
            if (costs[index].Amount.CompareTo(current) != 0) charged = true;
        }
        return new GenericDiscoveryMutationReceipt(
            true,
            paymentInvoked,
            charged,
            postconditionMatched,
            in before,
            in after,
            rows);
    }

    private static GenericDiscoveryMutationReceipt CaptureReceiptBestEffort(
        GenericDiscoveryNativeBindings native,
        object target,
        string nativeType,
        in GenericDiscoveryState before,
        CostBefore[] costs,
        bool paymentInvoked)
    {
        try
        {
            var after = CaptureState(native, target, nativeType);
            return BuildReceipt(
                native,
                in before,
                in after,
                costs,
                paymentInvoked,
                after.Discovered);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return default;
        }
    }

    private bool TryCapturePermit(out string reason)
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
                reason = "The suite no longer owns GenericDiscovery.";
            return false;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            reason = "The generic discovery mutation permit could not be captured: " +
                exception.GetBaseException().Message;
            return false;
        }
    }

    private void BindLifecycle()
    {
        if (GenericDiscoveryNativeBindings.TryCreate(
                out var bindings,
                out var reason,
                _resolveType,
                _includeContract))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

    private static bool IsExpected(Exception exception) => exception is not
        StackOverflowException and not
        OutOfMemoryException and not
        AccessViolationException;

    private readonly struct CostBefore
    {
        internal CostBefore(
            Guid resourceId,
            BigDouble expected,
            BigDouble amount,
            object resource)
        {
            ResourceId = resourceId;
            Expected = expected;
            Amount = amount;
            Resource = resource;
        }

        internal Guid ResourceId { get; }
        internal BigDouble Expected { get; }
        internal BigDouble Amount { get; }
        internal object Resource { get; }
    }
}
