using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayJoinedFenceValidator
{
    internal static void Validate(in ServiceCycleReplayDecodedManifest manifest,
        ServiceCycleTraceDocument semantic, ServiceCycleReplayArtifactRecord[] records,
        ServiceCycleReplayJoinResult joined,
        ServiceCycleReplayFormatWorkCounter? work = null)
    {
        var completeCycles = 0;
        var footerFence = 0L;
        for (var index = 0; index < joined.Footers.Length; index++)
        {
            work?.Add();
            if (joined.Footers[index].IsComplete) completeCycles++;
            if (footerFence == index && joined.Footers[index].IsComplete) footerFence = index + 1;
        }
        var recordFence = 0L;
        for (var index = 0; index < records.Length; index++)
        {
            work?.Add();
            if (!joined.Completeness.IsComplete(records[index].Cycle, work)) break;
            recordFence = records[index].Sequence;
        }
        var firstUnjoinedFooter = 0L;
        var firstUnjoinedSemantic = joined.FirstMissingFooterSemanticSequence;
        for (var index = 0; index < joined.Footers.Length; index++)
        {
            work?.Add();
            if (joined.Footers[index].IsComplete) continue;
            firstUnjoinedFooter = joined.Footers[index].Sequence;
            var indices = joined.SemanticEventIndices[index];
            if (indices.Length != 0)
            {
                var footerSemantic = semantic[indices[0]].Id.Sequence;
                if (firstUnjoinedSemantic == 0 || footerSemantic < firstUnjoinedSemantic)
                    firstUnjoinedSemantic = footerSemantic;
            }
            break;
        }
        if (manifest.Eligibility == ServiceCycleReplayArtifactEligibilityCode.SemanticTraceIncomplete &&
            firstUnjoinedSemantic == 0 && semantic.Dropped.IsPresent)
            firstUnjoinedSemantic = semantic.Dropped.FirstSequence;
        var semanticFence = manifest.Eligibility == ServiceCycleReplayArtifactEligibilityCode.Complete
            ? (semantic.Count == 0 ? 0 : semantic[^1].Id.Sequence)
            : firstUnjoinedSemantic > 1 ? firstUnjoinedSemantic - 1 : 0;
        if (manifest.JoinedCycleCount != completeCycles || manifest.JoinedFooterSequence != footerFence ||
            manifest.JoinedRecordSequence != recordFence || manifest.JoinedSemanticSequence != semanticFence ||
            manifest.FirstUnjoinedFooterSequence != firstUnjoinedFooter ||
            manifest.FirstUnjoinedSemanticSequence != firstUnjoinedSemantic)
            throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.SerializedJoinMismatch);
    }
}
