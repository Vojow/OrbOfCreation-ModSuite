using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

public readonly partial struct ServiceCycleSemanticPayload
{
    internal static ServiceCycleSemanticPayload BatchFact(
        in ServiceCycleTraceCycleIdentity identity,
        ulong batch,
        int disposition,
        int code,
        int actionCount,
        int committedCount,
        int terminalIndex,
        int suffixCount,
        long nativeCalls,
        long mutationAttempts,
        long mutationsCommitted,
        long timestampTicks) =>
        new(
            CycleFields | ServiceCycleSemanticFields.Batch | ServiceCycleSemanticFields.Disposition |
            ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.ActionCount |
            ServiceCycleSemanticFields.CommittedCount | ServiceCycleSemanticFields.ActionIndex |
            ServiceCycleSemanticFields.UntouchedSuffixCount | ServiceCycleSemanticFields.NativeCallTotals |
            ServiceCycleSemanticFields.Timestamp,
            identity.Service.Value, identity.LifecycleGeneration, identity.ConfigurationGeneration,
            identity.StrategyGeneration, identity.CaptureSequence, identity.CycleId, batch, 0, 0,
            timestampTicks, 0, 0, 0, 0, code, disposition, terminalIndex, actionCount, committedCount,
            suffixCount, 0, nativeCalls, mutationAttempts, mutationsCommitted, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    internal static ServiceCycleSemanticPayload ActionFact(
        in ServiceCycleTraceCycleIdentity identity,
        ulong batch,
        ulong action,
        int actionIndex,
        int disposition,
        int code,
        NativeMutationOutcome? nativeOutcome,
        long nativeCalls,
        long mutationAttempts,
        long mutationsCommitted,
        long timestampTicks,
        long durationTicks) =>
        new(
            CycleFields | ServiceCycleSemanticFields.Batch | ServiceCycleSemanticFields.Action |
            ServiceCycleSemanticFields.ActionIndex | ServiceCycleSemanticFields.Disposition |
            ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.NativeCallTotals |
            (nativeOutcome.HasValue ? ServiceCycleSemanticFields.NativeMutationOutcome : 0) |
            ServiceCycleSemanticFields.Timestamp | ServiceCycleSemanticFields.Duration,
            identity.Service.Value, identity.LifecycleGeneration, identity.ConfigurationGeneration,
            identity.StrategyGeneration, identity.CaptureSequence, identity.CycleId, batch, action, 0,
            timestampTicks, durationTicks, 0, 0, 0, code, disposition, actionIndex, 0, 0, 0, 0,
            nativeCalls, mutationAttempts, mutationsCommitted, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            nativeOutcome.HasValue ? (int)nativeOutcome.Value + 1 : 0);
}
