using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayReceiptDecoder
{
    internal static ServiceCycleReplayArtifactReceipt Read(ReadOnlySpan<byte> row, int index)
    {
        var flags = ServiceCycleReplayBinary.U32(row, 160);
        if ((flags & ~15u) != 0) throw Error(index);
        var present = (flags & 1) != 0;
        if (!present)
        {
            if (!ServiceCycleReplayBinary.IsZero(row.Slice(160, 172))) throw Error(index);
            return default;
        }
        var hasTerminal = (flags & 2) != 0;
        var hasNative = (flags & 4) != 0;
        var hasEmergency = (flags & 8) != 0;
        var dispositionValue = ServiceCycleReplayBinary.I32(row, 164);
        var cycle = ServiceCycleReplayBinary.ReadCycleKey(row, 168);
        var batch = ServiceCycleReplayBinary.U64(row, 216);
        var actionCount = ServiceCycleReplayBinary.I32(row, 224);
        var committed = ServiceCycleReplayBinary.I32(row, 228);
        var terminalIndex = ServiceCycleReplayBinary.I32(row, 232);
        var suffix = ServiceCycleReplayBinary.I32(row, 236);
        var resultCode = ServiceCycleReplayBinary.I32(row, 240);
        var actionDisposition = ServiceCycleReplayBinary.I32(row, 244);
        var actionCode = ServiceCycleReplayBinary.I32(row, 248);
        var nativeOutcome = ServiceCycleReplayBinary.I32(row, 252);
        var actionCalls = ServiceCycleReplayBinary.I64(row, 256);
        var actionAttempts = ServiceCycleReplayBinary.I64(row, 264);
        var actionCommitted = ServiceCycleReplayBinary.I64(row, 272);
        var calls = ServiceCycleReplayBinary.I64(row, 280);
        var attempts = ServiceCycleReplayBinary.I64(row, 288);
        var mutations = ServiceCycleReplayBinary.I64(row, 296);
        var completedAt = ServiceCycleReplayBinary.I64(row, 304);
        var episode = ServiceCycleReplayBinary.I64(row, 312);
        var transition = ServiceCycleReplayBinary.I64(row, 320);
        var reason = ServiceCycleReplayBinary.I32(row, 328);
        if (!cycle.IsValid || batch == 0 || actionCount < 0 || committed < 0 || suffix < 0 ||
            calls < 0 || attempts < 0 || mutations < 0 || attempts > calls || mutations > attempts ||
            dispositionValue is < (int)BatchTerminalDisposition.Completed or
                > (int)BatchTerminalDisposition.Orphaned) throw Error(index);
        ValidateShape(
            (BatchTerminalDisposition)dispositionValue, actionCount, committed, terminalIndex, suffix,
            resultCode, hasTerminal, actionDisposition, actionCode, hasNative, nativeOutcome,
            actionCalls, actionAttempts, actionCommitted, calls, attempts, mutations,
            hasEmergency, episode, transition, reason, index);
        var action = new ServiceCycleReplayArtifactActionResult(
            (ServiceActionDisposition)actionDisposition, actionCode, hasNative, nativeOutcome,
            actionCalls, actionAttempts, actionCommitted);
        return new ServiceCycleReplayArtifactReceipt(
            true, cycle, batch, (BatchTerminalDisposition)dispositionValue, actionCount, committed,
            terminalIndex, suffix, resultCode, action, hasTerminal, calls, attempts, mutations,
            completedAt, episode, transition, reason);
    }

    private static void ValidateShape(
        BatchTerminalDisposition disposition, int actionCount, int committed, int terminalIndex,
        int suffix, int resultCode, bool hasTerminal, int actionDisposition, int actionCode,
        bool hasNative, int nativeOutcome, long actionCalls, long actionAttempts, long actionCommitted,
        long calls, long attempts, long mutations, bool hasEmergency, long episode, long transition,
        int reason, int index)
    {
        var actionZero = actionDisposition == 0 && actionCode == 0 && !hasNative && nativeOutcome == 0 &&
            actionCalls == 0 && actionAttempts == 0 && actionCommitted == 0;
        if (disposition == BatchTerminalDisposition.Completed)
        {
            if (committed != actionCount || terminalIndex != -1 || suffix != 0 || resultCode != 1 ||
                hasTerminal || !actionZero || hasEmergency || episode != 0 || transition != 0 || reason != 0 ||
                (actionCount == 0 && (calls != 0 || attempts != 0 || mutations != 0)) ||
                (actionCount != 0 && (attempts != mutations || mutations < actionCount))) throw Error(index);
            return;
        }
        if (disposition == BatchTerminalDisposition.Orphaned)
        {
            if (committed > actionCount || terminalIndex != -1 || suffix != actionCount - committed ||
                resultCode != 3 || hasTerminal || !actionZero || hasEmergency || episode != 0 ||
                transition != 0 || reason != 0 ||
                (committed == 0 && (calls != 0 || attempts != 0 || mutations != 0)) ||
                (committed != 0 && (attempts != mutations || mutations < committed))) throw Error(index);
            return;
        }
        var expectedActionDisposition = disposition == BatchTerminalDisposition.Rejected
            ? ServiceActionDisposition.Rejected : ServiceActionDisposition.Faulted;
        if (actionCount == 0 || committed != terminalIndex || terminalIndex < 0 || terminalIndex >= actionCount ||
            suffix != actionCount - terminalIndex - 1 || !hasTerminal ||
            actionDisposition != (int)expectedActionDisposition || actionCode != resultCode ||
            actionCode <= 0 || actionCalls < 0 || actionAttempts < 0 || actionCommitted < 0 ||
            actionAttempts > actionCalls || actionCommitted > actionAttempts ||
            hasNative != (nativeOutcome != 0) || nativeOutcome is < 0 or > 5 ||
            calls < committed + actionCalls || attempts < committed + actionAttempts ||
            mutations != attempts - actionAttempts ||
            hasEmergency != (disposition == BatchTerminalDisposition.Rejected && actionCode == 2)) throw Error(index);
        if (hasEmergency)
        {
            if (episode <= 0 || transition <= 0 || reason is < 1 or > 3) throw Error(index);
        }
        else if (episode != 0 || transition != 0 || reason != 0) throw Error(index);
    }

    private static ServiceCycleReplayFormatException Error(int index) =>
        ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.CycleFooterInvalid, index);
}
