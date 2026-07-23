using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayPublicationEvidenceValidator
{
    internal static ServiceCycleReplaySemanticJoinCode Validate(
        ServiceCycleTraceDocument semantic,
        in ServiceCycleReplayArtifactFooter footer,
        ServiceCycleSemanticEvent captureStarted,
        ServiceCycleSemanticEvent captureCompleted) =>
        Validate(ServiceCycleReplaySemanticIndex.Build(semantic), semantic, in footer, captureStarted, captureCompleted);

    internal static ServiceCycleReplaySemanticJoinCode Validate(
        ServiceCycleReplaySemanticIndex semanticIndex,
        ServiceCycleTraceDocument semantic,
        in ServiceCycleReplayArtifactFooter footer,
        ServiceCycleSemanticEvent captureStarted,
        ServiceCycleSemanticEvent captureCompleted)
    {
        var cycle = footer.Context.Cycle;
        var configuration = semanticIndex.FindPublication(
            cycle.TraceServiceKey, cycle.Configuration, configuration: true);
        if (configuration.Count > 1)
            return ServiceCycleReplaySemanticJoinCode.ConfigurationPublicationDuplicate;
        if (configuration.Count == 0)
            return ServiceCycleReplaySemanticJoinCode.ConfigurationPublicationMissing;
        var strategy = semanticIndex.FindPublication(
            cycle.TraceServiceKey, cycle.Strategy, configuration: false);
        if (strategy.Count > 1)
            return ServiceCycleReplaySemanticJoinCode.StrategyPublicationDuplicate;
        if (strategy.Count == 0)
            return ServiceCycleReplaySemanticJoinCode.StrategyPublicationMissing;
        if (semantic[configuration.Index].Id.Sequence >= captureStarted.Id.Sequence ||
            semantic[strategy.Index].Id.Sequence >= captureCompleted.Id.Sequence)
            return ServiceCycleReplaySemanticJoinCode.PublicationOrderMismatch;
        return ServiceCycleReplaySemanticJoinCode.Complete;
    }
}
