using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
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
            context.Configuration);
    }

    public void ObserveStartupFailure(
        SuiteRuntimeConfiguration configuration,
        Exception exception)
    {
        if (MentorConfigurationPolicy.IsOperational(configuration))
            _dependencies.FeatureStatus?.Observe(
                true,
                FeatureStatusState.Faulted,
                FeatureStatusReasonCode.RuntimeFailure,
                "Mentor could not initialize its ServiceCycle runtime.");
    }
}

internal sealed class MentorFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly MentorFeatureDependencies _dependencies;
    private readonly MentorNativeAdapter _natives;
    private readonly ServiceRegistration<MentorCycleState, MentorCycleAction> _registration;
    private long _lifecycle;
    private SuiteRuntimeConfiguration _configuration;
    private bool _cycleObserved;

    internal MentorFeatureRuntime(
        MentorFeatureDependencies dependencies,
        MentorNativeAdapter natives,
        ServiceRegistration<MentorCycleState, MentorCycleAction> registration,
        long lifecycle,
        SuiteRuntimeConfiguration configuration)
    {
        _dependencies = dependencies;
        _natives = natives;
        _registration = registration;
        _lifecycle = lifecycle;
        _configuration = configuration;
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

    public void ObserveConfiguration(SuiteRuntimeConfiguration configuration)
    {
        _configuration = configuration;
        Publish();
    }

    public void ObserveLifecycle(
        long nativeLifecycle,
        SuiteRuntimeConfiguration configuration)
    {
        _lifecycle = nativeLifecycle;
        _configuration = configuration;
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
        var configured = MentorConfigurationPolicy.IsOperational(_configuration);
        _dependencies.FeatureStatus?.ObserveLifecycle(
            configured,
            !configured
                ? FeatureStatusState.ConfigurationDisabled
                : _cycleObserved
                    ? FeatureStatusState.Operational
                    : FeatureStatusState.NotReady,
            !configured
                ? FeatureStatusReasonCode.ConfigurationDisabled
                : _cycleObserved
                    ? FeatureStatusReasonCode.None
                    : FeatureStatusReasonCode.Initializing,
            !configured
                ? "Mentor mode is disabled in configuration."
                : _cycleObserved
                    ? string.Empty
                    : "Mentor is waiting for its first service cycle.",
            _lifecycle);
    }
}
