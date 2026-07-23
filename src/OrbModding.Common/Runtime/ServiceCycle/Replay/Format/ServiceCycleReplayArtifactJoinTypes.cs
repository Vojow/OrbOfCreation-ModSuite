using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

public readonly struct ServiceCycleReplaySemanticJoin : IEquatable<ServiceCycleReplaySemanticJoin>
{
    internal ServiceCycleReplaySemanticJoin(
        ServiceCycleReplaySemanticJoinCode code,
        ServiceCycleSemanticEventKind evaluationTerminalKind,
        ServiceCycleSemanticEventKind cycleTerminalKind,
        ulong statePublication,
        ulong projectionFingerprint,
        ulong batch,
        ulong terminalEventSequence)
    {
        Code = code;
        EvaluationTerminalKind = evaluationTerminalKind;
        CycleTerminalKind = cycleTerminalKind;
        StatePublication = statePublication;
        ProjectionFingerprint = projectionFingerprint;
        Batch = batch;
        TerminalEventSequence = terminalEventSequence;
    }

    public ServiceCycleReplaySemanticJoinCode Code { get; }
    public ServiceCycleSemanticEventKind EvaluationTerminalKind { get; }
    public ServiceCycleSemanticEventKind CycleTerminalKind { get; }
    public ulong StatePublication { get; }
    public ulong ProjectionFingerprint { get; }
    public ulong Batch { get; }
    public ulong TerminalEventSequence { get; }
    public bool Equals(ServiceCycleReplaySemanticJoin other) => Code == other.Code &&
        EvaluationTerminalKind == other.EvaluationTerminalKind && CycleTerminalKind == other.CycleTerminalKind &&
        StatePublication == other.StatePublication && ProjectionFingerprint == other.ProjectionFingerprint &&
        Batch == other.Batch && TerminalEventSequence == other.TerminalEventSequence;
    public override bool Equals(object? obj) => obj is ServiceCycleReplaySemanticJoin other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        (int)Code, (int)EvaluationTerminalKind, (int)CycleTerminalKind, StatePublication,
        ProjectionFingerprint, Batch, TerminalEventSequence);
    public static bool operator ==(ServiceCycleReplaySemanticJoin left, ServiceCycleReplaySemanticJoin right) =>
        left.Equals(right);
    public static bool operator !=(ServiceCycleReplaySemanticJoin left, ServiceCycleReplaySemanticJoin right) =>
        !left.Equals(right);
}

public readonly struct ServiceCycleReplayArtifactFooter
{
    internal ServiceCycleReplayArtifactFooter(
        long sequence,
        ServiceCycleReplayArtifactContext context,
        ServiceCycleReplayCycleFooterDisposition disposition,
        WakePolicy returnedWake,
        bool hasReturnedWake,
        ServiceStateProjectionSnapshot projection,
        bool hasProjection,
        int expectedActionCount,
        long firstRecordSequence,
        long lastRecordSequence,
        int retainedRecordCount,
        ServiceCycleReplayCompleteness completeness,
        long encodingDurationTicks,
        long encodingTimestampFrequency,
        long encodingAllocatedBytes,
        ServiceCycleReplaySemanticJoin join)
    {
        Sequence = sequence;
        Context = context;
        Disposition = disposition;
        ReturnedWake = returnedWake;
        HasReturnedWake = hasReturnedWake;
        Projection = projection;
        HasProjection = hasProjection;
        ExpectedActionCount = expectedActionCount;
        FirstRecordSequence = firstRecordSequence;
        LastRecordSequence = lastRecordSequence;
        RetainedRecordCount = retainedRecordCount;
        Completeness = completeness;
        EncodingDurationTicks = encodingDurationTicks;
        EncodingTimestampFrequency = encodingTimestampFrequency;
        EncodingAllocatedBytes = encodingAllocatedBytes;
        Join = join;
    }

    public long Sequence { get; }
    public ServiceCycleReplayArtifactContext Context { get; }
    public ServiceCycleReplayCycleFooterDisposition Disposition { get; }
    public WakePolicy ReturnedWake { get; }
    public bool HasReturnedWake { get; }
    public ServiceStateProjectionSnapshot Projection { get; }
    public bool HasProjection { get; }
    public int ExpectedActionCount { get; }
    public long FirstRecordSequence { get; }
    public long LastRecordSequence { get; }
    public int RetainedRecordCount { get; }
    public ServiceCycleReplayCompleteness Completeness { get; }
    public long EncodingDurationTicks { get; }
    public long EncodingTimestampFrequency { get; }
    public long EncodingAllocatedBytes { get; }
    public ServiceCycleReplaySemanticJoin Join { get; }
    public bool IsComplete => Completeness.IsComplete &&
        Disposition == ServiceCycleReplayCycleFooterDisposition.Provisional &&
        Join.Code == ServiceCycleReplaySemanticJoinCode.Complete;

    internal ServiceCycleReplayArtifactFooter WithJoin(ServiceCycleReplaySemanticJoin join) => new(
        Sequence,
        Context,
        Disposition,
        ReturnedWake,
        HasReturnedWake,
        Projection,
        HasProjection,
        ExpectedActionCount,
        FirstRecordSequence,
        LastRecordSequence,
        RetainedRecordCount,
        Completeness,
        EncodingDurationTicks,
        EncodingTimestampFrequency,
        EncodingAllocatedBytes,
        join);
}
