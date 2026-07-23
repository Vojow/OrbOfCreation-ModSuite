using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayFooterConverter
{
    internal static ServiceCycleReplayArtifactFooter Convert(in ServiceCycleReplayCycleFooter source)
    {
        var receipt = source.Context.PreviousReceipt;
        var action = receipt.TerminalAction;
        var actionCalls = action.NativeCallOutcome;
        var artifactAction = new ServiceCycleReplayArtifactActionResult(
            action.Disposition,
            action.Code.Value,
            action.HasNativeEvidence,
            action.HasNativeEvidence ? (int)action.NativeEvidence.Outcome + 1 : 0,
            actionCalls.NativeCallsAttempted,
            actionCalls.MutationAttempts,
            actionCalls.MutationsCommitted);
        var totals = receipt.NativeCallOutcome;
        var emergency = receipt.EmergencyStop;
        var artifactReceipt = new ServiceCycleReplayArtifactReceipt(
            receipt.IsPresent,
            receipt.Cycle,
            receipt.Batch,
            receipt.Disposition,
            receipt.ActionCount,
            receipt.CommittedCount,
            receipt.TerminalIndex,
            receipt.UntouchedSuffixCount,
            receipt.ResultCode,
            artifactAction,
            receipt.HasTerminalAction,
            totals.NativeCallsAttempted,
            totals.MutationAttempts,
            totals.MutationsCommitted,
            receipt.CompletedAt,
            emergency.Episode.Value,
            emergency.Transition.Value,
            (int)emergency.Reason);
        var context = new ServiceCycleReplayArtifactContext(
            source.Context.Cycle,
            artifactReceipt,
            source.Context.DecisionAt);
        return new ServiceCycleReplayArtifactFooter(
            source.Sequence,
            context,
            source.Disposition,
            source.ReturnedWake,
            source.HasReturnedWake,
            source.Projection,
            source.HasProjection,
            source.ExpectedActionCount,
            source.FirstRecordSequence,
            source.LastRecordSequence,
            source.RetainedRecordCount,
            source.Completeness,
            source.EncodingDurationTicks,
            source.EncodingTimestampFrequency,
            source.EncodingAllocatedBytes,
            default);
    }
}
