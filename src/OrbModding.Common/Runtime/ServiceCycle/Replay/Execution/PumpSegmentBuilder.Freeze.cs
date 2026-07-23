using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed partial class PumpSegmentBuilder
{
    internal ServiceCycleReplayPumpPlan Freeze(
        ServiceCycleSemanticEvent pump,
        int end,
        int startingOrdinal,
        bool emergency,
        HashSet<ServiceCycleTraceEventId> captureStarts,
        HashSet<ServiceCycleReplayProductionArtifactPlan.CapturePublicationIdentity> captureCompletions)
    {
        var delayed = default(ServiceCycleReplayCycleKey);
        foreach (var cycle in _completed)
        {
            if (_queued.Contains(cycle))
                continue;
            delayed = cycle;
            break;
        }

        var timing = default(ServiceCycleReplayCycleKey);
        var payload = pump.Payload;
        try
        {
            var phases = checked(
                payload.ResponseDurationTicks +
                payload.ActionDurationTicks +
                payload.CaptureDurationTicks);
            if (payload.ActionDurationTicks != _actionDuration ||
                payload.CaptureDurationTicks != _captureDuration ||
                phases > payload.TotalDurationTicks)
            {
                timing = _first;
            }
        }
        catch (OverflowException)
        {
            timing = _first;
        }

        var frameStart = checked(payload.TimestampTicks - payload.TotalDurationTicks);
        var boundary = default(ServiceCycleReplayControlBoundaryFailure);
        for (var index = 0; index < _controls.Count; index++)
        {
            var control = _controls[index];
            if (control.Control.Kind == ServiceCycleSemanticEventKind.StrategyPublished &&
                ServiceCycleReplayProductionArtifactPlan.IsCaptureDerived(
                    control.Control,
                    captureStarts,
                    captureCompletions))
            {
                continue;
            }

            if (control.Control.Payload.TimestampTicks > frameStart ||
                control.Control.Payload.TimestampTicks == frameStart && control.Owner >= StartIndex)
            {
                var service = control.Control.Payload.Service == 0
                    ? control.OwnerService
                    : control.Control.Payload.Service;
                boundary = new ServiceCycleReplayControlBoundaryFailure(
                    control.Index,
                    control.Owner,
                    checked((int)service),
                    control.Control.Kind);
                break;
            }
        }

        var ownerClock = BuildOwnerClock(pump, startingOrdinal, emergency, ref timing);
        return new ServiceCycleReplayPumpPlan(
            StartIndex,
            end,
            pump,
            _first,
            _responses.ToArray(),
            _startSequences,
            ownerClock,
            boundary,
            delayed,
            timing);
    }
}
