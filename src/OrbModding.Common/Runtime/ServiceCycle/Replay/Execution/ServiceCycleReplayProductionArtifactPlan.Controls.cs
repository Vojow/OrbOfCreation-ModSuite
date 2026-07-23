using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed partial class ServiceCycleReplayProductionArtifactPlan
{
    internal static bool TryCycle(ServiceCycleSemanticPayload payload, out ServiceCycleReplayCycleKey cycle)
    {
        if (payload.Service != 0 && payload.Service <= int.MaxValue && payload.Lifecycle != 0 &&
            payload.Configuration != 0 && payload.Strategy != 0 && payload.Capture != 0 && payload.Cycle != 0)
        {
            cycle = new ServiceCycleReplayCycleKey(
                (int)payload.Service, payload.Lifecycle, payload.Configuration,
                payload.Strategy, payload.Capture, payload.Cycle);
            return true;
        }
        cycle = default;
        return false;
    }

    private static bool TryControlKind(ServiceCycleSemanticEventKind kind, out ServiceCycleReplayControlKind control)
    {
        control = kind switch
        {
            ServiceCycleSemanticEventKind.ConfigurationPublished => ServiceCycleReplayControlKind.ConfigurationPublished,
            ServiceCycleSemanticEventKind.StrategyPublished => ServiceCycleReplayControlKind.StrategyPublished,
            ServiceCycleSemanticEventKind.LifecycleRequested => ServiceCycleReplayControlKind.LifecycleRequested,
            ServiceCycleSemanticEventKind.EmergencyEntered => ServiceCycleReplayControlKind.EmergencyEntered,
            ServiceCycleSemanticEventKind.EmergencyCleared => ServiceCycleReplayControlKind.EmergencyCleared,
            ServiceCycleSemanticEventKind.PumpCompleted => ServiceCycleReplayControlKind.PumpCompleted,
            _ => default,
        };
        return control != default;
    }

    internal static bool IsCaptureDerived(
        ServiceCycleSemanticEvent item,
        HashSet<ServiceCycleTraceEventId> starts,
        HashSet<CapturePublicationIdentity> completions) =>
        item.Parent.IsValid && starts.Contains(item.Parent) && completions.Contains(
            new CapturePublicationIdentity(item.Parent, item.Payload.Strategy, item.Payload.TimestampTicks));

    private static LifecycleGeneration SharedInitialLifecycle(ulong[] values)
    {
        ulong shared = 0;
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] == 0 || shared != 0 && values[index] != shared) return default;
            shared = values[index];
        }
        return shared == 0 ? default : new LifecycleGeneration(shared);
    }

    private static bool IsUnsupportedLifecycleConstruction(ServiceCycleSemanticEvent item) =>
        item.Kind == ServiceCycleSemanticEventKind.LifecycleConstructionDeferred ||
        (item.Kind is ServiceCycleSemanticEventKind.FaultObserved or
            ServiceCycleSemanticEventKind.RetryScheduled or ServiceCycleSemanticEventKind.FaultRecovered &&
         item.Payload.Disposition == (int)ServiceFaultCategory.LifecycleConstruction);

    private static readonly ServiceCycleReplayCycleKey StableFailureCycle = new(1, 1, 1, 1, 1, 1);

    private sealed class ControlDraft
    {
        private readonly List<ServiceCycleReplayCycleKey> _waits = new();

        internal ControlDraft(
            ServiceCycleSemanticEvent item,
            ServiceCycleReplayControlKind kind,
            int start,
            int end)
        {
            Item = item;
            Kind = kind;
            Start = start;
            End = end;
        }

        internal ServiceCycleSemanticEvent Item { get; }
        internal ServiceCycleReplayControlKind Kind { get; }
        private int Start { get; }
        private int End { get; }

        internal void AddLifecycleWait(ServiceCycleReplayCycleKey cycle)
        {
            if (!_waits.Contains(cycle)) _waits.Add(cycle);
        }

        internal ServiceCycleReplayControlStep ToStep()
        {
            var payload = Item.Payload;
            return new ServiceCycleReplayControlStep(
                Kind,
                checked((int)payload.Service),
                Kind switch
                {
                    ServiceCycleReplayControlKind.ConfigurationPublished => payload.Configuration,
                    ServiceCycleReplayControlKind.StrategyPublished => payload.Strategy,
                    ServiceCycleReplayControlKind.LifecycleRequested => payload.Lifecycle,
                    _ => 0,
                },
                payload.FrameIdentity,
                payload.Code,
                new MonotonicTimestamp(payload.TimestampTicks),
                Start,
                End,
                _waits.ToArray());
        }
    }
}
