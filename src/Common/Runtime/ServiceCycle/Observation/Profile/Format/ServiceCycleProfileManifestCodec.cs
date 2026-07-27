#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Format;

internal static class ServiceCycleProfileManifestCodec
{
    internal const ushort SchemaVersion = 1;
    internal const int ManifestBytes = 160;

    private const int ChecksumOffset = 136;
    private static readonly byte[] Magic = { (byte)'O', (byte)'S', (byte)'P', (byte)'M' };

    internal static int Encode(in ServiceCycleProfileManifestDocument document, Span<byte> destination)
    {
        Validate(in document);
        if (destination.Length < ManifestBytes)
            throw new ArgumentException("The destination is too small.", nameof(destination));

        var output = destination.Slice(0, ManifestBytes);
        output.Clear();
        var calibration = document.Calibration;
        Magic.CopyTo(output);
        ServiceCycleProfileBinary.U16(output, 4, SchemaVersion);
        ServiceCycleProfileBinary.U16(output, 6, ManifestBytes);
        ServiceCycleProfileBinary.U16(output, 8, ServiceCycleProfileSegmentCodec.SchemaVersion);
        ServiceCycleProfileBinary.U16(output, 10, ServiceCycleProfileRecordCodec.SchemaVersion);
        ServiceCycleProfileBinary.U16(output, 12, ServiceCycleProfileRecordCodec.RecordBytes);
        ServiceCycleProfileBinary.U32(output, 16, (uint)document.Completeness);
        ServiceCycleProfileBinary.U32(output, 20, (uint)document.Reason);
        ServiceCycleProfileBinary.U32(output, 24, ServiceCycleProfileFormatMetadata.Flags(in calibration));
        ServiceCycleProfileBinary.U64(output, 32, document.Session.Value);
        ServiceCycleProfileBinary.U64(output, 40, document.SegmentCount);
        ServiceCycleProfileBinary.U64(output, 48, document.AcceptedRecords);
        ServiceCycleProfileBinary.U64(output, 56, document.WrittenRecords);
        ServiceCycleProfileBinary.U64(output, 64, document.FirstIncompleteSequence);
        ServiceCycleProfileBinary.U64(output, 72, document.SegmentBytes);
        ServiceCycleProfileBinary.I64(output, 80, calibration.TimestampFrequency);
        ServiceCycleProfileBinary.I64(output, 88, calibration.RawTimestamp);
        ServiceCycleProfileBinary.I64(output, 96, calibration.MonotonicTimestampTicks);
        if (!calibration.BuildId.TryWriteBytes(output.Slice(104, 16)))
            throw new InvalidOperationException("The profile build identity could not be encoded.");
        ServiceCycleProfileBinary.I64(output, 120, document.MinimumStartedAtRawTicks);
        ServiceCycleProfileBinary.I64(output, 128, document.MaximumStartedAtRawTicks);
        ServiceCycleProfileBinary.U32(
            output,
            ChecksumOffset,
            TraceCrc32.ComputeExcluding(output, ChecksumOffset, 4));
        return ManifestBytes;
    }

    internal static ServiceCycleProfileManifestDocument Decode(ReadOnlySpan<byte> source)
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

    private static ServiceCycleProfileManifestDocument DecodeCore(ReadOnlySpan<byte> source)
    {
        if (source.Length != ManifestBytes || !source.Slice(0, 4).SequenceEqual(Magic) ||
            ServiceCycleProfileBinary.U16(source, 4) != SchemaVersion ||
            ServiceCycleProfileBinary.U16(source, 6) != ManifestBytes ||
            ServiceCycleProfileBinary.U16(source, 8) != ServiceCycleProfileSegmentCodec.SchemaVersion ||
            ServiceCycleProfileBinary.U16(source, 10) != ServiceCycleProfileRecordCodec.SchemaVersion ||
            ServiceCycleProfileBinary.U16(source, 12) != ServiceCycleProfileRecordCodec.RecordBytes ||
            ServiceCycleProfileBinary.U16(source, 14) != 0 ||
            ServiceCycleProfileBinary.U32(source, 28) != 0 ||
            !ServiceCycleProfileBinary.AllZero(source.Slice(140, 20)) ||
            ServiceCycleProfileBinary.U32(source, ChecksumOffset) !=
                TraceCrc32.ComputeExcluding(source, ChecksumOffset, 4))
            throw Invalid();

        var completenessValue = ServiceCycleProfileBinary.U32(source, 16);
        var reasonValue = ServiceCycleProfileBinary.U32(source, 20);
        if (completenessValue is < (uint)ServiceCycleProfileCompleteness.Complete or
                > (uint)ServiceCycleProfileCompleteness.Incomplete ||
            reasonValue is < (uint)ServiceCycleProfileTerminalReason.UserStopped or
                > (uint)ServiceCycleProfileTerminalReason.ProbeFailed)
            throw Invalid();
        var calibration = ServiceCycleProfileFormatMetadata.Calibration(
            ServiceCycleProfileBinary.U32(source, 24),
            ServiceCycleProfileBinary.I64(source, 80),
            ServiceCycleProfileBinary.I64(source, 88),
            ServiceCycleProfileBinary.I64(source, 96),
            new Guid(source.Slice(104, 16)));
        var document = new ServiceCycleProfileManifestDocument(
            (ServiceCycleProfileCompleteness)completenessValue,
            (ServiceCycleProfileTerminalReason)reasonValue,
            new ServiceCycleProfileSessionId(ServiceCycleProfileBinary.U64(source, 32)),
            in calibration,
            ServiceCycleProfileBinary.U64(source, 40),
            ServiceCycleProfileBinary.U64(source, 48),
            ServiceCycleProfileBinary.U64(source, 56),
            ServiceCycleProfileBinary.U64(source, 64),
            ServiceCycleProfileBinary.U64(source, 72),
            ServiceCycleProfileBinary.I64(source, 120),
            ServiceCycleProfileBinary.I64(source, 128));
        Validate(in document);
        return document;
    }

    private static void Validate(in ServiceCycleProfileManifestDocument document)
    {
        if (!document.Session.IsValid) throw new ArgumentException("A valid profile session is required.");
        if (!document.Calibration.IsValid) throw new ArgumentException("A valid profile calibration is required.");
        if (document.WrittenRecords > document.AcceptedRecords || document.AcceptedRecords > long.MaxValue)
            throw new ArgumentException("The profile record range is invalid.", nameof(document));
        var expectedBytes = checked(
            document.WrittenRecords * ServiceCycleProfileRecordCodec.RecordBytes +
            document.SegmentCount * (ServiceCycleProfileSegmentCodec.HeaderBytes + ServiceCycleProfileSegmentCodec.FooterBytes));
        if (document.WrittenRecords == 0 &&
                (document.SegmentCount != 0 || document.SegmentBytes != 0 ||
                    document.MinimumStartedAtRawTicks != 0 || document.MaximumStartedAtRawTicks != 0) ||
            document.WrittenRecords != 0 &&
                (document.SegmentCount == 0 || document.SegmentCount > document.WrittenRecords ||
                    document.SegmentBytes != expectedBytes ||
                    document.MaximumStartedAtRawTicks < document.MinimumStartedAtRawTicks))
            throw new ArgumentException("The profile segment evidence is inconsistent.", nameof(document));

        var complete = document.Completeness == ServiceCycleProfileCompleteness.Complete;
        if (complete)
        {
            if (document.Reason is not (ServiceCycleProfileTerminalReason.UserStopped or
                    ServiceCycleProfileTerminalReason.RuntimeShutdown) ||
                document.AcceptedRecords != document.WrittenRecords || document.FirstIncompleteSequence != 0)
                throw new ArgumentException("Complete profile evidence is inconsistent.", nameof(document));
        }
        else if (document.Completeness != ServiceCycleProfileCompleteness.Incomplete ||
            document.Reason == ServiceCycleProfileTerminalReason.UserStopped ||
            document.FirstIncompleteSequence != document.WrittenRecords + 1)
        {
            throw new ArgumentException("Incomplete profile evidence is inconsistent.", nameof(document));
        }
    }

    private static FormatException Invalid() => new("Invalid service-cycle profile manifest.");
}
#endif
