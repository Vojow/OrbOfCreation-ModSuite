using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayArtifactBuilder
{
    internal static ServiceCycleReplayPreparedArtifact Prepare(
        byte[] semanticBytes,
        ServiceCycleReplaySession session,
        in ServiceCycleReplayRecordingSnapshot snapshot)
    {
        if (semanticBytes is null) throw new ArgumentNullException(nameof(semanticBytes));
        if (session is null) throw new ArgumentNullException(nameof(session));
        ServiceCycleTraceDocument semantic;
        try { semantic = ServiceCycleTraceCodec.Decode(semanticBytes); }
        catch (FormatException)
        {
            throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.SemanticTraceRejected);
        }
        if (!snapshot.TraceSession.IsValid || snapshot.TraceSession != session.TraceSession ||
            snapshot.TraceSession != semantic.Session || !snapshot.HighWater.IsValid ||
            !snapshot.CodecManifests.IsValid)
            throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.FenceMismatch);

        var codecs = ServiceCycleReplaySnapshotReader.ReadCodecs(session, in snapshot);
        var payload = new byte[snapshot.HighWater.ByteCount];
        var records = ServiceCycleReplaySnapshotReader.ReadRecords(session, in snapshot, payload);
        var footers = ServiceCycleReplaySnapshotReader.ReadFooters(session, in snapshot);
        ServiceCycleReplayCodecCoverageValidator.Validate(codecs, records, footers);
        var joined = ServiceCycleReplaySemanticJoiner.Join(semantic, snapshot, footers, records);
        FindFirstUnjoined(semantic, snapshot, joined, out var firstIncompleteCycle,
            out var firstUnjoinedFooter, out var firstUnjoinedSemantic);
        var effective = new ServiceCycleReplayRecordingSnapshot(
            snapshot.TraceSession,
            snapshot.EncodingEnabled,
            snapshot.CodecManifests,
            snapshot.HighWater,
            firstIncompleteCycle,
            EffectiveCompleteness(joined.Eligibility, snapshot.Completeness, in firstIncompleteCycle),
            snapshot.Fault);
        return new ServiceCycleReplayPreparedArtifact(
            semanticBytes,
            semantic,
            snapshot,
            effective,
            codecs,
            records,
            payload,
            joined,
            firstUnjoinedFooter,
            firstUnjoinedSemantic);
    }

    private static void FindFirstUnjoined(
        ServiceCycleTraceDocument semantic,
        in ServiceCycleReplayRecordingSnapshot snapshot,
        ServiceCycleReplayJoinResult joined,
        out ServiceCycleReplayCycleKey firstIncompleteCycle,
        out long firstFooter,
        out ulong firstSemantic)
    {
        firstIncompleteCycle = snapshot.FirstIncompleteCycle;
        firstFooter = 0;
        firstSemantic = 0;
        if (joined.FirstMissingFooterSemanticSequence != 0)
        {
            if (joined.FirstMissingFooterCycle.IsValid)
                firstIncompleteCycle = joined.FirstMissingFooterCycle;
            firstSemantic = joined.FirstMissingFooterSemanticSequence;
        }
        for (var index = 0; index < joined.Footers.Length; index++)
        {
            if (joined.Footers[index].IsComplete) continue;
            firstFooter = joined.Footers[index].Sequence;
            var indices = joined.SemanticEventIndices[index];
            var footerSemantic = indices.Length == 0 ? 0 : semantic[indices[0]].Id.Sequence;
            if (!firstIncompleteCycle.IsValid || footerSemantic != 0 &&
                (firstSemantic == 0 || footerSemantic < firstSemantic))
                firstIncompleteCycle = joined.Footers[index].Context.Cycle;
            if (footerSemantic != 0 && (firstSemantic == 0 || footerSemantic < firstSemantic))
                firstSemantic = footerSemantic;
            break;
        }
        if (joined.Eligibility == ServiceCycleReplayArtifactEligibilityCode.SemanticTraceIncomplete &&
            firstSemantic == 0 && semantic.Dropped.IsPresent)
            firstSemantic = semantic.Dropped.FirstSequence;
    }

    private static ServiceCycleReplayCompleteness EffectiveCompleteness(
        ServiceCycleReplayArtifactEligibilityCode eligibility,
        ServiceCycleReplayCompleteness recording,
        in ServiceCycleReplayCycleKey firstIncompleteCycle)
    {
        if (!recording.IsComplete) return recording;
        return eligibility switch
        {
            ServiceCycleReplayArtifactEligibilityCode.Complete => ServiceCycleReplayCompleteness.Complete,
            ServiceCycleReplayArtifactEligibilityCode.SemanticTraceIncomplete =>
                ServiceCycleReplayCompleteness.Incomplete(
                    ServiceCycleReplayCompletenessCode.SemanticTraceIncomplete,
                    ServiceCycleReplayFailureLocation.SemanticTrace),
            ServiceCycleReplayArtifactEligibilityCode.RecordingDisabled =>
                ServiceCycleReplayCompleteness.Incomplete(
                    ServiceCycleReplayCompletenessCode.ContainerIncomplete,
                    ServiceCycleReplayFailureLocation.Container),
            _ when !firstIncompleteCycle.IsValid =>
                ServiceCycleReplayCompleteness.Incomplete(
                    ServiceCycleReplayCompletenessCode.SemanticTraceIncomplete,
                    ServiceCycleReplayFailureLocation.SemanticTrace),
            _ => ServiceCycleReplayCompleteness.Incomplete(
                ServiceCycleReplayCompletenessCode.CycleIncomplete,
                ServiceCycleReplayFailureLocation.Cycle),
        };
    }
}
