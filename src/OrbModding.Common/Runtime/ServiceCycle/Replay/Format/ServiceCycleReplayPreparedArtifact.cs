using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal sealed class ServiceCycleReplayPreparedArtifact
{
    internal ServiceCycleReplayPreparedArtifact(
        Memory<byte> semanticBytes,
        ServiceCycleTraceDocument semantic,
        ServiceCycleReplayRecordingSnapshot sourceRecording,
        ServiceCycleReplayRecordingSnapshot effectiveRecording,
        ServiceCycleReplayCodecManifestEntry[] codecs,
        ServiceCycleReplayArtifactRecord[] globalRecords,
        Memory<byte> payload,
        ServiceCycleReplayJoinResult joined,
        long firstUnjoinedFooterSequence,
        ulong firstUnjoinedSemanticSequence)
    {
        SemanticBytes = semanticBytes;
        Semantic = semantic;
        SourceRecording = sourceRecording;
        EffectiveRecording = effectiveRecording;
        Codecs = codecs;
        GlobalRecords = globalRecords;
        Payload = payload;
        Joined = joined;
        FirstUnjoinedFooterSequence = firstUnjoinedFooterSequence;
        FirstUnjoinedSemanticSequence = firstUnjoinedSemanticSequence;
    }

    /// <summary>
    /// Canonical semantic wire bytes. For decoded artifacts this is a view over the single owned
    /// artifact buffer; recording exports supply their already-owned semantic buffer directly.
    /// </summary>
    internal Memory<byte> SemanticBytes { get; }
    internal ServiceCycleTraceDocument Semantic { get; }
    internal ServiceCycleReplayRecordingSnapshot SourceRecording { get; }
    internal ServiceCycleReplayRecordingSnapshot EffectiveRecording { get; }
    internal ServiceCycleReplayCodecManifestEntry[] Codecs { get; }
    internal ServiceCycleReplayArtifactRecord[] GlobalRecords { get; }
    /// <summary>
    /// Canonical replay payload bytes. Decoded artifacts share the same owned backing buffer as
    /// <see cref="SemanticBytes"/> and record payload views.
    /// </summary>
    internal Memory<byte> Payload { get; }
    internal ServiceCycleReplayJoinResult Joined { get; }
    internal long FirstUnjoinedFooterSequence { get; }
    internal ulong FirstUnjoinedSemanticSequence { get; }
}
