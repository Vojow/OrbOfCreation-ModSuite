#if SERVICE_CYCLE_PROFILE
using System.Diagnostics;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal interface IServiceCycleProfileRawClock
{
    long Frequency { get; }
    long ReadTimestamp();
}

internal sealed class StopwatchServiceCycleProfileRawClock : IServiceCycleProfileRawClock
{
    internal static readonly StopwatchServiceCycleProfileRawClock Instance = new();

    private StopwatchServiceCycleProfileRawClock()
    {
    }

    public long Frequency => Stopwatch.Frequency;
    public long ReadTimestamp() => Stopwatch.GetTimestamp();
}
#endif
