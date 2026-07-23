using System;
using System.Buffers.Binary;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbAutomata;

internal sealed class AutoHarvestStateCodec : IServiceCycleReplayCodec<AutoHarvestStateRecord>
{
    internal const int EncodedBytes = 17;

    public ServiceCycleReplayCodecDescriptor Descriptor => new(1, EncodedBytes);

    public int Encode(in AutoHarvestStateRecord record, Span<byte> destination)
    {
        AutoHarvestReplayCodecPrimitives.RequireCapacity(destination.Length, EncodedBytes);
        BinaryPrimitives.WriteUInt64LittleEndian(destination, record.Lifecycle);
        destination[8] = (byte)record.NextPair;
        destination[9] = AutoHarvestReplayCodecPrimitives.WriteBool(record.HasPlannedAction);
        destination[10] = (byte)record.PlannedPair;
        WriteHealth(record.FruitHealth, destination.Slice(11));
        WriteHealth(record.TreasureHealth, destination.Slice(14));
        return EncodedBytes;
    }

    public AutoHarvestStateRecord Decode(ReadOnlySpan<byte> source)
    {
        AutoHarvestReplayCodecPrimitives.RequireLength(source.Length, EncodedBytes);
        var lifecycle = BinaryPrimitives.ReadUInt64LittleEndian(source);
        if (lifecycle == 0) throw new ArgumentException("The Auto Harvest replay lifecycle is invalid.");
        var nextPair = AutoHarvestReplayCodecPrimitives.ReadPair(source[8]);
        var hasPlannedAction = AutoHarvestReplayCodecPrimitives.ReadBool(source[9]);
        var plannedPair = AutoHarvestReplayCodecPrimitives.ReadPair(source[10]);
        if (!hasPlannedAction && plannedPair != default)
            throw new ArgumentException("The Auto Harvest replay pending action is invalid.");
        var fruitHealth = ReadHealth(source.Slice(11), AutoHarvestPair.FruitTree);
        var treasureHealth = ReadHealth(source.Slice(14), AutoHarvestPair.TreasureTree);
        return new AutoHarvestStateRecord(
            lifecycle,
            nextPair,
            hasPlannedAction,
            plannedPair,
            fruitHealth,
            treasureHealth);
    }

    private static void WriteHealth(in AutoHarvestPairHealthRecord health, Span<byte> destination)
    {
        destination[0] = AutoHarvestReplayCodecPrimitives.WriteBool(health.Selected);
        destination[1] = (byte)health.Kind;
        destination[2] = AutoHarvestReplayCodecPrimitives.WriteBool(health.FeatureScoped);
    }

    private static AutoHarvestPairHealthRecord ReadHealth(
        ReadOnlySpan<byte> source,
        AutoHarvestPair pair)
    {
        var selected = AutoHarvestReplayCodecPrimitives.ReadBool(source[0]);
        var kind = source[1] <= (byte)AutoHarvestPairHealthKind.Faulted
            ? (AutoHarvestPairHealthKind)source[1]
            : throw new ArgumentException("The Auto Harvest replay health kind is invalid.");
        var featureScoped = AutoHarvestReplayCodecPrimitives.ReadBool(source[2]);
        if (!selected && (kind != AutoHarvestPairHealthKind.NotSelected || featureScoped) ||
            selected && kind == AutoHarvestPairHealthKind.NotSelected ||
            featureScoped && kind is not AutoHarvestPairHealthKind.RegistryNotReady and
                not AutoHarvestPairHealthKind.ContractUnavailable and
                not AutoHarvestPairHealthKind.Faulted)
        {
            throw new ArgumentException("The Auto Harvest replay health is invalid.");
        }
        var health = new AutoHarvestPairHealth(pair, selected, kind, featureScoped);
        return new AutoHarvestPairHealthRecord(health);
    }
}
