using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed partial class ServiceCycleReplayClockScript
{
    private MonotonicTimestamp[] ReadWorkerSchedule(int traceServiceKey, LifecycleGeneration lifecycle)
    {
        var values = new List<MonotonicTimestamp>();
        for (var index = 0; index < _semantic.Count; index++)
        {
            var item = _semantic[index];
            if (item.Payload.Service != (ulong)traceServiceKey || item.Payload.Lifecycle != lifecycle.Value) continue;
            if (item.Kind is ServiceCycleSemanticEventKind.EvaluationStarted or
                ServiceCycleSemanticEventKind.StatePublished or ServiceCycleSemanticEventKind.EvaluationCompleted or
                ServiceCycleSemanticEventKind.EvaluationFaulted or ServiceCycleSemanticEventKind.ProjectionFaulted)
                values.Add(new MonotonicTimestamp(item.Payload.TimestampTicks));
        }
        return values.ToArray();
    }

    private bool[] Services(ServiceCycleReplayControlStep step, ServiceCycleSemanticEventKind kind)
    {
        var result = new bool[_serviceCount];
        for (var index = step.SemanticStartIndex; index < step.SemanticEndIndex; index++)
        {
            var item = _semantic[index];
            if (item.Kind != kind || item.Payload.Service == 0) continue;
            for (var ordinal = 0; ordinal < _artifactKeys.Length; ordinal++)
                if (item.Payload.Service == (ulong)_artifactKeys[ordinal]) result[ordinal] = true;
        }
        return result;
    }

    private ServiceCycleSemanticEvent? FindForService(
        ServiceCycleReplayControlStep step,
        int traceServiceKey,
        params ServiceCycleSemanticEventKind[] kinds)
    {
        for (var index = step.SemanticStartIndex; index < step.SemanticEndIndex; index++)
        {
            var item = _semantic[index];
            if (item.Payload.Service != (ulong)traceServiceKey) continue;
            for (var kindIndex = 0; kindIndex < kinds.Length; kindIndex++)
                if (item.Kind == kinds[kindIndex]) return item;
        }
        return null;
    }

    private ServiceCycleSemanticEvent FindForCapture(
        ServiceCycleReplayControlStep step,
        ServiceCycleSemanticPayload capture)
    {
        for (var index = step.SemanticStartIndex; index < step.SemanticEndIndex; index++)
        {
            var item = _semantic[index];
            var payload = item.Payload;
            if (item.Kind is not (ServiceCycleSemanticEventKind.CaptureCompleted or
                ServiceCycleSemanticEventKind.CaptureUnavailable or
                ServiceCycleSemanticEventKind.CaptureFaulted) ||
                payload.Service != capture.Service || payload.Lifecycle != capture.Lifecycle ||
                payload.Configuration != capture.Configuration || payload.Capture != capture.Capture ||
                payload.Cycle != capture.Cycle) continue;
            return item;
        }
        throw new InvalidOperationException("Capture-start clock evidence has no terminal event.");
    }

    private ServiceCycleSemanticEvent FindDirectStartTerminal(
        ServiceCycleReplayControlStep step,
        ServiceCycleSemanticEvent attempted)
    {
        ServiceCycleSemanticEvent terminal = default;
        var found = false;
        for (var index = step.SemanticStartIndex; index < step.SemanticEndIndex; index++)
        {
            var item = _semantic[index];
            if (item.Parent != attempted.Id || item.Kind is not (
                ServiceCycleSemanticEventKind.StartDeferred or
                ServiceCycleSemanticEventKind.StartFaulted or
                ServiceCycleSemanticEventKind.StartReady)) continue;
            if (found) throw new InvalidOperationException("Replay start evidence has duplicate terminals.");
            terminal = item;
            found = true;
        }
        return found
            ? terminal
            : throw new InvalidOperationException("Replay start evidence has no terminal.");
    }

    private int Ordinal(int start, int offset) => (start + offset) % _serviceCount;

    private static long DistributedDuration(long total, int count, int index)
    {
        if (count <= 0) return 0;
        var quotient = total / count;
        var remainder = total % count;
        return checked(quotient + (index < remainder ? 1 : 0));
    }

    private static int[] DenseKeys(int serviceCount)
    {
        if (serviceCount <= 0) throw new ArgumentOutOfRangeException(nameof(serviceCount));
        var keys = new int[serviceCount];
        for (var index = 0; index < keys.Length; index++) keys[index] = index + 1;
        return keys;
    }
}
