using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplaySemanticReceiptValidator
{
    internal static ServiceCycleReplaySemanticJoinCode Validate(
        ServiceCycleTraceDocument semantic,
        in ServiceCycleReplayArtifactReceipt receipt) =>
        Validate(ServiceCycleReplaySemanticIndex.Build(semantic), semantic, in receipt);

    internal static ServiceCycleReplaySemanticJoinCode Validate(
        ServiceCycleReplaySemanticIndex semanticIndex,
        ServiceCycleTraceDocument semantic,
        in ServiceCycleReplayArtifactReceipt receipt)
    {
        if (!receipt.IsPresent) return ServiceCycleReplaySemanticJoinCode.Complete;
        var match = semanticIndex.FindReceiptTerminal(in receipt);
        if (match.Count == 0) return ServiceCycleReplaySemanticJoinCode.PreviousReceiptMissing;
        if (match.Count != 1) return ServiceCycleReplaySemanticJoinCode.PreviousReceiptMismatch;
        var terminal = semantic[match.Index];
        var payload = terminal.Payload;
        if ((int)receipt.Disposition != payload.Disposition || receipt.ActionCount != payload.ActionCount ||
            receipt.CommittedCount != payload.CommittedCount || receipt.TerminalIndex != payload.ActionIndex ||
            receipt.UntouchedSuffixCount != payload.UntouchedSuffixCount || receipt.ResultCode != payload.Code ||
            receipt.NativeCallsAttempted != payload.NativeCallsAttempted ||
            receipt.MutationAttempts != payload.MutationAttempts ||
            receipt.MutationsCommitted != payload.MutationsCommitted || receipt.CompletedAt != payload.TimestampTicks ||
            !KindMatches(receipt.Disposition, terminal.Kind))
            return ServiceCycleReplaySemanticJoinCode.PreviousReceiptMismatch;
        if (receipt.HasTerminalAction && !FindTerminalAction(semanticIndex, semantic, in receipt))
            return ServiceCycleReplaySemanticJoinCode.PreviousReceiptMismatch;
        if (receipt.HasEmergencyContext && !HasEmergencyParent(semanticIndex, terminal, in receipt))
            return ServiceCycleReplaySemanticJoinCode.PreviousReceiptMismatch;
        return ServiceCycleReplaySemanticJoinCode.Complete;
    }

    private static bool FindTerminalAction(
        ServiceCycleReplaySemanticIndex semanticIndex,
        ServiceCycleTraceDocument semantic,
        in ServiceCycleReplayArtifactReceipt receipt)
    {
        var match = semanticIndex.FindActionTerminal(in receipt);
        if (match.Count != 1) return false;
        var item = semantic[match.Index];
        var action = receipt.TerminalAction;
        return (int)action.Disposition == item.Payload.Disposition && action.Code == item.Payload.Code &&
            action.NativeCallsAttempted == item.Payload.NativeCallsAttempted &&
            action.MutationAttempts == item.Payload.MutationAttempts &&
            action.MutationsCommitted == item.Payload.MutationsCommitted &&
            action.NativeOutcomeCode == (item.Payload.HasNativeOutcome ? item.Payload.NativeOutcomeCode : 0);
    }

    private static bool HasEmergencyParent(
        ServiceCycleReplaySemanticIndex semanticIndex,
        ServiceCycleSemanticEvent terminal,
        in ServiceCycleReplayArtifactReceipt receipt)
    {
        if (!semanticIndex.TryGetParent(terminal, out var parent)) return false;
        var parentIndex = semanticIndex.IndexOf(parent.Id);
        if (parentIndex < 0) return false;
        var transition = semanticIndex.EmergencyTransitionsThrough(parentIndex);
        return parent.Id == terminal.Parent && parent.Kind == ServiceCycleSemanticEventKind.EmergencyEntered &&
            parent.Payload.OccurrenceCount == receipt.EmergencyEpisode && parent.Payload.Code == receipt.EmergencyReason &&
            transition == receipt.EmergencyTransition;
    }

    private static bool KindMatches(BatchTerminalDisposition disposition, ServiceCycleSemanticEventKind kind) =>
        disposition switch
        {
            BatchTerminalDisposition.Completed => kind == ServiceCycleSemanticEventKind.BatchCompleted,
            BatchTerminalDisposition.Rejected or BatchTerminalDisposition.Faulted =>
                kind == ServiceCycleSemanticEventKind.BatchAborted,
            BatchTerminalDisposition.Orphaned => kind == ServiceCycleSemanticEventKind.BatchOrphaned,
            _ => false,
        };
}
