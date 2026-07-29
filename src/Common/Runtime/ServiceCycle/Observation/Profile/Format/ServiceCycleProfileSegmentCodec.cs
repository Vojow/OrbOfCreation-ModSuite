#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Format;

internal static class ServiceCycleProfileSegmentCodec
{
    internal const ushort SchemaVersion = 1;
    internal const int HeaderBytes = 128;
    internal const int FooterBytes = 40;
    internal const int MaximumRecords = 4_096;

    private const int FooterChecksumOffset = 32;
    private static readonly byte[] HeaderMagic = { (byte)'O', (byte)'S', (byte)'P', (byte)'S' };
    private static readonly byte[] FooterMagic = { (byte)'O', (byte)'S', (byte)'P', (byte)'F' };

    internal static int GetEncodedLength(int recordCount)
    {
        if (recordCount is <= 0 or > MaximumRecords)
            throw new ArgumentOutOfRangeException(nameof(recordCount));
        return checked(HeaderBytes + recordCount * ServiceCycleProfileRecordCodec.RecordBytes + FooterBytes);
    }

    internal static int Encode(
        ServiceCycleProfileSessionId session,
        ulong ordinal,
        ulong firstRecordSequence,
        in ServiceCycleProfileCalibration calibration,
        ReadOnlySpan<ServiceCycleProfileRecord> records,
        Span<byte> destination)
    {
        ValidateEnvelope(session, firstRecordSequence, in calibration, records);
        var length = GetEncodedLength(records.Length);
        if (destination.Length < length) throw new ArgumentException("The destination is too small.", nameof(destination));

        var output = destination.Slice(0, length);
        output.Clear();
        HeaderMagic.CopyTo(output);
        ServiceCycleProfileBinary.U16(output, 4, SchemaVersion);
        ServiceCycleProfileBinary.U16(output, 6, HeaderBytes);
        ServiceCycleProfileBinary.U16(output, 8, ServiceCycleProfileRecordCodec.SchemaVersion);
        ServiceCycleProfileBinary.U16(output, 10, ServiceCycleProfileRecordCodec.RecordBytes);
        ServiceCycleProfileBinary.U32(output, 12, ServiceCycleProfileFormatMetadata.Flags(in calibration));
        ServiceCycleProfileBinary.U64(output, 16, session.Value);
        ServiceCycleProfileBinary.U64(output, 24, ordinal);
        ServiceCycleProfileBinary.U64(output, 32, firstRecordSequence);
        ServiceCycleProfileBinary.U64(output, 40, checked((ulong)records.Length));
        ServiceCycleProfileBinary.I64(output, 48, calibration.TimestampFrequency);
        ServiceCycleProfileBinary.I64(output, 56, calibration.RawTimestamp);
        ServiceCycleProfileBinary.I64(output, 64, calibration.MonotonicTimestampTicks);
        if (!calibration.BuildId.TryWriteBytes(output.Slice(72, 16)))
            throw new InvalidOperationException("The profile build identity could not be encoded.");
        ServiceCycleProfileBinary.U64(output, 88, checked((ulong)length));
        TimestampRange(records, out var minimumStartedAt, out var maximumStartedAt);
        ServiceCycleProfileBinary.I64(output, 96, minimumStartedAt);
        ServiceCycleProfileBinary.I64(output, 104, maximumStartedAt);

        for (var index = 0; index < records.Length; index++)
            ServiceCycleProfileRecordCodec.Write(
                output.Slice(
                    HeaderBytes + index * ServiceCycleProfileRecordCodec.RecordBytes,
                    ServiceCycleProfileRecordCodec.RecordBytes),
                in records[index]);

        var footerOffset = length - FooterBytes;
        FooterMagic.CopyTo(output.Slice(footerOffset));
        ServiceCycleProfileBinary.U16(output, footerOffset + 4, SchemaVersion);
        ServiceCycleProfileBinary.U16(output, footerOffset + 6, FooterBytes);
        ServiceCycleProfileBinary.U64(output, footerOffset + 8, session.Value);
        ServiceCycleProfileBinary.U64(output, footerOffset + 16, ordinal);
        ServiceCycleProfileBinary.U64(
            output,
            footerOffset + 24,
            checked(firstRecordSequence + (ulong)records.Length - 1));
        var checksumOffset = footerOffset + FooterChecksumOffset;
        ServiceCycleProfileBinary.U32(
            output,
            checksumOffset,
            TraceCrc32.ComputeExcluding(output, checksumOffset, 4));
        return length;
    }

    internal static ServiceCycleProfileSegmentDocument Decode(ReadOnlySpan<byte> source)
    {
        try
        {
            return DecodeCore(source);
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

    private static ServiceCycleProfileSegmentDocument DecodeCore(ReadOnlySpan<byte> source)
    {
        if (source.Length < HeaderBytes + ServiceCycleProfileRecordCodec.RecordBytes + FooterBytes ||
            !source.Slice(0, 4).SequenceEqual(HeaderMagic) ||
            ServiceCycleProfileBinary.U16(source, 4) != SchemaVersion ||
            ServiceCycleProfileBinary.U16(source, 6) != HeaderBytes ||
            ServiceCycleProfileBinary.U16(source, 8) != ServiceCycleProfileRecordCodec.SchemaVersion ||
            ServiceCycleProfileBinary.U16(source, 10) != ServiceCycleProfileRecordCodec.RecordBytes ||
            !ServiceCycleProfileBinary.AllZero(source.Slice(112, 16)))
            throw Invalid();

        var flags = ServiceCycleProfileBinary.U32(source, 12);
        var sessionValue = ServiceCycleProfileBinary.U64(source, 16);
        var ordinal = ServiceCycleProfileBinary.U64(source, 24);
        var firstSequence = ServiceCycleProfileBinary.U64(source, 32);
        var countValue = ServiceCycleProfileBinary.U64(source, 40);
        var totalBytes = ServiceCycleProfileBinary.U64(source, 88);
        var minimumStartedAt = ServiceCycleProfileBinary.I64(source, 96);
        var maximumStartedAt = ServiceCycleProfileBinary.I64(source, 104);
        if (sessionValue == 0 || firstSequence == 0 || countValue is 0 or > MaximumRecords ||
            totalBytes != checked((ulong)GetEncodedLength((int)countValue)) ||
            totalBytes != checked((ulong)source.Length) || maximumStartedAt < minimumStartedAt)
            throw Invalid();

        var footerOffset = source.Length - FooterBytes;
        if (!source.Slice(footerOffset, 4).SequenceEqual(FooterMagic) ||
            ServiceCycleProfileBinary.U16(source, footerOffset + 4) != SchemaVersion ||
            ServiceCycleProfileBinary.U16(source, footerOffset + 6) != FooterBytes ||
            ServiceCycleProfileBinary.U64(source, footerOffset + 8) != sessionValue ||
            ServiceCycleProfileBinary.U64(source, footerOffset + 16) != ordinal ||
            ServiceCycleProfileBinary.U64(source, footerOffset + 24) != checked(firstSequence + countValue - 1) ||
            ServiceCycleProfileBinary.U32(source, footerOffset + 36) != 0)
            throw Invalid();
        var checksumOffset = footerOffset + FooterChecksumOffset;
        if (ServiceCycleProfileBinary.U32(source, checksumOffset) !=
            TraceCrc32.ComputeExcluding(source, checksumOffset, 4))
            throw Invalid();

        var calibration = ServiceCycleProfileFormatMetadata.Calibration(
            flags,
            ServiceCycleProfileBinary.I64(source, 48),
            ServiceCycleProfileBinary.I64(source, 56),
            ServiceCycleProfileBinary.I64(source, 64),
            new Guid(source.Slice(72, 16)));
        var records = new ServiceCycleProfileRecord[(int)countValue];
        for (var index = 0; index < records.Length; index++)
        {
            records[index] = ServiceCycleProfileRecordCodec.Read(source.Slice(
                HeaderBytes + index * ServiceCycleProfileRecordCodec.RecordBytes,
                ServiceCycleProfileRecordCodec.RecordBytes));
            ValidateAllocation(in calibration, in records[index]);
        }
        TimestampRange(records, out var actualMinimum, out var actualMaximum);
        if (actualMinimum != minimumStartedAt || actualMaximum != maximumStartedAt)
            throw Invalid();
        return new ServiceCycleProfileSegmentDocument(
            new ServiceCycleProfileSessionId(sessionValue),
            ordinal,
            firstSequence,
            in calibration,
            records);
    }

    private static void ValidateEnvelope(
        ServiceCycleProfileSessionId session,
        ulong firstRecordSequence,
        in ServiceCycleProfileCalibration calibration,
        ReadOnlySpan<ServiceCycleProfileRecord> records)
    {
        if (!session.IsValid) throw new ArgumentException("A valid profile session is required.", nameof(session));
        if (firstRecordSequence == 0) throw new ArgumentOutOfRangeException(nameof(firstRecordSequence));
        if (!calibration.IsValid) throw new ArgumentException("A valid profile calibration is required.", nameof(calibration));
        _ = GetEncodedLength(records.Length);
        _ = checked(firstRecordSequence + (ulong)records.Length - 1);
        for (var index = 0; index < records.Length; index++)
        {
            ServiceCycleProfileRecordValidation.Validate(in records[index]);
            ValidateAllocation(in calibration, in records[index]);
        }
    }

    private static void ValidateAllocation(
        in ServiceCycleProfileCalibration calibration,
        in ServiceCycleProfileRecord record)
    {
        if (!calibration.AllocationAvailable && record.TotalAllocatedBytes != 0)
            throw new ArgumentException("Allocation-unavailable profile records must not claim an allocation delta.");
    }

    private static void TimestampRange(
        ReadOnlySpan<ServiceCycleProfileRecord> records,
        out long minimum,
        out long maximum)
    {
        minimum = long.MaxValue;
        maximum = long.MinValue;
        for (var index = 0; index < records.Length; index++)
        {
            minimum = Math.Min(minimum, records[index].FirstStartedAtRawTicks);
            maximum = Math.Max(maximum, records[index].LastStartedAtRawTicks);
        }
    }

    private static FormatException Invalid() => new("Invalid service-cycle profile segment.");
}
#endif
