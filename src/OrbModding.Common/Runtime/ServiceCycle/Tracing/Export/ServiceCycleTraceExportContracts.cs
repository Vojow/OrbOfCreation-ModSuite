namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Export;

/// <summary>
/// Construction policy for the explicitly opted-in semantic snapshot exporter. After successful startup
/// reconciliation, the committed-snapshot value is the steady-state retention target. Publication commits
/// a replacement before deleting the oldest artifact, so a later deletion fault can leave at most one extra
/// committed snapshot before export latches faulted. A reconciliation fault may leave an inherited namespace
/// above that bound and accepts no export work.
/// </summary>
public readonly struct ServiceCycleTraceExportOptions
{
    public ServiceCycleTraceExportOptions(bool enabled, int maximumCommittedSnapshots = 4)
    {
        if (enabled && maximumCommittedSnapshots <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(maximumCommittedSnapshots));
        Enabled = enabled;
        MaximumCommittedSnapshots = maximumCommittedSnapshots;
    }

    public bool Enabled { get; }
    public int MaximumCommittedSnapshots { get; }
}

/// <summary>Stable lifecycle state of one semantic snapshot exporter.</summary>
public enum ServiceCycleTraceExportStatus
{
    Disabled = 0,
    Initializing = 1,
    Running = 2,
    Stopping = 3,
    Stopped = 4,
    Faulted = 5,
}

/// <summary>Exact result of one nonblocking owner-thread snapshot request.</summary>
public enum ServiceCycleTraceExportRequestResult
{
    Accepted = 0,
    Disabled = 1,
    Initializing = 2,
    Backpressured = 3,
    Stopping = 4,
    Stopped = 5,
    Faulted = 6,
}

/// <summary>Allocation-free numeric projection of exporter state and counters.</summary>
public readonly struct ServiceCycleTraceExportMetrics
{
    internal ServiceCycleTraceExportMetrics(
        ServiceCycleTraceExportStatus status,
        long acceptedSnapshots,
        long backpressureRejections,
        long unavailableRejections,
        long exportedSnapshots,
        long discardedSnapshots,
        long bytesWritten,
        int pendingSnapshots,
        int retainedSnapshots,
        int startupPrunedSnapshots,
        int staleTemporaryFilesRemoved,
        int faultCount)
    {
        Status = status;
        AcceptedSnapshots = acceptedSnapshots;
        BackpressureRejections = backpressureRejections;
        UnavailableRejections = unavailableRejections;
        ExportedSnapshots = exportedSnapshots;
        DiscardedSnapshots = discardedSnapshots;
        BytesWritten = bytesWritten;
        PendingSnapshots = pendingSnapshots;
        RetainedSnapshots = retainedSnapshots;
        StartupPrunedSnapshots = startupPrunedSnapshots;
        StaleTemporaryFilesRemoved = staleTemporaryFilesRemoved;
        FaultCount = faultCount;
    }

    public ServiceCycleTraceExportStatus Status { get; }
    public long AcceptedSnapshots { get; }
    public long BackpressureRejections { get; }
    public long UnavailableRejections { get; }
    public long RejectedSnapshots => BackpressureRejections + UnavailableRejections;
    public long ExportedSnapshots { get; }
    public long DiscardedSnapshots { get; }
    public long BytesWritten { get; }
    public int PendingSnapshots { get; }
    public int RetainedSnapshots { get; }
    public int StartupPrunedSnapshots { get; }
    public int StaleTemporaryFilesRemoved { get; }
    public int FaultCount { get; }
}
