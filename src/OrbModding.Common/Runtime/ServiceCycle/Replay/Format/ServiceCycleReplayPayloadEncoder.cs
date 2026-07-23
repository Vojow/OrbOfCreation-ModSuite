using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayPayloadEncoder
{
    private const uint CodecCanonicalFlag = 1;

    internal static void WriteCodecs(Span<byte> destination, ServiceCycleReplayCodecManifestEntry[] codecs)
    {
        for (var index = 0; index < codecs.Length; index++)
        {
            var row = destination.Slice(index * ServiceCycleReplayArtifactFormat.CodecManifestEntryBytes,
                ServiceCycleReplayArtifactFormat.CodecManifestEntryBytes);
            var codec = codecs[index];
            ServiceCycleReplayBinary.I32(row, 0, codec.TraceServiceKey);
            ServiceCycleReplayBinary.U16(row, 4, (ushort)codec.Role);
            ServiceCycleReplayBinary.U16(row, 6, codec.Descriptor.SchemaVersion);
            ServiceCycleReplayBinary.U32(row, 8, checked((uint)codec.Descriptor.MaximumEncodedBytes));
            ServiceCycleReplayBinary.U32(row, 12, CodecCanonicalFlag);
        }
    }

    internal static void WriteRecords(Span<byte> destination, ServiceCycleReplayArtifactRecord[] records)
    {
        var payloadOffset = 0;
        for (var index = 0; index < records.Length; index++)
        {
            var row = destination.Slice(index * ServiceCycleReplayArtifactFormat.RecordIndexEntryBytes,
                ServiceCycleReplayArtifactFormat.RecordIndexEntryBytes);
            var record = records[index];
            ServiceCycleReplayBinary.I64(row, 0, record.Sequence);
            var cycle = record.Cycle;
            ServiceCycleReplayBinary.WriteCycleKey(row, 8, in cycle);
            ServiceCycleReplayBinary.U16(row, 56, (ushort)record.Identity.Kind);
            ServiceCycleReplayBinary.U16(row, 58, record.SchemaVersion);
            ServiceCycleReplayBinary.I32(row, 60, record.Identity.Index);
            ServiceCycleReplayBinary.U64(row, 64, checked((ulong)payloadOffset));
            ServiceCycleReplayBinary.U32(row, 72, checked((uint)record.PayloadView.Length));
            ServiceCycleReplayBinary.U32(row, 76, record.Checksum);
            payloadOffset = checked(payloadOffset + record.PayloadView.Length);
        }
    }
}
