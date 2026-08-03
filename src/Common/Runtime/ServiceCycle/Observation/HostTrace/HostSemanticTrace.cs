using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace;

/// <summary>
/// The always-attached semantic trace: a bounded ring of the most recent events, in memory, that a
/// user can dump when something goes wrong.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a recorder. An always-on disk trace would cost roughly 69 MiB an hour, which both
/// blows the suite's disk budget and contradicts the full-trace mandate's own near-zero idle cost; the
/// profiling recorder is a separate companion. What an always-attached ring buys instead is that the
/// events leading up to a bug already exist when the user notices it, rather than only after they
/// reproduce it.
/// </para>
/// <para>
/// Writing into the ring is the emission path the pump already walks, so an attached host trace adds
/// an array store per event and nothing else. Nothing reaches the disk until a dump is asked for.
/// </para>
/// </remarks>
internal static class HostSemanticTrace
{
    /// <summary>
    /// How many recent events the ring holds. At the observed emission rate — around four thousand
    /// events a minute with three services registered — this is roughly the last two minutes, which
    /// is the window between noticing something wrong and reaching the menu. The array is a few
    /// megabytes and is allocated once, at composition.
    /// </summary>
    internal const int EventCapacity = 8_192;

    internal static ServiceCycleSemanticRecorder Create(
        ServiceCycleTraceSessionId session,
        int serviceCapacity) =>
        new(session, EventCapacity, serviceCapacity);
}
