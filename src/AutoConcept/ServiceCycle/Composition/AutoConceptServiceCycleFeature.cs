using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbAutomata;

internal sealed class AutoConceptServiceCycleFeature : IAutomataServiceCycleFeature
{
    private static readonly ServiceActionDispatchPolicy ActionDispatchPolicy =
        ServiceActionDispatchPolicy.Bounded(1);

    private readonly AutoConceptFeatureDependencies _dependencies;

    public AutoConceptServiceCycleFeature(AutoConceptFeatureDependencies dependencies) =>
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context)
    {
        var adapters = AutoConceptServiceAdapterComposition.Create(_dependencies);
        var registration = context.Registry.Register(adapters.Definition, ActionDispatchPolicy);
        return new AutoConceptFeatureRuntime(
            _dependencies,
            adapters.Natives,
            registration,
            context.LifecycleValue,
            context.Configuration);
    }

    public void ObserveStartupFailure(SuiteRuntimeConfiguration configuration, Exception exception)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (AutoConceptConfigurationPolicy.IsOperational(configuration))
        {
            _dependencies.FeatureStatus?.Observe(
                true,
                FeatureStatusState.Faulted,
                FeatureStatusReasonCode.RuntimeFailure,
                "Auto Concept could not initialize its ServiceCycle runtime.");
        }
    }
}

internal sealed class AutoConceptFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly AutoConceptFeatureDependencies _dependencies;
    private readonly AutoConceptNativeAdapter _natives;
    private readonly ServiceRegistration<AutoConceptCycleState, AutoConceptCycleAction> _registration;
    private readonly long _lifecycleValue;
    private readonly SuiteRuntimeConfiguration _initialConfiguration;
    private AutoConceptServiceCycleDiagnosticsBridge? _diagnostics;

    internal AutoConceptFeatureRuntime(
        AutoConceptFeatureDependencies dependencies,
        AutoConceptNativeAdapter natives,
        ServiceRegistration<AutoConceptCycleState, AutoConceptCycleAction> registration,
        long lifecycleValue,
        SuiteRuntimeConfiguration initialConfiguration)
    {
        _dependencies = dependencies;
        _natives = natives;
        _registration = registration;
        _lifecycleValue = lifecycleValue;
        _initialConfiguration = initialConfiguration;
    }

    public void ActivateDiagnostics()
    {
        _diagnostics = new AutoConceptServiceCycleDiagnosticsBridge(
            _lifecycleValue,
            _initialConfiguration,
            _dependencies.OwnsActionFamily(),
            _dependencies.FeatureStatus);
    }

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report) =>
        _diagnostics?.Observe(pump, in report, _dependencies.OwnsActionFamily());

    public void ObserveConfiguration(SuiteRuntimeConfiguration configuration) =>
        _diagnostics?.ObserveConfiguration(configuration, _dependencies.OwnsActionFamily());

    public void ObserveLifecycle(long nativeLifecycle, SuiteRuntimeConfiguration configuration)
    {
        _natives.InvalidateLifecycle();
        _diagnostics?.ObserveLifecycle(
            nativeLifecycle, configuration, _dependencies.OwnsActionFamily());
    }

    public void DisposeDiagnostics() => _diagnostics = null;

    public void DisposeRegistration()
    {
        _registration.Dispose();
        _natives.Dispose();
    }
}
