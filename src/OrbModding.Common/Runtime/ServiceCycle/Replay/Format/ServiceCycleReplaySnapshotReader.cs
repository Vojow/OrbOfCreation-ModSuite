using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

/// <summary>Copies the exact append-only replay prefixes captured by a recording snapshot.</summary>
internal static class ServiceCycleReplaySnapshotReader
{
    internal static ServiceCycleReplayCodecManifestEntry[] ReadCodecs(
        ServiceCycleReplaySession session,
        in ServiceCycleReplayRecordingSnapshot snapshot)
    {
        var count = snapshot.CodecManifests.Count;
        var manifestFence = snapshot.CodecManifests;
        var result = new ServiceCycleReplayCodecManifestEntry[checked(count * 3)];
        for (var index = 0; index < count; index++)
        {
            if (!session.TryReadCodecManifestAt(index, in manifestFence, out var manifest) ||
                !manifest.IsValid)
                throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.CodecManifestInvalid, index);
            var offset = index * 3;
            result[offset] = new ServiceCycleReplayCodecManifestEntry(
                manifest.TraceServiceKey, ServiceCycleReplayCodecRole.CycleInput, manifest.CycleInput);
            result[offset + 1] = new ServiceCycleReplayCodecManifestEntry(
                manifest.TraceServiceKey, ServiceCycleReplayCodecRole.State, manifest.State);
            result[offset + 2] = new ServiceCycleReplayCodecManifestEntry(
                manifest.TraceServiceKey, ServiceCycleReplayCodecRole.Action, manifest.Action);
        }
        Array.Sort(result, CodecComparer.Instance);
        for (var index = 1; index < result.Length; index++)
            if (result[index - 1].TraceServiceKey == result[index].TraceServiceKey &&
                result[index - 1].Role == result[index].Role)
                throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.CodecManifestInvalid, index);
        return result;
    }

    internal static ServiceCycleReplayArtifactRecord[] ReadRecords(
        ServiceCycleReplaySession session,
        in ServiceCycleReplayRecordingSnapshot snapshot,
        byte[] payload)
    {
        var fence = snapshot.HighWater;
        if (fence.RecordSequence != fence.RecordCount)
            throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.RecordSequenceInvalid);
        var result = new ServiceCycleReplayArtifactRecord[fence.RecordCount];
        var expectedOffset = 0;
        for (var index = 0; index < result.Length; index++)
        {
            var header = session.ReadRecordHeader(index, in fence);
            if (header.Sequence != index + 1)
                throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.RecordSequenceInvalid, index);
            if (!header.Cycle.IsValid)
                throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.RecordCycleInvalid, index);
            if (!header.Identity.IsValid || header.SchemaVersion == 0)
                throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.RecordIdentityInvalid, index);
            if (header.ByteOffset != expectedOffset || header.ByteLength < 0 ||
                header.ByteLength > payload.Length - expectedOffset)
                throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.RecordPayloadPartitionInvalid, index);
            session.CopyBytes(header.ByteOffset, payload.AsSpan(expectedOffset, header.ByteLength), in fence);
            var memory = payload.AsMemory(expectedOffset, header.ByteLength);
            result[index] = new ServiceCycleReplayArtifactRecord(
                header.Sequence,
                header.Cycle,
                header.Identity,
                header.SchemaVersion,
                memory,
                ServiceCycleReplayCrc32.Compute(memory.Span));
            expectedOffset = checked(expectedOffset + header.ByteLength);
        }
        if (expectedOffset != payload.Length)
            throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.RecordPayloadPartitionInvalid);
        return result;
    }

    internal static ServiceCycleReplayArtifactFooter[] ReadFooters(
        ServiceCycleReplaySession session,
        in ServiceCycleReplayRecordingSnapshot snapshot)
    {
        var fence = snapshot.HighWater;
        if (fence.FooterSequence != fence.FooterCount)
            throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.CycleFooterOrderInvalid);
        var result = new ServiceCycleReplayArtifactFooter[fence.FooterCount];
        for (var index = 0; index < result.Length; index++)
        {
            var source = session.ReadFooter(index, in fence);
            if (source.Sequence != index + 1 || !source.Context.Cycle.IsValid)
                throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.CycleFooterOrderInvalid, index);
            result[index] = ServiceCycleReplayFooterConverter.Convert(in source);
        }
        return result;
    }

    private sealed class CodecComparer : System.Collections.Generic.IComparer<ServiceCycleReplayCodecManifestEntry>
    {
        internal static readonly CodecComparer Instance = new();

        public int Compare(ServiceCycleReplayCodecManifestEntry x, ServiceCycleReplayCodecManifestEntry y)
        {
            var service = x.TraceServiceKey.CompareTo(y.TraceServiceKey);
            return service != 0 ? service : x.Role.CompareTo(y.Role);
        }
    }
}
