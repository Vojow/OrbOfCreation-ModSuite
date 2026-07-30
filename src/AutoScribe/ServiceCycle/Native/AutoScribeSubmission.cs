using System;
using OrbModding.Common;

namespace OrbAutomata;

internal enum AutoScribePreflight
{
    Proceeded = 0,
    ContractUnavailable = 1,
    Quarantined = 2,
    IdentityUnavailable = 3,
    RelationshipMismatch = 4,
    RecipeUnavailable = 5,
    TargetUnavailable = 6,
    QueueFull = 7,
    CompetingSupply = 8,
    Unaffordable = 9,
    MutationPermitUnavailable = 10,
    PostPaymentFault = 11,
    VerificationFailed = 12,
}

internal enum AutoScribeNativeStage
{
    None = 0,
    Payment = 1,
    Construction = 2,
    Initiation = 3,
    Admission = 4,
    Verification = 5,
}

/// <summary>
/// Exact lifecycle receipt. Partial commits keep each observed side effect instead of pretending
/// the four-step transaction either happened wholly or not at all.
/// </summary>
internal readonly struct AutoScribeMutationReceipt
{
    internal AutoScribeMutationReceipt(
        bool evidenceAvailable,
        bool paymentInvoked,
        bool resourcesCharged,
        bool costMatched,
        bool ceilingTransitionObserved,
        bool admittedToQueue,
        bool admittedToInstantStock,
        int queueDelta,
        int stockDelta)
    {
        EvidenceAvailable = evidenceAvailable;
        PaymentInvoked = paymentInvoked;
        ResourcesCharged = resourcesCharged;
        CostMatched = costMatched;
        CeilingTransitionObserved = ceilingTransitionObserved;
        AdmittedToQueue = admittedToQueue;
        AdmittedToInstantStock = admittedToInstantStock;
        QueueDelta = queueDelta;
        StockDelta = stockDelta;
    }

    internal bool EvidenceAvailable { get; }
    internal bool PaymentInvoked { get; }
    internal bool ResourcesCharged { get; }
    internal bool CostMatched { get; }
    internal bool CeilingTransitionObserved { get; }
    internal bool AdmittedToQueue { get; }
    internal bool AdmittedToInstantStock { get; }
    internal int QueueDelta { get; }
    internal int StockDelta { get; }
}

internal readonly struct AutoScribeSubmission
{
    internal AutoScribeSubmission(
        AutoScribePreflight preflight,
        AutoScribeNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        in AutoScribeMutationReceipt receipt,
        string reason)
    {
        if (preflight != AutoScribePreflight.Proceeded && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("An Auto Scribe failure requires an exact reason.", nameof(reason));
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Receipt = receipt;
        Reason = reason ?? string.Empty;
    }

    internal AutoScribePreflight Preflight { get; }
    internal AutoScribeNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal AutoScribeMutationReceipt Receipt { get; }
    internal string Reason { get; }
    internal bool Verified =>
        Preflight == AutoScribePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static AutoScribeSubmission Reject(
        AutoScribePreflight preflight,
        string reason) =>
        new(
            preflight,
            AutoScribeNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed,
            default,
            default,
            reason);
}
