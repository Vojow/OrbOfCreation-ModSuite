using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbModding.Common.Runtime.ServiceCycle.Diagnostics;

public readonly struct ServiceCyclePumpDiagnosticsSnapshot
{
    internal ServiceCyclePumpDiagnosticsSnapshot(in SuiteFramePumpCumulativeSnapshot source)
    {
        CurrentLifecycle = source.CurrentLifecycle;
        EmergencyStopEngaged = source.EmergencyStopEngaged;
        EmergencyTransition = source.EmergencyTransition;
        ActiveEmergency = source.ActiveEmergency;
        LatestEmergency = source.LatestEmergency;
        AcceptedFrameCount = source.AcceptedFrameCount;
        RejectedFrameCount = source.RejectedFrameCount;
        HasAcceptedFrame = source.HasAcceptedFrame;
        LastAcceptedFrameIdentity = source.LastAcceptedFrameIdentity;
        LastReport = source.LastReport;
        ResponsesAcquired = source.ResponsesAcquired;
        ActionsAttempted = source.ActionsAttempted;
        CapturesAttempted = source.CapturesAttempted;
        EmergencyBatchesRejected = source.EmergencyBatchesRejected;
        LifecyclePositionTransitions = source.LifecyclePositionTransitions;
        ResponseDuration = source.ResponseDuration;
        ActionDuration = source.ActionDuration;
        CaptureDuration = source.CaptureDuration;
        TotalDuration = source.TotalDuration;
    }

    public LifecycleGeneration CurrentLifecycle { get; }
    public bool EmergencyStopEngaged { get; }
    public EmergencyStopTransitionGeneration EmergencyTransition { get; }
    /// <summary>Engagement episode currently active, or unavailable while disengaged.</summary>
    public EmergencyStopContext ActiveEmergency { get; }
    /// <summary>Latest engagement episode, retained after disengagement.</summary>
    public EmergencyStopContext LatestEmergency { get; }
    public bool HasEmergencyEpisode => LatestEmergency.IsValid;
    public long AcceptedFrameCount { get; }
    public long RejectedFrameCount { get; }
    public bool HasAcceptedFrame { get; }
    public long LastAcceptedFrameIdentity { get; }
    public SuiteFramePumpReport LastReport { get; }
    public bool HasLastReport => LastReport.Accepted || RejectedFrameCount > 0;
    public long ResponsesAcquired { get; }
    public long ActionsAttempted { get; }
    public long CapturesAttempted { get; }
    public long EmergencyBatchesRejected { get; }
    public long LifecyclePositionTransitions { get; }
    public MonotonicDuration ResponseDuration { get; }
    public MonotonicDuration ActionDuration { get; }
    public MonotonicDuration CaptureDuration { get; }
    public MonotonicDuration TotalDuration { get; }
}
