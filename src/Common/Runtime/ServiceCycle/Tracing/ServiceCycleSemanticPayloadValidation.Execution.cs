using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

internal static partial class ServiceCycleSemanticPayloadValidation
{
    private static void ValidateExecution(
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload)
    {
        switch (kind)
        {
            case ServiceCycleSemanticEventKind.StatePublished:
                break;
            case ServiceCycleSemanticEventKind.BatchPublished:
                Require(payload.Disposition == 0 && payload.Code == 0 && payload.ActionCount >= 0 &&
                    payload.CommittedCount == 0 && payload.ActionIndex == -1 && payload.UntouchedSuffixCount == 0 &&
                    NativeTotalsAreZero(in payload), nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.ActionAttempted:
                Require(payload.Disposition == 0 && payload.Code == 0 && payload.ActionIndex >= 0 &&
                    NativeTotalsAreZero(in payload), nameof(payload));
                break;
            // A commit is either a verified native mutation or a publication. The publishing shape is
            // recognisable and unforgeable: no mutation outcome and zero native totals is something a
            // native commit cannot produce, because ServiceActionResult refuses to build one.
            case ServiceCycleSemanticEventKind.ActionCommitted:
                Require(payload.Disposition == (int)ServiceActionDisposition.Committed &&
                    IsActionCode(payload.Code, CommonActionResultCodes.Committed.Value) && payload.ActionIndex >= 0 &&
                    (payload.HasNativeOutcome
                        ? payload.NativeOutcome == NativeMutationOutcome.Verified &&
                            NativeEvidenceIsCoherent(in payload)
                        : NativeTotalsAreZero(in payload)),
                    nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.ActionSkipped:
                Require(payload.Disposition == (int)ServiceActionDisposition.Skipped &&
                    IsActionCode(payload.Code, CommonActionResultCodes.Skipped.Value) &&
                    payload.ActionIndex >= 0 &&
                    (payload.HasNativeOutcome
                        ? payload.NativeOutcome == NativeMutationOutcome.PostconditionFailed &&
                            NativeEvidenceIsCoherent(in payload)
                        : NativeTotalsAreZero(in payload)),
                    nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.ActionRejected:
                Require(payload.Disposition == (int)ServiceActionDisposition.Rejected && IsRejectedCode(payload.Code) &&
                    payload.ActionIndex >= 0 && NativeTotalsAreZero(in payload), nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.ActionFaulted:
                Require(payload.Disposition == (int)ServiceActionDisposition.Faulted &&
                    IsActionCode(payload.Code, CommonActionResultCodes.AdapterFault.Value) &&
                    payload.ActionIndex >= 0 &&
                    (payload.HasNativeOutcome
                        ? payload.NativeOutcome != NativeMutationOutcome.Verified && NativeEvidenceIsCoherent(in payload)
                        : NativeTotalsAreZero(in payload)), nameof(payload));
                break;
            // The batch payload no longer carries a publication ledger. Its one surviving
            // publication-only shape is therefore all actions committed with zero native totals;
            // it is indistinguishable here from omitted native evidence for those commits.
            case ServiceCycleSemanticEventKind.BatchCompleted:
                Require(payload.Disposition == (int)BatchTerminalDisposition.Completed &&
                    payload.Code == CommonActionResultCodes.Committed.Value &&
                    payload.ActionCount >= 0 && payload.CommittedCount <= payload.ActionCount &&
                    payload.ActionIndex == -1 && payload.UntouchedSuffixCount == 0 &&
                    NativeTotalsAreCoherent(in payload) &&
                    (NativeTotalsAreZero(in payload) &&
                        payload.CommittedCount == payload.ActionCount ||
                        (payload.CommittedCount == 0 && payload.MutationAttempts == 0
                            ? NativeTotalsAreZero(in payload)
                            : payload.MutationAttempts >= payload.CommittedCount &&
                                payload.MutationsCommitted >= payload.CommittedCount &&
                                (payload.CommittedCount != 0 ||
                                    payload.MutationsCommitted == 0))),
                    nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.BatchAborted:
                Require(payload.Disposition is (int)BatchTerminalDisposition.Rejected or (int)BatchTerminalDisposition.Faulted &&
                    (payload.Disposition == (int)BatchTerminalDisposition.Rejected
                        ? IsRejectedCode(payload.Code)
                        : IsActionCode(payload.Code, CommonActionResultCodes.AdapterFault.Value)) &&
                    payload.ActionCount > 0 && payload.ActionIndex >= 0 &&
                    payload.CommittedCount <= payload.ActionIndex &&
                    payload.UntouchedSuffixCount == payload.ActionCount - payload.ActionIndex - 1 &&
                    NativeTotalsAreCoherent(in payload) &&
                    payload.MutationsCommitted >= payload.CommittedCount &&
                    (payload.CommittedCount != 0 || payload.MutationsCommitted == 0),
                    nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.BatchOrphaned:
                Require(payload.Disposition == (int)BatchTerminalDisposition.Orphaned &&
                    payload.Code == CommonActionResultCodes.LifecycleReplaced.Value &&
                    payload.ActionCount >= 0 && payload.CommittedCount >= 0 &&
                    payload.CommittedCount <= payload.ActionCount && payload.ActionIndex == -1 &&
                    payload.UntouchedSuffixCount <= payload.ActionCount - payload.CommittedCount &&
                    NativeTotalsAreCoherent(in payload) &&
                    (payload.CommittedCount == 0 && payload.MutationAttempts == 0
                        ? NativeTotalsAreZero(in payload)
                        : payload.MutationAttempts >= payload.CommittedCount &&
                            payload.MutationsCommitted >= payload.CommittedCount &&
                            (payload.CommittedCount != 0 ||
                                payload.MutationsCommitted == 0)), nameof(payload));
                break;
        }
    }

    private static bool NativeTotalsAreZero(in ServiceCycleSemanticPayload p) =>
        p.NativeCallsAttempted == 0 && p.MutationAttempts == 0 && p.MutationsCommitted == 0;

    private static bool NativeTotalsAreCoherent(in ServiceCycleSemanticPayload p) =>
        p.NativeCallsAttempted >= 0 && p.MutationAttempts >= 0 && p.MutationsCommitted >= 0 &&
        p.MutationAttempts <= p.NativeCallsAttempted && p.MutationsCommitted <= p.MutationAttempts;

    private static bool NativeEvidenceIsCoherent(in ServiceCycleSemanticPayload p) =>
        p.NativeOutcomeCode is >= 1 and <= 5 &&
        NativeTotalsAreCoherent(in p) && p.NativeOutcome switch
        {
            NativeMutationOutcome.Verified =>
                p.MutationAttempts > 0 && p.MutationsCommitted == p.MutationAttempts,
            NativeMutationOutcome.BeforeCaptureFailed => NativeTotalsAreZero(in p),
            NativeMutationOutcome.ExecutionThrew or NativeMutationOutcome.AfterCaptureFailed or
                NativeMutationOutcome.PostconditionFailed => p.MutationAttempts > 0 && p.MutationsCommitted == 0,
            _ => false,
        };

    private static bool IsRejectedCode(int code) =>
        code is >= 2 and <= 6 || code >= ServiceActionResultCode.FirstFeatureCode;
}
