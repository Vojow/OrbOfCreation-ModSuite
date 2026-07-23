using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayPayloadDecoder
{
    internal static ServiceCycleReplayCodecManifestEntry[] ReadCodecs(ReadOnlySpan<byte> source, int count,
        in ServiceCycleReplayArtifactLimits limits,
        ServiceCycleReplayFormatWorkCounter? work = null)
    {
        if (count > limits.MaximumCodecEntries || count % 3 != 0 ||
            source.Length != count * ServiceCycleReplayArtifactFormat.CodecManifestEntryBytes)
            throw Error(ServiceCycleReplayFormatErrorCode.CodecManifestInvalid);
        var result = new ServiceCycleReplayCodecManifestEntry[count];
        for (var index = 0; index < count; index++)
        {
            work?.Add();
            var row = source.Slice(index * ServiceCycleReplayArtifactFormat.CodecManifestEntryBytes,
                ServiceCycleReplayArtifactFormat.CodecManifestEntryBytes);
            var service = ServiceCycleReplayBinary.I32(row, 0);
            var roleValue = ServiceCycleReplayBinary.U16(row, 4);
            var schema = ServiceCycleReplayBinary.U16(row, 6);
            var maximum = ServiceCycleReplayBinary.U32(row, 8);
            if (service <= 0 || roleValue is < 1 or > 3 || schema == 0 || maximum == 0 ||
                maximum > ServiceCycleReplayCodecLimits.MaximumEncodedBytes ||
                ServiceCycleReplayBinary.U32(row, 12) != 1 || !ServiceCycleReplayBinary.IsZero(row.Slice(16, 8)))
                throw Error(ServiceCycleReplayFormatErrorCode.CodecManifestInvalid, index);
            try
            {
                result[index] = new ServiceCycleReplayCodecManifestEntry(service,
                    (ServiceCycleReplayCodecRole)roleValue,
                    new ServiceCycleReplayCodecDescriptor(schema, checked((int)maximum)));
            }
            catch (ArgumentException) { throw Error(ServiceCycleReplayFormatErrorCode.CodecManifestInvalid, index); }
            if (index != 0 && Compare(result[index - 1], result[index]) >= 0)
                throw Error(ServiceCycleReplayFormatErrorCode.CodecManifestOrderInvalid, index);
        }
        for (var index = 0; index < count; index += 3)
        {
            work?.Add();
            if (result[index].Role != ServiceCycleReplayCodecRole.CycleInput ||
                result[index + 1].Role != ServiceCycleReplayCodecRole.State ||
                result[index + 2].Role != ServiceCycleReplayCodecRole.Action ||
                result[index].TraceServiceKey != result[index + 1].TraceServiceKey ||
                result[index].TraceServiceKey != result[index + 2].TraceServiceKey)
                throw Error(ServiceCycleReplayFormatErrorCode.CodecManifestCoverageInvalid, index);
        }
        return result;
    }

    internal static ServiceCycleReplayArtifactRecord[] ReadRecords(byte[] encoded,
        ServiceCycleReplaySection indexSection, ServiceCycleReplaySection payloadSection,
        ServiceCycleReplayCodecManifestEntry[] codecs, in ServiceCycleReplayArtifactLimits limits,
        ServiceCycleReplayFormatWorkCounter? work = null) =>
        ReadRecords(encoded, indexSection, payloadSection, ServiceCycleReplayCodecIndex.Build(codecs, work), in limits, work);

    internal static ServiceCycleReplayArtifactRecord[] ReadRecords(byte[] encoded,
        ServiceCycleReplaySection indexSection, ServiceCycleReplaySection payloadSection,
        ServiceCycleReplayCodecIndex codecs, in ServiceCycleReplayArtifactLimits limits,
        ServiceCycleReplayFormatWorkCounter? work = null)
    {
        if (indexSection.Count > limits.MaximumRecords ||
            indexSection.Length != indexSection.Count * ServiceCycleReplayArtifactFormat.RecordIndexEntryBytes)
            throw Error(ServiceCycleReplayFormatErrorCode.RecordIndexInvalid);
        var result = new ServiceCycleReplayArtifactRecord[indexSection.Count];
        var expectedPayloadOffset = 0;
        for (var index = 0; index < result.Length; index++)
        {
            work?.Add();
            var row = encoded.AsSpan(indexSection.Offset + index * ServiceCycleReplayArtifactFormat.RecordIndexEntryBytes,
                ServiceCycleReplayArtifactFormat.RecordIndexEntryBytes);
            var sequence = ServiceCycleReplayBinary.I64(row, 0);
            if (sequence != index + 1) throw Error(ServiceCycleReplayFormatErrorCode.RecordSequenceInvalid, index);
            var cycle = ServiceCycleReplayBinary.ReadCycleKey(row, 8);
            if (!cycle.IsValid) throw Error(ServiceCycleReplayFormatErrorCode.RecordCycleInvalid, index);
            var kindValue = ServiceCycleReplayBinary.U16(row, 56);
            var schema = ServiceCycleReplayBinary.U16(row, 58);
            var recordIndex = ServiceCycleReplayBinary.I32(row, 60);
            ServiceCycleReplayRecordIdentity identity;
            try { identity = new ServiceCycleReplayRecordIdentity((ServiceCycleReplayRecordKind)kindValue, recordIndex); }
            catch (ArgumentException) { throw Error(ServiceCycleReplayFormatErrorCode.RecordIdentityInvalid, index); }
            var payloadOffsetValue = ServiceCycleReplayBinary.U64(row, 64);
            var payloadLengthValue = ServiceCycleReplayBinary.U32(row, 72);
            if (payloadOffsetValue > int.MaxValue || payloadLengthValue > int.MaxValue ||
                payloadOffsetValue != (ulong)expectedPayloadOffset ||
                payloadLengthValue > (uint)(payloadSection.Length - expectedPayloadOffset) ||
                !ServiceCycleReplayBinary.IsZero(row.Slice(80, 8)))
                throw Error(ServiceCycleReplayFormatErrorCode.RecordPayloadPartitionInvalid, index);
            var payloadLength = (int)payloadLengthValue;
            var memory = encoded.AsMemory(payloadSection.Offset + expectedPayloadOffset, payloadLength);
            var crc = ServiceCycleReplayBinary.U32(row, 76);
            if (crc != ServiceCycleReplayCrc32.Compute(memory.Span))
                throw Error(ServiceCycleReplayFormatErrorCode.RecordChecksumMismatch, index);
            if (!codecs.TryGetDescriptor(cycle.TraceServiceKey, identity.Kind,
                    out var descriptor, work) || schema != descriptor.SchemaVersion || payloadLength > descriptor.MaximumEncodedBytes)
                throw Error(ServiceCycleReplayFormatErrorCode.RecordSchemaMismatch, index);
            result[index] = new ServiceCycleReplayArtifactRecord(sequence, cycle, identity, schema, memory, crc);
            expectedPayloadOffset = checked(expectedPayloadOffset + payloadLength);
        }
        if (expectedPayloadOffset != payloadSection.Length)
            throw Error(ServiceCycleReplayFormatErrorCode.RecordPayloadPartitionInvalid);
        return result;
    }

    internal static ServiceCycleReplayArtifactFooter[] ReadFooters(byte[] encoded,
        ServiceCycleReplaySection section, in ServiceCycleReplayArtifactLimits limits,
        ServiceCycleReplayFormatWorkCounter? work = null)
    {
        if (section.Count > limits.MaximumCycleFooters ||
            section.Length != section.Count * ServiceCycleReplayArtifactFormat.CycleFooterBytes)
            throw Error(ServiceCycleReplayFormatErrorCode.CycleFooterInvalid);
        var result = new ServiceCycleReplayArtifactFooter[section.Count];
        for (var index = 0; index < result.Length; index++)
        {
            work?.Add();
            result[index] = ServiceCycleReplayFooterDecoder.Read(
                encoded.AsSpan(section.Offset + index * ServiceCycleReplayArtifactFormat.CycleFooterBytes,
                    ServiceCycleReplayArtifactFormat.CycleFooterBytes), index);
        }
        return result;
    }

    private static int Compare(ServiceCycleReplayCodecManifestEntry left,
        ServiceCycleReplayCodecManifestEntry right)
    {
        var service = left.TraceServiceKey.CompareTo(right.TraceServiceKey);
        return service != 0 ? service : left.Role.CompareTo(right.Role);
    }

    private static ServiceCycleReplayFormatException Error(ServiceCycleReplayFormatErrorCode code, int index = -1) =>
        ServiceCycleReplayBinary.Error(code, index);
}
