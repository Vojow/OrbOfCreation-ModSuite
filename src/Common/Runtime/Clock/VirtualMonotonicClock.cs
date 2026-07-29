using System;

namespace OrbModding.Common.Runtime;

/// <summary>A deterministic, manually advanced clock. It never reads wall time.</summary>
public sealed class VirtualMonotonicClock : IMonotonicClock
{
    public VirtualMonotonicClock(MonotonicTimestamp initial = default)
    {
        Now = initial;
    }

    public MonotonicTimestamp Now { get; private set; }

    public void Advance(MonotonicDuration duration)
    {
        Now += duration;
    }

    public void AdvanceTo(MonotonicTimestamp timestamp)
    {
        if (timestamp < Now)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp), "A monotonic clock cannot move backwards.");
        }

        Now = timestamp;
    }
}
