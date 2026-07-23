using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal sealed class ServiceCycleReplayJoinResult
{
    internal ServiceCycleReplayJoinResult(
        ServiceCycleReplayArtifactFooter[] footers,
        ServiceCycleReplayArtifactRecord[][] records,
        int[][] semanticEventIndices,
        ServiceCycleReplayArtifactEligibilityCode eligibility,
        ServiceCycleReplayCycleKey firstMissingFooterCycle,
        ulong firstMissingFooterSemanticSequence,
        ServiceCycleReplayFormatWorkCounter? work = null)
    {
        Footers = footers;
        Records = records;
        SemanticEventIndices = semanticEventIndices;
        Completeness = ServiceCycleReplayCompletenessIndex.Build(footers, work);
        Eligibility = eligibility;
        FirstMissingFooterCycle = firstMissingFooterCycle;
        FirstMissingFooterSemanticSequence = firstMissingFooterSemanticSequence;
    }

    internal ServiceCycleReplayArtifactFooter[] Footers { get; }
    internal ServiceCycleReplayArtifactRecord[][] Records { get; }
    internal int[][] SemanticEventIndices { get; }
    internal ServiceCycleReplayCompletenessIndex Completeness { get; }
    internal ServiceCycleReplayArtifactEligibilityCode Eligibility { get; }
    internal ServiceCycleReplayCycleKey FirstMissingFooterCycle { get; }
    internal ulong FirstMissingFooterSemanticSequence { get; }
}
