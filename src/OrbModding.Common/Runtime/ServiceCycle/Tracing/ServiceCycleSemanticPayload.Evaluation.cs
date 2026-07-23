using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

public readonly partial struct ServiceCycleSemanticPayload
{
    internal static ServiceCycleSemanticPayload Evaluation(
        in ServiceCycleTraceCycleIdentity identity,
        int code,
        int actionCount,
        long timestampTicks,
        long durationTicks) =>
        new(
            CycleFields | ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.ActionCount |
            ServiceCycleSemanticFields.Timestamp | ServiceCycleSemanticFields.Duration,
            identity.Service.Value, identity.LifecycleGeneration, identity.ConfigurationGeneration,
            identity.StrategyGeneration, identity.CaptureSequence, identity.CycleId, 0, 0, 0,
            timestampTicks, durationTicks, 0, 0, 0, code, 0, 0, actionCount, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    internal static ServiceCycleSemanticPayload EvaluationCompleted(
        in ServiceCycleTraceCycleIdentity identity,
        int actionCount,
        WakePolicy returnedWake,
        long timestampTicks,
        long durationTicks)
        => EvaluationOutcome(in identity, 0, actionCount, returnedWake, timestampTicks, durationTicks);

    internal static ServiceCycleSemanticPayload ProjectionFaulted(
        in ServiceCycleTraceCycleIdentity identity,
        int code,
        int actionCount,
        WakePolicy returnedWake,
        long timestampTicks,
        long durationTicks) =>
        EvaluationOutcome(in identity, code, actionCount, returnedWake, timestampTicks, durationTicks);

    private static ServiceCycleSemanticPayload EvaluationOutcome(
        in ServiceCycleTraceCycleIdentity identity,
        int code,
        int actionCount,
        WakePolicy returnedWake,
        long timestampTicks,
        long durationTicks)
    {
        if (!returnedWake.IsValid || returnedWake.Kind == WakePolicyKind.Default)
            throw new ArgumentException("A concrete returned wake policy is required.", nameof(returnedWake));
        var wakeOperand = returnedWake.Kind switch
        {
            WakePolicyKind.AfterDecision or WakePolicyKind.AfterBatch => returnedWake.Delay.Ticks,
            WakePolicyKind.At => returnedWake.DueTime.Ticks,
            _ => 0,
        };
        return new ServiceCycleSemanticPayload(
            CycleFields | ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.ActionCount |
            ServiceCycleSemanticFields.Timestamp | ServiceCycleSemanticFields.Duration |
            ServiceCycleSemanticFields.Disposition | ServiceCycleSemanticFields.Deadline,
            identity.Service.Value, identity.LifecycleGeneration, identity.ConfigurationGeneration,
            identity.StrategyGeneration, identity.CaptureSequence, identity.CycleId, 0, 0, 0,
            timestampTicks, durationTicks, wakeOperand, 0, 0, code, (int)returnedWake.Kind, 0,
            actionCount, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    internal static ServiceCycleSemanticPayload EvaluationDeferred(
        in ServiceCycleTraceCycleIdentity identity,
        int code,
        long timestampTicks,
        long durationTicks,
        long deadlineTicks) =>
        new(
            CycleFields | ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.ActionCount |
            ServiceCycleSemanticFields.Timestamp | ServiceCycleSemanticFields.Duration |
            ServiceCycleSemanticFields.Deadline,
            identity.Service.Value, identity.LifecycleGeneration, identity.ConfigurationGeneration,
            identity.StrategyGeneration, identity.CaptureSequence, identity.CycleId, 0, 0, 0,
            timestampTicks, durationTicks, deadlineTicks, 0, 0, code, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    internal static ServiceCycleSemanticPayload State(
        in ServiceCycleTraceCycleIdentity identity,
        ulong publication,
        ulong fingerprint,
        long timestampTicks) =>
        new(
            CycleFields | ServiceCycleSemanticFields.StatePublication | ServiceCycleSemanticFields.Fingerprint |
            ServiceCycleSemanticFields.Timestamp,
            identity.Service.Value, identity.LifecycleGeneration, identity.ConfigurationGeneration,
            identity.StrategyGeneration, identity.CaptureSequence, identity.CycleId, 0, 0, publication,
            timestampTicks, 0, 0, 0, fingerprint, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
