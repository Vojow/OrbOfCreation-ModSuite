using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

public readonly struct ServiceCycleReplaySessionOptions
{
    public ServiceCycleReplaySessionOptions(
        bool encodingEnabled,
        int byteCapacity,
        int recordCapacity,
        int cycleFooterCapacity,
        int serviceCapacity = 1)
    {
        if (byteCapacity < 0) throw new ArgumentOutOfRangeException(nameof(byteCapacity));
        if (recordCapacity < 0) throw new ArgumentOutOfRangeException(nameof(recordCapacity));
        if (cycleFooterCapacity < 0) throw new ArgumentOutOfRangeException(nameof(cycleFooterCapacity));
        if (serviceCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(serviceCapacity));
        if (encodingEnabled && (byteCapacity == 0 || recordCapacity == 0 || cycleFooterCapacity == 0))
            throw new ArgumentException("Enabled replay recording requires nonzero byte, record, and footer capacities.");
        EncodingEnabled = encodingEnabled;
        ByteCapacity = byteCapacity;
        RecordCapacity = recordCapacity;
        CycleFooterCapacity = cycleFooterCapacity;
        ServiceCapacity = serviceCapacity;
    }

    public bool EncodingEnabled { get; }
    public int ByteCapacity { get; }
    public int RecordCapacity { get; }
    public int CycleFooterCapacity { get; }
    public int ServiceCapacity { get; }
}

public enum ServiceCycleReplayCodecRole
{
    CycleInput = 1,
    State = 2,
    Action = 3,
}

/// <summary>Frozen canonical codec schemas for one trace service ordinal.</summary>
public readonly struct ServiceCycleReplayCodecManifest : IEquatable<ServiceCycleReplayCodecManifest>
{
    internal ServiceCycleReplayCodecManifest(
        int traceServiceKey,
        ServiceCycleReplayCodecDescriptor cycleInput,
        ServiceCycleReplayCodecDescriptor state,
        ServiceCycleReplayCodecDescriptor action)
    {
        TraceServiceKey = traceServiceKey;
        CycleInput = cycleInput;
        State = state;
        Action = action;
    }

    public int TraceServiceKey { get; }
    public bool CanonicalEncodingRequired => true;
    public ServiceCycleReplayCodecDescriptor CycleInput { get; }
    public ServiceCycleReplayCodecDescriptor State { get; }
    public ServiceCycleReplayCodecDescriptor Action { get; }
    public bool IsValid => TraceServiceKey > 0 && CycleInput.IsValid && State.IsValid && Action.IsValid;

    public ServiceCycleReplayCodecDescriptor GetDescriptor(ServiceCycleReplayCodecRole role) => role switch
    {
        ServiceCycleReplayCodecRole.CycleInput => CycleInput,
        ServiceCycleReplayCodecRole.State => State,
        ServiceCycleReplayCodecRole.Action => Action,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public bool Equals(ServiceCycleReplayCodecManifest other) =>
        TraceServiceKey == other.TraceServiceKey && CycleInput == other.CycleInput &&
        State == other.State && Action == other.Action;
    public override bool Equals(object? obj) => obj is ServiceCycleReplayCodecManifest other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(TraceServiceKey, CycleInput, State, Action);
    public static bool operator ==(ServiceCycleReplayCodecManifest left, ServiceCycleReplayCodecManifest right) =>
        left.Equals(right);
    public static bool operator !=(ServiceCycleReplayCodecManifest left, ServiceCycleReplayCodecManifest right) =>
        !left.Equals(right);
}
