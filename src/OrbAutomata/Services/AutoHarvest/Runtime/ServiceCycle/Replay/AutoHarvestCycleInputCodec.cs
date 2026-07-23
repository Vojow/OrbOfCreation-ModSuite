using System;
using System.Buffers.Binary;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbAutomata;

internal sealed class AutoHarvestCycleInputCodec : IServiceCycleReplayCodec<AutoHarvestCycleInputRecord>
{
    internal const int EncodedBytes = 31;
    private const int PairBytes = 11;
    private const byte KnownFlags = 0b00_111111;

    public ServiceCycleReplayCodecDescriptor Descriptor => new(1, EncodedBytes);

    public int Encode(in AutoHarvestCycleInputRecord record, Span<byte> destination)
    {
        AutoHarvestReplayCodecPrimitives.RequireCapacity(destination.Length, EncodedBytes);
        WritePair(record.Fruit, destination);
        WritePair(record.Treasure, destination.Slice(PairBytes));
        destination[22] = Flags(record);
        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(23), record.EvaluationIntervalTicks);
        return EncodedBytes;
    }

    public AutoHarvestCycleInputRecord Decode(ReadOnlySpan<byte> source)
    {
        AutoHarvestReplayCodecPrimitives.RequireLength(source.Length, EncodedBytes);
        var fruit = ReadPair(source).ToCapture(AutoHarvestPair.FruitTree);
        var treasure = ReadPair(source.Slice(PairBytes)).ToCapture(AutoHarvestPair.TreasureTree);
        var flags = source[22];
        if ((flags & ~KnownFlags) != 0)
            throw new ArgumentException("The Auto Harvest replay flags are invalid.");
        var frame = new AutoHarvestCycleFrame(fruit, treasure, (flags & 32) != 0);
        var config = AutoHarvestConfigurationFactory.Create(
            (flags & 1) != 0,
            (flags & 2) != 0,
            (flags & 4) != 0,
            (flags & 8) != 0,
            (flags & 16) != 0,
            new OrbModding.Common.Runtime.MonotonicDuration(
                BinaryPrimitives.ReadInt64LittleEndian(source.Slice(23))));
        return new AutoHarvestCycleInputRecord(frame, config);
    }

    private static void WritePair(in AutoHarvestPairCaptureRecord record, Span<byte> destination)
    {
        destination[0] = (byte)record.CaptureKind;
        destination[1] = (byte)record.UnavailableReason;
        destination[2] = (byte)record.FailureScope;
        destination[3] = (byte)record.Identity;
        destination[4] = (byte)record.PlotVisibility;
        destination[5] = (byte)record.ActionAvailability;
        destination[6] = (byte)record.Prerequisites;
        destination[7] = (byte)record.Readiness;
        destination[8] = (byte)record.ActionSafety;
        destination[9] = (byte)record.NoDuplicate;
        destination[10] = (byte)record.ActionSlotAvailability;
    }

    private static AutoHarvestPairCaptureRecord ReadPair(ReadOnlySpan<byte> source)
    {
        var facts = new AutoHarvestPairFacts(
            ReadEvidence(source[3]),
            ReadEvidence(source[4]),
            ReadEvidence(source[5]),
            ReadEvidence(source[6]),
            ReadEvidence(source[7]),
            ReadSafety(source[8]),
            ReadEvidence(source[9]),
            ReadEvidence(source[10]));
        return AutoHarvestPairCaptureRecord.Decode(
            ReadCaptureKind(source[0]),
            ReadUnavailableReason(source[1]),
            ReadFailureScope(source[2]),
            facts);
    }

    private static byte Flags(in AutoHarvestCycleInputRecord record) => (byte)(
        (record.MasterEnabled ? 1 : 0) |
        (record.EmergencyDisabled ? 2 : 0) |
        (record.ActiveMode ? 4 : 0) |
        (record.FruitSelected ? 8 : 0) |
        (record.TreasureSelected ? 16 : 0) |
        (record.OwnsActionFamily ? 32 : 0));

    private static AutoHarvestPairCaptureKind ReadCaptureKind(byte value) => value switch
    {
        (byte)AutoHarvestPairCaptureKind.NotSelected => AutoHarvestPairCaptureKind.NotSelected,
        (byte)AutoHarvestPairCaptureKind.Captured => AutoHarvestPairCaptureKind.Captured,
        (byte)AutoHarvestPairCaptureKind.Unavailable => AutoHarvestPairCaptureKind.Unavailable,
        _ => throw new ArgumentException("The Auto Harvest replay capture kind is invalid."),
    };

    private static AutoHarvestCaptureUnavailableReason ReadUnavailableReason(byte value) => value switch
    {
        (byte)AutoHarvestCaptureUnavailableReason.None => AutoHarvestCaptureUnavailableReason.None,
        (byte)AutoHarvestCaptureUnavailableReason.RegistryNotReady => AutoHarvestCaptureUnavailableReason.RegistryNotReady,
        (byte)AutoHarvestCaptureUnavailableReason.ContractUnavailable => AutoHarvestCaptureUnavailableReason.ContractUnavailable,
        (byte)AutoHarvestCaptureUnavailableReason.Faulted => AutoHarvestCaptureUnavailableReason.Faulted,
        _ => throw new ArgumentException("The Auto Harvest replay unavailable reason is invalid."),
    };

    private static AutoHarvestCaptureFailureScope ReadFailureScope(byte value) => value switch
    {
        0 => default,
        (byte)AutoHarvestCaptureFailureScope.Feature => AutoHarvestCaptureFailureScope.Feature,
        (byte)AutoHarvestCaptureFailureScope.Pair => AutoHarvestCaptureFailureScope.Pair,
        _ => throw new ArgumentException("The Auto Harvest replay failure scope is invalid."),
    };

    private static AutoHarvestEvidenceState ReadEvidence(byte value) => value switch
    {
        (byte)AutoHarvestEvidenceState.Unknown => AutoHarvestEvidenceState.Unknown,
        (byte)AutoHarvestEvidenceState.Rejected => AutoHarvestEvidenceState.Rejected,
        (byte)AutoHarvestEvidenceState.Verified => AutoHarvestEvidenceState.Verified,
        _ => throw new ArgumentException("The Auto Harvest replay evidence is invalid."),
    };

    private static AutoHarvestActionSafetyState ReadSafety(byte value) => value switch
    {
        (byte)AutoHarvestActionSafetyState.Unknown => AutoHarvestActionSafetyState.Unknown,
        (byte)AutoHarvestActionSafetyState.Destructive => AutoHarvestActionSafetyState.Destructive,
        (byte)AutoHarvestActionSafetyState.ResourceDrain => AutoHarvestActionSafetyState.ResourceDrain,
        (byte)AutoHarvestActionSafetyState.UnsafeCompletionEffects => AutoHarvestActionSafetyState.UnsafeCompletionEffects,
        (byte)AutoHarvestActionSafetyState.NativePhaseCyclePreserving => AutoHarvestActionSafetyState.NativePhaseCyclePreserving,
        _ => throw new ArgumentException("The Auto Harvest replay action safety is invalid."),
    };
}
