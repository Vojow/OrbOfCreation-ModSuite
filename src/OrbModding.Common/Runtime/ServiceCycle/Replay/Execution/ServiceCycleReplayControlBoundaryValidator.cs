using System;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>
/// Rejects controls whose recorded position requires them to run from inside <c>PumpFrame</c>.
/// The current replay control program can only apply controls between pump calls, so moving one
/// across this boundary would change the ordering of feature callbacks and their terminal facts.
/// </summary>
internal static class ServiceCycleReplayControlBoundaryValidator
{
    internal static ServiceCycleReplayControlBoundaryFailure Validate(
        ServiceCycleTraceDocument semantic,
        int[] replayTraceServiceKeys)
    {
        if (semantic is null) throw new ArgumentNullException(nameof(semantic));
        if (replayTraceServiceKeys is null)
            throw new ArgumentNullException(nameof(replayTraceServiceKeys));

        var segmentStart = 0;
        for (var index = 0; index < semantic.Count; index++)
        {
            var control = semantic[index];
            if (control.Kind == ServiceCycleSemanticEventKind.PumpCompleted)
            {
                segmentStart = index + 1;
                continue;
            }
            if (!IsReplayMutationControl(semantic, index, replayTraceServiceKeys)) continue;

            var pumpIndex = FindNextAcceptedPump(semantic, index + 1);
            if (pumpIndex < 0) continue;
            var pump = semantic[pumpIndex].Payload;
            var frameStartTicks = checked(pump.TimestampTicks - pump.TotalDurationTicks);
            if (control.Payload.TimestampTicks > frameStartTicks)
            {
                return Failure(
                    semantic, index, FindPriorCallbackBoundary(semantic, segmentStart, index), control);
            }

            // A constant or coarse clock can place the callback, control and pump boundary at the
            // same tick. StartAttempted, ActionAttempted and CaptureStarted are emitted immediately
            // before invoking the corresponding feature callback, so any preceding fact proves the control was
            // issued from inside that pump even when timing alone cannot distinguish the boundary.
            if (control.Payload.TimestampTicks == frameStartTicks)
            {
                var ownerIndex = FindPriorCallbackBoundary(semantic, segmentStart, index);
                if (ownerIndex >= 0) return Failure(semantic, index, ownerIndex, control);
            }
        }
        return default;
    }

    private static int FindNextAcceptedPump(ServiceCycleTraceDocument semantic, int startIndex)
    {
        for (var index = startIndex; index < semantic.Count; index++)
        {
            var item = semantic[index];
            if (item.Kind == ServiceCycleSemanticEventKind.PumpCompleted)
                return item.Payload.PumpAccepted ? index : -1;
        }
        return -1;
    }

    private static int FindPriorCallbackBoundary(
        ServiceCycleTraceDocument semantic,
        int startIndex,
        int controlIndex)
    {
        for (var index = controlIndex - 1; index >= startIndex; index--)
        {
            var kind = semantic[index].Kind;
            if (kind is ServiceCycleSemanticEventKind.StartAttempted or
                ServiceCycleSemanticEventKind.ActionAttempted or
                ServiceCycleSemanticEventKind.CaptureStarted)
                return index;
        }
        return -1;
    }

    private static bool IsReplayMutationControl(
        ServiceCycleTraceDocument semantic,
        int itemIndex,
        int[] replayTraceServiceKeys)
    {
        var item = semantic[itemIndex];
        if (item.Kind is ServiceCycleSemanticEventKind.EmergencyEntered or
            ServiceCycleSemanticEventKind.EmergencyCleared)
            return true;
        if (item.Kind is not (ServiceCycleSemanticEventKind.LifecycleRequested or
            ServiceCycleSemanticEventKind.ConfigurationPublished or
            ServiceCycleSemanticEventKind.StrategyPublished)) return false;
        for (var index = 0; index < replayTraceServiceKeys.Length; index++)
            if (item.Payload.Service == (ulong)replayTraceServiceKeys[index])
                return item.Kind != ServiceCycleSemanticEventKind.StrategyPublished ||
                    !IsCaptureDerivedStrategyPublication(semantic, in item);
        return false;
    }

    private static bool IsCaptureDerivedStrategyPublication(
        ServiceCycleTraceDocument semantic,
        in ServiceCycleSemanticEvent publication)
    {
        if (!publication.Parent.IsValid) return false;
        ServiceCycleSemanticEvent capture = default;
        var foundCapture = false;
        for (var index = 0; index < semantic.Count; index++)
        {
            if (semantic[index].Id != publication.Parent) continue;
            capture = semantic[index];
            foundCapture = true;
            break;
        }
        if (!foundCapture || capture.Kind != ServiceCycleSemanticEventKind.CaptureStarted ||
            capture.Payload.Service != publication.Payload.Service) return false;
        for (var index = 0; index < semantic.Count; index++)
        {
            var terminal = semantic[index];
            if (terminal.Kind == ServiceCycleSemanticEventKind.CaptureCompleted &&
                terminal.Parent == capture.Id &&
                terminal.Payload.Service == publication.Payload.Service &&
                terminal.Payload.Strategy == publication.Payload.Strategy &&
                terminal.Payload.TimestampTicks == publication.Payload.TimestampTicks)
                return true;
        }
        return false;
    }

    private static ServiceCycleReplayControlBoundaryFailure Failure(
        ServiceCycleTraceDocument semantic,
        int controlIndex,
        int ownerIndex,
        ServiceCycleSemanticEvent control)
    {
        var service = control.Payload.Service;
        if (service == 0 && ownerIndex >= 0) service = semantic[ownerIndex].Payload.Service;
        return new ServiceCycleReplayControlBoundaryFailure(
            controlIndex,
            ownerIndex,
            checked((int)service),
            control.Kind);
    }
}

internal readonly struct ServiceCycleReplayControlBoundaryFailure
{
    internal ServiceCycleReplayControlBoundaryFailure(
        int controlEventIndex,
        int ownerEventIndex,
        int traceServiceKey,
        ServiceCycleSemanticEventKind controlKind)
    {
        ControlEventNumber = checked(controlEventIndex + 1);
        OwnerEventNumber = ownerEventIndex < 0 ? 0 : checked(ownerEventIndex + 1);
        TraceServiceKey = traceServiceKey;
        ControlKind = controlKind;
        Detail = controlKind == ServiceCycleSemanticEventKind.StrategyPublished
            ? ServiceCycleReplayExecutionDetailCode.ControlOrderRejected
            : ServiceCycleReplayExecutionDetailCode.InPumpControlUnsupported;
    }

    internal int ControlEventIndex => ControlEventNumber - 1;
    internal int OwnerEventIndex => OwnerEventNumber - 1;
    internal int TraceServiceKey { get; }
    internal ServiceCycleSemanticEventKind ControlKind { get; }
    internal ServiceCycleReplayExecutionDetailCode Detail { get; }
    internal bool IsValid => ControlEventNumber != 0;

    private int ControlEventNumber { get; }
    private int OwnerEventNumber { get; }
}
