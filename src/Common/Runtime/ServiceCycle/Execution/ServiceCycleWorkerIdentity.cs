using System.Globalization;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

/// <summary>
/// Stable physical-worker identity. A lifecycle replacement may keep the retiring worker alive while
/// its successor runs, so the logical service identity alone is not a safe thread/schedule key.
/// </summary>
internal static class ServiceCycleWorkerIdentity
{
    internal static string Create(ServiceId serviceId, LifecycleGeneration lifecycle) =>
        $"Orb.ServiceCycle.{serviceId.Value}.lifecycle-{lifecycle.Value.ToString(CultureInfo.InvariantCulture)}";
}
