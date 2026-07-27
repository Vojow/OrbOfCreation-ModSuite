using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Auto Buy's contribution to the one Automata-owned ServiceCycle host. It supplies its typed
/// definition and native adapters and registers them against the shared registry, but never owns
/// the registry, pump, host, or observability.
/// </summary>
internal sealed class AutoBuyServiceCycleFeature : IAutomataServiceCycleFeature
{
    private static readonly ServiceActionDispatchPolicy ActionDispatchPolicy =
        ServiceActionDispatchPolicy.Bounded(16);

    private readonly AutoBuyFeatureDependencies _dependencies;

    public AutoBuyServiceCycleFeature(AutoBuyFeatureDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context)
    {
        var adapters = AutoBuyServiceAdapterComposition.Create(
            _dependencies
#if SERVICE_CYCLE_PROFILE
            , context.ProfileProbe
#endif
            );
        var registration = context.Registry.Register(
            adapters.Definition,
            ActionDispatchPolicy);
        return new AutoBuyFeatureRuntime(
            _dependencies,
            registration,
            context.LifecycleValue,
            context.Configuration);
    }

    public void ObserveStartupFailure(SuiteRuntimeConfiguration configuration, Exception exception)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (AutoBuyConfigurationPolicy.IsOperational(configuration))
        {
            _dependencies.FeatureStatus?.Observe(
                true,
                FeatureStatusState.Faulted,
                FeatureStatusReasonCode.RuntimeFailure,
                "Auto Buy could not initialize its ServiceCycle runtime.");
        }
    }
}

/// <summary>
/// The non-generic per-frame runtime for Auto Buy inside the shared host. Its native adapters are
/// stateless (a fresh reader and purchase adapter resolve Type-keyed contracts that are stable
/// across lifecycles), so beyond the typed registration handle it owns only its feature-status
/// bridge. The decision journal records every cycle, but it is not what the UI reads: the toggle
/// button, its tooltip, and the Mod Config health row all read the feature status registry, and
/// without the bridge below nothing refreshed that registry once gameplay was live.
/// </summary>
internal sealed class AutoBuyFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly AutoBuyFeatureDependencies _dependencies;
    private readonly ServiceRegistration<
        AutoBuyCycleState,
        AutoBuyCycleAction> _registration;
    private readonly long _lifecycleValue;
    private readonly SuiteRuntimeConfiguration _initialConfiguration;
    private AutoBuyServiceCycleDiagnosticsBridge? _diagnostics;

    internal AutoBuyFeatureRuntime(
        AutoBuyFeatureDependencies dependencies,
        ServiceRegistration<
            AutoBuyCycleState,
            AutoBuyCycleAction> registration,
        long lifecycleValue,
        SuiteRuntimeConfiguration initialConfiguration)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _lifecycleValue = lifecycleValue;
        _initialConfiguration = initialConfiguration ?? throw new ArgumentNullException(nameof(initialConfiguration));
    }

    public void ActivateDiagnostics()
    {
        _diagnostics = new AutoBuyServiceCycleDiagnosticsBridge(
            _lifecycleValue,
            _initialConfiguration,
            _dependencies.OwnershipMask(),
            _dependencies.FeatureStatus);
    }

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report)
    {
        _diagnostics?.Observe(pump, in report, _dependencies.OwnershipMask());
    }

    public void ObserveConfiguration(SuiteRuntimeConfiguration configuration)
    {
        _diagnostics?.ObserveConfiguration(configuration, _dependencies.OwnershipMask());
    }

    public void ObserveLifecycle(long nativeLifecycle, SuiteRuntimeConfiguration configuration)
    {
        // The native reader and purchase adapter cache only Type-keyed contracts, which are stable
        // across lifecycles, so there is nothing to invalidate on a lifecycle boundary.
        _diagnostics?.ObserveLifecycle(nativeLifecycle, configuration, _dependencies.OwnershipMask());
    }

    public void DisposeDiagnostics() => _diagnostics = null;

    public void DisposeRegistration() => _registration.Dispose();
}
