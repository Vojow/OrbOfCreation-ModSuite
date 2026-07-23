using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

namespace OrbAutomata;

internal interface IAutomataReplayExportPort
{
    AutomataReplayExportStepResult ContinueSnapshot();
    void Stop();
}

internal enum AutomataReplayExportStepResult
{
    Accepted = 0,
    Pending = 1,
    Unavailable = 2,
}

internal sealed class AutomataReplayExportPort : IAutomataReplayExportPort
{
    private readonly ServiceCycleReplayArtifactExporter _exporter;
    private readonly int _maximumSemanticEventsPerFrame;

    public AutomataReplayExportPort(
        ServiceCycleReplayArtifactExporter exporter,
        int maximumSemanticEventsPerFrame)
    {
        _exporter = exporter ?? throw new System.ArgumentNullException(nameof(exporter));
        if (maximumSemanticEventsPerFrame <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(maximumSemanticEventsPerFrame));
        _maximumSemanticEventsPerFrame = maximumSemanticEventsPerFrame;
    }

    public AutomataReplayExportStepResult ContinueSnapshot()
    {
        var result = _exporter.ContinueFrozenSnapshot(
            _maximumSemanticEventsPerFrame,
            out _);
        return Map(result);
    }

    public void Stop() => _exporter.Stop();

    private static AutomataReplayExportStepResult Map(ServiceCycleReplayExportRequestResult result) =>
        result == ServiceCycleReplayExportRequestResult.Accepted
            ? AutomataReplayExportStepResult.Accepted
            : result is ServiceCycleReplayExportRequestResult.Initializing or
            ServiceCycleReplayExportRequestResult.Backpressured or
            ServiceCycleReplayExportRequestResult.SnapshotContended or
            ServiceCycleReplayExportRequestResult.Copying
                ? AutomataReplayExportStepResult.Pending
                : AutomataReplayExportStepResult.Unavailable;
}
