using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayReceiptEncoder
{
    internal static void Write(Span<byte> row, in ServiceCycleReplayArtifactReceipt receipt)
    {
        var action = receipt.TerminalAction;
        var flags = (receipt.IsPresent ? 1u : 0u) | (receipt.HasTerminalAction ? 2u : 0u) |
            (action.HasNativeEvidence ? 4u : 0u) | (receipt.HasEmergencyContext ? 8u : 0u);
        ServiceCycleReplayBinary.U32(row, 160, flags);
        ServiceCycleReplayBinary.I32(row, 164, (int)receipt.Disposition);
        var cycle = receipt.Cycle;
        ServiceCycleReplayBinary.WriteCycleKey(row, 168, in cycle);
        ServiceCycleReplayBinary.U64(row, 216, receipt.Batch);
        ServiceCycleReplayBinary.I32(row, 224, receipt.ActionCount);
        ServiceCycleReplayBinary.I32(row, 228, receipt.CommittedCount);
        ServiceCycleReplayBinary.I32(row, 232, receipt.TerminalIndex);
        ServiceCycleReplayBinary.I32(row, 236, receipt.UntouchedSuffixCount);
        ServiceCycleReplayBinary.I32(row, 240, receipt.ResultCode);
        ServiceCycleReplayBinary.I32(row, 244, (int)action.Disposition);
        ServiceCycleReplayBinary.I32(row, 248, action.Code);
        ServiceCycleReplayBinary.I32(row, 252, action.NativeOutcomeCode);
        ServiceCycleReplayBinary.I64(row, 256, action.NativeCallsAttempted);
        ServiceCycleReplayBinary.I64(row, 264, action.MutationAttempts);
        ServiceCycleReplayBinary.I64(row, 272, action.MutationsCommitted);
        ServiceCycleReplayBinary.I64(row, 280, receipt.NativeCallsAttempted);
        ServiceCycleReplayBinary.I64(row, 288, receipt.MutationAttempts);
        ServiceCycleReplayBinary.I64(row, 296, receipt.MutationsCommitted);
        ServiceCycleReplayBinary.I64(row, 304, receipt.CompletedAt);
        ServiceCycleReplayBinary.I64(row, 312, receipt.EmergencyEpisode);
        ServiceCycleReplayBinary.I64(row, 320, receipt.EmergencyTransition);
        ServiceCycleReplayBinary.I32(row, 328, receipt.EmergencyReason);
    }
}
