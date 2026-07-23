using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Format;

public sealed class ServiceCycleReplayArtifactCodecTests
{
    public enum UnjoinedSemanticCase
    {
        None,
        CaptureUnavailable,
        CaptureFaulted,
        ReplayableStatePublished,
        OrdinaryEvaluationStarted,
    }

    [Fact]
    public void CanonicalV1RoundTripsExactSemanticAndRecordBytes()
    {
        var fixture = ArtifactFixture.Create();
        var snapshot = fixture.Snapshot;
        var encoded = ServiceCycleReplayArtifactCodec.Encode(fixture.Semantic, fixture.Session, in snapshot);

        var decoded = ServiceCycleReplayArtifactCodec.Decode(encoded);

        Assert.True(decoded.IsComplete);
        Assert.Equal(ServiceCycleReplayArtifactFormat.SchemaVersion, decoded.SchemaVersion);
        Assert.Equal((ushort)5, ServiceCycleReplayArtifactFormat.EmbeddedSemanticSchemaVersion);
        Assert.Equal(ServiceCycleReplayArtifactFormat.HeaderBytes, BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(6, 2)));
        Assert.Equal(ServiceCycleReplayArtifactFormat.RequiredSectionCount, BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(10, 2)));
        Assert.Equal(
            ServiceCycleReplayArtifactFormat.EmbeddedSemanticSchemaVersion,
            BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(
                ServiceCycleReplayArtifactFormat.HeaderBytes +
                ServiceCycleReplayArtifactFormat.DirectoryEntryBytes + 2,
                2)));
        Assert.Equal(
            ServiceCycleReplayArtifactFormat.EmbeddedSemanticSchemaVersion,
            BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(SectionOffset(encoded, 0), 2)));
        Assert.Equal(1, decoded.CycleCount);
        Assert.Equal(3, decoded.CodecCount);
        Assert.Equal(
            new ServiceCycleReplayCodecDescriptor(1, 8),
            decoded.GetCodecDescriptor(
                1,
                OrbModding.Common.Runtime.ServiceCycle.Replay.Format.ServiceCycleReplayCodecRole.CycleInput));
        Assert.Equal(
            new ServiceCycleReplayCodecDescriptor(1, 8),
            decoded.GetCodecDescriptor(
                1,
                OrbModding.Common.Runtime.ServiceCycle.Replay.Format.ServiceCycleReplayCodecRole.State));
        Assert.Equal(
            new ServiceCycleReplayCodecDescriptor(1, 8),
            decoded.GetCodecDescriptor(
                1,
                OrbModding.Common.Runtime.ServiceCycle.Replay.Format.ServiceCycleReplayCodecRole.Action));
        Assert.Equal(3, decoded.GetCycle(0).RecordCount);
        Assert.Equal(new byte[] { 11 }, decoded.GetCycle(0).GetRecord(0).GetPayloadCopy());
        var semanticOffset = SectionOffset(encoded, 1);
        var semanticLength = SectionLength(encoded, 1);
        Assert.Equal(fixture.Semantic, encoded.AsSpan(semanticOffset, semanticLength).ToArray());
        var reencoded = new byte[decoded.EncodedLength];
        Assert.Equal(encoded.Length, ServiceCycleReplayArtifactCodec.Reencode(decoded, reencoded));
        Assert.Equal(encoded, reencoded);
    }

    [Fact]
    public void DecodeOwnsOneCanonicalBackingBufferForAllWireByteViews()
    {
        var encoded = ArtifactFixture.Encode();

        var decoded = ServiceCycleReplayArtifactCodec.Decode(encoded);
        var prepared = decoded.Prepared;

        Assert.True(MemoryMarshal.TryGetArray(
            (ReadOnlyMemory<byte>)prepared.SemanticBytes, out var semantic));
        Assert.True(MemoryMarshal.TryGetArray(
            (ReadOnlyMemory<byte>)prepared.Payload, out var payload));
        Assert.True(MemoryMarshal.TryGetArray(
            prepared.GlobalRecords[0].PayloadView, out var record));
        Assert.NotSame(encoded, semantic.Array);
        Assert.Same(semantic.Array, payload.Array);
        Assert.Same(semantic.Array, record.Array);
        Assert.Equal(SectionOffset(encoded, 1), semantic.Offset);
        Assert.Equal(SectionOffset(encoded, 4), payload.Offset);
        Assert.Equal(payload.Offset, record.Offset);
    }

    [Fact]
    public void PublicRecordPayloadAccessCannotMutateAdmittedWireBytes()
    {
        var encoded = ArtifactFixture.Encode();
        var decoded = ServiceCycleReplayArtifactCodec.Decode(encoded);
        var record = decoded.GetCycle(0).GetRecord(0);
        var copy = record.GetPayloadCopy();

        copy[0] = 99;
        var reencoded = new byte[decoded.EncodedLength];
        ServiceCycleReplayArtifactCodec.Reencode(decoded, reencoded);

        Assert.Equal(1, record.PayloadLength);
        Assert.Equal(new byte[] { 11 }, record.GetPayloadCopy());
        Assert.Equal(encoded, reencoded);
    }

    [Fact]
    public void EncodeRejectsAnOversizedSnapshotBeforeAllocatingItsPayloadCopy()
    {
        var fixture = ArtifactFixture.Create();
        var source = fixture.Snapshot;
        var oversized = new ServiceCycleReplayRecordingSnapshot(
            source.TraceSession,
            source.EncodingEnabled,
            source.CodecManifests,
            new ServiceCycleReplayHighWaterFence(
                source.HighWater.Publication,
                source.HighWater.RecordSequence,
                source.HighWater.FooterSequence,
                source.HighWater.RecordCount,
                source.HighWater.FooterCount,
                ServiceCycleReplayArtifactFormat.MaximumArtifactBytes),
            source.FirstIncompleteCycle,
            source.Completeness,
            source.Fault);

        var error = Assert.Throws<ServiceCycleReplayFormatException>(() =>
            ServiceCycleReplayArtifactCodec.Encode(
                fixture.Semantic.AsSpan(), fixture.Session, in oversized));

        Assert.Equal(ServiceCycleReplayFormatErrorCode.ArtifactLimitExceeded, error.Code);
    }

    [Fact]
    public void DestinationEncodeRejectsCapacityBeforePreparingSnapshotBuffers()
    {
        var fixture = ArtifactFixture.Create();
        var source = fixture.Snapshot;
        var allocationSized = new ServiceCycleReplayRecordingSnapshot(
            source.TraceSession,
            source.EncodingEnabled,
            source.CodecManifests,
            new ServiceCycleReplayHighWaterFence(
                source.HighWater.Publication,
                0,
                source.HighWater.FooterSequence,
                0,
                source.HighWater.FooterCount,
                16 * 1024 * 1024),
            source.FirstIncompleteCycle,
            source.Completeness,
            source.Fault);

        Assert.Throws<ArgumentException>(() =>
            ServiceCycleReplayArtifactCodec.Encode(
                fixture.Semantic, fixture.Session, in allocationSized, Array.Empty<byte>()));
    }

    [Fact]
    public void ReencodeSerializesTheDecodedModelInsteadOfRetainedSourceBytes()
    {
        var encoded = ArtifactFixture.Encode();
        var decoded = ServiceCycleReplayArtifactCodec.Decode(encoded);
        var model = decoded.Prepared;
        var changedPayload = new byte[] { 77 };
        model.Payload.Span[0] = changedPayload[0];
        var original = model.GlobalRecords[0];
        model.GlobalRecords[0] = new ServiceCycleReplayArtifactRecord(
            original.Sequence,
            original.Cycle,
            original.Identity,
            original.SchemaVersion,
            changedPayload,
            ServiceCycleReplayCrc32.Compute(changedPayload));

        var reencoded = new byte[decoded.EncodedLength];
        ServiceCycleReplayArtifactCodec.Reencode(decoded, reencoded);
        var rebuilt = ServiceCycleReplayArtifactCodec.Decode(reencoded);

        Assert.NotEqual(encoded, reencoded);
        Assert.Equal(changedPayload, rebuilt.GetCycle(0).GetRecord(0).GetPayloadCopy());
    }

    [Fact]
    public void QueuedReplayableCycleWithoutFooterIsEncodedAsIncomplete()
    {
        var fixture = ArtifactFixture.Create(includeQueuedCycleWithoutFooter: true);
        var snapshot = fixture.Snapshot;
        var encoded = ServiceCycleReplayArtifactCodec.Encode(
            fixture.Semantic, fixture.Session, in snapshot);

        var decoded = ServiceCycleReplayArtifactCodec.Decode(encoded);

        Assert.False(decoded.IsComplete);
        Assert.Equal(ServiceCycleReplayArtifactEligibilityCode.SemanticJoinIncomplete, decoded.Eligibility);
        Assert.Equal(1, decoded.CycleCount);
        Assert.Equal((ulong)15, decoded.Fence.SemanticLastEventSequence);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void EveryInFlightOrOrdinaryQueuedCycleWithoutFooterIsIncomplete(
        bool captureStarted,
        bool captureCompleted,
        bool ordinaryQueued)
    {
        var fixture = ArtifactFixture.Create(
            includeCaptureStartedWithoutFooter: captureStarted,
            includeCaptureCompletedWithoutFooter: captureCompleted,
            includeOrdinaryQueuedCycleWithoutFooter: ordinaryQueued);
        var snapshot = fixture.Snapshot;
        var encoded = ServiceCycleReplayArtifactCodec.Encode(
            fixture.Semantic, fixture.Session, in snapshot);

        var decoded = ServiceCycleReplayArtifactCodec.Decode(encoded);

        Assert.False(decoded.IsComplete);
        Assert.Equal(ServiceCycleReplayArtifactEligibilityCode.SemanticJoinIncomplete, decoded.Eligibility);
        Assert.Equal(
            captureStarted
                ? ServiceCycleReplayCompletenessCode.SemanticTraceIncomplete
                : ServiceCycleReplayCompletenessCode.CycleIncomplete,
            decoded.Completeness.Code);
    }

    [Theory]
    [InlineData(UnjoinedSemanticCase.ReplayableStatePublished)]
    [InlineData(UnjoinedSemanticCase.OrdinaryEvaluationStarted)]
    public void AnyUnjoinedCycleScopedSemanticEventMakesTheArtifactIncomplete(
        UnjoinedSemanticCase unjoined)
    {
        var fixture = ArtifactFixture.Create(additionalUnjoined: unjoined);
        var snapshot = fixture.Snapshot;

        var decoded = ServiceCycleReplayArtifactCodec.Decode(
            ServiceCycleReplayArtifactCodec.Encode(fixture.Semantic, fixture.Session, in snapshot));

        Assert.False(decoded.IsComplete);
        Assert.Equal(ServiceCycleReplayArtifactEligibilityCode.SemanticJoinIncomplete, decoded.Eligibility);
        Assert.Equal(ServiceCycleReplayCompletenessCode.CycleIncomplete, decoded.Completeness.Code);
        Assert.Equal((ulong)15, decoded.Fence.SemanticLastEventSequence);
    }

    [Theory]
    [InlineData(UnjoinedSemanticCase.CaptureUnavailable)]
    [InlineData(UnjoinedSemanticCase.CaptureFaulted)]
    public void NoWorkerCaptureTerminalDoesNotRequireACycleFooter(UnjoinedSemanticCase terminal)
    {
        var fixture = ArtifactFixture.Create(additionalUnjoined: terminal);
        var snapshot = fixture.Snapshot;

        var decoded = ServiceCycleReplayArtifactCodec.Decode(
            ServiceCycleReplayArtifactCodec.Encode(fixture.Semantic, fixture.Session, in snapshot));

        Assert.True(decoded.IsComplete);
        Assert.Equal(ServiceCycleReplayArtifactEligibilityCode.Complete, decoded.Eligibility);
        Assert.Equal((ulong)18, decoded.Fence.SemanticLastEventSequence);
    }

    [Fact]
    public void AttackerSizedFooterActionCountFailsBeforeJoinAllocation()
    {
        var bytes = ArtifactFixture.Encode();
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(SectionOffset(bytes, 5) + 64, 4), int.MaxValue);
        RefreshSection(bytes, 5);
        RefreshGlobal(bytes);

        var error = Assert.Throws<ServiceCycleReplayFormatException>(() =>
            ServiceCycleReplayArtifactCodec.Decode(bytes));

        Assert.Equal(ServiceCycleReplayFormatErrorCode.CycleFooterInvalid, error.Code);
    }

    [Theory]
    [InlineData(0, ServiceCycleReplayFormatErrorCode.MagicMismatch)]
    [InlineData(12, ServiceCycleReplayFormatErrorCode.HeaderFlagsUnsupported)]
    [InlineData(84, ServiceCycleReplayFormatErrorCode.ReservedBytesNonzero)]
    public void HeaderMutationsHaveStableErrors(int offset, ServiceCycleReplayFormatErrorCode expected)
    {
        var bytes = ArtifactFixture.Encode();
        bytes[offset] ^= 1;

        var error = Assert.Throws<ServiceCycleReplayFormatException>(() =>
            ServiceCycleReplayArtifactCodec.Decode(bytes));

        Assert.Equal(expected, error.Code);
    }

    [Fact]
    public void UnknownDirectoryKindRejectsBeforeSectionInterpretation()
    {
        var bytes = ArtifactFixture.Encode();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(ServiceCycleReplayArtifactFormat.HeaderBytes, 2), 99);
        RefreshGlobal(bytes);

        var error = Assert.Throws<ServiceCycleReplayFormatException>(() =>
            ServiceCycleReplayArtifactCodec.Decode(bytes));

        Assert.Equal(ServiceCycleReplayFormatErrorCode.SectionKindUnsupported, error.Code);
    }

    [Fact]
    public void SectionAndPerRecordChecksumsAreIndependent()
    {
        var sectionCorrupt = ArtifactFixture.Encode();
        sectionCorrupt[SectionOffset(sectionCorrupt, 4)] ^= 1;
        RefreshGlobal(sectionCorrupt);
        Assert.Equal(
            ServiceCycleReplayFormatErrorCode.SectionChecksumMismatch,
            Assert.Throws<ServiceCycleReplayFormatException>(() =>
                ServiceCycleReplayArtifactCodec.Decode(sectionCorrupt)).Code);

        var recordCorrupt = ArtifactFixture.Encode();
        recordCorrupt[SectionOffset(recordCorrupt, 4)] ^= 1;
        RefreshSection(recordCorrupt, 4);
        RefreshGlobal(recordCorrupt);
        Assert.Equal(
            ServiceCycleReplayFormatErrorCode.RecordChecksumMismatch,
            Assert.Throws<ServiceCycleReplayFormatException>(() =>
                ServiceCycleReplayArtifactCodec.Decode(recordCorrupt)).Code);
    }

    [Fact]
    public void RecordOrderPartitionAndCodecSchemaMutationsFailAtTheirStableGate()
    {
        var sequence = ArtifactFixture.Encode();
        BinaryPrimitives.WriteInt64LittleEndian(sequence.AsSpan(SectionOffset(sequence, 3), 8), 2);
        RefreshSection(sequence, 3);
        RefreshGlobal(sequence);
        Assert.Equal(
            ServiceCycleReplayFormatErrorCode.RecordSequenceInvalid,
            Assert.Throws<ServiceCycleReplayFormatException>(() =>
                ServiceCycleReplayArtifactCodec.Decode(sequence)).Code);

        var partition = ArtifactFixture.Encode();
        BinaryPrimitives.WriteUInt64LittleEndian(partition.AsSpan(SectionOffset(partition, 3) + 64, 8), 1);
        RefreshSection(partition, 3);
        RefreshGlobal(partition);
        Assert.Equal(
            ServiceCycleReplayFormatErrorCode.RecordPayloadPartitionInvalid,
            Assert.Throws<ServiceCycleReplayFormatException>(() =>
                ServiceCycleReplayArtifactCodec.Decode(partition)).Code);

        var schema = ArtifactFixture.Encode();
        BinaryPrimitives.WriteUInt16LittleEndian(schema.AsSpan(SectionOffset(schema, 3) + 58, 2), 2);
        RefreshSection(schema, 3);
        RefreshGlobal(schema);
        Assert.Equal(
            ServiceCycleReplayFormatErrorCode.RecordSchemaMismatch,
            Assert.Throws<ServiceCycleReplayFormatException>(() =>
                ServiceCycleReplayArtifactCodec.Decode(schema)).Code);
    }

    [Fact]
    public void FooterReserveAndSerializedJoinMutationsCannotBecomeExecutable()
    {
        var reserve = ArtifactFixture.Encode();
        reserve[SectionOffset(reserve, 5) + 376] = 1;
        RefreshSection(reserve, 5);
        RefreshGlobal(reserve);
        Assert.Equal(
            ServiceCycleReplayFormatErrorCode.CycleFooterInvalid,
            Assert.Throws<ServiceCycleReplayFormatException>(() =>
                ServiceCycleReplayArtifactCodec.Decode(reserve)).Code);

        var join = ArtifactFixture.Encode();
        BinaryPrimitives.WriteInt32LittleEndian(
            join.AsSpan(SectionOffset(join, 5) + 332, 4),
            (int)ServiceCycleReplaySemanticJoinCode.StatePublicationMissing);
        RefreshSection(join, 5);
        RefreshGlobal(join);
        Assert.Equal(
            ServiceCycleReplayFormatErrorCode.SerializedJoinMismatch,
            Assert.Throws<ServiceCycleReplayFormatException>(() =>
                ServiceCycleReplayArtifactCodec.Decode(join)).Code);
    }

    [Fact]
    public void SemanticProjectionMutationIsCaughtByExactCrossSectionJoin()
    {
        var bytes = ArtifactFixture.Encode();
        var semanticOffset = SectionOffset(bytes, 1);
        const int stateEventIndex = 9;
        var fingerprintOffset = semanticOffset + ServiceCycleTraceCodec.HeaderBytes +
            stateEventIndex * ServiceCycleTraceCodec.RecordBytes + 152;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(fingerprintOffset, 8), 123);
        var semanticLength = SectionLength(bytes, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(semanticOffset + 56, 4),
            TraceCrc32.ComputeExcluding(bytes.AsSpan(semanticOffset, semanticLength), 56, 4));
        RefreshSection(bytes, 1);
        RefreshGlobal(bytes);

        var error = Assert.Throws<ServiceCycleReplayFormatException>(() =>
            ServiceCycleReplayArtifactCodec.Decode(bytes));

        Assert.Equal(ServiceCycleReplayFormatErrorCode.SerializedJoinMismatch, error.Code);
    }

    [Fact]
    public void HardArtifactLimitRejectsBeforeAllocationHeavyDecode()
    {
        var bytes = ArtifactFixture.Encode();
        var limits = new ServiceCycleReplayArtifactLimits(
            bytes.Length - 1, 100, 100, 100, 100);

        var error = Assert.Throws<ServiceCycleReplayFormatException>(() =>
            ServiceCycleReplayArtifactCodec.Decode(bytes, limits));

        Assert.Equal(ServiceCycleReplayFormatErrorCode.ArtifactLimitExceeded, error.Code);
    }

    [Theory]
    [InlineData(0, 2, ServiceCycleReplayFormatErrorCode.SectionOrderInvalid)]
    [InlineData(2, 2, ServiceCycleReplayFormatErrorCode.SectionVersionUnsupported)]
    [InlineData(4, 1, ServiceCycleReplayFormatErrorCode.SectionFlagsUnsupported)]
    [InlineData(36, 1, ServiceCycleReplayFormatErrorCode.ReservedBytesNonzero)]
    public void DirectoryMetadataMutationsHaveStableGates(
        int relativeOffset,
        int value,
        ServiceCycleReplayFormatErrorCode expected)
    {
        var bytes = ArtifactFixture.Encode();
        var offset = ServiceCycleReplayArtifactFormat.HeaderBytes + relativeOffset;
        if (relativeOffset is 0 or 2)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), checked((ushort)value));
        else
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), checked((uint)value));
        RefreshGlobal(bytes);

        Assert.Equal(expected, Assert.Throws<ServiceCycleReplayFormatException>(() =>
            ServiceCycleReplayArtifactCodec.Decode(bytes)).Code);
    }

    [Fact]
    public void OscrV1RejectsAnyEmbeddedSemanticSchemaOtherThanPinnedSchemaFive()
    {
        var directory = ArtifactFixture.Encode();
        var semanticDirectory = ServiceCycleReplayArtifactFormat.HeaderBytes +
            ServiceCycleReplayArtifactFormat.DirectoryEntryBytes;
        BinaryPrimitives.WriteUInt16LittleEndian(
            directory.AsSpan(semanticDirectory + 2, 2),
            checked((ushort)(ServiceCycleReplayArtifactFormat.EmbeddedSemanticSchemaVersion + 1)));
        RefreshGlobal(directory);
        Assert.Equal(
            ServiceCycleReplayFormatErrorCode.SectionVersionUnsupported,
            Assert.Throws<ServiceCycleReplayFormatException>(() =>
                ServiceCycleReplayArtifactCodec.Decode(directory)).Code);

        AssertSectionMutation(
            0,
            0,
            span => BinaryPrimitives.WriteUInt16LittleEndian(
                span,
                checked((ushort)(ServiceCycleReplayArtifactFormat.EmbeddedSemanticSchemaVersion + 1))),
            ServiceCycleReplayFormatErrorCode.ManifestInvalid);
    }

    [Fact]
    public void DirectoryGapsAndTrailingBytesAreRejected()
    {
        var gap = ArtifactFixture.Encode();
        var firstDirectory = ServiceCycleReplayArtifactFormat.HeaderBytes;
        BinaryPrimitives.WriteUInt64LittleEndian(gap.AsSpan(firstDirectory + 8, 8),
            checked((ulong)SectionOffset(gap, 0) + 1));
        RefreshGlobal(gap);
        Assert.Equal(ServiceCycleReplayFormatErrorCode.SectionBoundsInvalid,
            Assert.Throws<ServiceCycleReplayFormatException>(() =>
                ServiceCycleReplayArtifactCodec.Decode(gap)).Code);

        var canonical = ArtifactFixture.Encode();
        var trailing = new byte[canonical.Length + 1];
        canonical.CopyTo(trailing, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(trailing.AsSpan(16, 8), checked((ulong)trailing.Length));
        RefreshGlobal(trailing);
        Assert.Equal(ServiceCycleReplayFormatErrorCode.SectionBoundsInvalid,
            Assert.Throws<ServiceCycleReplayFormatException>(() =>
                ServiceCycleReplayArtifactCodec.Decode(trailing)).Code);
    }

    [Fact]
    public void TruncationAtStructuralBoundariesAndBoundedInteriorSamplesFailsClosed()
    {
        var bytes = ArtifactFixture.Encode();
        var lengths = new SortedSet<int>();
        var fixedPrefix = ServiceCycleReplayArtifactFormat.HeaderBytes +
            ServiceCycleReplayArtifactFormat.RequiredSectionCount *
            ServiceCycleReplayArtifactFormat.DirectoryEntryBytes;
        for (var length = 0; length <= fixedPrefix; length++) lengths.Add(length);

        for (var section = 0; section < ServiceCycleReplayArtifactFormat.RequiredSectionCount; section++)
        {
            var start = SectionOffset(bytes, section);
            var end = checked(start + SectionLength(bytes, section));
            AddTruncatedLength(lengths, bytes.Length, start - 1);
            AddTruncatedLength(lengths, bytes.Length, start);
            AddTruncatedLength(lengths, bytes.Length, start + 1);
            AddTruncatedLength(lengths, bytes.Length, start + (end - start) / 2);
            AddTruncatedLength(lengths, bytes.Length, end - 1);
            AddTruncatedLength(lengths, bytes.Length, end);
        }

        const int interiorSampleCount = 64;
        for (var sample = 1; sample <= interiorSampleCount; sample++)
        {
            AddTruncatedLength(
                lengths,
                bytes.Length,
                checked((int)((long)bytes.Length * sample / (interiorSampleCount + 1))));
        }

        foreach (var length in lengths) AssertTruncationRejected(bytes, length);
    }

    [Fact]
    public void CodecRowsEnforceRoleOrderCanonicalFlagAndReserve()
    {
        AssertSectionMutation(2, 4, span => BinaryPrimitives.WriteUInt16LittleEndian(span, 0),
            ServiceCycleReplayFormatErrorCode.CodecManifestInvalid);
        AssertSectionMutation(2, ServiceCycleReplayArtifactFormat.CodecManifestEntryBytes + 4,
            span => BinaryPrimitives.WriteUInt16LittleEndian(span, 1),
            ServiceCycleReplayFormatErrorCode.CodecManifestOrderInvalid);
        AssertSectionMutation(2, 12, span => BinaryPrimitives.WriteUInt32LittleEndian(span, 0),
            ServiceCycleReplayFormatErrorCode.CodecManifestInvalid);
        AssertSectionMutation(2, 16, span => span[0] = 1,
            ServiceCycleReplayFormatErrorCode.CodecManifestInvalid);
    }

    [Fact]
    public void RecordRowsEnforceIdentityChecksumAndAllReserves()
    {
        AssertSectionMutation(3, 56, span => BinaryPrimitives.WriteUInt16LittleEndian(span, 0),
            ServiceCycleReplayFormatErrorCode.RecordIdentityInvalid);
        AssertSectionMutation(3, 60, span => BinaryPrimitives.WriteInt32LittleEndian(span, 1),
            ServiceCycleReplayFormatErrorCode.RecordIdentityInvalid);
        AssertSectionMutation(3, 76, span => span[0] ^= 1,
            ServiceCycleReplayFormatErrorCode.RecordChecksumMismatch);
        AssertSectionMutation(3, 80, span => span[0] = 1,
            ServiceCycleReplayFormatErrorCode.RecordPayloadPartitionInvalid);
        AssertSectionMutation(3, 12, span => span[0] = 1,
            ServiceCycleReplayFormatErrorCode.ReservedBytesNonzero);
    }

    [Fact]
    public void ManifestAndFooterCycleKeyReservesAreRejectedAtTheirOwnGate()
    {
        AssertSectionMutation(0, 164, span => span[0] = 1,
            ServiceCycleReplayFormatErrorCode.ReservedBytesNonzero);
        AssertSectionMutation(5, 12, span => span[0] = 1,
            ServiceCycleReplayFormatErrorCode.ReservedBytesNonzero);
    }

    [Fact]
    public void FooterFlagsWakeProjectionAndCompletenessShapesAreStrict()
    {
        AssertSectionMutation(5, 60, span => BinaryPrimitives.WriteUInt32LittleEndian(span, 4),
            ServiceCycleReplayFormatErrorCode.CycleFooterInvalid);
        AssertSectionMutation(5, 136, span => BinaryPrimitives.WriteInt32LittleEndian(span, 99),
            ServiceCycleReplayFormatErrorCode.CycleFooterInvalid);
        AssertSectionMutation(5, 140, span => BinaryPrimitives.WriteInt32LittleEndian(span, 17),
            ServiceCycleReplayFormatErrorCode.CycleFooterInvalid);
        AssertSectionMutation(5, 88, span => BinaryPrimitives.WriteInt32LittleEndian(span, 99),
            ServiceCycleReplayFormatErrorCode.CycleFooterInvalid);
        AssertSectionMutation(5, 104, span => BinaryPrimitives.WriteInt64LittleEndian(span, -1),
            ServiceCycleReplayFormatErrorCode.CycleFooterInvalid);
    }

    [Theory]
    [InlineData(ServiceCycleReplayCycleFooterDisposition.EvaluationAborted)]
    [InlineData(ServiceCycleReplayCycleFooterDisposition.ProjectionAborted)]
    public void AbortedFooterDispositionRejectsProvisionalWakeProjectionClaims(
        ServiceCycleReplayCycleFooterDisposition disposition)
    {
        var bytes = ArtifactFixture.Encode();
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(SectionOffset(bytes, 5) + 56, 4),
            (int)disposition);
        RefreshSection(bytes, 5);
        RefreshGlobal(bytes);

        var error = Assert.Throws<ServiceCycleReplayFormatException>(() =>
            ServiceCycleReplayArtifactCodec.Decode(bytes));

        Assert.Equal(ServiceCycleReplayFormatErrorCode.CycleFooterInvalid, error.Code);
    }

    [Fact]
    public void ManifestCountsFencesAndCompletenessHaveStableGates()
    {
        AssertSectionMutation(0, 16, span => BinaryPrimitives.WriteUInt32LittleEndian(span, 4),
            ServiceCycleReplayFormatErrorCode.ManifestInvalid);
        AssertSectionMutation(0, 40, span => BinaryPrimitives.WriteUInt64LittleEndian(span, 2),
            ServiceCycleReplayFormatErrorCode.FenceMismatch);
        AssertSectionMutation(0, 72, span => BinaryPrimitives.WriteInt64LittleEndian(span, 9),
            ServiceCycleReplayFormatErrorCode.FenceMismatch);
        AssertSectionMutation(0, 136, span => BinaryPrimitives.WriteInt32LittleEndian(span, 99),
            ServiceCycleReplayFormatErrorCode.ManifestInvalid);
    }

    [Fact]
    public void GlobalChecksumRejectsBeforeSectionParsing()
    {
        var bytes = ArtifactFixture.Encode();
        bytes[SectionOffset(bytes, 0)] ^= 1;

        Assert.Equal(ServiceCycleReplayFormatErrorCode.GlobalChecksumMismatch,
            Assert.Throws<ServiceCycleReplayFormatException>(() =>
                ServiceCycleReplayArtifactCodec.Decode(bytes)).Code);
    }

    private static void AssertSectionMutation(
        int section,
        int relativeOffset,
        SpanMutation mutate,
        ServiceCycleReplayFormatErrorCode expected)
    {
        var bytes = ArtifactFixture.Encode();
        mutate(bytes.AsSpan(SectionOffset(bytes, section) + relativeOffset));
        RefreshSection(bytes, section);
        RefreshGlobal(bytes);
        Assert.Equal(expected, Assert.Throws<ServiceCycleReplayFormatException>(() =>
            ServiceCycleReplayArtifactCodec.Decode(bytes)).Code);
    }

    private delegate void SpanMutation(Span<byte> span);

    private static void AddTruncatedLength(ISet<int> lengths, int encodedLength, int length)
    {
        if (length >= 0 && length < encodedLength) lengths.Add(length);
    }

    private static void AssertTruncationRejected(byte[] bytes, int length)
    {
        try
        {
            ServiceCycleReplayArtifactCodec.Decode(bytes.AsSpan(0, length));
        }
        catch (ServiceCycleReplayFormatException)
        {
            return;
        }

        throw new Xunit.Sdk.XunitException(
            $"A canonical artifact truncated to {length} bytes was accepted.");
    }

    private static int SectionOffset(byte[] bytes, int index) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(
        bytes.AsSpan(ServiceCycleReplayArtifactFormat.HeaderBytes +
            index * ServiceCycleReplayArtifactFormat.DirectoryEntryBytes + 8, 8)));

    private static int SectionLength(byte[] bytes, int index) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(
        bytes.AsSpan(ServiceCycleReplayArtifactFormat.HeaderBytes +
            index * ServiceCycleReplayArtifactFormat.DirectoryEntryBytes + 16, 8)));

    private static void RefreshSection(byte[] bytes, int index)
    {
        var offset = SectionOffset(bytes, index);
        var length = SectionLength(bytes, index);
        var directory = ServiceCycleReplayArtifactFormat.HeaderBytes +
            index * ServiceCycleReplayArtifactFormat.DirectoryEntryBytes;
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(directory + 32, 4),
            ServiceCycleReplayCrc32.Compute(bytes.AsSpan(offset, length)));
    }

    private static void RefreshGlobal(byte[] bytes) => BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(80, 4), ServiceCycleReplayCrc32.ComputeExcluding(bytes, 80, 4));

    internal sealed class ArtifactFixture
    {
        private ArtifactFixture(
            ServiceCycleReplaySession session,
            ServiceCycleReplayRecordingSnapshot snapshot,
            byte[] semantic)
        {
            Session = session;
            Snapshot = snapshot;
            Semantic = semantic;
        }

        internal ServiceCycleReplaySession Session { get; }
        internal ServiceCycleReplayRecordingSnapshot Snapshot { get; }
        internal byte[] Semantic { get; }

        internal static byte[] Encode()
        {
            var fixture = Create();
            var snapshot = fixture.Snapshot;
            return ServiceCycleReplayArtifactCodec.Encode(fixture.Semantic, fixture.Session, in snapshot);
        }

        internal static ArtifactFixture Create(
            bool includeQueuedCycleWithoutFooter = false,
            bool includeCaptureStartedWithoutFooter = false,
            bool includeCaptureCompletedWithoutFooter = false,
            bool includeOrdinaryQueuedCycleWithoutFooter = false,
            UnjoinedSemanticCase additionalUnjoined = UnjoinedSemanticCase.None)
        {
            var traceSession = new ServiceCycleTraceSessionId(901);
            var session = new ServiceCycleReplaySession(
                traceSession,
                new ServiceCycleReplaySessionOptions(true, 64, 16, 4));
            var descriptor = new ServiceCycleReplayCodecDescriptor(1, 8);
            session.BindCodecManifest(1, new object(), descriptor, descriptor, descriptor);
            var identity = new ServiceCycleIdentity(
                new ServiceId("test.replay-format"),
                new LifecycleGeneration(2),
                new ConfigGeneration(3),
                new StrategyGeneration(4),
                new CaptureSequence(5),
                new CycleId(6));
            var key = new ServiceCycleReplayCycleKey(1, in identity);
            var scratch = new byte[8];
            Append(session, in key, descriptor, scratch, ServiceCycleReplayRecordKind.CycleInput, 0, 11);
            Append(session, in key, descriptor, scratch, ServiceCycleReplayRecordKind.PreviousState, 0, 22);
            Append(session, in key, descriptor, scratch, ServiceCycleReplayRecordKind.NextState, 0, 33);
            var context = new ServiceCycleReplayContext(
                1,
                new ServiceCycleContext(identity, default, new MonotonicTimestamp(100)));
            var footer = new ServiceCycleReplayCycleFooter(
                0,
                context,
                ServiceCycleReplayCycleFooterDisposition.Provisional,
                WakePolicy.Immediate,
                true,
                default,
                true,
                0,
                1,
                3,
                3,
                ServiceCycleReplayCompleteness.Complete,
                1,
                Stopwatch.Frequency,
                0);
            Assert.True(session.TryAppendFooter(in footer, out _));
            Assert.True(session.TryReadSnapshot(out var snapshot));
            return new ArtifactFixture(
                session,
                snapshot,
                CreateSemantic(
                    traceSession,
                    includeQueuedCycleWithoutFooter,
                    includeCaptureStartedWithoutFooter,
                    includeCaptureCompletedWithoutFooter,
                    includeOrdinaryQueuedCycleWithoutFooter,
                    additionalUnjoined));
        }

        private static void Append(
            ServiceCycleReplaySession session,
            in ServiceCycleReplayCycleKey key,
            ServiceCycleReplayCodecDescriptor descriptor,
            byte[] scratch,
            ServiceCycleReplayRecordKind kind,
            int index,
            byte value)
        {
            scratch[0] = value;
            Assert.True(session.TryAppendRecord(
                in key,
                new ServiceCycleReplayRecordIdentity(kind, index),
                in descriptor,
                scratch,
                1,
                out _));
        }

        private static byte[] CreateSemantic(
            ServiceCycleTraceSessionId session,
            bool includeQueuedCycleWithoutFooter,
            bool includeCaptureStartedWithoutFooter,
            bool includeCaptureCompletedWithoutFooter,
            bool includeOrdinaryQueuedCycleWithoutFooter,
            UnjoinedSemanticCase additionalUnjoined)
        {
            var cycle = new ServiceCycleTraceCycleIdentity(
                new ServiceCycleTraceServiceId(1), 2, 3, 4, 5, 6);
            var capture = new ServiceCycleTraceCaptureIdentity(
                new ServiceCycleTraceServiceId(1), 2, 3, 5, 6);
            var projection = default(ServiceStateProjectionSnapshot);
            var fingerprint = ServiceCycleProjectionFingerprint.Compute(in projection);
            var events = new System.Collections.Generic.List<ServiceCycleSemanticEvent>
            {
                Event(session, 1, ServiceCycleSemanticEventKind.ConfigurationPublished,
                    ServiceCycleSemanticPayload.Publication(false, cycle.Service, 3, 89)),
                Event(session, 2, ServiceCycleSemanticEventKind.StartAttempted,
                    ServiceCycleSemanticPayload.StartAttempted(cycle.Service, 2, 3, 90), 1),
                Event(session, 3, ServiceCycleSemanticEventKind.StartReady,
                    ServiceCycleSemanticPayload.StartReady(
                        cycle.Service, 2, 3, CommonServiceDecisionCodes.Ready.Value, 90, 0), 2),
                Event(session, 4, ServiceCycleSemanticEventKind.CaptureStarted,
                    ServiceCycleSemanticPayload.CaptureFact(in capture, 0, 0, 90, 0), 3),
                Event(session, 5, ServiceCycleSemanticEventKind.StrategyPublished,
                    ServiceCycleSemanticPayload.Publication(true, cycle.Service, 4, 90), 4),
                Event(session, 6, ServiceCycleSemanticEventKind.CaptureCompleted,
                    ServiceCycleSemanticPayload.CaptureFact(
                        in capture, 4, CommonServiceDecisionCodes.Captured.Value, 91, 1), 4),
                Event(session, 7, ServiceCycleSemanticEventKind.CycleQueued,
                    ServiceCycleSemanticPayload.CycleFact(
                        in cycle, CommonServiceDecisionCodes.Ready.Value, 92, 1), 6),
                Event(session, 8, ServiceCycleSemanticEventKind.CycleStarted,
                    ServiceCycleSemanticPayload.CycleFact(in cycle, 0, 100, 0), 7),
                Event(session, 9, ServiceCycleSemanticEventKind.EvaluationStarted,
                    ServiceCycleSemanticPayload.Evaluation(in cycle, 0, 0, 100, 0), 8),
                Event(session, 10, ServiceCycleSemanticEventKind.StatePublished,
                    ServiceCycleSemanticPayload.State(in cycle, 1, fingerprint, 101), 9),
                Event(session, 11, ServiceCycleSemanticEventKind.EvaluationCompleted,
                    ServiceCycleSemanticPayload.EvaluationCompleted(
                        in cycle, 0, WakePolicy.Immediate, 102, 2), 10),
                Event(session, 12, ServiceCycleSemanticEventKind.BatchPublished,
                    ServiceCycleSemanticPayload.BatchFact(
                        in cycle, 1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 103), 11),
                Event(session, 13, ServiceCycleSemanticEventKind.BatchCompleted,
                    ServiceCycleSemanticPayload.BatchFact(in cycle, 1, (int)BatchTerminalDisposition.Completed,
                        CommonActionResultCodes.Committed.Value, 0, 0, -1, 0, 0, 0, 0, 104), 12),
                Event(session, 14, ServiceCycleSemanticEventKind.CycleCompleted,
                    ServiceCycleSemanticPayload.CycleFact(in cycle, 0, 104, 0), 13),
            };
            if (includeQueuedCycleWithoutFooter)
            {
                var missing = new ServiceCycleTraceCycleIdentity(
                    new ServiceCycleTraceServiceId(1), 2, 3, 4, 7, 8);
                events.Add(Event(session, 15, ServiceCycleSemanticEventKind.CycleQueued,
                    ServiceCycleSemanticPayload.CycleFact(
                        in missing, CommonServiceDecisionCodes.Ready.Value, 105, 0), 14));
            }
            else if (includeCaptureStartedWithoutFooter)
            {
                var missing = new ServiceCycleTraceCaptureIdentity(
                    new ServiceCycleTraceServiceId(1), 2, 3, 7, 8);
                events.Add(Event(session, 15, ServiceCycleSemanticEventKind.StartAttempted,
                    ServiceCycleSemanticPayload.StartAttempted(missing.Service, 2, 3, 105), 14));
                events.Add(Event(session, 16, ServiceCycleSemanticEventKind.StartReady,
                    ServiceCycleSemanticPayload.StartReady(
                        missing.Service, 2, 3, CommonServiceDecisionCodes.Ready.Value, 105, 0), 15));
                events.Add(Event(session, 17, ServiceCycleSemanticEventKind.CaptureStarted,
                    ServiceCycleSemanticPayload.CaptureFact(in missing, 0, 0, 105, 0), 16));
            }
            else if (includeCaptureCompletedWithoutFooter)
            {
                var missing = new ServiceCycleTraceCaptureIdentity(
                    new ServiceCycleTraceServiceId(1), 2, 3, 7, 8);
                events.Add(Event(session, 15, ServiceCycleSemanticEventKind.CaptureCompleted,
                    ServiceCycleSemanticPayload.CaptureFact(
                        in missing, 4, CommonServiceDecisionCodes.Captured.Value, 105, 0), 14));
            }
            else if (includeOrdinaryQueuedCycleWithoutFooter)
            {
                var missing = new ServiceCycleTraceCycleIdentity(
                    new ServiceCycleTraceServiceId(2), 2, 3, 4, 7, 8);
                events.Add(Event(session, 15, ServiceCycleSemanticEventKind.CycleQueued,
                    ServiceCycleSemanticPayload.CycleFact(
                        in missing, CommonServiceDecisionCodes.Ready.Value, 105, 0), 14));
            }
            else if (additionalUnjoined is UnjoinedSemanticCase.CaptureUnavailable or
                UnjoinedSemanticCase.CaptureFaulted)
            {
                var missing = new ServiceCycleTraceCaptureIdentity(
                    new ServiceCycleTraceServiceId(1), 2, 3, 7, 8);
                events.Add(Event(session, 15, ServiceCycleSemanticEventKind.StartAttempted,
                    ServiceCycleSemanticPayload.StartAttempted(missing.Service, 2, 3, 105), 14));
                events.Add(Event(session, 16, ServiceCycleSemanticEventKind.StartReady,
                    ServiceCycleSemanticPayload.StartReady(
                        missing.Service, 2, 3, CommonServiceDecisionCodes.Ready.Value, 105, 0), 15));
                events.Add(Event(session, 17, ServiceCycleSemanticEventKind.CaptureStarted,
                    ServiceCycleSemanticPayload.CaptureFact(in missing, 0, 0, 105, 0), 16));
                events.Add(additionalUnjoined == UnjoinedSemanticCase.CaptureUnavailable
                    ? Event(session, 18, ServiceCycleSemanticEventKind.CaptureUnavailable,
                        ServiceCycleSemanticPayload.CaptureUnavailable(
                            in missing,
                            CommonServiceDecisionCodes.CaptureUnavailable.Value,
                            WakePolicy.AfterDecision(new MonotonicDuration(5)),
                            105,
                            0),
                        17)
                    : Event(session, 18, ServiceCycleSemanticEventKind.CaptureFaulted,
                        ServiceCycleSemanticPayload.CaptureFact(
                            in missing, 0, CommonActionResultCodes.AdapterFault.Value, 105, 0),
                        17));
            }
            else if (additionalUnjoined == UnjoinedSemanticCase.ReplayableStatePublished)
            {
                var missing = new ServiceCycleTraceCycleIdentity(
                    new ServiceCycleTraceServiceId(1), 2, 3, 4, 7, 8);
                events.Add(Event(session, 15, ServiceCycleSemanticEventKind.StatePublished,
                    ServiceCycleSemanticPayload.State(in missing, 1, 0, 105), 14));
            }
            else if (additionalUnjoined == UnjoinedSemanticCase.OrdinaryEvaluationStarted)
            {
                var missing = new ServiceCycleTraceCycleIdentity(
                    new ServiceCycleTraceServiceId(2), 2, 3, 4, 7, 8);
                events.Add(Event(session, 15, ServiceCycleSemanticEventKind.EvaluationStarted,
                    ServiceCycleSemanticPayload.Evaluation(in missing, 0, 0, 105, 0), 14));
            }
            var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Count)];
            ServiceCycleTraceCodec.Encode(session, default, events.ToArray(), bytes);
            return bytes;
        }

        private static ServiceCycleSemanticEvent Event(
            ServiceCycleTraceSessionId session,
            ulong sequence,
            ServiceCycleSemanticEventKind kind,
            ServiceCycleSemanticPayload payload,
            ulong parent = 0) => new(
                new ServiceCycleTraceEventId(session, sequence),
                parent == 0 ? default : new ServiceCycleTraceEventId(session, parent),
                kind,
                in payload);
    }
}
