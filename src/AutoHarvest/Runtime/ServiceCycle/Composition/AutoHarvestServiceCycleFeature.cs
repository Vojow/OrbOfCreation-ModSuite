using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Auto Harvest's contribution to the one Automata-owned ServiceCycle host. It supplies
/// its typed definition and native adapters and registers them against the shared
/// registry, but never owns the registry, pump, host, or observability.
/// </summary>
internal sealed class AutoHarvestServiceCycleFeature : IAutomataServiceCycleFeature
{
    private readonly AutoHarvestFeatureDependencies _dependencies;

    public AutoHarvestServiceCycleFeature(AutoHarvestFeatureDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context)
    {
        var adapters = AutoHarvestServiceAdapterComposition.Create(
            _dependencies
#if SERVICE_CYCLE_PROFILE
            , context.ProfileProbe
#endif
            );
        var registration = context.Registry.Register(adapters.Definition);
        return new AutoHarvestFeatureRuntime(
            _dependencies,
            adapters.Bindings,
            adapters.Gates,
            registration,
            context.LifecycleValue,
            context.Configuration);
    }

    public void ObserveStartupFailure(SuiteRuntimeConfiguration configuration, Exception exception)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (configuration.General.Enabled &&
            configuration.AutoHarvest.Mode == AutoHarvestOperationMode.Active &&
            (configuration.AutoHarvest.CollectFruitTrees || configuration.AutoHarvest.CollectTreasureTrees))
        {
            _dependencies.FeatureStatus?.Observe(
                true,
                FeatureStatusState.Faulted,
                FeatureStatusReasonCode.RuntimeFailure,
                "Auto Harvest could not initialize its ServiceCycle runtime.");
        }
    }
}

/// <summary>
/// The non-generic per-frame runtime for Auto Harvest inside the shared host. It owns
/// the feature's typed registration handle, native binding cache, native gate set, and
/// diagnostics bridge. Disposal is split so the neutral owner can tear diagnostics down
/// before host shutdown and the registration/bindings down after it.
/// </summary>
internal sealed class AutoHarvestFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly AutoHarvestFeatureDependencies _dependencies;
    private readonly AutoHarvestBindingResolver _bindings;
    private readonly AutoHarvestNativeGateSet _gates;
    private readonly ServiceRegistration<
        AutoHarvestCycleState,
        AutoHarvestCycleAction> _registration;
    private readonly long _lifecycleValue;
    private readonly SuiteRuntimeConfiguration _initialConfiguration;
    private AutoHarvestServiceCycleDiagnosticsBridge? _diagnostics;

    internal AutoHarvestFeatureRuntime(
        AutoHarvestFeatureDependencies dependencies,
        AutoHarvestBindingResolver bindings,
        AutoHarvestNativeGateSet gates,
        ServiceRegistration<
            AutoHarvestCycleState,
            AutoHarvestCycleAction> registration,
        long lifecycleValue,
        SuiteRuntimeConfiguration initialConfiguration)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _gates = gates ?? throw new ArgumentNullException(nameof(gates));
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _lifecycleValue = lifecycleValue;
        _initialConfiguration = initialConfiguration ?? throw new ArgumentNullException(nameof(initialConfiguration));
    }

    public void ActivateDiagnostics()
    {
        _diagnostics = new AutoHarvestServiceCycleDiagnosticsBridge(
            _lifecycleValue,
            _initialConfiguration,
            _dependencies.OwnsActionFamily(),
            _dependencies.RuntimeDiagnostics,
            _dependencies.FeatureStatus);
    }

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report)
    {
        _diagnostics?.Observe(pump, in report, _dependencies.OwnsActionFamily());
    }

    public void ObserveConfiguration(SuiteRuntimeConfiguration configuration)
    {
        _diagnostics?.ObserveConfiguration(configuration, _dependencies.OwnsActionFamily());
    }

    public void ObserveLifecycle(long nativeLifecycle, SuiteRuntimeConfiguration configuration)
    {
        _bindings.InvalidateLifecycle();
        _gates.ObserveLifecycle(nativeLifecycle);
        _diagnostics?.ObserveLifecycle(nativeLifecycle, configuration, _dependencies.OwnsActionFamily());
    }

    public void DisposeDiagnostics() => _diagnostics?.Dispose();

    public void DisposeRegistration()
    {
        try { _registration.Dispose(); }
        finally { _bindings.InvalidateLifecycle(); }
    }
}
