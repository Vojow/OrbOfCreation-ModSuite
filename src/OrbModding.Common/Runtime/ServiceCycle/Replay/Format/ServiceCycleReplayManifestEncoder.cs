using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayManifestEncoder
{
    internal static void Write(
        Span<byte> manifest,
        ServiceCycleReplayPreparedArtifact artifact,
        ServiceCycleReplayFormatWorkCounter? work = null)
    {
        ServiceCycleReplayBinary.U16(
            manifest, 0, ServiceCycleReplayArtifactFormat.EmbeddedSemanticSchemaVersion);
        ServiceCycleReplayBinary.U16(manifest, 2, ServiceCycleReplayArtifactFormat.CodecManifestEntryBytes);
        ServiceCycleReplayBinary.U16(manifest, 4, ServiceCycleReplayArtifactFormat.RecordIndexEntryBytes);
        ServiceCycleReplayBinary.U16(manifest, 6, ServiceCycleReplayArtifactFormat.CycleFooterBytes);
        ServiceCycleReplayBinary.U32(manifest, 8, checked((uint)(artifact.Codecs.Length / 3)));
        ServiceCycleReplayBinary.U32(manifest, 12, checked((uint)artifact.Codecs.Length));
        ServiceCycleReplayBinary.U32(manifest, 16, checked((uint)artifact.GlobalRecords.Length));
        ServiceCycleReplayBinary.U32(manifest, 20, checked((uint)artifact.Joined.Footers.Length));
        ServiceCycleReplayBinary.U32(manifest, 24, checked((uint)artifact.Payload.Length));
        ServiceCycleReplayBinary.U32(manifest, 28, checked((uint)artifact.SemanticBytes.Length));
        ServiceCycleReplayBinary.U32(manifest, 32, checked((uint)artifact.Semantic.Count));
        ServiceCycleReplayBinary.I32(manifest, 36, (int)artifact.Joined.Eligibility);
        var semantic = artifact.Semantic;
        ServiceCycleReplayBinary.U64(manifest, 40, semantic.Count == 0 ? 0 : semantic[0].Id.Sequence);
        ServiceCycleReplayBinary.U64(manifest, 48, semantic.Count == 0 ? 0 : semantic[^1].Id.Sequence);
        ServiceCycleReplayBinary.U64(manifest, 56, semantic.Dropped.IsPresent ? semantic.Dropped.FirstSequence : 0);
        ServiceCycleReplayBinary.U64(manifest, 64, semantic.Dropped.IsPresent ? semantic.Dropped.LastSequence : 0);
        var replay = artifact.SourceRecording.HighWater;
        ServiceCycleReplayBinary.I64(manifest, 72, replay.Publication);
        ServiceCycleReplayBinary.I64(manifest, 80, replay.RecordSequence);
        ServiceCycleReplayBinary.I64(manifest, 88, replay.FooterSequence);
        ServiceCycleReplayBinary.I64(manifest, 96, artifact.SourceRecording.CodecManifests.Publication);
        ServiceCycleReplayBinary.U32(manifest, 104, checked((uint)artifact.SourceRecording.CodecManifests.Count));
        var joinedCycleCount = 0;
        for (var index = 0; index < artifact.Joined.Footers.Length; index++)
        {
            work?.Add();
            if (artifact.Joined.Footers[index].IsComplete) joinedCycleCount++;
        }
        ServiceCycleReplayBinary.U32(manifest, 108, checked((uint)joinedCycleCount));
        ComputeJoinedFences(artifact, out var semanticFence, out var recordFence, out var footerFence, work);
        ServiceCycleReplayBinary.U64(manifest, 112, semanticFence);
        ServiceCycleReplayBinary.I64(manifest, 120, recordFence);
        ServiceCycleReplayBinary.I64(manifest, 128, footerFence);
        ServiceCycleReplayCompletenessEncoder.Write(manifest, 136, artifact.EffectiveRecording.Completeness);
        var fault = artifact.EffectiveRecording.Fault;
        ServiceCycleReplayBinary.I32(manifest, 152, fault.IsValid ? (int)fault.Code : 0);
        ServiceCycleReplayBinary.I32(manifest, 156, fault.IsValid ? fault.DetailCode : 0);
        ServiceCycleReplayBinary.WriteCycleKey(manifest, 160, artifact.EffectiveRecording.FirstIncompleteCycle);
        ServiceCycleReplayBinary.I64(manifest, 208, artifact.FirstUnjoinedFooterSequence);
        ServiceCycleReplayBinary.U64(manifest, 216, artifact.FirstUnjoinedSemanticSequence);
    }

    private static void ComputeJoinedFences(
        ServiceCycleReplayPreparedArtifact artifact,
        out ulong semantic,
        out long records,
        out long footers,
        ServiceCycleReplayFormatWorkCounter? work)
    {
        semantic = artifact.Joined.Eligibility == ServiceCycleReplayArtifactEligibilityCode.Complete
            ? (artifact.Semantic.Count == 0 ? 0 : artifact.Semantic[^1].Id.Sequence)
            : artifact.FirstUnjoinedSemanticSequence > 1 ? artifact.FirstUnjoinedSemanticSequence - 1 : 0;
        footers = 0;
        for (var index = 0; index < artifact.Joined.Footers.Length; index++)
        {
            work?.Add();
            if (!artifact.Joined.Footers[index].IsComplete) break;
            footers = artifact.Joined.Footers[index].Sequence;
        }
        records = 0;
        for (var index = 0; index < artifact.GlobalRecords.Length; index++)
        {
            work?.Add();
            if (!artifact.Joined.Completeness.IsComplete(artifact.GlobalRecords[index].Cycle, work)) break;
            records = artifact.GlobalRecords[index].Sequence;
        }
    }
}
