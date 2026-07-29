using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

/// <summary>Fixed, numeric-only payload for the append-only service-cycle event vocabulary.</summary>
public readonly partial struct ServiceCycleSemanticPayload : IEquatable<ServiceCycleSemanticPayload>
{
    internal ServiceCycleSemanticPayload(
        ServiceCycleSemanticFields fields,
        ulong service,
        ulong lifecycle,
        ulong configuration,
        ulong strategy,
        ulong capture,
        ulong cycle,
        ulong batch,
        ulong action,
        ulong statePublication,
        long timestampTicks,
        long durationTicks,
        long deadlineTicks,
        long frameIdentity,
        ulong fingerprint,
        int code,
        int disposition,
        int actionIndex,
        int actionCount,
        int committedCount,
        int untouchedSuffixCount,
        int occurrenceCount,
        long nativeCallsAttempted,
        long mutationAttempts,
        long mutationsCommitted,
        int responsesAcquired,
        int actionsAttempted,
        int capturesAttempted,
        int emergencyBatchesRejected,
        long lifecycleTransitions,
        long responseDurationTicks,
        long actionDurationTicks,
        long captureDurationTicks,
        long totalDurationTicks,
        int nativeOutcome,
        int publishedCount = 0,
        ulong world = 0,
        int cyclesStarted = 0,
        int worldGateDeferrals = 0)
    {
        PublishedCount = publishedCount;
        World = world;
        CyclesStarted = cyclesStarted;
        WorldGateDeferrals = worldGateDeferrals;
        Fields = fields;
        Service = service;
        Lifecycle = lifecycle;
        Configuration = configuration;
        Strategy = strategy;
        Capture = capture;
        Cycle = cycle;
        Batch = batch;
        Action = action;
        StatePublication = statePublication;
        TimestampTicks = timestampTicks;
        DurationTicks = durationTicks;
        DeadlineTicks = deadlineTicks;
        FrameIdentity = frameIdentity;
        Fingerprint = fingerprint;
        Code = code;
        Disposition = disposition;
        ActionIndex = actionIndex;
        ActionCount = actionCount;
        CommittedCount = committedCount;
        UntouchedSuffixCount = untouchedSuffixCount;
        OccurrenceCount = occurrenceCount;
        NativeCallsAttempted = nativeCallsAttempted;
        MutationAttempts = mutationAttempts;
        MutationsCommitted = mutationsCommitted;
        ResponsesAcquired = responsesAcquired;
        ActionsAttempted = actionsAttempted;
        CapturesAttempted = capturesAttempted;
        EmergencyBatchesRejected = emergencyBatchesRejected;
        LifecycleTransitions = lifecycleTransitions;
        ResponseDurationTicks = responseDurationTicks;
        ActionDurationTicks = actionDurationTicks;
        CaptureDurationTicks = captureDurationTicks;
        TotalDurationTicks = totalDurationTicks;
        NativeOutcomeCode = nativeOutcome;
    }

    public ServiceCycleSemanticFields Fields { get; }
    public ulong Service { get; }
    public ulong Lifecycle { get; }
    public ulong Configuration { get; }
    public ulong Strategy { get; }
    public ulong World { get; }
    public ulong Capture { get; }
    public ulong Cycle { get; }
    public ulong Batch { get; }
    public ulong Action { get; }
    public ulong StatePublication { get; }
    public long TimestampTicks { get; }
    public long DurationTicks { get; }
    public long DeadlineTicks { get; }
    public long FrameIdentity { get; }
    public ulong Fingerprint { get; }
    public int Code { get; }
    public int Disposition { get; }
    public int ActionIndex { get; }
    public int ActionCount { get; }
    public int CommittedCount { get; }
    public int UntouchedSuffixCount { get; }
    public int OccurrenceCount { get; }
    public long NativeCallsAttempted { get; }
    public long MutationAttempts { get; }
    public long MutationsCommitted { get; }
    public int ResponsesAcquired { get; }
    public int ActionsAttempted { get; }
    public int CapturesAttempted { get; }
    public int EmergencyBatchesRejected { get; }

    /// <summary>How many services the pump opened a cycle for in this frame.</summary>
    public int CyclesStarted { get; }

    /// <summary>
    /// How many services the world freshness gate held closed in this frame. Read next to
    /// <see cref="CyclesStarted"/>: "0 started / 3 held" is a stall, "0 started / 0 held" is an idle
    /// suite.
    /// </summary>
    public int WorldGateDeferrals { get; }
    public long LifecycleTransitions { get; }
    public bool PumpAccepted => Code == 1;
    public int StartingOrdinal => ActionIndex;
    public long ResponseDurationTicks { get; }
    public long ActionDurationTicks { get; }
    public long CaptureDurationTicks { get; }
    public long TotalDurationTicks { get; }
    public bool HasNativeOutcome => (Fields & ServiceCycleSemanticFields.NativeMutationOutcome) != 0;
    public NativeMutationOutcome? NativeOutcome => HasNativeOutcome
        ? (NativeMutationOutcome)(NativeOutcomeCode - 1)
        : null;
    internal int NativeOutcomeCode { get; }

    /// <summary>
    /// Committed actions in this batch that published a snapshot rather than mutating the game, and
    /// therefore could not produce native evidence.
    /// </summary>
    public int PublishedCount { get; }

    /// <summary>
    /// Reads the exact wake returned by a successful evaluator. The semantic schema stores the
    /// wake kind in <see cref="Disposition"/> and its single numeric operand in
    /// <see cref="DeadlineTicks"/>.
    /// </summary>
    public bool TryGetReturnedWake(out WakePolicy wake)
    {
        var required = ServiceCycleSemanticFields.Disposition | ServiceCycleSemanticFields.Deadline;
        if ((Fields & required) != required)
        {
            wake = default;
            return false;
        }

        switch ((WakePolicyKind)Disposition)
        {
            case WakePolicyKind.Immediate when DeadlineTicks == 0:
                wake = WakePolicy.Immediate;
                return true;
            case WakePolicyKind.AfterDecision:
                wake = WakePolicy.AfterDecision(new MonotonicDuration(DeadlineTicks));
                return true;
            case WakePolicyKind.AfterBatch:
                wake = WakePolicy.AfterBatch(new MonotonicDuration(DeadlineTicks));
                return true;
            case WakePolicyKind.At:
                wake = WakePolicy.At(new MonotonicTimestamp(DeadlineTicks));
                return true;
            default:
                wake = default;
                return false;
        }
    }

    /// <summary>
    /// The frame identity a fact carries when it ran outside any pump frame. Frame zero is a legal
    /// frame, so absence has to be a value no frame can take rather than zero.
    /// </summary>
    internal const long Unframed = -1;

    private static ServiceCycleSemanticFields FrameField(long frameIdentity) =>
        frameIdentity < 0 ? ServiceCycleSemanticFields.None : ServiceCycleSemanticFields.FrameIdentity;

    private static long FrameValue(long frameIdentity) => frameIdentity < 0 ? 0 : frameIdentity;

    internal const ServiceCycleSemanticFields CycleFields =
        ServiceCycleSemanticFields.Service | ServiceCycleSemanticFields.Lifecycle |
        ServiceCycleSemanticFields.Configuration | ServiceCycleSemanticFields.Strategy |
        ServiceCycleSemanticFields.World | ServiceCycleSemanticFields.Cycle;

    internal const ServiceCycleSemanticFields CaptureFields =
        ServiceCycleSemanticFields.Service | ServiceCycleSemanticFields.Lifecycle |
        ServiceCycleSemanticFields.Configuration | ServiceCycleSemanticFields.Capture |
        ServiceCycleSemanticFields.Cycle;

    public bool Equals(ServiceCycleSemanticPayload other) =>
        Fields == other.Fields && Service == other.Service && Lifecycle == other.Lifecycle &&
        Configuration == other.Configuration && Strategy == other.Strategy && World == other.World &&
        Capture == other.Capture && Cycle == other.Cycle && Batch == other.Batch &&
        Action == other.Action &&
        StatePublication == other.StatePublication && TimestampTicks == other.TimestampTicks &&
        DurationTicks == other.DurationTicks && DeadlineTicks == other.DeadlineTicks &&
        FrameIdentity == other.FrameIdentity && Fingerprint == other.Fingerprint && Code == other.Code &&
        Disposition == other.Disposition && ActionIndex == other.ActionIndex && ActionCount == other.ActionCount &&
        CommittedCount == other.CommittedCount && UntouchedSuffixCount == other.UntouchedSuffixCount &&
        OccurrenceCount == other.OccurrenceCount && NativeCallsAttempted == other.NativeCallsAttempted &&
        MutationAttempts == other.MutationAttempts && MutationsCommitted == other.MutationsCommitted &&
        ResponsesAcquired == other.ResponsesAcquired && ActionsAttempted == other.ActionsAttempted &&
        CapturesAttempted == other.CapturesAttempted && EmergencyBatchesRejected == other.EmergencyBatchesRejected &&
        LifecycleTransitions == other.LifecycleTransitions && ResponseDurationTicks == other.ResponseDurationTicks &&
        ActionDurationTicks == other.ActionDurationTicks && CaptureDurationTicks == other.CaptureDurationTicks &&
        TotalDurationTicks == other.TotalDurationTicks && NativeOutcomeCode == other.NativeOutcomeCode &&
        PublishedCount == other.PublishedCount && CyclesStarted == other.CyclesStarted &&
        WorldGateDeferrals == other.WorldGateDeferrals;
    public override bool Equals(object? obj) => obj is ServiceCycleSemanticPayload other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Fields, Service, Lifecycle, Configuration, Strategy, Capture, Cycle, Batch);
}
