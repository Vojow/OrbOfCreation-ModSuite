using System;
using System.Collections.Generic;
using BepInEx.Logging;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbAutomata;

/// <summary>
/// Builds the one Automata-owned ServiceCycle host from a set of feature
/// contributions. It owns the single registry, observability, and host; each feature
/// owns only its typed registration, native adapters, and diagnostics. This is the
/// neutral composition seam that both Auto Harvest and Auto Buy join.
/// </summary>
internal static class AutomataServiceCycleComposition
{
    public static AutomataServiceCycleRuntime Create(
        SuiteRuntimeConfiguration configuration,
        AutomataServiceCycleHostDependencies hostDependencies,
        IReadOnlyList<IAutomataServiceCycleFeature> features,
        ManualLogSource log)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (hostDependencies is null) throw new ArgumentNullException(nameof(hostDependencies));
        if (features is null) throw new ArgumentNullException(nameof(features));
        if (features.Count == 0)
            throw new ArgumentException("The Automata ServiceCycle host requires at least one feature.", nameof(features));
        if (log is null) throw new ArgumentNullException(nameof(log));

        var lifecycle = AutomataServiceCycleHost.ToLifecycle(hostDependencies.ReadLifecycleEpoch());
        ServiceCycleRegistry? registry = null;
        AutomataServiceCycleHost? host = null;
        AutomataServiceCycleObservability? observability = null;
        var featureRuntimes = new List<IAutomataServiceCycleFeatureRuntime>(features.Count);
        try
        {
            registry = new ServiceCycleRegistry(features.Count, lifecycle);
            // The registry already owns the suite's one configuration publication, seeded with the
            // all-defaults snapshot. This is the first reading of the settings file.
            registry.ConfigurationPublication.Publish(configuration);
            var configurationPublication = registry.Configuration;
            observability = AutomataServiceCycleObservability.Create(
                registry.Clock,
                traceActive: false,
                log);
#if SERVICE_CYCLE_PROFILE
            var profileProbe = observability.ProfileProbe;
#endif

            for (var index = 0; index < features.Count; index++)
            {
                var context = new AutomataServiceCycleFeatureContext(
                    registry,
                    configuration,
                    checked((long)lifecycle.Value)
#if SERVICE_CYCLE_PROFILE
                    , profileProbe
#endif
                    );
                featureRuntimes.Add(features[index].Register(in context));
            }

            host = new AutomataServiceCycleHost(
                registry,
                hostDependencies.ReadFrameIdentity,
                hostDependencies.PumpTiming,
                // Always attached, never streaming: the ring holds the recent past in memory so a
                // user can dump it after something goes wrong, rather than having to have armed a
                // recorder before it did.
                HostSemanticTrace.Create(
                    new ServiceCycleTraceSessionId(checked((ulong)DateTime.UtcNow.Ticks)),
                    features.Count)
#if SERVICE_CYCLE_PROFILE
                , profileProbe
#endif
                );
            var observabilityOptions = hostDependencies.Observability;
            host.AttachObservability(observability, in observabilityOptions);
            observability = null;
            for (var index = 0; index < featureRuntimes.Count; index++)
                featureRuntimes[index].ActivateDiagnostics();
            return new AutomataServiceCycleRuntime(
                hostDependencies.ReadLifecycleEpoch,
                configurationPublication,
                host,
                featureRuntimes.ToArray());
        }
        catch
        {
            DisposeFailedConstruction(featureRuntimes, observability, host, registry);
            throw;
        }
    }

    private static void DisposeFailedConstruction(
        List<IAutomataServiceCycleFeatureRuntime> featureRuntimes,
        IDisposable? observability,
        AutomataServiceCycleHost? host,
        ServiceCycleRegistry? registry)
    {
        try
        {
            for (var index = 0; index < featureRuntimes.Count; index++)
                featureRuntimes[index].DisposeDiagnostics();
        }
        finally
        {
            try { observability?.Dispose(); }
            finally
            {
                try
                {
                    if (host is not null)
                    {
                        host.Shutdown();
                    }
                    else
                    {
                        registry?.Dispose();
                    }
                }
                finally
                {
                    for (var index = 0; index < featureRuntimes.Count; index++)
                        featureRuntimes[index].DisposeRegistration();
                }
            }
        }
    }
}
