using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

/// <summary>Allocation-free evidence for one caller-supplied frame identity.</summary>
public readonly struct SuiteFramePumpReport
{
    internal SuiteFramePumpReport(
        long frameIdentity,
        bool accepted,
        int startingOrdinal,
        int responsesAcquired,
        int actionsAttempted,
        int capturesAttempted,
        int emergencyBatchesRejected,
        long lifecyclePositionTransitions,
        MonotonicDuration responseDuration,
        MonotonicDuration actionDuration,
        MonotonicDuration captureDuration,
        MonotonicDuration totalDuration)
    {
        FrameIdentity = frameIdentity;
        Accepted = accepted;
        StartingOrdinal = startingOrdinal;
        ResponsesAcquired = responsesAcquired;
        ActionsAttempted = actionsAttempted;
        CapturesAttempted = capturesAttempted;
        EmergencyBatchesRejected = emergencyBatchesRejected;
        LifecyclePositionTransitions = lifecyclePositionTransitions;
        ResponseDuration = responseDuration;
        ActionDuration = actionDuration;
        CaptureDuration = captureDuration;
        TotalDuration = totalDuration;
    }

    public long FrameIdentity { get; }
    public bool Accepted { get; }
    public int StartingOrdinal { get; }
    public int ResponsesAcquired { get; }
    public int ActionsAttempted { get; }
    public int CapturesAttempted { get; }
    public int EmergencyBatchesRejected { get; }
    /// <summary>
    /// Exact count of physical runner-position state transitions observed during this accepted frame.
    /// </summary>
    public long LifecyclePositionTransitions { get; }
    public MonotonicDuration ResponseDuration { get; }
    public MonotonicDuration ActionDuration { get; }
    public MonotonicDuration CaptureDuration { get; }
    public MonotonicDuration TotalDuration { get; }
}

internal readonly struct SuiteFramePumpCumulativeSnapshot
{
    internal SuiteFramePumpCumulativeSnapshot(
        LifecycleGeneration currentLifecycle,
        bool emergencyStopEngaged,
        EmergencyStopTransitionGeneration emergencyTransition,
        EmergencyStopContext activeEmergency,
        EmergencyStopContext latestEmergency,
        long acceptedFrameCount,
        long rejectedFrameCount,
        bool hasAcceptedFrame,
        long lastAcceptedFrameIdentity,
        SuiteFramePumpReport lastReport,
        long responsesAcquired,
        long actionsAttempted,
        long capturesAttempted,
        long emergencyBatchesRejected,
        long lifecyclePositionTransitions,
        MonotonicDuration responseDuration,
        MonotonicDuration actionDuration,
        MonotonicDuration captureDuration,
        MonotonicDuration totalDuration)
    {
        CurrentLifecycle = currentLifecycle;
        EmergencyStopEngaged = emergencyStopEngaged;
        EmergencyTransition = emergencyTransition;
        ActiveEmergency = activeEmergency;
        LatestEmergency = latestEmergency;
        AcceptedFrameCount = acceptedFrameCount;
        RejectedFrameCount = rejectedFrameCount;
        HasAcceptedFrame = hasAcceptedFrame;
        LastAcceptedFrameIdentity = lastAcceptedFrameIdentity;
        LastReport = lastReport;
        ResponsesAcquired = responsesAcquired;
        ActionsAttempted = actionsAttempted;
        CapturesAttempted = capturesAttempted;
        EmergencyBatchesRejected = emergencyBatchesRejected;
        LifecyclePositionTransitions = lifecyclePositionTransitions;
        ResponseDuration = responseDuration;
        ActionDuration = actionDuration;
        CaptureDuration = captureDuration;
        TotalDuration = totalDuration;
    }

    internal LifecycleGeneration CurrentLifecycle { get; }
    internal bool EmergencyStopEngaged { get; }
    internal EmergencyStopTransitionGeneration EmergencyTransition { get; }
    internal EmergencyStopContext ActiveEmergency { get; }
    internal EmergencyStopContext LatestEmergency { get; }
    internal long AcceptedFrameCount { get; }
    internal long RejectedFrameCount { get; }
    internal bool HasAcceptedFrame { get; }
    internal long LastAcceptedFrameIdentity { get; }
    internal SuiteFramePumpReport LastReport { get; }
    internal long ResponsesAcquired { get; }
    internal long ActionsAttempted { get; }
    internal long CapturesAttempted { get; }
    internal long EmergencyBatchesRejected { get; }
    internal long LifecyclePositionTransitions { get; }
    internal MonotonicDuration ResponseDuration { get; }
    internal MonotonicDuration ActionDuration { get; }
    internal MonotonicDuration CaptureDuration { get; }
    internal MonotonicDuration TotalDuration { get; }
}
