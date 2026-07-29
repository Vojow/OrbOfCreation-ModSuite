using System;
using System.Threading;
using OrbModding.Common.Runtime;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal sealed class ThreadSafeTestClock : IMonotonicClock
{
    private long _ticks;

    internal ThreadSafeTestClock(long initialTicks = 0)
    {
        if (initialTicks < 0) throw new ArgumentOutOfRangeException(nameof(initialTicks));
        _ticks = initialTicks;
    }

    public MonotonicTimestamp Now => new(Interlocked.Read(ref _ticks));
    internal void Advance(MonotonicDuration duration) => Interlocked.Add(ref _ticks, duration.Ticks);

    internal void AdvanceTo(MonotonicTimestamp timestamp)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _ticks);
            if (timestamp.Ticks < current) throw new ArgumentOutOfRangeException(nameof(timestamp));
            if (Interlocked.CompareExchange(ref _ticks, timestamp.Ticks, current) == current) return;
        }
    }
}
