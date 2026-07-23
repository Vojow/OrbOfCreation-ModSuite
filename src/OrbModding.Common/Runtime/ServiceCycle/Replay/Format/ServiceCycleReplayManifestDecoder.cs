using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal readonly struct ServiceCycleReplayDecodedManifest
{
    internal ServiceCycleReplayDecodedManifest(ServiceCycleReplayArtifactEligibilityCode eligibility,
        ServiceCycleReplayHighWaterFence replayFence, long codecManifestPublication, int codecManifestCount,
        int joinedCycleCount, ulong joinedSemanticSequence, long joinedRecordSequence, long joinedFooterSequence,
        ServiceCycleReplayCompleteness effectiveCompleteness, ServiceCycleReplayFault effectiveFault,
        ServiceCycleReplayCycleKey firstIncompleteCycle, long firstUnjoinedFooterSequence,
        ulong firstUnjoinedSemanticSequence)
    { Eligibility = eligibility; ReplayFence = replayFence; CodecManifestPublication = codecManifestPublication;
      CodecManifestCount = codecManifestCount; JoinedCycleCount = joinedCycleCount;
      JoinedSemanticSequence = joinedSemanticSequence; JoinedRecordSequence = joinedRecordSequence;
      JoinedFooterSequence = joinedFooterSequence; EffectiveCompleteness = effectiveCompleteness;
      EffectiveFault = effectiveFault; FirstIncompleteCycle = firstIncompleteCycle;
      FirstUnjoinedFooterSequence = firstUnjoinedFooterSequence;
      FirstUnjoinedSemanticSequence = firstUnjoinedSemanticSequence; }
    internal ServiceCycleReplayArtifactEligibilityCode Eligibility { get; }
    internal ServiceCycleReplayHighWaterFence ReplayFence { get; }
    internal long CodecManifestPublication { get; }
    internal int CodecManifestCount { get; }
    internal int JoinedCycleCount { get; }
    internal ulong JoinedSemanticSequence { get; }
    internal long JoinedRecordSequence { get; }
    internal long JoinedFooterSequence { get; }
    internal ServiceCycleReplayCompleteness EffectiveCompleteness { get; }
    internal ServiceCycleReplayFault EffectiveFault { get; }
    internal ServiceCycleReplayCycleKey FirstIncompleteCycle { get; }
    internal long FirstUnjoinedFooterSequence { get; }
    internal ulong FirstUnjoinedSemanticSequence { get; }
}
internal static class ServiceCycleReplayManifestDecoder
{
    internal static ServiceCycleReplayDecodedManifest Decode(ReadOnlySpan<byte> source,
        ServiceCycleReplaySection[] sections, ServiceCycleTraceDocument semantic,
        in ServiceCycleReplayContainerHeader header)
    {
        if (source.Length != ServiceCycleReplayArtifactFormat.ManifestBytes ||
            ServiceCycleReplayBinary.U16(source, 0) !=
                ServiceCycleReplayArtifactFormat.EmbeddedSemanticSchemaVersion ||
            ServiceCycleReplayBinary.U16(source, 2) != ServiceCycleReplayArtifactFormat.CodecManifestEntryBytes ||
            ServiceCycleReplayBinary.U16(source, 4) != ServiceCycleReplayArtifactFormat.RecordIndexEntryBytes ||
            ServiceCycleReplayBinary.U16(source, 6) != ServiceCycleReplayArtifactFormat.CycleFooterBytes)
            throw Error(ServiceCycleReplayFormatErrorCode.ManifestInvalid);
        var serviceCount = Count(source, 8);
        var codecCount = Count(source, 12);
        var recordCount = Count(source, 16);
        var footerCount = Count(source, 20);
        var payloadBytes = Count(source, 24);
        var semanticBytes = Count(source, 28);
        var semanticCount = Count(source, 32);
        var eligibilityValue = ServiceCycleReplayBinary.I32(source, 36);
        if (eligibilityValue is < (int)ServiceCycleReplayArtifactEligibilityCode.Complete or
            > (int)ServiceCycleReplayArtifactEligibilityCode.NativeEvidenceIncomplete ||
            serviceCount * 3L != codecCount || codecCount != sections[2].Count ||
            recordCount != sections[3].Count || footerCount != sections[5].Count ||
            payloadBytes != sections[4].Length || sections[4].Count != payloadBytes ||
            semanticBytes != sections[1].Length || semanticCount != sections[1].Count || semanticCount != semantic.Count)
            throw Error(ServiceCycleReplayFormatErrorCode.ManifestInvalid);
        if (ServiceCycleReplayBinary.U64(source, 40) != (semantic.Count == 0 ? 0 : semantic[0].Id.Sequence) ||
            ServiceCycleReplayBinary.U64(source, 48) != (semantic.Count == 0 ? 0 : semantic[^1].Id.Sequence) ||
            ServiceCycleReplayBinary.U64(source, 56) != (semantic.Dropped.IsPresent ? semantic.Dropped.FirstSequence : 0) ||
            ServiceCycleReplayBinary.U64(source, 64) != (semantic.Dropped.IsPresent ? semantic.Dropped.LastSequence : 0))
            throw Error(ServiceCycleReplayFormatErrorCode.FenceMismatch);
        var replayPublication = ServiceCycleReplayBinary.I64(source, 72);
        var replayRecord = ServiceCycleReplayBinary.I64(source, 80);
        var replayFooter = ServiceCycleReplayBinary.I64(source, 88);
        var codecPublication = ServiceCycleReplayBinary.I64(source, 96);
        var codecManifestCount = Count(source, 104);
        var joinedCycleCount = Count(source, 108);
        var joinedSemantic = ServiceCycleReplayBinary.U64(source, 112);
        var joinedRecord = ServiceCycleReplayBinary.I64(source, 120);
        var joinedFooter = ServiceCycleReplayBinary.I64(source, 128);
        if (replayPublication != header.ReplayPublication || replayRecord != header.ReplayRecordSequence ||
            replayFooter != header.ReplayFooterSequence || replayRecord != recordCount || replayFooter != footerCount ||
            codecPublication < 0 || codecManifestCount != serviceCount || joinedCycleCount > footerCount ||
            joinedRecord < 0 || joinedFooter < 0) throw Error(ServiceCycleReplayFormatErrorCode.FenceMismatch);
        var completeness = ServiceCycleReplayFooterValueDecoder.ReadCompleteness(
            source, 136, ServiceCycleReplayFormatErrorCode.ManifestInvalid, -1);
        var fault = ReadFault(ServiceCycleReplayBinary.I32(source, 152),
            ServiceCycleReplayBinary.I32(source, 156), completeness);
        var firstIncomplete = ServiceCycleReplayBinary.ReadCycleKey(source, 160);
        var firstUnjoinedFooter = ServiceCycleReplayBinary.I64(source, 208);
        var firstUnjoinedSemantic = ServiceCycleReplayBinary.U64(source, 216);
        if (firstUnjoinedFooter < 0 || completeness.IsComplete && (firstIncomplete.IsValid || fault.IsValid) ||
            !completeness.IsComplete && completeness.FailureLocation.Scope is
                (ServiceCycleReplayFailureScope.Record or ServiceCycleReplayFailureScope.Cycle) && !firstIncomplete.IsValid)
            throw Error(ServiceCycleReplayFormatErrorCode.ManifestInvalid);
        return new ServiceCycleReplayDecodedManifest(
            (ServiceCycleReplayArtifactEligibilityCode)eligibilityValue,
            new ServiceCycleReplayHighWaterFence(replayPublication, replayRecord, replayFooter,
                recordCount, footerCount, payloadBytes),
            codecPublication, codecManifestCount, joinedCycleCount, joinedSemantic, joinedRecord, joinedFooter,
            completeness, fault, firstIncomplete, firstUnjoinedFooter, firstUnjoinedSemantic);
    }

    private static ServiceCycleReplayFault ReadFault(int code, int detail,
        ServiceCycleReplayCompleteness completeness)
    {
        if (code == 0)
        {
            if (detail != 0) throw Error(ServiceCycleReplayFormatErrorCode.ManifestInvalid);
            return default;
        }
        if (detail < 0 || completeness.IsComplete || code is < 1 or > 10)
            throw Error(ServiceCycleReplayFormatErrorCode.ManifestInvalid);
        try { return new ServiceCycleReplayFault((ServiceCycleReplayFaultCode)code,
            completeness.FailureLocation, detail); }
        catch (ArgumentException) { throw Error(ServiceCycleReplayFormatErrorCode.ManifestInvalid); }
    }

    private static int Count(ReadOnlySpan<byte> source, int offset)
    {
        var value = ServiceCycleReplayBinary.U32(source, offset);
        if (value > int.MaxValue) throw Error(ServiceCycleReplayFormatErrorCode.LengthOverflow);
        return (int)value;
    }

    private static ServiceCycleReplayFormatException Error(ServiceCycleReplayFormatErrorCode code) =>
        ServiceCycleReplayBinary.Error(code);
}
