namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

public readonly struct ServiceCycleReplayExportOptions
{
    public ServiceCycleReplayExportOptions(bool enabled, int maximumCommittedArtifacts = 4)
    {
        if (enabled && maximumCommittedArtifacts <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(maximumCommittedArtifacts));
        Enabled = enabled;
        MaximumCommittedArtifacts = maximumCommittedArtifacts;
    }
    public bool Enabled { get; }
    public int MaximumCommittedArtifacts { get; }
}
public enum ServiceCycleReplayExportStatus
{
    Disabled = 0,
    Initializing = 1,
    Running = 2,
    Stopping = 3,
    Stopped = 4,
    Faulted = 5,
}

public enum ServiceCycleReplayExportRequestResult
{
    Accepted = 0,
    Disabled = 1,
    Initializing = 2,
    Backpressured = 3,
    SnapshotContended = 4,
    Stopping = 5,
    Stopped = 6,
    Faulted = 7,
    Copying = 8,
}

public readonly struct ServiceCycleReplayExportMetrics
{
    internal ServiceCycleReplayExportMetrics(
        ServiceCycleReplayExportStatus status,
        long accepted,
        long backpressured,
        long snapshotContended,
        long unavailable,
        long exported,
        long discarded,
        long bytesWritten,
        long semanticEventsCopied,
        int peakSemanticEventsCopiedPerRequest,
        int pending,
        int retained,
        int startupPruned,
        int staleTemporaryRemoved,
        int faults)
    {
        Status = status;
        AcceptedArtifacts = accepted;
        BackpressureRejections = backpressured;
        SnapshotContentionRejections = snapshotContended;
        UnavailableRejections = unavailable;
        ExportedArtifacts = exported;
        DiscardedArtifacts = discarded;
        BytesWritten = bytesWritten;
        SemanticEventsCopied = semanticEventsCopied;
        PeakSemanticEventsCopiedPerRequest = peakSemanticEventsCopiedPerRequest;
        PendingArtifacts = pending;
        RetainedArtifacts = retained;
        StartupPrunedArtifacts = startupPruned;
        StaleTemporaryFilesRemoved = staleTemporaryRemoved;
        FaultCount = faults;
    }
    public ServiceCycleReplayExportStatus Status { get; }
    public long AcceptedArtifacts { get; }
    public long BackpressureRejections { get; }
    public long SnapshotContentionRejections { get; }
    public long UnavailableRejections { get; }
    public long ExportedArtifacts { get; }
    public long DiscardedArtifacts { get; }
    public long BytesWritten { get; }
    public long SemanticEventsCopied { get; }
    public int PeakSemanticEventsCopiedPerRequest { get; }
    public int PendingArtifacts { get; }
    public int RetainedArtifacts { get; }
    public int StartupPrunedArtifacts { get; }
    public int StaleTemporaryFilesRemoved { get; }
    public int FaultCount { get; }
}
