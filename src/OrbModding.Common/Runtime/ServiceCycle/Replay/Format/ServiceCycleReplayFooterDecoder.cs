using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayFooterDecoder
{
    internal static ServiceCycleReplayArtifactFooter Read(ReadOnlySpan<byte> row, int index)
    {
        if (row.Length != ServiceCycleReplayArtifactFormat.CycleFooterBytes) throw Error(index);
        var sequence = ServiceCycleReplayBinary.I64(row, 0);
        var cycle = ServiceCycleReplayBinary.ReadCycleKey(row, 8);
        if (sequence != index + 1 || !cycle.IsValid) throw Error(index);
        var dispositionValue = ServiceCycleReplayBinary.I32(row, 56);
        if (dispositionValue is < (int)ServiceCycleReplayCycleFooterDisposition.Provisional or
            > (int)ServiceCycleReplayCycleFooterDisposition.ProjectionAborted) throw Error(index);
        var flags = ServiceCycleReplayBinary.U32(row, 60);
        if ((flags & ~3u) != 0) throw Error(index);
        var hasWake = (flags & 1) != 0;
        var hasProjection = (flags & 2) != 0;
        var expectedActions = ServiceCycleReplayBinary.I32(row, 64);
        var retained = ServiceCycleReplayBinary.I32(row, 68);
        var firstRecord = ServiceCycleReplayBinary.I64(row, 72);
        var lastRecord = ServiceCycleReplayBinary.I64(row, 80);
        if (expectedActions < 0 || retained < 0 ||
            retained == 0 && (firstRecord != 0 || lastRecord != 0) ||
            retained != 0 && (firstRecord <= 0 || lastRecord < firstRecord)) throw Error(index);
        var disposition = (ServiceCycleReplayCycleFooterDisposition)dispositionValue;
        var requiredNonActions = disposition == ServiceCycleReplayCycleFooterDisposition.EvaluationAborted ? 2 : 3;
        var completeness = ServiceCycleReplayFooterValueDecoder.ReadCompleteness(
            row, 88, ServiceCycleReplayFormatErrorCode.CycleFooterInvalid, index);
        var expectedRecords = (long)requiredNonActions + expectedActions;
        if (retained > expectedRecords || completeness.IsComplete && retained != expectedRecords ||
            disposition == ServiceCycleReplayCycleFooterDisposition.Provisional && (!hasWake || !hasProjection) ||
            disposition == ServiceCycleReplayCycleFooterDisposition.EvaluationAborted && (hasWake || hasProjection) ||
            disposition == ServiceCycleReplayCycleFooterDisposition.ProjectionAborted && (!hasWake || hasProjection))
            throw Error(index);
        var decisionAt = ServiceCycleReplayBinary.I64(row, 104);
        var encodingDuration = ServiceCycleReplayBinary.I64(row, 112);
        var frequency = ServiceCycleReplayBinary.I64(row, 120);
        var allocated = ServiceCycleReplayBinary.I64(row, 128);
        if (decisionAt < 0 || encodingDuration < 0 || frequency <= 0 || allocated < 0) throw Error(index);
        var wake = ServiceCycleReplayFooterValueDecoder.ReadWake(row, hasWake, index);
        var projection = ServiceCycleReplayFooterValueDecoder.ReadProjection(row, hasProjection, index);
        var receipt = ServiceCycleReplayReceiptDecoder.Read(row, index);
        var context = new ServiceCycleReplayArtifactContext(cycle, receipt, decisionAt);
        var join = ServiceCycleReplaySemanticJoinDecoder.Read(row, index);
        if (!ServiceCycleReplayBinary.IsZero(row.Slice(376, 8))) throw Error(index);
        return new ServiceCycleReplayArtifactFooter(
            sequence, context, disposition,
            wake, hasWake, projection, hasProjection, expectedActions, firstRecord, lastRecord,
            retained, completeness, encodingDuration, frequency, allocated, join);
    }

    private static ServiceCycleReplayFormatException Error(int index) =>
        ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.CycleFooterInvalid, index);
}
