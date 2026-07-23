using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>Reads one execution descriptor once and freezes the exact artifact-approved contract.</summary>
internal sealed class ServiceCycleReplayFrozenCodec<TRecord> : IServiceCycleReplayCodec<TRecord>
    where TRecord : struct, IServiceCycleReplayRecord
{
    private readonly IServiceCycleReplayCodec<TRecord> _inner;

    private ServiceCycleReplayFrozenCodec(
        IServiceCycleReplayCodec<TRecord> inner,
        ServiceCycleReplayCodecDescriptor descriptor)
    {
        _inner = inner;
        Descriptor = descriptor;
    }

    public ServiceCycleReplayCodecDescriptor Descriptor { get; }
    public int Encode(in TRecord record, Span<byte> destination) => _inner.Encode(in record, destination);
    public TRecord Decode(ReadOnlySpan<byte> source) => _inner.Decode(source);

    internal static bool TryCreate(
        ServiceCycleReplayArtifactDocument artifact,
        int traceServiceKey,
        ServiceCycleReplayCodecRole role,
        IServiceCycleReplayCodec<TRecord> codec,
        out ServiceCycleReplayFrozenCodec<TRecord>? frozen)
    {
        ServiceCycleReplayCodecDescriptor descriptor;
        try { descriptor = codec.Descriptor; }
        catch (Exception exception) when (ServiceCycleReplayContainedRunner.IsContainable(exception))
        {
            frozen = null;
            return false;
        }
        if (!TryExpected(artifact, traceServiceKey, role, out var expected) || descriptor != expected)
        {
            frozen = null;
            return false;
        }
        frozen = new ServiceCycleReplayFrozenCodec<TRecord>(codec, descriptor);
        return true;
    }

    private static bool TryExpected(
        ServiceCycleReplayArtifactDocument artifact,
        int traceServiceKey,
        ServiceCycleReplayCodecRole role,
        out ServiceCycleReplayCodecDescriptor descriptor)
    {
        for (var index = 0; index < artifact.CodecCount; index++)
        {
            var entry = artifact.GetCodec(index);
            if (entry.TraceServiceKey != traceServiceKey || entry.Role != role) continue;
            descriptor = entry.Descriptor;
            return true;
        }
        descriptor = default;
        return false;
    }
}
