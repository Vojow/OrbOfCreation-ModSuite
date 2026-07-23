using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>Rebuilds ordinary contexts and native evidence from validated Format wire values.</summary>
internal static class ServiceCycleReplayArtifactContextAdapter
{
    internal static ServiceCycleReplayContext Create(
        ServiceId service,
        in ServiceCycleReplayArtifactContext artifact)
    {
        var artifactCycle = artifact.Cycle;
        var artifactPrevious = artifact.PreviousReceipt;
        var cycle = ServiceCycleReplayContextFactory.Identity(service, in artifactCycle);
        var previous = Receipt(service, in artifactPrevious);
        var context = new ServiceCycleContext(
            cycle,
            previous,
            new MonotonicTimestamp(artifact.DecisionAt));
        return new ServiceCycleReplayContext(artifactCycle.TraceServiceKey, in context);
    }

    private static BatchReceipt Receipt(
        ServiceId service,
        in ServiceCycleReplayArtifactReceipt artifact)
    {
        if (!artifact.IsPresent) return default;
        var artifactCycle = artifact.Cycle;
        var cycle = ServiceCycleReplayContextFactory.Identity(service, in artifactCycle);
        var batch = new BatchId(artifact.Batch);
        var totals = new ServiceNativeCallTotals(
            artifact.NativeCallsAttempted,
            artifact.MutationAttempts,
            artifact.MutationsCommitted);
        var completedAt = new MonotonicTimestamp(artifact.CompletedAt);
        if (artifact.Disposition == BatchTerminalDisposition.Completed)
            return BatchReceipt.Completed(cycle, batch, artifact.ActionCount, totals, completedAt);
        if (artifact.Disposition == BatchTerminalDisposition.Orphaned)
            return BatchReceipt.Orphaned(
                cycle, batch, artifact.ActionCount, artifact.CommittedCount, totals, completedAt);
        var artifactTerminal = artifact.TerminalAction;
        var terminal = ActionResult(in artifactTerminal);
        var emergency = artifact.HasEmergencyContext
            ? new EmergencyStopContext(
                new EmergencyStopEpisodeId(artifact.EmergencyEpisode),
                new EmergencyStopTransitionGeneration(artifact.EmergencyTransition),
                (EmergencyStopReason)artifact.EmergencyReason)
            : default;
        return BatchReceipt.Terminated(
            cycle,
            batch,
            artifact.ActionCount,
            artifact.CommittedCount,
            artifact.TerminalIndex,
            terminal,
            totals,
            completedAt,
            emergency);
    }

    private static ServiceActionResult ActionResult(in ServiceCycleReplayArtifactActionResult artifact)
    {
        var code = artifact.Code >= ServiceActionResultCode.FirstFeatureCode
            ? new ServiceActionResultCode(artifact.Code)
            : ServiceActionResultCode.Reserved(artifact.Code);
        if (artifact.Disposition == ServiceActionDisposition.Rejected)
            return ServiceActionResult.Rejected(code);
        if (!artifact.HasNativeEvidence)
            return ServiceActionResult.Faulted(code);
        var call = new NativeMutationCallOutcome(
            checked((int)artifact.NativeCallsAttempted),
            checked((int)artifact.MutationAttempts),
            checked((int)artifact.MutationsCommitted));
        var evidence = ServiceNativeMutationEvidence.Observed(
            (NativeMutationOutcome)(artifact.NativeOutcomeCode - 1),
            call);
        return artifact.Disposition == ServiceActionDisposition.Committed
            ? ServiceActionResult.Committed(code, evidence)
            : ServiceActionResult.Faulted(code, evidence);
    }
}
