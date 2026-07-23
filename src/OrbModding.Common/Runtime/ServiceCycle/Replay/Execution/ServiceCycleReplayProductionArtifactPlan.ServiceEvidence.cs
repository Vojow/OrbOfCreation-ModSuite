using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed partial class ServiceCycleReplayProductionArtifactPlan
{
    private void IndexServiceEvent(
        ServiceCycleSemanticEvent item,
        int serviceIndex,
        List<ulong>[] configurations,
        List<ServiceCycleReplayCaptureAttempt>[] captures,
        List<MutableStartAttempt>[] starts,
        Dictionary<ServiceCycleTraceEventId, StartLocation> startIndices,
        List<StrategyDraft>[] strategies,
        HashSet<ServiceCycleTraceEventId> captureStarts,
        HashSet<CapturePublicationIdentity> captureCompletions,
        Dictionary<WorkerKey, List<MonotonicTimestamp>> workerSchedules,
        List<ServiceCycleReplayNativeOutcome>[] native,
        bool beforeInitialLifecycle)
    {
        switch (item.Kind)
        {
            case ServiceCycleSemanticEventKind.ConfigurationPublished:
                configurations[serviceIndex].Add(item.Payload.Configuration);
                break;
            case ServiceCycleSemanticEventKind.CaptureStarted:
                captureStarts.Add(item.Id);
                break;
            case ServiceCycleSemanticEventKind.CaptureCompleted:
                captures[serviceIndex].Add(new ServiceCycleReplayCaptureAttempt(item.Kind, item.Payload));
                captureCompletions.Add(new CapturePublicationIdentity(
                    item.Parent, item.Payload.Strategy, item.Payload.TimestampTicks));
                break;
            case ServiceCycleSemanticEventKind.CaptureUnavailable:
            case ServiceCycleSemanticEventKind.CaptureFaulted:
                captures[serviceIndex].Add(new ServiceCycleReplayCaptureAttempt(item.Kind, item.Payload));
                break;
            case ServiceCycleSemanticEventKind.StartAttempted:
                startIndices.Add(item.Id, new StartLocation(serviceIndex, starts[serviceIndex].Count));
                starts[serviceIndex].Add(new MutableStartAttempt(item));
                break;
            case ServiceCycleSemanticEventKind.StartReady:
            case ServiceCycleSemanticEventKind.StartDeferred:
            case ServiceCycleSemanticEventKind.StartFaulted:
                if (startIndices.TryGetValue(item.Parent, out var location))
                {
                    var value = starts[location.ServiceIndex][location.Index];
                    if (value.HasTerminal) throw new InvalidOperationException("Replay start evidence has duplicate terminals.");
                    value.SetTerminal(item);
                    starts[location.ServiceIndex][location.Index] = value;
                }
                break;
            case ServiceCycleSemanticEventKind.StrategyPublished:
                strategies[serviceIndex].Add(new StrategyDraft(
                    item.Payload.Strategy, item.Parent, item.Payload.TimestampTicks,
                    beforeInitialLifecycle));
                break;
        }

        if (item.Payload.Lifecycle != 0 && item.Kind is (
            ServiceCycleSemanticEventKind.EvaluationStarted or
            ServiceCycleSemanticEventKind.StatePublished or
            ServiceCycleSemanticEventKind.EvaluationCompleted or
            ServiceCycleSemanticEventKind.EvaluationFaulted or
            ServiceCycleSemanticEventKind.ProjectionFaulted))
        {
            var key = new WorkerKey(serviceIndex + 1, item.Payload.Lifecycle);
            if (!workerSchedules.TryGetValue(key, out var schedule))
                workerSchedules.Add(key, schedule = new List<MonotonicTimestamp>());
            schedule.Add(new MonotonicTimestamp(item.Payload.TimestampTicks));
        }

        if (TryNativeOutcome(item, out var outcome)) native[serviceIndex].Add(outcome);
    }

    private static bool TryNativeOutcome(ServiceCycleSemanticEvent item, out ServiceCycleReplayNativeOutcome outcome)
    {
        outcome = default;
        if (item.Kind is not (ServiceCycleSemanticEventKind.ActionCommitted or
            ServiceCycleSemanticEventKind.ActionRejected or ServiceCycleSemanticEventKind.ActionFaulted) ||
            !TryCycle(item.Payload, out var cycle)) return false;
        var code = item.Payload.Code;
        if (item.Kind == ServiceCycleSemanticEventKind.ActionRejected &&
            (code == CommonActionResultCodes.EmergencyStop.Value ||
             code == CommonActionResultCodes.LifecycleReplaced.Value)) return false;
        var resultCode = code >= ServiceActionResultCode.FirstFeatureCode
            ? new ServiceActionResultCode(code) : ServiceActionResultCode.Reserved(code);
        ServiceActionResult result;
        if (item.Kind == ServiceCycleSemanticEventKind.ActionRejected)
            result = ServiceActionResult.Rejected(resultCode);
        else if (!item.Payload.HasNativeOutcome)
            result = ServiceActionResult.Faulted(resultCode);
        else
        {
            var call = new NativeMutationCallOutcome(
                checked((int)item.Payload.NativeCallsAttempted),
                checked((int)item.Payload.MutationAttempts),
                checked((int)item.Payload.MutationsCommitted));
            var evidence = ServiceNativeMutationEvidence.Observed(item.Payload.NativeOutcome!.Value, call);
            result = item.Kind == ServiceCycleSemanticEventKind.ActionCommitted
                ? ServiceActionResult.Committed(resultCode, evidence)
                : ServiceActionResult.Faulted(resultCode, evidence);
        }
        outcome = new ServiceCycleReplayNativeOutcome(
            cycle, item.Payload.Batch, item.Payload.ActionIndex, result);
        return true;
    }

    private readonly struct StartLocation
    {
        internal StartLocation(int serviceIndex, int index)
        {
            ServiceIndex = serviceIndex;
            Index = index;
        }

        internal int ServiceIndex { get; }
        internal int Index { get; }
    }

    private readonly struct StrategyDraft
    {
        internal StrategyDraft(
            ulong generation,
            ServiceCycleTraceEventId parent,
            long timestamp,
            bool before)
        {
            Generation = generation;
            Parent = parent;
            TimestampTicks = timestamp;
            BeforeInitialLifecycle = before;
        }

        internal ulong Generation { get; }
        internal ServiceCycleTraceEventId Parent { get; }
        internal long TimestampTicks { get; }
        internal bool BeforeInitialLifecycle { get; }
    }

    private struct MutableStartAttempt
    {
        private readonly ServiceCycleSemanticEvent _attempt;
        private ServiceCycleSemanticEvent _terminal;

        internal MutableStartAttempt(ServiceCycleSemanticEvent attempt)
        {
            _attempt = attempt;
            _terminal = default;
            HasTerminal = false;
        }

        internal bool HasTerminal { get; private set; }

        internal void SetTerminal(ServiceCycleSemanticEvent terminal)
        {
            _terminal = terminal;
            HasTerminal = true;
        }

        internal ServiceCycleReplayStartAttempt Freeze() => new(_attempt, _terminal);
    }
}
