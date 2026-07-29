using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

/// <summary>
/// Owner-thread accumulator for accepted/rejected frame evidence and terminals committed outside
/// a frame. Authoritative lifecycle totals are supplied when a snapshot is projected.
/// </summary>
internal sealed class SuiteFramePumpObservability
{
    private long _acceptedFrameCount;
    private long _rejectedFrameCount;
    private long _lastAcceptedFrameIdentity;
    private long _responsesAcquired;
    private long _actionsAttempted;
    private long _capturesAttempted;
    private long _worldGateDeferrals;
    private long _emergencyBatchesRejected;
    private long _responseTicks;
    private long _actionTicks;
    private long _captureTicks;
    private long _totalTicks;
    private SuiteFramePumpReport _lastReport;
    private bool _hasAcceptedFrame;

    internal long AcceptedFrameCount => _acceptedFrameCount;
    internal bool HasAcceptedFrame => _hasAcceptedFrame;
    internal long LastAcceptedFrameIdentity => _lastAcceptedFrameIdentity;

    internal void RecordReport(in SuiteFramePumpReport report)
    {
        _lastReport = report;
        if (!report.Accepted)
        {
            _rejectedFrameCount = checked(_rejectedFrameCount + 1);
            return;
        }

        _hasAcceptedFrame = true;
        _lastAcceptedFrameIdentity = report.FrameIdentity;
        _acceptedFrameCount = checked(_acceptedFrameCount + 1);
        _responsesAcquired = checked(_responsesAcquired + report.ResponsesAcquired);
        _actionsAttempted = checked(_actionsAttempted + report.ActionsAttempted);
        _capturesAttempted = checked(_capturesAttempted + report.CapturesAttempted);
        _worldGateDeferrals = checked(_worldGateDeferrals + report.WorldGateDeferrals);
        _emergencyBatchesRejected = checked(
            _emergencyBatchesRejected + report.EmergencyBatchesRejected);
        _responseTicks = AddTicks(_responseTicks, report.ResponseDuration.Ticks);
        _actionTicks = AddTicks(_actionTicks, report.ActionDuration.Ticks);
        _captureTicks = AddTicks(_captureTicks, report.CaptureDuration.Ticks);
        _totalTicks = AddTicks(_totalTicks, report.TotalDuration.Ticks);
    }

    internal void RecordOutOfFrameEmergencyRejections(int rejectedBatchCount)
    {
        if (rejectedBatchCount < 0)
            throw new ArgumentOutOfRangeException(nameof(rejectedBatchCount));
        _emergencyBatchesRejected = checked(_emergencyBatchesRejected + rejectedBatchCount);
    }

    internal SuiteFramePumpCumulativeSnapshot Snapshot(
        LifecycleGeneration currentLifecycle,
        in SuiteEmergencyStopSnapshot emergency,
        long authoritativeLifecyclePositionTransitions) => new(
            currentLifecycle,
            emergency.IsEngaged,
            emergency.Transition,
            emergency.Active,
            emergency.Latest,
            _acceptedFrameCount,
            _rejectedFrameCount,
            _hasAcceptedFrame,
            _lastAcceptedFrameIdentity,
            _lastReport,
            _responsesAcquired,
            _actionsAttempted,
            _capturesAttempted,
            _worldGateDeferrals,
            _emergencyBatchesRejected,
            authoritativeLifecyclePositionTransitions,
            new MonotonicDuration(_responseTicks),
            new MonotonicDuration(_actionTicks),
            new MonotonicDuration(_captureTicks),
            new MonotonicDuration(_totalTicks));

    private static long AddTicks(long total, long elapsed) =>
        elapsed > long.MaxValue - total ? long.MaxValue : total + elapsed;
}

internal readonly struct SuiteEmergencyStopSnapshot
{
    internal SuiteEmergencyStopSnapshot(
        bool isEngaged,
        EmergencyStopTransitionGeneration transition,
        EmergencyStopContext active,
        EmergencyStopContext latest)
    {
        IsEngaged = isEngaged;
        Transition = transition;
        Active = active;
        Latest = latest;
    }

    internal bool IsEngaged { get; }
    internal EmergencyStopTransitionGeneration Transition { get; }
    internal EmergencyStopContext Active { get; }
    internal EmergencyStopContext Latest { get; }
}
