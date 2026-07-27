using System;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal static class ServiceCycleSemanticTraceBinder
{
    internal static ServiceCycleSemanticRuntimeTrace Create(
        ServiceCycleSemanticRecorder recorder,
        ServiceCycleRegistry registry,
        int ordinalCount)
    {
        if (recorder is null) throw new ArgumentNullException(nameof(recorder));
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        var trace = new ServiceCycleSemanticRuntimeTrace(recorder, ordinalCount);
        var observedAt = registry.Clock.Now;
        for (var ordinal = 0; ordinal < ordinalCount; ordinal++)
        {
            var slot = registry.GetSlot(ordinal);
            var lifecycleSnapshot = slot.LifecycleSnapshot;
            trace.Bind(
                ordinal,
                slot.ServiceId,
                slot.IsDisposed ? default : slot.LatestConfiguration,
                slot.IsDisposed ? default : slot.LatestStrategy,
                slot.IsDisposed ? default : lifecycleSnapshot.ActiveLifecycle,
                slot.LifecycleSemanticVersion,
                observedAt);
        }
        return trace;
    }

    internal static bool IsSettled(ServiceCycleRegistry registry, int ordinalCount)
    {
        for (var ordinal = 0; ordinal < ordinalCount; ordinal++)
            if (!registry.GetSlot(ordinal).IsBetweenCycles) return false;
        return true;
    }
}
