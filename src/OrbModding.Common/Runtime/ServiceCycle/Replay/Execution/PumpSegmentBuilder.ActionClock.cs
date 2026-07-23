using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed partial class PumpSegmentBuilder
{
    private void BuildActionClock(
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

            var attempted = _actionAttempts[ordinal];
            var terminal = _actionTerminals[ordinal];
            var started = attempted?.Payload.TimestampTicks ?? current;
            values.Add(new MonotonicTimestamp(started));
            if (terminal is { } action)
            {
                var executionStart = checked(
                    action.Payload.TimestampTicks - action.Payload.DurationTicks);
                if (executionStart < started ||
                    executionStart < frameStart ||
                    action.Payload.TimestampTicks > frameEnd)
                {
                    timing = _first;
                }
                values.Add(new MonotonicTimestamp(executionStart));
                values.Add(new MonotonicTimestamp(action.Payload.TimestampTicks));
                current = action.Payload.TimestampTicks;
                transitioned[ordinal] = true;
            }
            values.Add(new MonotonicTimestamp(current));
        }
    }
}
