using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed partial class PumpSegmentBuilder
{
    private void BuildStartClock(
        List<MonotonicTimestamp> values,
        bool[] transitioned,
        int startingOrdinal,
        long frameStart,
        long frameEnd,
        ref long current,
        ref ServiceCycleReplayCycleKey timing)
    {
        for (var offset = 0; offset < _capacity; offset++)
        {
            var ordinal = (startingOrdinal + offset) % _capacity;
            if (transitioned[ordinal])
                continue;

            values.Add(new MonotonicTimestamp(current));
            var start = _startAttempts[ordinal];
            if (start is null)
            {
                values.Add(new MonotonicTimestamp(current));
                continue;
            }

            values.Add(new MonotonicTimestamp(start.Value.Payload.TimestampTicks));
            var startTerminal = _startTerminals[ordinal] ??
                throw new InvalidOperationException("Replay start evidence has no terminal.");
            values.Add(new MonotonicTimestamp(startTerminal.Payload.TimestampTicks));
            current = startTerminal.Payload.TimestampTicks;
            if (startTerminal.Kind is ServiceCycleSemanticEventKind.StartDeferred or
                ServiceCycleSemanticEventKind.StartFaulted)
            {
                values.Add(new MonotonicTimestamp(current));
                continue;
            }

            var capture = _captureStarts[ordinal];
            if (capture is null)
            {
                values.Add(new MonotonicTimestamp(current));
                continue;
            }

            var terminal = _captureTerminals[ordinal] ??
                throw new InvalidOperationException("Capture-start clock evidence has no terminal.");
            values.Add(new MonotonicTimestamp(capture.Value.Payload.TimestampTicks));
            var executionStart = checked(
                terminal.Payload.TimestampTicks - terminal.Payload.DurationTicks);
            if (executionStart < capture.Value.Payload.TimestampTicks ||
                executionStart < frameStart ||
                terminal.Payload.TimestampTicks > frameEnd)
            {
                timing = _first;
            }
            values.Add(new MonotonicTimestamp(executionStart));
            current = terminal.Payload.TimestampTicks;
            values.Add(new MonotonicTimestamp(current));

            if (terminal.Kind == ServiceCycleSemanticEventKind.CaptureCompleted)
            {
                var queued = _queuedEvents[ordinal] ??
                    throw new InvalidOperationException("Captured replay clock evidence has no queue event.");
                current = queued.Payload.TimestampTicks;
                values.Add(new MonotonicTimestamp(current));
            }
            values.Add(new MonotonicTimestamp(current));
        }
    }
}
