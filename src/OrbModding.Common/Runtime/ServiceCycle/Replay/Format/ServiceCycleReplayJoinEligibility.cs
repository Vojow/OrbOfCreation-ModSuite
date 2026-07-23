using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayJoinEligibility
{
    internal static ServiceCycleReplayArtifactEligibilityCode Initial(
        ServiceCycleTraceDocument semantic,
        ServiceCycleReplayRecordingSnapshot recording)
    {
        if (!recording.EncodingEnabled) return ServiceCycleReplayArtifactEligibilityCode.RecordingDisabled;
        if (!recording.Completeness.IsComplete) return ServiceCycleReplayArtifactEligibilityCode.RecordingIncomplete;
        return semantic.IsComplete
            ? ServiceCycleReplayArtifactEligibilityCode.Complete
            : ServiceCycleReplayArtifactEligibilityCode.SemanticTraceIncomplete;
    }

    internal static ServiceCycleReplayArtifactEligibilityCode ForFooter(
        in ServiceCycleReplayArtifactFooter footer)
    {
        if (!footer.Completeness.IsComplete) return ServiceCycleReplayArtifactEligibilityCode.FooterIncomplete;
        if (footer.Disposition == ServiceCycleReplayCycleFooterDisposition.EvaluationAborted)
            return ServiceCycleReplayArtifactEligibilityCode.EvaluationAborted;
        if (footer.Disposition == ServiceCycleReplayCycleFooterDisposition.ProjectionAborted)
            return ServiceCycleReplayArtifactEligibilityCode.ProjectionAborted;
        return footer.Join.Code switch
        {
            ServiceCycleReplaySemanticJoinCode.Complete => ServiceCycleReplayArtifactEligibilityCode.Complete,
            ServiceCycleReplaySemanticJoinCode.RecordBoundsMismatch or
            ServiceCycleReplaySemanticJoinCode.UnjoinedRecord or
            ServiceCycleReplaySemanticJoinCode.RequiredRecordMissing or
            ServiceCycleReplaySemanticJoinCode.RequiredRecordDuplicate or
            ServiceCycleReplaySemanticJoinCode.ActionRecordGap =>
                ServiceCycleReplayArtifactEligibilityCode.RecordCoverageIncomplete,
            ServiceCycleReplaySemanticJoinCode.PreviousReceiptMissing or
            ServiceCycleReplaySemanticJoinCode.PreviousReceiptMismatch =>
                ServiceCycleReplayArtifactEligibilityCode.PreviousReceiptIncomplete,
            ServiceCycleReplaySemanticJoinCode.ActionEvidenceMissing or
            ServiceCycleReplaySemanticJoinCode.ActionEvidenceDuplicate or
            ServiceCycleReplaySemanticJoinCode.NativeEvidenceMismatch =>
                ServiceCycleReplayArtifactEligibilityCode.NativeEvidenceIncomplete,
            _ => ServiceCycleReplayArtifactEligibilityCode.SemanticJoinIncomplete,
        };
    }
}
