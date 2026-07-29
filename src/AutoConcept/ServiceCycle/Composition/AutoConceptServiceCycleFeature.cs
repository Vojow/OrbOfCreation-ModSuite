using System;
using OrbModding.Common;
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
            context.ConfigurationGeneration);
    }

}

internal sealed class AutoConceptFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly AutoConceptFeatureDependencies _dependencies;
    private readonly AutoConceptNativeAdapter _natives;
    private readonly ServiceRegistration<AutoConceptCycleState, AutoConceptCycleAction> _registration;
    private readonly long _lifecycleValue;
    private readonly ConfigGeneration _initialConfigurationGeneration;
    private AutoConceptServiceCycleDiagnosticsBridge? _diagnostics;

    internal AutoConceptFeatureRuntime(
        AutoConceptFeatureDependencies dependencies,
        AutoConceptNativeAdapter natives,
        ServiceRegistration<AutoConceptCycleState, AutoConceptCycleAction> registration,
        long lifecycleValue,
        ConfigGeneration initialConfigurationGeneration)
    {
        _dependencies = dependencies;
        _natives = natives;
        _registration = registration;
        _lifecycleValue = lifecycleValue;
        _initialConfigurationGeneration = initialConfigurationGeneration;
    }

    public void ActivateDiagnostics()
    {
        _diagnostics = new AutoConceptServiceCycleDiagnosticsBridge(
            _lifecycleValue,
            _initialConfigurationGeneration,
            _dependencies.OwnsActionFamily(),
            _dependencies.FeatureStatus);
    }

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report) =>
        _diagnostics?.Observe(pump, in report, _dependencies.OwnsActionFamily());

    public void ObserveConfiguration(ConfigGeneration configurationGeneration) =>
        _diagnostics?.ObserveConfiguration(configurationGeneration);

    public void ObserveLifecycle(
        long nativeLifecycle,
        ConfigGeneration configurationGeneration)
    {
        _natives.InvalidateLifecycle();
        _diagnostics?.ObserveLifecycle(
            nativeLifecycle,
            configurationGeneration,
            _dependencies.OwnsActionFamily());
    }

    public void DisposeDiagnostics() => _diagnostics = null;

    public void DisposeRegistration()
    {
        _registration.Dispose();
        _natives.Dispose();
    }
}
