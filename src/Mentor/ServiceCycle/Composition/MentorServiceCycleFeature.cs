using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbMentor;

internal sealed class MentorServiceCycleFeature : IAutomataServiceCycleFeature
{
    private readonly MentorFeatureDependencies _dependencies;

    internal MentorServiceCycleFeature(MentorFeatureDependencies dependencies) =>
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public IAutomataServiceCycleFeatureRuntime Register(
        in AutomataServiceCycleFeatureContext context)
    {
        var adapters = MentorServiceAdapterComposition.Create(_dependencies);
        var registration = context.Registry.Register(
            adapters.Definition,
            ServiceActionDispatchPolicy.Single);
        return new MentorFeatureRuntime(
            _dependencies,
            adapters.Natives,
            registration,
            context.LifecycleValue,
            context.ConfigurationGeneration);
    }

}

internal sealed class MentorFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly MentorFeatureDependencies _dependencies;
    private readonly MentorNativeAdapter _natives;
    private readonly ServiceRegistration<MentorCycleState, MentorCycleAction> _registration;
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _cycleObserved;

    internal MentorFeatureRuntime(
        MentorFeatureDependencies dependencies,
        MentorNativeAdapter natives,
        ServiceRegistration<MentorCycleState, MentorCycleAction> registration,
        long lifecycle,
        ConfigGeneration configurationGeneration)
    {
        _dependencies = dependencies;
        _natives = natives;
        _registration = registration;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
    }

    public void ActivateDiagnostics() => Publish();

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report)
    {
        if (!_cycleObserved && report.ResponsesAcquired > 0)
        {
            _cycleObserved = true;
            Publish();
        }
    }

    public void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _configurationGeneration = configurationGeneration;
        _cycleObserved = false;
    }

    public void ObserveLifecycle(
        long nativeLifecycle,
        ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _lifecycle = nativeLifecycle;
        _configurationGeneration = configurationGeneration;
        _cycleObserved = false;
        _natives.InvalidateLifecycle();
        Publish();
    }

    public void DisposeDiagnostics()
    {
    }

    public void DisposeRegistration()
    {
        _registration.Dispose();
        _natives.Dispose();
    }

    private void Publish()
    {
        _dependencies.FeatureStatus.ObserveRuntimeLifecycle(
            _cycleObserved
                    ? FeatureStatusState.Operational
                    : FeatureStatusState.NotReady,
            _cycleObserved
                    ? FeatureStatusReasonCode.None
                    : FeatureStatusReasonCode.Initializing,
            _cycleObserved
                    ? string.Empty
                    : "Mentor is waiting for its first service cycle.",
            _lifecycle,
            _configurationGeneration);
    }
}
