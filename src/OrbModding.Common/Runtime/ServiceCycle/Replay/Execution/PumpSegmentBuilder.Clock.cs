using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed partial class PumpSegmentBuilder
{
    private MonotonicTimestamp[] BuildOwnerClock(
        ServiceCycleSemanticEvent pump,
        int startingOrdinal,
        bool emergency,
        ref ServiceCycleReplayCycleKey timing)
    {
        var payload = pump.Payload;
        if (!payload.PumpAccepted)
            return new[] { new MonotonicTimestamp(payload.TimestampTicks) };

        var values = new List<MonotonicTimestamp>();
        var frameEnd = payload.TimestampTicks;
        var frameStart = checked(frameEnd - payload.TotalDurationTicks);
        var current = frameStart;
        values.Add(new MonotonicTimestamp(current));

        var transitioned = new bool[_capacity];
        var responseOrdinals = new bool[_capacity];
        for (var index = 0; index < _responses.Count; index++)
            responseOrdinals[_responses[index].TraceServiceKey - 1] = true;

        var responseCount = 0;
        for (var index = 0; index < responseOrdinals.Length; index++)
        {
            if (responseOrdinals[index])
                responseCount++;
        }

        var responseIndex = 0;
        for (var offset = 0; offset < _capacity; offset++)
        {
            var ordinal = (startingOrdinal + offset) % _capacity;
            if (!responseOrdinals[ordinal])
                continue;

            var duration = Distributed(
                payload.ResponseDurationTicks,
                responseCount,
                responseIndex++);
            values.Add(new MonotonicTimestamp(current));
            current = checked(current + duration);
            values.Add(new MonotonicTimestamp(current));
            transitioned[ordinal] = true;
        }

        if (emergency)
        {
            values.Add(new MonotonicTimestamp(current));
        }
        else
        {
            BuildActionClock(values, transitioned, startingOrdinal, frameStart, frameEnd, ref current, ref timing);
            BuildStartClock(values, transitioned, startingOrdinal, frameStart, frameEnd, ref current, ref timing);
        }

        values.Add(new MonotonicTimestamp(current));
        values.Add(new MonotonicTimestamp(current));
        values.Add(new MonotonicTimestamp(frameEnd));
        for (var index = 1; index < values.Count; index++)
        {
            if (values[index] >= values[index - 1])
                continue;
            timing = _first;
            break;
        }
        return values.ToArray();
    }

    private static long Distributed(long total, int count, int index)
    {
        if (count <= 0)
            return 0;
        var quotient = total / count;
        var remainder = total % count;
        return checked(quotient + (index < remainder ? 1 : 0));
    }
}
