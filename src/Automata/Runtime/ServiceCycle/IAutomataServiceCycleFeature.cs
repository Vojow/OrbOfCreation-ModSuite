using System;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Configuration;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

/// <summary>
/// Host-provided context handed to a feature during shared-host composition. The
/// feature performs its own typed registration against the shared registry, keeping
/// its four service generics internal so the neutral host owner never sees them.
/// </summary>
internal readonly struct AutomataServiceCycleFeatureContext
{
    internal AutomataServiceCycleFeatureContext(
        ServiceCycleRegistry registry,
        SuiteRuntimeConfiguration configuration,
        long lifecycleValue
#if SERVICE_CYCLE_PROFILE
        , ServiceCycleProfileProbe profileProbe
#endif
        )
    {
        Registry = registry;
        Configuration = configuration;
        LifecycleValue = lifecycleValue;
#if SERVICE_CYCLE_PROFILE
        ProfileProbe = profileProbe;
#endif
    }

    internal ServiceCycleRegistry Registry { get; }
    internal SuiteRuntimeConfiguration Configuration { get; }
    internal long LifecycleValue { get; }
#if SERVICE_CYCLE_PROFILE
    internal ServiceCycleProfileProbe ProfileProbe { get; }
#endif
}

/// <summary>
/// A feature contribution to the one Automata-owned ServiceCycle host. It supplies
/// its typed definition and native adapters but never the registry, pump, host, or
/// observability.
/// </summary>
internal interface IAutomataServiceCycleFeature
{
    /// <summary>
    /// Registers the feature's typed definition into the shared registry and returns a
    /// non-generic runtime that the neutral owner drives per frame, per configuration
    /// publication, and per lifecycle boundary.
    /// </summary>
    IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context);

    /// <summary>
    /// Reports the feature's own faulted status when shared-host construction fails
    /// before any runtime exists. The whole host is unavailable, so every feature is
    /// given the chance to surface a feature-scoped status to the user.
    /// </summary>
    void ObserveStartupFailure(SuiteRuntimeConfiguration configuration, Exception exception);
}

/// <summary>
/// The non-generic per-feature runtime driven by the neutral host owner. It owns the
/// feature's typed registration handle, native binding cache, and diagnostics
/// projection. Disposal is split so the owner can preserve the exact ordering of
/// diagnostics teardown (before host shutdown) and registration/binding teardown
/// (after host shutdown).
/// </summary>
internal interface IAutomataServiceCycleFeatureRuntime
{
    /// <summary>
    /// Stands up the feature's diagnostics once the shared host and its observability
    /// are live. Kept separate from <see cref="IAutomataServiceCycleFeature.Register"/>
    /// so a host that fails to start its observability never registers feature
    /// diagnostics that would then need unwinding.
    /// </summary>
    void ActivateDiagnostics();

    void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report);

    /// <summary>
    /// Notes a configuration publication the suite has already made. The snapshot itself lives in
    /// the registry's one publication; this is only the hook features use to refresh diagnostics.
    /// </summary>
    void ObserveConfiguration(SuiteRuntimeConfiguration configuration);

    void ObserveLifecycle(long nativeLifecycle, SuiteRuntimeConfiguration configuration);

    /// <summary>Tears down diagnostics before the shared host is shut down.</summary>
    void DisposeDiagnostics();

    /// <summary>Tears down the typed registration and native bindings after host shutdown.</summary>
    void DisposeRegistration();
}
