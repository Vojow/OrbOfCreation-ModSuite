using System;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbModding.Common.Runtime.ServiceCycle.Diagnostics;

public readonly struct ServiceCyclePumpTimingSample
{
    internal ServiceCyclePumpTimingSample(in SuiteFramePumpReport report)
    {
        FrameIdentity = report.FrameIdentity;
        ResponsesAcquired = report.ResponsesAcquired;
        ActionsAttempted = report.ActionsAttempted;
        CapturesAttempted = report.CapturesAttempted;
        ResponseDuration = report.ResponseDuration;
        ActionDuration = report.ActionDuration;
        CaptureDuration = report.CaptureDuration;
        TotalDuration = report.TotalDuration;
    }

    public long FrameIdentity { get; }
    public int ResponsesAcquired { get; }
    public int ActionsAttempted { get; }
    public int CapturesAttempted { get; }
    public MonotonicDuration ResponseDuration { get; }
    public MonotonicDuration ActionDuration { get; }
    public MonotonicDuration CaptureDuration { get; }
    public MonotonicDuration TotalDuration { get; }
}

public readonly struct ServiceCyclePumpTimingCopyResult
{
    internal ServiceCyclePumpTimingCopyResult(int availableCount, int writtenCount, long revision)
    {
        AvailableCount = availableCount;
        WrittenCount = writtenCount;
        Revision = revision;
    }

    public int AvailableCount { get; }
    public int WrittenCount { get; }
    public long Revision { get; }
    public bool IsComplete => AvailableCount == WrittenCount;
}

public interface IServiceCyclePumpTimingSink
{
    void Observe(in SuiteFramePumpReport report);
}

public interface IServiceCyclePumpTimingSource
{
    int Capacity { get; }
    long Revision { get; }
    ServiceCyclePumpTimingCopyResult CopyTo(Span<ServiceCyclePumpTimingSample> destination);
}

/// <summary>Owner-thread recent-frame projection for lightweight in-game diagnostics.</summary>
public sealed class ServiceCyclePumpTimingRegistry : IServiceCyclePumpTimingSink, IServiceCyclePumpTimingSource
{
    public const int DefaultCapacity = 1_200;
    private readonly ServiceCyclePumpTimingSample[] _samples;
    private int _next;
    private int _count;
    private long _revision;

    public ServiceCyclePumpTimingRegistry(int capacity = DefaultCapacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _samples = new ServiceCyclePumpTimingSample[capacity];
    }

    public static ServiceCyclePumpTimingRegistry Shared { get; } = new();

    public int Capacity => _samples.Length;
    public long Revision => _revision;

    public void Observe(in SuiteFramePumpReport report)
    {
        if (!report.Accepted) return;
        _samples[_next] = new ServiceCyclePumpTimingSample(in report);
        _next = (_next + 1) % _samples.Length;
        if (_count < _samples.Length) _count++;
        _revision = checked(_revision + 1);
    }

    public ServiceCyclePumpTimingCopyResult CopyTo(Span<ServiceCyclePumpTimingSample> destination)
    {
        var written = Math.Min(_count, destination.Length);
        var first = (_next - _count + _samples.Length) % _samples.Length;
        var skip = _count - written;
        for (var index = 0; index < written; index++)
            destination[index] = _samples[(first + skip + index) % _samples.Length];
        return new ServiceCyclePumpTimingCopyResult(_count, written, _revision);
    }
}
