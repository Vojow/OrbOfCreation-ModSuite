using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbAutomata;

/// <summary>
/// Spell Leveling's contribution to the one Automata-owned ServiceCycle host. It supplies its typed
/// definition and native adapter and registers them against the shared registry, but never owns the
/// registry, pump, host, or observability.
/// </summary>
internal sealed class SpellLevelServiceCycleFeature : IAutomataServiceCycleFeature
{
    // One action per cycle is all the worker ever plans, so the batch bound is one. It is stated
    // rather than left to a default: a bound of one is what makes "the boundary refused it, look
    // again next generation" the whole of this service's pacing.
    private static readonly ServiceActionDispatchPolicy ActionDispatchPolicy =
        ServiceActionDispatchPolicy.Bounded(1);

    private readonly SpellLevelFeatureDependencies _dependencies;

    public SpellLevelServiceCycleFeature(SpellLevelFeatureDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context)
    {
        var adapters = SpellLevelServiceAdapterComposition.Create(_dependencies);
        var registration = context.Registry.Register(adapters.Definition, ActionDispatchPolicy);
        return new SpellLevelFeatureRuntime(
            _dependencies,
            adapters.Natives,
            registration,
            context.LifecycleValue,
            context.ConfigurationGeneration);
    }

}

/// <summary>
/// The non-generic per-frame runtime for Spell Leveling inside the shared host.
/// </summary>
/// <remarks>
/// Unlike Auto Buy's, this one does invalidate its native adapter on a lifecycle boundary. The adapter
/// caches instance-keyed contracts — the <c>SpellManager</c> singleton, its recipe list, the resolved
/// level-all upgrade — and every one of those is replaced when the game reloads.
/// </remarks>
internal sealed class SpellLevelFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly SpellLevelFeatureDependencies _dependencies;
    private readonly SpellLevelNativeAdapter _natives;
    private readonly ServiceRegistration<
        SpellLevelCycleState,
        SpellLevelCycleAction> _registration;
    private readonly long _lifecycleValue;
    private readonly ConfigGeneration _initialConfigurationGeneration;
    private SpellLevelServiceCycleDiagnosticsBridge? _diagnostics;

    internal SpellLevelFeatureRuntime(
        SpellLevelFeatureDependencies dependencies,
        SpellLevelNativeAdapter natives,
        ServiceRegistration<
            SpellLevelCycleState,
            SpellLevelCycleAction> registration,
        long lifecycleValue,
        ConfigGeneration initialConfigurationGeneration)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _natives = natives ?? throw new ArgumentNullException(nameof(natives));
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _lifecycleValue = lifecycleValue;
        _initialConfigurationGeneration = initialConfigurationGeneration;
    }

    public void ActivateDiagnostics()
    {
        _diagnostics = new SpellLevelServiceCycleDiagnosticsBridge(
            _lifecycleValue,
            _initialConfigurationGeneration,
            _dependencies.OwnsActionFamily(),
            _dependencies.Capability,
            _natives,
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
