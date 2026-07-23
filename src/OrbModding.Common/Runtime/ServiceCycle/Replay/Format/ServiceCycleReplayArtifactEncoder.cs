using System;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayArtifactEncoder
{
    private const int GlobalChecksumOffset = 80;
    private static readonly byte[] Magic = { (byte)'O', (byte)'S', (byte)'C', (byte)'R' };

    internal static int GetEncodedLength(ServiceCycleReplayPreparedArtifact artifact)
    {
        var codecBytes = checked(artifact.Codecs.Length * ServiceCycleReplayArtifactFormat.CodecManifestEntryBytes);
        var recordBytes = checked(artifact.GlobalRecords.Length * ServiceCycleReplayArtifactFormat.RecordIndexEntryBytes);
        var footerBytes = checked(artifact.Joined.Footers.Length * ServiceCycleReplayArtifactFormat.CycleFooterBytes);
        var length = checked(ServiceCycleReplayArtifactFormat.HeaderBytes +
            ServiceCycleReplayArtifactFormat.RequiredSectionCount * ServiceCycleReplayArtifactFormat.DirectoryEntryBytes +
            ServiceCycleReplayArtifactFormat.ManifestBytes + artifact.SemanticBytes.Length + codecBytes +
            recordBytes + artifact.Payload.Length + footerBytes);
        if (length > ServiceCycleReplayArtifactFormat.MaximumArtifactBytes)
            throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.ArtifactLimitExceeded);
        return length;
    }

    internal static int Encode(ServiceCycleReplayPreparedArtifact artifact, Span<byte> destination)
        => Encode(artifact, destination, null);

    internal static int Encode(
        ServiceCycleReplayPreparedArtifact artifact,
        Span<byte> destination,
        ServiceCycleReplayFormatWorkCounter? work)
    {
        var length = GetEncodedLength(artifact);
        if (destination.Length < length) throw new ArgumentException("The destination is too small.", nameof(destination));
        var output = destination.Slice(0, length);
        output.Clear();
        var directoryBytes = ServiceCycleReplayArtifactFormat.RequiredSectionCount *
            ServiceCycleReplayArtifactFormat.DirectoryEntryBytes;
        var next = ServiceCycleReplayArtifactFormat.HeaderBytes + directoryBytes;
        var sections = new ServiceCycleReplaySection[ServiceCycleReplayArtifactFormat.RequiredSectionCount];
        work?.Add(checked(
            artifact.Semantic.Count + artifact.Codecs.Length + artifact.GlobalRecords.Length +
            artifact.Joined.Footers.Length));

        var manifestOffset = next;
        ServiceCycleReplayManifestEncoder.Write(
            output.Slice(manifestOffset, ServiceCycleReplayArtifactFormat.ManifestBytes), artifact, work);
        sections[0] = Section(ServiceCycleReplaySectionKind.Manifest, 1, manifestOffset,
            ServiceCycleReplayArtifactFormat.ManifestBytes, 1, output);
        next += ServiceCycleReplayArtifactFormat.ManifestBytes;

        artifact.SemanticBytes.Span.CopyTo(output.Slice(next));
        sections[1] = Section(
            ServiceCycleReplaySectionKind.SemanticTrace,
            ServiceCycleReplayArtifactFormat.EmbeddedSemanticSchemaVersion,
            next, artifact.SemanticBytes.Length, artifact.Semantic.Count, output);
        next += artifact.SemanticBytes.Length;

        var codecLength = artifact.Codecs.Length * ServiceCycleReplayArtifactFormat.CodecManifestEntryBytes;
        ServiceCycleReplayPayloadEncoder.WriteCodecs(output.Slice(next, codecLength), artifact.Codecs);
        sections[2] = Section(ServiceCycleReplaySectionKind.CodecManifest, 1, next, codecLength,
            artifact.Codecs.Length, output);
        next += codecLength;

        var recordLength = artifact.GlobalRecords.Length * ServiceCycleReplayArtifactFormat.RecordIndexEntryBytes;
        ServiceCycleReplayPayloadEncoder.WriteRecords(output.Slice(next, recordLength), artifact.GlobalRecords);
        sections[3] = Section(ServiceCycleReplaySectionKind.ReplayRecordIndex, 1, next, recordLength,
            artifact.GlobalRecords.Length, output);
        next += recordLength;

        artifact.Payload.Span.CopyTo(output.Slice(next));
        sections[4] = Section(ServiceCycleReplaySectionKind.ReplayPayload, 1, next, artifact.Payload.Length,
            artifact.Payload.Length, output);
        next += artifact.Payload.Length;

        var footerLength = artifact.Joined.Footers.Length * ServiceCycleReplayArtifactFormat.CycleFooterBytes;
        ServiceCycleReplayFooterEncoder.WriteAll(output.Slice(next, footerLength), artifact.Joined.Footers);
        sections[5] = Section(ServiceCycleReplaySectionKind.CycleFooters, 1, next, footerLength,
            artifact.Joined.Footers.Length, output);
        next += footerLength;
        if (next != length) throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.LengthMismatch);

        WriteDirectory(
            output.Slice(ServiceCycleReplayArtifactFormat.HeaderBytes, directoryBytes),
            sections);
        WriteHeader(output, artifact, length, directoryBytes);
        ServiceCycleReplayBinary.U32(output, GlobalChecksumOffset,
            ServiceCycleReplayCrc32.ComputeExcluding(output, GlobalChecksumOffset, 4));
        return length;
    }

    private static ServiceCycleReplaySection Section(
        ServiceCycleReplaySectionKind kind,
        ushort version,
        int offset,
        int length,
        int count,
        ReadOnlySpan<byte> artifact) => new(
            kind, version, offset, length, count,
            ServiceCycleReplayCrc32.Compute(artifact.Slice(offset, length)));

    private static void WriteHeader(
        Span<byte> output,
        ServiceCycleReplayPreparedArtifact artifact,
        int length,
        int directoryBytes)
    {
        Magic.CopyTo(output);
        ServiceCycleReplayBinary.U16(output, 4, ServiceCycleReplayArtifactFormat.SchemaVersion);
        ServiceCycleReplayBinary.U16(output, 6, ServiceCycleReplayArtifactFormat.HeaderBytes);
        ServiceCycleReplayBinary.U16(output, 8, ServiceCycleReplayArtifactFormat.DirectoryEntryBytes);
        ServiceCycleReplayBinary.U16(output, 10, ServiceCycleReplayArtifactFormat.RequiredSectionCount);
        ServiceCycleReplayBinary.U32(output, 12, 0);
        ServiceCycleReplayBinary.U64(output, 16, checked((ulong)length));
        ServiceCycleReplayBinary.U64(output, 24, ServiceCycleReplayArtifactFormat.HeaderBytes);
        ServiceCycleReplayBinary.U64(output, 32, checked((ulong)directoryBytes));
        ServiceCycleReplayBinary.U64(output, 40, artifact.Semantic.Session.Value);
        var lastSemantic = artifact.Semantic.Count == 0 ? 0 : artifact.Semantic[^1].Id.Sequence;
        ServiceCycleReplayBinary.U64(output, 48, lastSemantic);
        var fence = artifact.SourceRecording.HighWater;
        ServiceCycleReplayBinary.I64(output, 56, fence.Publication);
        ServiceCycleReplayBinary.I64(output, 64, fence.RecordSequence);
        ServiceCycleReplayBinary.I64(output, 72, fence.FooterSequence);
        ServiceCycleReplayBinary.U32(output, GlobalChecksumOffset, 0);
        // 84..95 are reserved and were cleared above.
    }

    private static void WriteDirectory(Span<byte> destination, ServiceCycleReplaySection[] sections)
    {
        for (var index = 0; index < sections.Length; index++)
        {
            var row = destination.Slice(
                index * ServiceCycleReplayArtifactFormat.DirectoryEntryBytes,
                ServiceCycleReplayArtifactFormat.DirectoryEntryBytes);
            var section = sections[index];
            ServiceCycleReplayBinary.U16(row, 0, (ushort)section.Kind);
            ServiceCycleReplayBinary.U16(row, 2, section.Version);
            ServiceCycleReplayBinary.U32(row, 4, 0);
            ServiceCycleReplayBinary.U64(row, 8, checked((ulong)section.Offset));
            ServiceCycleReplayBinary.U64(row, 16, checked((ulong)section.Length));
            ServiceCycleReplayBinary.U64(row, 24, checked((ulong)section.Count));
            ServiceCycleReplayBinary.U32(row, 32, section.Checksum);
            ServiceCycleReplayBinary.U32(row, 36, 0);
        }
    }

}
