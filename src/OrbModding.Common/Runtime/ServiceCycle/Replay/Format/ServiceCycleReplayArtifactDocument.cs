using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

public readonly struct ServiceCycleReplayArtifactFence
{
    internal ServiceCycleReplayArtifactFence(
        ServiceCycleTraceSessionId semanticSession,
        ulong semanticLastEventSequence,
        ServiceCycleReplayHighWaterFence replay)
    {
        SemanticSession = semanticSession;
        SemanticLastEventSequence = semanticLastEventSequence;
        Replay = replay;
    }

    public ServiceCycleTraceSessionId SemanticSession { get; }
    public ulong SemanticLastEventSequence { get; }
    public ServiceCycleReplayHighWaterFence Replay { get; }
}

public sealed class ServiceCycleReplayArtifactDocument
{
    private readonly ServiceCycleReplayPreparedArtifact _prepared;
    private readonly ServiceCycleReplayCodecManifestEntry[] _codecs;
    private readonly ServiceCycleReplayCodecIndex _codecIndex;
    private readonly ServiceCycleReplayArtifactCycle[] _cycles;

    internal ServiceCycleReplayArtifactDocument(
        ServiceCycleReplayPreparedArtifact prepared,
        ServiceCycleReplayArtifactFence fence,
        ServiceCycleTraceDocument semanticTrace,
        ServiceCycleReplayRecordingSnapshot recording,
        ServiceCycleReplayCodecManifestEntry[] codecs,
        ServiceCycleReplayCodecIndex codecIndex,
        ServiceCycleReplayArtifactCycle[] cycles,
        ServiceCycleReplayArtifactEligibilityCode eligibility)
    {
        _prepared = prepared;
        Fence = fence;
        SemanticTrace = semanticTrace;
        Recording = recording;
        _codecs = codecs;
        _codecIndex = codecIndex;
        _cycles = cycles;
        Eligibility = eligibility;
    }

    public ushort SchemaVersion => ServiceCycleReplayArtifactFormat.SchemaVersion;
    public ServiceCycleReplayArtifactFence Fence { get; }
    public ServiceCycleTraceDocument SemanticTrace { get; }
    public ServiceCycleReplayRecordingSnapshot Recording { get; }
    public ServiceCycleReplayArtifactEligibilityCode Eligibility { get; }
    public bool IsComplete => Eligibility == ServiceCycleReplayArtifactEligibilityCode.Complete;
    public ServiceCycleReplayCompleteness Completeness => Recording.Completeness;
    public ServiceCycleReplayFault Fault => Recording.Fault;
    public int CodecCount => _codecs.Length;
    public int CycleCount => _cycles.Length;
    public int EncodedLength => ServiceCycleReplayArtifactEncoder.GetEncodedLength(_prepared);
    public ServiceCycleReplayCodecManifestEntry GetCodec(int index) => _codecs[index];
    public ServiceCycleReplayCodecDescriptor GetCodecDescriptor(
        int traceServiceKey,
        ServiceCycleReplayCodecRole role)
    {
        if (traceServiceKey <= 0) throw new ArgumentOutOfRangeException(nameof(traceServiceKey));
        if (role is < ServiceCycleReplayCodecRole.CycleInput or > ServiceCycleReplayCodecRole.Action)
            throw new ArgumentOutOfRangeException(nameof(role));
        return _codecIndex.TryGetDescriptor(traceServiceKey, role, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException("The artifact has no matching replay codec descriptor.");
    }
    public ServiceCycleReplayArtifactCycle GetCycle(int index) => _cycles[index];

    internal ServiceCycleReplayPreparedArtifact Prepared => _prepared;
}

public sealed class ServiceCycleReplayArtifactCycle
{
    private readonly ServiceCycleReplayArtifactRecord[] _records;
    private readonly int[] _semanticEventIndices;
    private readonly ServiceCycleTraceDocument _semantic;

    internal ServiceCycleReplayArtifactCycle(
        ServiceCycleReplayArtifactFooter footer,
        ServiceCycleReplayArtifactRecord[] records,
        int[] semanticEventIndices,
        ServiceCycleTraceDocument semantic)
    {
        Footer = footer;
        _records = records;
        _semanticEventIndices = semanticEventIndices;
        _semantic = semantic;
    }

    public ServiceCycleReplayArtifactFooter Footer { get; }
    public ServiceCycleReplayCycleKey Key => Footer.Context.Cycle;
    public ServiceCycleReplaySemanticJoin Join => Footer.Join;
    public bool IsComplete => Footer.IsComplete;
    public int RecordCount => _records.Length;
    public int SemanticEventCount => _semanticEventIndices.Length;
    public ServiceCycleReplayArtifactRecord GetRecord(int index) => _records[index];
    public ServiceCycleSemanticEvent GetSemanticEvent(int index) => _semantic[_semanticEventIndices[index]];
}

public sealed class ServiceCycleReplayArtifactRecord
{
    internal ServiceCycleReplayArtifactRecord(
        long sequence,
        ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayRecordIdentity identity,
        ushort schemaVersion,
        ReadOnlyMemory<byte> payload,
        uint checksum)
    {
        Sequence = sequence;
        Cycle = cycle;
        Identity = identity;
        SchemaVersion = schemaVersion;
        _payload = payload;
        Checksum = checksum;
    }

    private readonly ReadOnlyMemory<byte> _payload;
    public long Sequence { get; }
    public ServiceCycleReplayCycleKey Cycle { get; }
    public ServiceCycleReplayRecordIdentity Identity { get; }
    public ushort SchemaVersion { get; }
    public int PayloadLength => _payload.Length;
    public byte[] GetPayloadCopy() => _payload.ToArray();
    public void CopyPayloadTo(Span<byte> destination)
    {
        if (destination.Length < _payload.Length)
            throw new ArgumentException("The destination is too small.", nameof(destination));
        _payload.Span.CopyTo(destination);
    }
    public uint Checksum { get; }
    internal ReadOnlyMemory<byte> PayloadView => _payload;
}
