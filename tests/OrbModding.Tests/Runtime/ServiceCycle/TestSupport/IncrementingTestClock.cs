using System;
using System.Threading;
using OrbModding.Common.Runtime;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

/// <summary>Globally ordered test clock that exposes causal sampling mistakes hidden by a constant clock.</summary>
internal sealed class IncrementingTestClock : IMonotonicClock
{
    private long _ticks;

    internal IncrementingTestClock(long initialTicks = 0)
    {
        if (initialTicks < 0) throw new ArgumentOutOfRangeException(nameof(initialTicks));
        _ticks = initialTicks;
    }

    public MonotonicTimestamp Now => new(Interlocked.Increment(ref _ticks));
}
