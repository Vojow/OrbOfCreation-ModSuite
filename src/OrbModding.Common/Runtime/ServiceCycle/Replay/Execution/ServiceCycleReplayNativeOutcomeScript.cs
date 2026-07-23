using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

public readonly struct ServiceCycleReplayNativeOutcome
{
    internal ServiceCycleReplayNativeOutcome(
        ServiceCycleReplayCycleKey cycle,
        ulong batch,
        int actionIndex,
        ServiceActionResult result)
    {
        Cycle = cycle;
        Batch = batch;
        ActionIndex = actionIndex;
        Result = result;
    }

    public ServiceCycleReplayCycleKey Cycle { get; }
    public ulong Batch { get; }
    public int ActionIndex { get; }
    public ServiceActionResult Result { get; }
}

/// <summary>
/// Ordered authoritative native evidence. Common-owned emergency/lifecycle suffix rejection never enters
/// this script because the real pump does not call the feature action adapter for those suffixes.
/// </summary>
public sealed class ServiceCycleReplayNativeOutcomeScript
{
    private readonly ServiceCycleReplayNativeOutcome[] _outcomes;
    private int _cursor;

    private ServiceCycleReplayNativeOutcomeScript(ServiceCycleReplayNativeOutcome[] outcomes) =>
        _outcomes = outcomes;

    internal static ServiceCycleReplayNativeOutcomeScript FromPrepared(
        ServiceCycleReplayNativeOutcome[] outcomes) =>
        new(outcomes is null ? throw new ArgumentNullException(nameof(outcomes)) :
            (ServiceCycleReplayNativeOutcome[])outcomes.Clone());

    public int Count => _outcomes.Length;
    public int ConsumedCount => _cursor;
    public bool IsComplete => _cursor == _outcomes.Length;

    public ServiceActionResult Take(in ServiceActionContext context)
    {
        if (_cursor == _outcomes.Length)
            throw new InvalidOperationException("Replay attempted an action absent from authoritative evidence.");
        var expected = _outcomes[_cursor];
        var cycle = context.Cycle;
        if (cycle.Lifecycle.Value != expected.Cycle.Lifecycle ||
            cycle.Config.Value != expected.Cycle.Configuration ||
            cycle.Strategy.Value != expected.Cycle.Strategy ||
            cycle.Capture.Value != expected.Cycle.Capture ||
            cycle.Cycle.Value != expected.Cycle.Cycle ||
            context.Batch.Value != expected.Batch ||
            context.ActionIndex != expected.ActionIndex)
        {
            throw new InvalidOperationException("Replay action order diverged from authoritative evidence.");
        }
        _cursor++;
        return expected.Result;
    }

    public static ServiceCycleReplayNativeOutcomeScript FromArtifact(
        ServiceCycleReplayArtifactDocument artifact,
        int traceServiceKey)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        if (!artifact.IsComplete)
            throw new InvalidOperationException("Only a complete joined replay artifact can script native outcomes.");
        if (traceServiceKey <= 0) throw new ArgumentOutOfRangeException(nameof(traceServiceKey));
        var count = 0;
        for (var cycleIndex = 0; cycleIndex < artifact.CycleCount; cycleIndex++)
        {
            var cycle = artifact.GetCycle(cycleIndex);
            if (cycle.Key.TraceServiceKey != traceServiceKey) continue;
            for (var eventIndex = 0; eventIndex < cycle.SemanticEventCount; eventIndex++)
                if (IsAdapterResult(cycle.GetSemanticEvent(eventIndex))) count++;
        }
        var outcomes = new ServiceCycleReplayNativeOutcome[count];
        var output = 0;
        for (var cycleIndex = 0; cycleIndex < artifact.CycleCount; cycleIndex++)
        {
            var cycle = artifact.GetCycle(cycleIndex);
            if (cycle.Key.TraceServiceKey != traceServiceKey) continue;
            for (var eventIndex = 0; eventIndex < cycle.SemanticEventCount; eventIndex++)
            {
                var item = cycle.GetSemanticEvent(eventIndex);
                if (!IsAdapterResult(item)) continue;
                var payload = item.Payload;
                outcomes[output++] = new ServiceCycleReplayNativeOutcome(
                    cycle.Key,
                    payload.Batch,
                    payload.ActionIndex,
                    Result(item.Kind, in payload));
            }
        }
        return new ServiceCycleReplayNativeOutcomeScript(outcomes);
    }

    private static bool IsAdapterResult(ServiceCycleSemanticEvent item)
    {
        if (item.Kind is not (
                ServiceCycleSemanticEventKind.ActionCommitted or
                ServiceCycleSemanticEventKind.ActionRejected or
                ServiceCycleSemanticEventKind.ActionFaulted))
            return false;

        // The main-thread batch controller synthesizes these suffix rejections and deliberately does
        // not enter IServiceCycleDefinition.TryExecute. They remain semantic evidence, but they are
        // not feature-adapter outcomes and therefore must not advance the replay outcome cursor.
        var code = item.Payload.Code;
        return item.Kind != ServiceCycleSemanticEventKind.ActionRejected ||
            code != CommonActionResultCodes.EmergencyStop.Value &&
            code != CommonActionResultCodes.LifecycleReplaced.Value;
    }

    private static ServiceActionResult Result(
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload)
    {
        var code = payload.Code >= ServiceActionResultCode.FirstFeatureCode
            ? new ServiceActionResultCode(payload.Code)
            : ServiceActionResultCode.Reserved(payload.Code);
        if (kind == ServiceCycleSemanticEventKind.ActionRejected)
            return ServiceActionResult.Rejected(code);
        if (!payload.HasNativeOutcome)
            return ServiceActionResult.Faulted(code);
        var call = new NativeMutationCallOutcome(
            checked((int)payload.NativeCallsAttempted),
            checked((int)payload.MutationAttempts),
            checked((int)payload.MutationsCommitted));
        var evidence = ServiceNativeMutationEvidence.Observed(payload.NativeOutcome!.Value, call);
        return kind == ServiceCycleSemanticEventKind.ActionCommitted
            ? ServiceActionResult.Committed(code, evidence)
            : ServiceActionResult.Faulted(code, evidence);
    }
}
