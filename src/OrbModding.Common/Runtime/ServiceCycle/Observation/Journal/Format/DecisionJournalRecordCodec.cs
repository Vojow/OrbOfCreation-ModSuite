using System;
using System.Buffers.Binary;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;

internal static class DecisionJournalRecordCodec
{
    internal const int RecordBytes = 512;
    private const ushort HasWakeFlag = 1;
    private const ushort HasProjectionFlag = 2;
    private const int ProjectionOffset = 184;
    private const int ProjectionEntryBytes = 16;
    private const int ReservedOffset = ProjectionOffset +
        ServiceStateProjectionSnapshot.MaximumEntryCount * ProjectionEntryBytes;

    internal static void Write(Span<byte> destination, in DecisionJournalRecord record)
    {
        if (destination.Length != RecordBytes)
            throw new ArgumentException("A journal record requires its exact fixed-size destination.", nameof(destination));
        DecisionJournalRecordValidation.Validate(in record);
        destination.Clear();

        WriteU16(destination, 0, (ushort)record.Kind);
        var flags = (ushort)((record.HasWake ? HasWakeFlag : 0) |
            (record.HasProjection ? HasProjectionFlag : 0));
        WriteU16(destination, 2, flags);
        WriteI32(destination, 4, record.TransitionCode);
        WriteU64(destination, 8, record.Service.Value);
        WriteU64(destination, 16, record.Lifecycle);
        WriteU64(destination, 24, record.Configuration);
        WriteU64(destination, 32, record.Strategy);
        WriteU64(destination, 40, record.FirstCapture);
        WriteU64(destination, 48, record.LastCapture);
        WriteU64(destination, 56, record.FirstCycle);
        WriteU64(destination, 64, record.LastCycle);
        WriteI64(destination, 72, record.FirstTimestampTicks);
        WriteI64(destination, 80, record.LastTimestampTicks);
        WriteI64(destination, 88, record.RepeatCount);
        WriteI32(destination, 96, record.StartDecisionCode);
        WriteI32(destination, 100, record.CaptureDecisionCode);
        WriteI32(destination, 104, record.HasWake ? (int)record.Wake.Kind : 0);
        WriteI32(destination, 108, (int)record.FaultCategory);
        WriteI64(destination, 112, record.HasWake ? WakeValue(record.Wake) : 0);
        WriteI32(destination, 120, record.FaultCode);
        WriteI32(destination, 124, record.FirstFaultOccurrence);
        WriteI32(destination, 128, record.LastFaultOccurrence);
        WriteI32(destination, 132, (int)record.TerminalDisposition);
        WriteI32(destination, 136, record.TerminalResultCode);
        WriteI32(destination, 140, record.ActionCount);
        WriteI64(destination, 144, record.CommittedActions);
        WriteI64(destination, 152, record.NativeCallsAttempted);
        WriteI64(destination, 160, record.MutationAttempts);
        WriteI64(destination, 168, record.MutationsCommitted);
        WriteI32(destination, 176, record.HasProjection ? record.Projection.Count : 0);

        if (!record.HasProjection) return;
        for (var index = 0; index < record.Projection.Count; index++)
        {
            var entry = record.Projection.GetEntry(index);
            var offset = ProjectionOffset + index * ProjectionEntryBytes;
            WriteI32(destination, offset, entry.Key.Value);
            WriteI32(destination, offset + 4, (int)entry.Value.Kind);
            WriteI64(destination, offset + 8, ProjectionValue(entry.Value));
        }
    }

    internal static DecisionJournalRecord Read(ReadOnlySpan<byte> source)
    {
        try
        {
            return ReadCore(source);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw Invalid();
        }
        catch (OverflowException)
        {
            throw Invalid();
        }
    }

    private static DecisionJournalRecord ReadCore(ReadOnlySpan<byte> source)
    {
        if (source.Length != RecordBytes || ReadI32(source, 180) != 0 ||
            !IsZero(source.Slice(ReservedOffset)))
            throw Invalid();
        var flags = ReadU16(source, 2);
        if ((flags & ~(HasWakeFlag | HasProjectionFlag)) != 0) throw Invalid();
        var hasWake = (flags & HasWakeFlag) != 0;
        var hasProjection = (flags & HasProjectionFlag) != 0;
        var projection = ReadProjection(source, hasProjection);
        var record = new DecisionJournalRecord(
            (DecisionJournalRecordKind)ReadU16(source, 0),
            TraceService(ReadU64(source, 8)),
            ReadU64(source, 16),
            ReadU64(source, 24),
            ReadU64(source, 32),
            ReadU64(source, 40),
            ReadU64(source, 48),
            ReadU64(source, 56),
            ReadU64(source, 64),
            ReadI64(source, 72),
            ReadI64(source, 80),
            ReadI64(source, 88),
            ReadI32(source, 96),
            ReadI32(source, 100),
            hasWake,
            ReadWake(ReadI32(source, 104), ReadI64(source, 112), hasWake),
            hasProjection,
            in projection,
            (ServiceFaultCategory)ReadI32(source, 108),
            ReadI32(source, 120),
            ReadI32(source, 124),
            ReadI32(source, 128),
            (BatchTerminalDisposition)ReadI32(source, 132),
            ReadI32(source, 136),
            ReadI32(source, 140),
            ReadI64(source, 144),
            ReadI64(source, 152),
            ReadI64(source, 160),
            ReadI64(source, 168),
            ReadI32(source, 4));
        DecisionJournalRecordValidation.Validate(in record);
        return record;
    }

    private static ServiceStateProjectionSnapshot ReadProjection(
        ReadOnlySpan<byte> source,
        bool hasProjection)
    {
        var count = ReadI32(source, 176);
        if (count < 0 || count > ServiceStateProjectionSnapshot.MaximumEntryCount ||
            !hasProjection && count != 0)
            throw Invalid();

        var buffer = new ServiceStateProjectionWriteBuffer(ServiceStateProjectionSnapshot.MaximumEntryCount);
        var builder = new ServiceStateProjectionBuilder(buffer);
        for (var index = 0; index < count; index++)
        {
            var offset = ProjectionOffset + index * ProjectionEntryBytes;
            var key = new ServiceProjectionKey(ReadI32(source, offset));
            var kind = (ServiceProjectionValueKind)ReadI32(source, offset + 4);
            var raw = ReadI64(source, offset + 8);
            builder.Add(key, ProjectionValue(kind, raw));
        }
        var unused = source.Slice(
            ProjectionOffset + count * ProjectionEntryBytes,
            (ServiceStateProjectionSnapshot.MaximumEntryCount - count) * ProjectionEntryBytes);
        if (!IsZero(unused)) throw Invalid();
        return builder.CaptureSnapshot();
    }

    private static ServiceProjectionValue ProjectionValue(ServiceProjectionValueKind kind, long raw) => kind switch
    {
        ServiceProjectionValueKind.Boolean when raw is 0 or 1 => ServiceProjectionValue.FromBoolean(raw != 0),
        ServiceProjectionValueKind.Integer => ServiceProjectionValue.FromInteger(raw),
        ServiceProjectionValueKind.FloatingPoint =>
            ServiceProjectionValue.FromFloatingPoint(BitConverter.Int64BitsToDouble(raw)),
        _ => throw Invalid(),
    };

    private static long ProjectionValue(ServiceProjectionValue value) => value.Kind switch
    {
        ServiceProjectionValueKind.Boolean or ServiceProjectionValueKind.Integer => value.Integer,
        ServiceProjectionValueKind.FloatingPoint => BitConverter.DoubleToInt64Bits(value.FloatingPoint),
        _ => throw new ArgumentException("The projection value is invalid.", nameof(value)),
    };

    private static WakePolicy ReadWake(int kind, long value, bool present)
    {
        if (!present)
        {
            if (kind != 0 || value != 0) throw Invalid();
            return default;
        }
        if (value < 0) throw Invalid();
        return (WakePolicyKind)kind switch
        {
            WakePolicyKind.Default when value == 0 => WakePolicy.Default,
            WakePolicyKind.Immediate when value == 0 => WakePolicy.Immediate,
            WakePolicyKind.AfterDecision => WakePolicy.AfterDecision(new MonotonicDuration(value)),
            WakePolicyKind.AfterBatch => WakePolicy.AfterBatch(new MonotonicDuration(value)),
            WakePolicyKind.At => WakePolicy.At(new MonotonicTimestamp(value)),
            _ => throw Invalid(),
        };
    }

    private static long WakeValue(WakePolicy wake) => wake.Kind switch
    {
        WakePolicyKind.Default or WakePolicyKind.Immediate => 0,
        WakePolicyKind.AfterDecision or WakePolicyKind.AfterBatch => wake.Delay.Ticks,
        WakePolicyKind.At => wake.DueTime.Ticks,
        _ => throw new ArgumentException("The journal wake is invalid.", nameof(wake)),
    };

    private static ServiceCycleTraceServiceId TraceService(ulong value) =>
        value == 0 ? default : new ServiceCycleTraceServiceId(value);

    private static bool IsZero(ReadOnlySpan<byte> bytes)
    {
        for (var index = 0; index < bytes.Length; index++)
            if (bytes[index] != 0) return false;
        return true;
    }

    private static FormatException Invalid() => new("Invalid decision-journal record.");
    private static ushort ReadU16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
    private static int ReadI32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
    private static ulong ReadU64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset, 8));
    private static long ReadI64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(offset, 8));
    private static void WriteU16(Span<byte> bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.Slice(offset, 2), value);
    private static void WriteI32(Span<byte> bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(offset, 4), value);
    private static void WriteU64(Span<byte> bytes, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(offset, 8), value);
    private static void WriteI64(Span<byte> bytes, int offset, long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(bytes.Slice(offset, 8), value);
}
