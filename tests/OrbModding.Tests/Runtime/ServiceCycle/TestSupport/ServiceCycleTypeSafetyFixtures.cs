using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal static class ServiceCycleTypeSafetyFixtures
{
    internal static void AssertConfigurationRejected<TConfig>(
        ServiceCycleRegistry registry,
        TConfig configuration)
        where TConfig : notnull =>
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new TypeSafetyDefinition<SafeFrame, TConfig, SafeState, ImmutableAction>(
                new SafeFrame(), new SafeState()),
            configuration,
            new LifecycleGeneration(1)));

    internal static void AssertFrameRejected<TFrame>(ServiceCycleRegistry registry, TFrame frame) =>
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new TypeSafetyDefinition<TFrame, ImmutableConfig, SafeState, ImmutableAction>(frame, new SafeState()),
            new ImmutableConfig(1),
            new LifecycleGeneration(1)));
}

internal sealed class TypeSafetyDefinition<TFrame, TConfig, TState, TAction> :
    IServiceCycleDefinition<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    private readonly TFrame _frame;
    private readonly TState _state;

    internal TypeSafetyDefinition(TFrame frame, TState state)
    {
        _frame = frame;
        _state = state;
    }

    internal int FrameCreates { get; private set; }
    public ServiceId ServiceId => new("test.type-safety");
    public WakePolicy DefaultWakePolicy => WakePolicy.Immediate;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(10)),
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
    public TFrame CreateFrame() { FrameCreates++; return _frame; }
    public IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction> CreateWorkerDefinition() =>
        new WorkerDefinition(_state);
    public ServiceStartDecision ShouldStart(in TConfig config, in ServiceCycleStartContext context) =>
        ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
    public ServiceCaptureResult Capture(ref TFrame frame, in TConfig config, in ServiceCaptureContext context) =>
        ServiceCaptureResult.Captured(new StrategyGeneration(1), CommonServiceDecisionCodes.Captured);
    public ServiceActionResult TryExecute(
        in TAction action,
        in TConfig config,
        in ServiceActionContext context) =>
        ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);

    private sealed class WorkerDefinition : IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction>
    {
        private readonly TState _state;
        internal WorkerDefinition(TState state) => _state = state;
        public TState CreateState(LifecycleGeneration lifecycle) => _state;
        public void ReleaseState(ref TState state) { }
        public void ReleaseFrame(ref TFrame frame) { }
        public WakePolicy Evaluate(
            in TFrame frame,
            in TConfig config,
            in ServiceCycleContext context,
            ref TState state,
            ServiceActionWriter<TAction> actions) => WakePolicy.Immediate;
        public void ProjectState(
            in TState state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output) { }
    }
}

internal sealed class SafeFrame { }
internal sealed class SafeState { }
internal sealed class ImmutableConfig
{
    internal ImmutableConfig(int value) => Value = value;
    internal int Value { get; }
}
internal readonly struct ImmutableAction { }
