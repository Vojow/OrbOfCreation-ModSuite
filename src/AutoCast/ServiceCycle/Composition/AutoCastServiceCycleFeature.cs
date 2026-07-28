using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbAutomata;

/// <summary>
/// Auto Cast's contribution to the one Automata-owned ServiceCycle host. It supplies its typed
/// definition and native adapter and registers them against the shared registry, but never owns the
/// registry, pump, host, or observability.
/// </summary>
internal sealed class AutoCastServiceCycleFeature : IAutomataServiceCycleFeature
{
    // One action per cycle is all the worker ever plans, so the batch bound is one. It is stated
    // rather than left to a default: casting is the game's most conspicuous mutation, and a bound of
    // one is what guarantees a single generation can never fire twice.
    private static readonly ServiceActionDispatchPolicy ActionDispatchPolicy =
        ServiceActionDispatchPolicy.Bounded(1);

    private readonly AutoCastFeatureDependencies _dependencies;

    public AutoCastServiceCycleFeature(AutoCastFeatureDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context)
    {
        var adapters = AutoCastServiceAdapterComposition.Create(_dependencies);
        var registration = context.Registry.Register(adapters.Definition, ActionDispatchPolicy);
        return new AutoCastFeatureRuntime(
            _dependencies,
            adapters.Natives,
            registration,
            context.LifecycleValue,
            context.Configuration);
    }

    public void ObserveStartupFailure(SuiteRuntimeConfiguration configuration, Exception exception)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (AutoCastConfigurationPolicy.IsOperational(configuration))
        {
            _dependencies.FeatureStatus?.Observe(
                true,
                FeatureStatusState.Faulted,
                FeatureStatusReasonCode.RuntimeFailure,
                "Auto Cast could not initialize its ServiceCycle runtime.");
        }
    }
}

/// <summary>
/// The non-generic per-frame runtime for Auto Cast inside the shared host.
/// </summary>
/// <remarks>
/// The lifecycle boundary drops two things, and both have to go together. The native adapter caches
/// instance-keyed contracts — the <c>SpellManager</c> singleton and its equipped loadout, both
/// replaced when the game reloads — and the manual pause is a fact about a run of the game that just
/// ended. Keeping either would let the new run inherit the old one's answers.
/// </remarks>
internal sealed class AutoCastFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly AutoCastFeatureDependencies _dependencies;
    private readonly AutoCastNativeAdapter _natives;
    private readonly ServiceRegistration<
        AutoCastCycleState,
        AutoCastCycleAction> _registration;
    private readonly long _lifecycleValue;
    private readonly SuiteRuntimeConfiguration _initialConfiguration;
    private AutoCastServiceCycleDiagnosticsBridge? _diagnostics;

    internal AutoCastFeatureRuntime(
        AutoCastFeatureDependencies dependencies,
        AutoCastNativeAdapter natives,
        ServiceRegistration<
            AutoCastCycleState,
            AutoCastCycleAction> registration,
        long lifecycleValue,
        SuiteRuntimeConfiguration initialConfiguration)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _natives = natives ?? throw new ArgumentNullException(nameof(natives));
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _lifecycleValue = lifecycleValue;
        _initialConfiguration = initialConfiguration ?? throw new ArgumentNullException(nameof(initialConfiguration));
    }

    public void ActivateDiagnostics()
    {
        _diagnostics = new AutoCastServiceCycleDiagnosticsBridge(
            _lifecycleValue,
            _initialConfiguration,
            _dependencies.OwnsActionFamily(),
            _dependencies.ManualPause,
            _dependencies.FeatureStatus);
    }

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report) =>
        _diagnostics?.Observe(pump, in report, _dependencies.OwnsActionFamily());

    public void ObserveConfiguration(SuiteRuntimeConfiguration configuration) =>
        _diagnostics?.ObserveConfiguration(configuration, _dependencies.OwnsActionFamily());

    public void ObserveLifecycle(long nativeLifecycle, SuiteRuntimeConfiguration configuration)
    {
        _natives.InvalidateLifecycle();
        _dependencies.ManualPause.Reset();
        _diagnostics?.ObserveLifecycle(nativeLifecycle, configuration, _dependencies.OwnsActionFamily());
    }

    public void DisposeDiagnostics() => _diagnostics = null;

    public void DisposeRegistration()
    {
        _registration.Dispose();
        _natives.Dispose();
    }
}
