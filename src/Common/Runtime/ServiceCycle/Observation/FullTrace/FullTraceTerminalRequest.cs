using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace;

internal sealed class FullTraceTerminalRequest
{
    private int _reason;

    internal void Set(FullTraceTerminalReason reason)
    {
        if (reason is not (FullTraceTerminalReason.UserStopped or FullTraceTerminalReason.RuntimeShutdown))
            throw new ArgumentOutOfRangeException(nameof(reason));
        var previous = Interlocked.CompareExchange(ref _reason, (int)reason, 0);
        if (previous != 0 && previous != (int)reason)
            throw new InvalidOperationException("The full-trace terminal reason is already fixed.");
    }

    internal FullTraceTerminalReason GetRequired() => Volatile.Read(ref _reason) switch
    {
        (int)FullTraceTerminalReason.UserStopped => FullTraceTerminalReason.UserStopped,
        (int)FullTraceTerminalReason.RuntimeShutdown => FullTraceTerminalReason.RuntimeShutdown,
        _ => throw new InvalidOperationException("A complete full-trace session requires a terminal reason."),
    };
}
