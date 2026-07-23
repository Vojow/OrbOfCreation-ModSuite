using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplaySemanticActionValidator
{
    internal static ServiceCycleReplaySemanticJoinCode Validate(
        ServiceCycleTraceDocument semantic,
        int[] eventIndices,
        ServiceCycleSemanticEvent published,
        ServiceCycleSemanticEvent terminal) =>
        Validate(ServiceCycleReplaySemanticIndex.Build(semantic), semantic, eventIndices, published, terminal);

    internal static ServiceCycleReplaySemanticJoinCode Validate(
        ServiceCycleReplaySemanticIndex semanticIndex,
        ServiceCycleTraceDocument semantic,
        int[] eventIndices,
        ServiceCycleSemanticEvent published,
        ServiceCycleSemanticEvent terminal)
    {
        var payload = terminal.Payload;
        var expectedTerminals = terminal.Kind switch
        {
            ServiceCycleSemanticEventKind.BatchCompleted => payload.ActionCount,
            ServiceCycleSemanticEventKind.BatchAborted => payload.CommittedCount + 1,
            ServiceCycleSemanticEventKind.BatchOrphaned => payload.CommittedCount,
            _ => -1,
        };
        if (expectedTerminals < 0 || expectedTerminals > payload.ActionCount)
            return ServiceCycleReplaySemanticJoinCode.NativeEvidenceMismatch;
        if (expectedTerminals > eventIndices.Length)
            return ServiceCycleReplaySemanticJoinCode.ActionEvidenceMissing;
        var attempts = expectedTerminals == 0 ? Array.Empty<int>() : new int[expectedTerminals];
        var terminals = expectedTerminals == 0 ? Array.Empty<int>() : new int[expectedTerminals];
        Array.Fill(attempts, -1);
        Array.Fill(terminals, -1);
        var nextAttempt = 0;
        var emergencyAction = -1;
        var emergencyParent = default(ServiceCycleTraceEventId);
        long calls = 0;
        long mutationAttempts = 0;
        long mutations = 0;
        for (var index = 0; index < eventIndices.Length; index++)
        {
            var eventIndex = eventIndices[index];
            var item = semantic[eventIndex];
            if (item.Kind == ServiceCycleSemanticEventKind.ActionAttempted)
            {
                if (item.Payload.Batch != payload.Batch)
                    return ServiceCycleReplaySemanticJoinCode.NativeEvidenceMismatch;
                var actionIndex = item.Payload.ActionIndex;
                if ((uint)actionIndex >= (uint)attempts.Length)
                    return ServiceCycleReplaySemanticJoinCode.ActionAttemptOrderMismatch;
                if (attempts[actionIndex] >= 0)
                    return ServiceCycleReplaySemanticJoinCode.ActionAttemptDuplicate;
                if (actionIndex != nextAttempt)
                    return ServiceCycleReplaySemanticJoinCode.ActionAttemptOrderMismatch;
                attempts[actionIndex] = eventIndex;
                nextAttempt++;
                continue;
            }
            if (item.Kind is not (ServiceCycleSemanticEventKind.ActionCommitted or
                ServiceCycleSemanticEventKind.ActionRejected or ServiceCycleSemanticEventKind.ActionFaulted)) continue;
            if (item.Payload.Batch != payload.Batch)
                return ServiceCycleReplaySemanticJoinCode.NativeEvidenceMismatch;
            var terminalIndex = item.Payload.ActionIndex;
            if ((uint)terminalIndex >= (uint)terminals.Length)
                return ServiceCycleReplaySemanticJoinCode.NativeEvidenceMismatch;
            if (terminals[terminalIndex] >= 0)
                return ServiceCycleReplaySemanticJoinCode.ActionEvidenceDuplicate;
            terminals[terminalIndex] = eventIndex;
            var expectedKind = terminalIndex < payload.CommittedCount
                ? ServiceCycleSemanticEventKind.ActionCommitted
                : payload.Disposition == (int)BatchTerminalDisposition.Rejected
                    ? ServiceCycleSemanticEventKind.ActionRejected
                    : ServiceCycleSemanticEventKind.ActionFaulted;
            if (item.Kind != expectedKind)
                return ServiceCycleReplaySemanticJoinCode.NativeEvidenceMismatch;
            if (IsEmergencyRejection(item, terminal))
            {
                if (emergencyAction >= 0 || attempts[terminalIndex] >= 0 ||
                    !semanticIndex.TryGetParent(item, out var parent) ||
                    parent.Kind != ServiceCycleSemanticEventKind.EmergencyEntered)
                    return ServiceCycleReplaySemanticJoinCode.ActionAttemptCausalityMismatch;
                emergencyAction = terminalIndex;
                emergencyParent = item.Parent;
            }
            else
            {
                if (attempts[terminalIndex] < 0)
                    return ServiceCycleReplaySemanticJoinCode.ActionAttemptMissing;
                var attempt = semantic[attempts[terminalIndex]];
                if (item.Parent != attempt.Id || item.Payload.Action != attempt.Payload.Action)
                    return ServiceCycleReplaySemanticJoinCode.ActionAttemptCausalityMismatch;
            }
            calls = Add(calls, item.Payload.NativeCallsAttempted);
            mutationAttempts = Add(mutationAttempts, item.Payload.MutationAttempts);
            mutations = Add(mutations, item.Payload.MutationsCommitted);
        }
        for (var index = 0; index < terminals.Length; index++)
        {
            if (terminals[index] < 0) return ServiceCycleReplaySemanticJoinCode.ActionEvidenceMissing;
            if (index != emergencyAction && attempts[index] < 0)
                return ServiceCycleReplaySemanticJoinCode.ActionAttemptMissing;
        }
        if (nextAttempt != expectedTerminals - (emergencyAction >= 0 ? 1 : 0))
            return ServiceCycleReplaySemanticJoinCode.ActionAttemptMissing;
        if (emergencyAction >= 0)
        {
            if (emergencyAction != expectedTerminals - 1 || terminal.Parent != emergencyParent)
                return ServiceCycleReplaySemanticJoinCode.BatchTerminalCausalityMismatch;
        }
        else
        {
            var predecessor = expectedTerminals == 0
                ? published.Id
                : semantic[terminals[expectedTerminals - 1]].Id;
            if (!semanticIndex.IsAncestor(predecessor, terminal) ||
                !semanticIndex.IsAncestor(published.Id, terminal))
                return ServiceCycleReplaySemanticJoinCode.BatchTerminalCausalityMismatch;
        }
        for (var index = 0; index < expectedTerminals; index++)
        {
            if (index == emergencyAction) continue;
            var predecessor = index == 0
                ? published.Id
                : semantic[terminals[index - 1]].Id;
            if (!semanticIndex.IsAncestor(predecessor, semantic[attempts[index]]))
                return ServiceCycleReplaySemanticJoinCode.ActionAttemptCausalityMismatch;
        }
        return calls == payload.NativeCallsAttempted && mutationAttempts == payload.MutationAttempts &&
            mutations == payload.MutationsCommitted
            ? ServiceCycleReplaySemanticJoinCode.Complete
            : ServiceCycleReplaySemanticJoinCode.NativeEvidenceMismatch;
    }

    private static bool IsEmergencyRejection(
        ServiceCycleSemanticEvent action,
        ServiceCycleSemanticEvent terminal) =>
        action.Kind == ServiceCycleSemanticEventKind.ActionRejected &&
        action.Payload.Code == CommonActionResultCodes.EmergencyStop.Value &&
        terminal.Kind == ServiceCycleSemanticEventKind.BatchAborted &&
        terminal.Payload.Disposition == (int)BatchTerminalDisposition.Rejected &&
        terminal.Payload.Code == CommonActionResultCodes.EmergencyStop.Value;

    private static long Add(long left, long right)
    {
        try { return checked(left + right); }
        catch (OverflowException) { return long.MinValue; }
    }
}
