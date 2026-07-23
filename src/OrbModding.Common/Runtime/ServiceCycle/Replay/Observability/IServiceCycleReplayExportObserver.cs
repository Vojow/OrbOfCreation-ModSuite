namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Observability;

/// <summary>
/// Receives one-shot export outcomes. Callbacks can run on the exporter worker or its owner thread and
/// can follow a nonblocking exporter stop, disposal, or terminal status transition while accepted work
/// drains. Implementations must tolerate these late calls and must not access Unity or game objects.
/// </summary>
public interface IServiceCycleReplayExportObserver
{
    void ArtifactCommitted(int ordinal, int bytes);
    void ArtifactDiscarded(int ordinal, ServiceCycleReplayArtifactDiscardReason reason);
    void ExporterFaulted(ServiceCycleReplayExporterFaultReason reason);
}

/// <summary>Why an artifact accepted for export did not commit.</summary>
public enum ServiceCycleReplayArtifactDiscardReason
{
    WriteFailed = 0,
    ExporterFaulted = 1,
}

/// <summary>Why the optional exporter stopped accepting work.</summary>
public enum ServiceCycleReplayExporterFaultReason
{
    SourceFault = 0,
    StartupFailure = 1,
    EncodingOrStorageFailure = 2,
    RetentionFailure = 3,
    WorkerFailure = 4,
    OrdinalExhausted = 5,
}
