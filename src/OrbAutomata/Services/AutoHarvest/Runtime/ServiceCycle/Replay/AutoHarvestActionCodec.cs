using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbAutomata;

internal sealed class AutoHarvestActionCodec : IServiceCycleReplayCodec<AutoHarvestActionRecord>
{
    public ServiceCycleReplayCodecDescriptor Descriptor => new(1, 1);

    public int Encode(in AutoHarvestActionRecord record, Span<byte> destination)
    {
        AutoHarvestReplayCodecPrimitives.RequireCapacity(destination.Length, 1);
        destination[0] = (byte)record.Pair;
        return 1;
    }

    public AutoHarvestActionRecord Decode(ReadOnlySpan<byte> source)
    {
        AutoHarvestReplayCodecPrimitives.RequireLength(source.Length, 1);
        return new AutoHarvestActionRecord(AutoHarvestReplayCodecPrimitives.ReadPair(source[0]));
    }
}
