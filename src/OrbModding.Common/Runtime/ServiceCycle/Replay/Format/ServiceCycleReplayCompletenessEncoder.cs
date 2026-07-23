using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayCompletenessEncoder
{
    internal static void Write(Span<byte> destination, int offset, ServiceCycleReplayCompleteness completeness)
    {
        ServiceCycleReplayBinary.I32(destination, offset, (int)completeness.Code);
        if (completeness.IsComplete) return;
        var location = completeness.FailureLocation;
        ServiceCycleReplayBinary.I32(destination, offset + 4, (int)location.Scope);
        if (location.Scope != ServiceCycleReplayFailureScope.Record) return;
        ServiceCycleReplayBinary.I32(destination, offset + 8, (int)location.Record.Kind);
        ServiceCycleReplayBinary.I32(destination, offset + 12, location.Record.Index);
    }
}
