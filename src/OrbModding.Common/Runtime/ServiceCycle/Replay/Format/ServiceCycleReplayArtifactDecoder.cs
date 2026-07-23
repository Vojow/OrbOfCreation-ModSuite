using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

/// <summary>Orders the strict decode gates; wire-section details live in focused decoders.</summary>
internal static class ServiceCycleReplayArtifactDecoder
{
    internal static ServiceCycleReplayArtifactDocument Decode(
        ReadOnlySpan<byte> source,
        in ServiceCycleReplayArtifactLimits limits) => Decode(source, in limits, null);

    internal static ServiceCycleReplayArtifactDocument Decode(
        ReadOnlySpan<byte> source,
        in ServiceCycleReplayArtifactLimits limits,
        ServiceCycleReplayFormatWorkCounter? work)
    {
        var container = ServiceCycleReplayContainerDecoder.Decode(source, in limits);
        var sections = container.Sections;
        var semantic = container.Semantic;
        var header = container.Header;
        var manifest = ServiceCycleReplayManifestDecoder.Decode(
            source.Slice(sections[0].Offset, sections[0].Length), sections, semantic, in header);
        var codecs = ServiceCycleReplayPayloadDecoder.ReadCodecs(
            source.Slice(sections[2].Offset, sections[2].Length), sections[2].Count, in limits, work);
        var codecIndex = ServiceCycleReplayCodecIndex.Build(codecs, work);
        var encoded = source.ToArray();
        var records = ServiceCycleReplayPayloadDecoder.ReadRecords(
            encoded, sections[3], sections[4], codecIndex, in limits, work);
        var serializedFooters = ServiceCycleReplayPayloadDecoder.ReadFooters(encoded, sections[5], in limits, work);
        ServiceCycleReplayCodecCoverageValidator.Validate(codecIndex, records, serializedFooters, work);

        var rawCompleteness = manifest.Eligibility == ServiceCycleReplayArtifactEligibilityCode.RecordingIncomplete
            ? manifest.EffectiveCompleteness : ServiceCycleReplayCompleteness.Complete;
        var rawFirstIncomplete = manifest.Eligibility == ServiceCycleReplayArtifactEligibilityCode.RecordingIncomplete
            ? manifest.FirstIncompleteCycle : default;
        var manifestFence = new ServiceCycleReplayCodecManifestFence(
            manifest.CodecManifestPublication, manifest.CodecManifestCount);
        var rawRecording = new ServiceCycleReplayRecordingSnapshot(
            semantic.Session,
            manifest.Eligibility != ServiceCycleReplayArtifactEligibilityCode.RecordingDisabled,
            manifestFence,
            manifest.ReplayFence,
            rawFirstIncomplete,
            rawCompleteness,
            manifest.EffectiveFault);
        var joined = ServiceCycleReplaySemanticJoiner.Join(
            semantic, rawRecording, serializedFooters, records, work);
        if (joined.Eligibility != manifest.Eligibility)
            throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.SerializedJoinMismatch);
        for (var index = 0; index < serializedFooters.Length; index++)
        {
            work?.Add();
            if (serializedFooters[index].Join != joined.Footers[index].Join)
                throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.SerializedJoinMismatch, index);
        }
        ServiceCycleReplayJoinedFenceValidator.Validate(in manifest, semantic, records, joined, work);

        var effectiveRecording = new ServiceCycleReplayRecordingSnapshot(
            semantic.Session,
            manifest.Eligibility != ServiceCycleReplayArtifactEligibilityCode.RecordingDisabled,
            manifestFence,
            manifest.ReplayFence,
            manifest.FirstIncompleteCycle,
            manifest.EffectiveCompleteness,
            manifest.EffectiveFault);
        var cycles = new ServiceCycleReplayArtifactCycle[joined.Footers.Length];
        for (var index = 0; index < cycles.Length; index++)
        {
            work?.Add();
            cycles[index] = new ServiceCycleReplayArtifactCycle(
                joined.Footers[index], joined.Records[index], joined.SemanticEventIndices[index], semantic);
        }
        // Keep exactly one owned canonical wire buffer. The semantic and payload sections, plus
        // every record payload, are views into this buffer. Re-encoding therefore remains exact
        // without retaining duplicate full semantic or replay-payload copies.
        var semanticBytes = encoded.AsMemory(sections[1].Offset, sections[1].Length);
        var payload = encoded.AsMemory(sections[4].Offset, sections[4].Length);
        var prepared = new ServiceCycleReplayPreparedArtifact(
            semanticBytes,
            semantic,
            rawRecording,
            effectiveRecording,
            codecs,
            records,
            payload,
            joined,
            manifest.FirstUnjoinedFooterSequence,
            manifest.FirstUnjoinedSemanticSequence);
        return new ServiceCycleReplayArtifactDocument(
            prepared,
            new ServiceCycleReplayArtifactFence(semantic.Session, container.Header.SemanticLastSequence,
                manifest.ReplayFence),
            semantic,
            effectiveRecording,
            codecs,
            codecIndex,
            cycles,
            manifest.Eligibility);
    }
}
