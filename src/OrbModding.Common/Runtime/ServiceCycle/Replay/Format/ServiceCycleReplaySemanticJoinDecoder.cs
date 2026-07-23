using System;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplaySemanticJoinDecoder
{
    internal static ServiceCycleReplaySemanticJoin Read(ReadOnlySpan<byte> row, int index)
    {
        var code = ServiceCycleReplayBinary.I32(row, 332);
        var cycleKind = ServiceCycleReplayBinary.I32(row, 336);
        var evaluationKind = ServiceCycleReplayBinary.I32(row, 340);
        var fingerprint = ServiceCycleReplayBinary.U64(row, 344);
        var publication = ServiceCycleReplayBinary.U64(row, 352);
        var batch = ServiceCycleReplayBinary.U64(row, 360);
        var terminalSequence = ServiceCycleReplayBinary.U64(row, 368);
        if (code is < (int)ServiceCycleReplaySemanticJoinCode.Complete or
            > (int)ServiceCycleReplaySemanticJoinCode.BatchTerminalCausalityMismatch ||
            cycleKind != 0 && cycleKind is not ((int)ServiceCycleSemanticEventKind.CycleCompleted) and
                not ((int)ServiceCycleSemanticEventKind.CycleOrphaned) and
                not ((int)ServiceCycleSemanticEventKind.CycleFaulted) ||
            evaluationKind != 0 && evaluationKind is not ((int)ServiceCycleSemanticEventKind.EvaluationCompleted) and
                not ((int)ServiceCycleSemanticEventKind.EvaluationFaulted)) throw Error(index);
        return new ServiceCycleReplaySemanticJoin(
            (ServiceCycleReplaySemanticJoinCode)code,
            (ServiceCycleSemanticEventKind)evaluationKind,
            (ServiceCycleSemanticEventKind)cycleKind,
            publication,
            fingerprint,
            batch,
            terminalSequence);
    }

    private static ServiceCycleReplayFormatException Error(int index) =>
        ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.CycleFooterInvalid, index);
}
