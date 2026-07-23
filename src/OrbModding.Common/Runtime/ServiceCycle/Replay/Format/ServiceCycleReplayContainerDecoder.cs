using System;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal readonly struct ServiceCycleReplayContainerHeader
{
    internal ServiceCycleReplayContainerHeader(ulong semanticSession, ulong semanticLastSequence,
        long replayPublication, long replayRecordSequence, long replayFooterSequence,
        int directoryOffset, int directoryLength)
    { SemanticSession = semanticSession; SemanticLastSequence = semanticLastSequence;
      ReplayPublication = replayPublication; ReplayRecordSequence = replayRecordSequence;
      ReplayFooterSequence = replayFooterSequence; DirectoryOffset = directoryOffset; DirectoryLength = directoryLength; }
    internal ulong SemanticSession { get; }
    internal ulong SemanticLastSequence { get; }
    internal long ReplayPublication { get; }
    internal long ReplayRecordSequence { get; }
    internal long ReplayFooterSequence { get; }
    internal int DirectoryOffset { get; }
    internal int DirectoryLength { get; }
}
internal sealed class ServiceCycleReplayDecodedContainer
{
    internal ServiceCycleReplayDecodedContainer(ServiceCycleReplayContainerHeader header,
        ServiceCycleReplaySection[] sections, ServiceCycleTraceDocument semantic)
    { Header = header; Sections = sections; Semantic = semantic; }
    internal ServiceCycleReplayContainerHeader Header { get; }
    internal ServiceCycleReplaySection[] Sections { get; }
    internal ServiceCycleTraceDocument Semantic { get; }
}

internal static class ServiceCycleReplayContainerDecoder
{
    private const int GlobalChecksumOffset = 80;
    private static readonly byte[] Magic = { (byte)'O', (byte)'S', (byte)'C', (byte)'R' };

    internal static ServiceCycleReplayDecodedContainer Decode(
        ReadOnlySpan<byte> source,
        in ServiceCycleReplayArtifactLimits limits)
    {
        var header = ReadHeader(source, in limits);
        if (ServiceCycleReplayBinary.U32(source, GlobalChecksumOffset) !=
            ServiceCycleReplayCrc32.ComputeExcluding(source, GlobalChecksumOffset, 4))
            throw Error(ServiceCycleReplayFormatErrorCode.GlobalChecksumMismatch);
        var sections = ReadDirectory(source, in header, in limits);
        var semanticSection = sections[1];
        ServiceCycleTraceDocument semantic;
        try
        {
            semantic = ServiceCycleTraceCodec.Decode(
                source.Slice(semanticSection.Offset, semanticSection.Length), limits.MaximumSemanticEvents);
        }
        catch (FormatException) { throw Error(ServiceCycleReplayFormatErrorCode.SemanticTraceRejected); }
        if (semantic.Session.Value != header.SemanticSession ||
            (semantic.Count == 0 ? 0 : semantic[^1].Id.Sequence) != header.SemanticLastSequence)
            throw Error(ServiceCycleReplayFormatErrorCode.FenceMismatch);
        return new ServiceCycleReplayDecodedContainer(header, sections, semantic);
    }

    private static ServiceCycleReplayContainerHeader ReadHeader(
        ReadOnlySpan<byte> source,
        in ServiceCycleReplayArtifactLimits limits)
    {
        if (source.Length < ServiceCycleReplayArtifactFormat.HeaderBytes)
            throw Error(ServiceCycleReplayFormatErrorCode.SourceTooShort);
        if (source.Length > limits.MaximumArtifactBytes || source.Length > ServiceCycleReplayArtifactFormat.MaximumArtifactBytes)
            throw Error(ServiceCycleReplayFormatErrorCode.ArtifactLimitExceeded);
        if (!source.Slice(0, 4).SequenceEqual(Magic)) throw Error(ServiceCycleReplayFormatErrorCode.MagicMismatch);
        if (ServiceCycleReplayBinary.U16(source, 4) != ServiceCycleReplayArtifactFormat.SchemaVersion)
            throw Error(ServiceCycleReplayFormatErrorCode.ContainerVersionUnsupported);
        if (ServiceCycleReplayBinary.U16(source, 6) != ServiceCycleReplayArtifactFormat.HeaderBytes ||
            ServiceCycleReplayBinary.U16(source, 8) != ServiceCycleReplayArtifactFormat.DirectoryEntryBytes ||
            ServiceCycleReplayBinary.U16(source, 10) != ServiceCycleReplayArtifactFormat.RequiredSectionCount)
            throw Error(ServiceCycleReplayFormatErrorCode.HeaderShapeInvalid);
        if (ServiceCycleReplayBinary.U32(source, 12) != 0)
            throw Error(ServiceCycleReplayFormatErrorCode.HeaderFlagsUnsupported);
        if (!ServiceCycleReplayBinary.IsZero(source.Slice(84, 12)))
            throw Error(ServiceCycleReplayFormatErrorCode.ReservedBytesNonzero);
        var total = ServiceCycleReplayBinary.U64(source, 16);
        var directoryOffset = ServiceCycleReplayBinary.U64(source, 24);
        var directoryLength = ServiceCycleReplayBinary.U64(source, 32);
        var expectedDirectory = checked((ulong)(ServiceCycleReplayArtifactFormat.RequiredSectionCount *
            ServiceCycleReplayArtifactFormat.DirectoryEntryBytes));
        if (total > int.MaxValue || total != (ulong)source.Length ||
            directoryOffset != ServiceCycleReplayArtifactFormat.HeaderBytes || directoryLength != expectedDirectory)
            throw Error(ServiceCycleReplayFormatErrorCode.LengthMismatch);
        var semanticSession = ServiceCycleReplayBinary.U64(source, 40);
        var semanticLast = ServiceCycleReplayBinary.U64(source, 48);
        var replayPublication = ServiceCycleReplayBinary.I64(source, 56);
        var replayRecord = ServiceCycleReplayBinary.I64(source, 64);
        var replayFooter = ServiceCycleReplayBinary.I64(source, 72);
        if (semanticSession == 0 || replayPublication < 0 || replayRecord < 0 || replayFooter < 0)
            throw Error(ServiceCycleReplayFormatErrorCode.HeaderShapeInvalid);
        return new ServiceCycleReplayContainerHeader(semanticSession, semanticLast, replayPublication,
            replayRecord, replayFooter, checked((int)directoryOffset), checked((int)directoryLength));
    }

    private static ServiceCycleReplaySection[] ReadDirectory(ReadOnlySpan<byte> source,
        in ServiceCycleReplayContainerHeader header, in ServiceCycleReplayArtifactLimits limits)
    {
        var sections = new ServiceCycleReplaySection[ServiceCycleReplayArtifactFormat.RequiredSectionCount];
        var expectedOffset = checked(header.DirectoryOffset + header.DirectoryLength);
        for (var index = 0; index < sections.Length; index++)
        {
            var row = source.Slice(header.DirectoryOffset + index * ServiceCycleReplayArtifactFormat.DirectoryEntryBytes,
                ServiceCycleReplayArtifactFormat.DirectoryEntryBytes);
            var kindValue = ServiceCycleReplayBinary.U16(row, 0);
            if (kindValue != index + 1)
                throw Error(kindValue is < 1 or > ServiceCycleReplayArtifactFormat.RequiredSectionCount
                    ? ServiceCycleReplayFormatErrorCode.SectionKindUnsupported
                    : ServiceCycleReplayFormatErrorCode.SectionOrderInvalid, index);
            var kind = (ServiceCycleReplaySectionKind)kindValue;
            var version = ServiceCycleReplayBinary.U16(row, 2);
            var expectedVersion = kind == ServiceCycleReplaySectionKind.SemanticTrace
                ? ServiceCycleReplayArtifactFormat.EmbeddedSemanticSchemaVersion : (ushort)1;
            if (version != expectedVersion) throw Error(ServiceCycleReplayFormatErrorCode.SectionVersionUnsupported, index);
            if (ServiceCycleReplayBinary.U32(row, 4) != 0)
                throw Error(ServiceCycleReplayFormatErrorCode.SectionFlagsUnsupported, index);
            if (ServiceCycleReplayBinary.U32(row, 36) != 0)
                throw Error(ServiceCycleReplayFormatErrorCode.ReservedBytesNonzero, index);
            var offsetValue = ServiceCycleReplayBinary.U64(row, 8);
            var lengthValue = ServiceCycleReplayBinary.U64(row, 16);
            var countValue = ServiceCycleReplayBinary.U64(row, 24);
            if (offsetValue > int.MaxValue || lengthValue > int.MaxValue || countValue > int.MaxValue)
                throw Error(ServiceCycleReplayFormatErrorCode.LengthOverflow, index);
            var offset = (int)offsetValue;
            var length = (int)lengthValue;
            var count = (int)countValue;
            if (offset != expectedOffset || length > source.Length - offset)
                throw Error(ServiceCycleReplayFormatErrorCode.SectionBoundsInvalid, index);
            CheckLimit(kind, count, in limits, index);
            var checksum = ServiceCycleReplayBinary.U32(row, 32);
            if (checksum != ServiceCycleReplayCrc32.Compute(source.Slice(offset, length)))
                throw Error(ServiceCycleReplayFormatErrorCode.SectionChecksumMismatch, index);
            sections[index] = new ServiceCycleReplaySection(kind, version, offset, length, count, checksum);
            expectedOffset = checked(offset + length);
        }
        if (expectedOffset != source.Length) throw Error(ServiceCycleReplayFormatErrorCode.SectionBoundsInvalid);
        return sections;
    }

    private static void CheckLimit(ServiceCycleReplaySectionKind kind, int count,
        in ServiceCycleReplayArtifactLimits limits, int index)
    {
        var accepted = kind switch
        {
            ServiceCycleReplaySectionKind.Manifest => count == 1,
            ServiceCycleReplaySectionKind.SemanticTrace => count <= limits.MaximumSemanticEvents,
            ServiceCycleReplaySectionKind.CodecManifest => count <= limits.MaximumCodecEntries,
            ServiceCycleReplaySectionKind.ReplayRecordIndex => count <= limits.MaximumRecords,
            ServiceCycleReplaySectionKind.ReplayPayload => true,
            ServiceCycleReplaySectionKind.CycleFooters => count <= limits.MaximumCycleFooters,
            _ => false,
        };
        if (!accepted) throw Error(ServiceCycleReplayFormatErrorCode.ArtifactLimitExceeded, index);
    }

    private static ServiceCycleReplayFormatException Error(ServiceCycleReplayFormatErrorCode code, int index = -1) =>
        ServiceCycleReplayBinary.Error(code, index);
}
