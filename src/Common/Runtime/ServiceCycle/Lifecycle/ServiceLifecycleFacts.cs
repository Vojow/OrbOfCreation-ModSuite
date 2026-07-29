using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

public enum ServiceRunnerPositionState
{
    Vacant = 0,
    Current = 1,
    Retiring = 2,
}

/// <summary>
/// Bounded lifecycle terminal evidence retained by a slot for diagnostics and semantic tracing.
/// A main-owned batch carries its exact receipt; earlier phases carry only the published cycle identity.
/// </summary>
public readonly struct ServiceLifecycleTerminalFact
{
    internal ServiceLifecycleTerminalFact(
        long sequence,
        LifecycleGeneration retiredLifecycle,
        LifecycleGeneration requestedLifecycle,
        ServiceCyclePhase phase,
        ServiceCycleIdentity cycle,
        BatchId batch,
        ServiceWorkerResponse response,
        BatchReceipt receipt,
        MonotonicTimestamp observedAt)
    {
        Sequence = sequence;
        RetiredLifecycle = retiredLifecycle;
        RequestedLifecycle = requestedLifecycle;
        Phase = phase;
        Cycle = cycle;
        Batch = batch;
        Response = response;
        Receipt = receipt;
        ObservedAt = observedAt;
    }

    public long Sequence { get; }
    public LifecycleGeneration RetiredLifecycle { get; }
    public LifecycleGeneration RequestedLifecycle { get; }
    public ServiceCyclePhase Phase { get; }
    public ServiceCycleIdentity Cycle { get; }
    public BatchId Batch { get; }
    public BatchReceipt Receipt { get; }
    internal ServiceWorkerResponse Response { get; }
    public MonotonicTimestamp ObservedAt { get; }
    public bool IsPresent => Sequence > 0;
    public bool HasPublishedCycle => Cycle.IsValid && Batch.IsValid;
    public bool HasReceipt => Receipt.IsPresent;
    internal bool HasResponse => Response.Sequence > 0;
}

/// <summary>One committed lifecycle-runner construction deferral caused by expected resource contention.</summary>
public readonly struct ServiceLifecycleConstructionDeferralFact
{
    internal ServiceLifecycleConstructionDeferralFact(
        long sequence,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt,
        MonotonicTimestamp retryDue)
    {
        Sequence = sequence;
        Lifecycle = lifecycle;
        ObservedAt = observedAt;
        RetryDue = retryDue;
    }

    public long Sequence { get; }
    public LifecycleGeneration Lifecycle { get; }
    public MonotonicTimestamp ObservedAt { get; }
    public MonotonicTimestamp RetryDue { get; }
    public bool IsPresent => Sequence > 0;
}

/// <summary>The last frame on which the world freshness gate held this service's cycle closed.</summary>
/// <remarks>
/// The gate is otherwise silent: a service it parks attempts nothing, so a parked frame looks
/// exactly like a frame the service had no work for. That is precisely the shape a stalled
/// collector takes — every mutating service quietly stops while every counter reads healthy — so
/// the slot keeps the last deferral as a fact rather than leaving the stall to be inferred from an
/// absence of evidence.
/// </remarks>
public readonly struct ServiceWorldGateDeferralFact
{
    internal ServiceWorldGateDeferralFact(
        long sequence,
        long frameIdentity,
        long lastActionFrame,
        WorldGeneration world)
    {
        Sequence = sequence;
        FrameIdentity = frameIdentity;
        LastActionFrame = lastActionFrame;
        World = world;
    }

    public long Sequence { get; }
    /// <summary>The pump frame the service was held on.</summary>
    public long FrameIdentity { get; }
    /// <summary>
    /// The generation the service is waiting past: the frame it last changed the game on, or the one
    /// that was live when it went live, whichever is later.
    /// </summary>
    public long LastActionFrame { get; }
    /// <summary>The live generation at the time, or the invalid default when no source could answer.</summary>
    public WorldGeneration World { get; }
    public bool IsPresent => Sequence > 0;
}

internal readonly struct ServiceRunnerPositionSnapshot
{
    internal ServiceRunnerPositionSnapshot(
        int index,
        ServiceRunnerPositionState state,
        LifecycleGeneration lifecycle,
        ServiceHandoffPhase handoffPhase,
        ServiceRunnerStorageSnapshot storage)
    {
        Index = index;
        State = state;
        Lifecycle = lifecycle;
        HandoffPhase = handoffPhase;
        Storage = storage;
    }

    internal int Index { get; }
    internal ServiceRunnerPositionState State { get; }
    internal LifecycleGeneration Lifecycle { get; }
    internal ServiceHandoffPhase HandoffPhase { get; }
    internal ServiceRunnerStorageSnapshot Storage { get; }
}

internal readonly struct ServiceLifecycleSlotSnapshot
{
    internal ServiceLifecycleSlotSnapshot(
        LifecycleGeneration desiredLifecycle,
        ServiceRunnerPositionSnapshot position0,
        ServiceRunnerPositionSnapshot position1,
        ServiceLifecycleTerminalFact latestTerminal,
        ServiceLifecycleConstructionDeferralFact latestConstructionDeferral,
        ServiceWorldGateDeferralFact latestWorldGateDeferral,
        ServiceFault constructionFault,
        MonotonicTimestamp constructionRetryDue,
        long constructionAttemptCount,
        long constructionContentionCount)
    {
        DesiredLifecycle = desiredLifecycle;
        Position0 = position0;
        Position1 = position1;
        LatestTerminal = latestTerminal;
        LatestConstructionDeferral = latestConstructionDeferral;
        LatestWorldGateDeferral = latestWorldGateDeferral;
        ConstructionFault = constructionFault;
        ConstructionRetryDue = constructionRetryDue;
        ConstructionAttemptCount = constructionAttemptCount;
        ConstructionContentionCount = constructionContentionCount;
    }

    internal LifecycleGeneration DesiredLifecycle { get; }
    internal ServiceRunnerPositionSnapshot Position0 { get; }
    internal ServiceRunnerPositionSnapshot Position1 { get; }
    internal ServiceLifecycleTerminalFact LatestTerminal { get; }
    internal ServiceLifecycleConstructionDeferralFact LatestConstructionDeferral { get; }
    internal ServiceWorldGateDeferralFact LatestWorldGateDeferral { get; }
    internal ServiceFault ConstructionFault { get; }
    internal MonotonicTimestamp ConstructionRetryDue { get; }
    internal long ConstructionAttemptCount { get; }
    internal long ConstructionContentionCount { get; }
    internal LifecycleGeneration ActiveLifecycle =>
        Position0.State == ServiceRunnerPositionState.Current ? Position0.Lifecycle :
        Position1.State == ServiceRunnerPositionState.Current ? Position1.Lifecycle : default;
    internal int LivePositionCount =>
        (Position0.State == ServiceRunnerPositionState.Vacant ? 0 : 1) +
        (Position1.State == ServiceRunnerPositionState.Vacant ? 0 : 1);
}

internal readonly struct ServiceRunnerRetirement
{
    internal ServiceRunnerRetirement(
        ServiceCyclePhase phase,
        ServiceCycleIdentity cycle,
        BatchId batch,
        ServiceWorkerResponse response,
        BatchReceipt receipt)
    {
        Phase = phase;
        Cycle = cycle;
        Batch = batch;
        Response = response;
        Receipt = receipt;
    }

    internal ServiceCyclePhase Phase { get; }
    internal ServiceCycleIdentity Cycle { get; }
    internal BatchId Batch { get; }
    internal BatchReceipt Receipt { get; }
    internal ServiceWorkerResponse Response { get; }
}
