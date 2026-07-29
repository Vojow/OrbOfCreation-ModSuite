using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbAutomata;

internal sealed class AutoAgromancyServiceCycleFeature : IAutomataServiceCycleFeature
{
    private static readonly ServiceActionDispatchPolicy DispatchPolicy =
        ServiceActionDispatchPolicy.Bounded(1);
    private readonly AutoAgromancyFeatureDependencies _dependencies;

    internal AutoAgromancyServiceCycleFeature(
        AutoAgromancyFeatureDependencies dependencies) =>
        _dependencies = dependencies ??
            throw new ArgumentNullException(nameof(dependencies));

    public IAutomataServiceCycleFeatureRuntime Register(
        in AutomataServiceCycleFeatureContext context)
    {
        var adapters = AutoAgromancyServiceAdapterComposition.Create(_dependencies);
        var registration = context.Registry.Register(
            adapters.Definition,
            DispatchPolicy);
        return new AutoAgromancyFeatureRuntime(
            _dependencies,
            adapters.Actions,
            registration,
            context.LifecycleValue,
            context.ConfigurationGeneration);
    }
}

internal sealed class AutoAgromancyFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly AutoAgromancyFeatureDependencies _dependencies;
    private readonly AutoAgromancyCycleActionAdapter _actions;
    private readonly ServiceRegistration<
        AutoAgromancyCycleState,
        AutoAgromancyCycleAction> _registration;
    private readonly long _lifecycle;
    private readonly ConfigGeneration _configurationGeneration;
    private AutoAgromancyServiceCycleDiagnosticsBridge? _diagnostics;

    internal AutoAgromancyFeatureRuntime(
        AutoAgromancyFeatureDependencies dependencies,
        AutoAgromancyCycleActionAdapter actions,
        ServiceRegistration<AutoAgromancyCycleState, AutoAgromancyCycleAction> registration,
        long lifecycle,
        ConfigGeneration configurationGeneration)
    {
        _dependencies = dependencies;
        _actions = actions;
        _registration = registration;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
    }

    public void ActivateDiagnostics() =>
        _diagnostics = new AutoAgromancyServiceCycleDiagnosticsBridge(
            _lifecycle,
            _configurationGeneration,
            _dependencies.OwnsActionFamily(),
            _dependencies.FeatureStatus);

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report) =>
        _diagnostics?.Observe(pump, in report, _dependencies.OwnsActionFamily());

    public void ObserveConfiguration(ConfigGeneration configurationGeneration) =>
        _diagnostics?.ObserveConfiguration(configurationGeneration);

    public void ObserveLifecycle(
        long nativeLifecycle,
        ConfigGeneration configurationGeneration)
    {
        _actions.InvalidateLifecycle();
        _diagnostics?.ObserveLifecycle(
            nativeLifecycle,
            configurationGeneration,
            _dependencies.OwnsActionFamily());
    }

    public void DisposeDiagnostics() => _diagnostics = null;
    public void DisposeRegistration() => _registration.Dispose();
}
