using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;

/// <summary>
/// One service's action outcomes in one fixed minute of journal time.
/// </summary>
public readonly struct ServiceActionTimelineCellSnapshot
{
    internal ServiceActionTimelineCellSnapshot(
        long minuteKey,
        ServiceId service,
        ServiceShape shape,
        long committed,
        long skipped,
        long rejected,
        long faulted)
    {
        if (!service.IsValid) throw new ArgumentException("A valid service identity is required.", nameof(service));
        if (shape is not (ServiceShape.Source or ServiceShape.Ordinary))
            throw new ArgumentOutOfRangeException(nameof(shape));
        if (committed < 0) throw new ArgumentOutOfRangeException(nameof(committed));
        if (skipped < 0) throw new ArgumentOutOfRangeException(nameof(skipped));
        if (rejected < 0) throw new ArgumentOutOfRangeException(nameof(rejected));
        if (faulted < 0) throw new ArgumentOutOfRangeException(nameof(faulted));
        MinuteKey = minuteKey;
        Service = service;
        Shape = shape;
        Committed = committed;
        Skipped = skipped;
        Rejected = rejected;
        FaultedCount = faulted;
    }

    /// <summary>
    /// Fixed minute ordinal derived from the monotonic timestamp already carried by journal evidence.
    /// </summary>
    public long MinuteKey { get; }
    public ServiceId Service { get; }
    public ServiceShape Shape { get; }
    public long Committed { get; }
    public long Skipped { get; }
    public long Rejected { get; }
    public long FaultedCount { get; }
    public bool Faulted => FaultedCount > 0;
}

public readonly struct ServiceActionTimelineCopyResult
{
    internal ServiceActionTimelineCopyResult(
        int serviceCount,
        int bucketCount,
        int availableCount,
        int writtenCount,
        long revision)
    {
        ServiceCount = serviceCount;
        BucketCount = bucketCount;
        AvailableCount = availableCount;
        WrittenCount = writtenCount;
        Revision = revision;
    }

    public int ServiceCount { get; }
    public int BucketCount { get; }
    public int AvailableCount { get; }
    public int WrittenCount { get; }
    public long Revision { get; }
    public bool IsComplete => AvailableCount == WrittenCount;
}

/// <summary>
/// Event-driven 30-minute action history. Its revision changes only at a minute boundary, when the
/// visible committed-work or fault-marker truth changes, or when a lifecycle reset clears that truth.
/// </summary>
public interface IServiceActionTimelineSource
{
    int TimelineServiceCount { get; }
    int TimelineBucketCapacity { get; }
    int TimelineCellCapacity { get; }
    long TimelineRevision { get; }
    ServiceActionTimelineCopyResult CopyTimelineTo(
        Span<ServiceActionTimelineCellSnapshot> destination);
}
