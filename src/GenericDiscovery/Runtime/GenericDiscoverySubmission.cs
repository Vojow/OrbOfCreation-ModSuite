using System;
using OrbModding.Common;

namespace OrbAutomata;

internal enum GenericDiscoveryPreflight
{
    Proceeded = 0,
    ContractUnavailable = 1,
    WrongThread = 3,
    LifecycleReplaced = 4,
    IdentityUnavailable = 5,
    UnsupportedType = 6,
    NotVisible = 7,
    AlreadyDiscovered = 8,
    DiscoveryUnavailable = 9,
    Unaffordable = 10,
    MutationPermitUnavailable = 11,
    PostCommitFault = 12,
    VerificationFailed = 13,
}

internal enum GenericDiscoveryNativeStage
{
    None = 0,
    Payment = 1,
    Discover = 2,
    Verification = 3,
}

internal readonly struct GenericDiscoveryCostReceipt
{
    internal GenericDiscoveryCostReceipt(
        Guid resourceId,
        BigDouble expected,
        BigDouble before,
        BigDouble after)
    {
        ResourceId = resourceId;
        Expected = expected;
        Before = before;
        After = after;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Expected { get; }
    internal BigDouble Before { get; }
    internal BigDouble After { get; }
    internal BigDouble ObservedDelta => Before - After;
}

internal readonly struct GenericDiscoveryState
{
    internal GenericDiscoveryState(
        string nativeType,
        bool visible,
        bool canDiscover,
        bool discovered,
        bool required)
    {
        NativeType = nativeType ?? string.Empty;
        Visible = visible;
        CanDiscover = canDiscover;
        Discovered = discovered;
        Required = required;
    }

    internal string NativeType { get; }
    internal bool Visible { get; }
    internal bool CanDiscover { get; }
    internal bool Discovered { get; }
    internal bool Required { get; }
}

internal readonly struct GenericDiscoveryMutationReceipt
{
    internal GenericDiscoveryMutationReceipt(
        bool evidenceAvailable,
        bool paymentInvoked,
        bool resourcesCharged,
        bool postconditionMatched,
        in GenericDiscoveryState before,
        in GenericDiscoveryState after,
        GenericDiscoveryCostReceipt[] costs)
    {
        EvidenceAvailable = evidenceAvailable;
        PaymentInvoked = paymentInvoked;
        ResourcesCharged = resourcesCharged;
        PostconditionMatched = postconditionMatched;
        Before = before;
        After = after;
        Costs = costs ?? Array.Empty<GenericDiscoveryCostReceipt>();
    }

    internal bool EvidenceAvailable { get; }
    internal bool PaymentInvoked { get; }
    internal bool ResourcesCharged { get; }
    internal bool PostconditionMatched { get; }
    internal GenericDiscoveryState Before { get; }
    internal GenericDiscoveryState After { get; }
    internal GenericDiscoveryCostReceipt[] Costs { get; }
}

internal readonly struct GenericDiscoverySubmission
{
    internal GenericDiscoverySubmission(
        GenericDiscoveryPreflight preflight,
        GenericDiscoveryNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        in GenericDiscoveryMutationReceipt receipt,
        string reason)
    {
        if (preflight != GenericDiscoveryPreflight.Proceeded && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A generic discovery failure requires an exact reason.", nameof(reason));
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Receipt = receipt;
        Reason = reason ?? string.Empty;
    }

    internal GenericDiscoveryPreflight Preflight { get; }
    internal GenericDiscoveryNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal GenericDiscoveryMutationReceipt Receipt { get; }
    internal string Reason { get; }
    internal bool Verified =>
        Preflight == GenericDiscoveryPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static GenericDiscoverySubmission Reject(
        GenericDiscoveryPreflight preflight,
        string reason) =>
        new(
            preflight,
            GenericDiscoveryNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed,
            default,
            default,
            reason);
}
