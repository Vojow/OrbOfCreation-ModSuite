using System;
using System.Collections.Concurrent;
using System.Threading;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using RuntimeLifecycleGeneration = OrbModding.Common.Runtime.LifecycleGeneration;
using RuntimeStrategyGeneration = OrbModding.Common.Runtime.StrategyGeneration;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal sealed class SyntheticFrame
{
    public int StrategyValue { get; internal set; }
}

internal readonly struct SyntheticConfig
{
    public SyntheticConfig(int value) => Value = value;
    public int Value { get; }
}

internal sealed class SyntheticState
{
    public int Evaluations { get; internal set; }
}

internal readonly struct SyntheticAction
{
    public SyntheticAction(int value) => Value = value;
    public int Value { get; }
}

internal sealed class SyntheticServiceDefinition :
    IServiceCycleDefinition<SyntheticFrame, SyntheticConfig, SyntheticState, SyntheticAction>
{
    private readonly SyntheticWorkerControl _worker;
    private readonly ManualResetEventSlim _resourcesReleased = new(false);
    internal SyntheticServiceDefinition(string serviceId)
    {
        ServiceId = new ServiceId(serviceId);
        _worker = new SyntheticWorkerControl(SyntheticReleaseSignals.Register(_resourcesReleased));
    }

    public ServiceId ServiceId { get; }
    public WakePolicy DefaultWakePolicy { get; set; } = WakePolicy.Immediate;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; set; } = new(
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(10)),
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
    public bool ThrowFromStateFactory { get => _worker.ThrowFromStateFactory; set => _worker.ThrowFromStateFactory = value; }
    public bool ReturnNullState { get => _worker.ReturnNullState; set => _worker.ReturnNullState = value; }
    public bool ThrowFromWorkerFactory { get; set; }
    public bool ReturnNullFrame { get; set; }
    public bool ThrowFromStateRelease { get => _worker.ThrowFromStateRelease; set => _worker.ThrowFromStateRelease = value; }
    public bool ThrowFromFrameRelease { get => _worker.ThrowFromFrameRelease; set => _worker.ThrowFromFrameRelease = value; }
    public int FrameCreateCount { get; private set; }
    public int StateCreateCount => _worker.StateCreateCount;
    public int FrameReleaseCount => _worker.FrameReleaseCount;
    public int StateReleaseCount => _worker.StateReleaseCount;
    public ManualResetEventSlim ResourcesReleased => _resourcesReleased;

    public SyntheticFrame CreateFrame()
    {
        FrameCreateCount++;
        return ReturnNullFrame ? null! : new SyntheticFrame();
    }

    public IServiceCycleWorkerDefinition<SyntheticFrame, SyntheticConfig, SyntheticState, SyntheticAction>
        CreateWorkerDefinition()
    {
        if (ThrowFromWorkerFactory) throw new InvalidOperationException("synthetic worker construction failure");
        return new WorkerDefinition(_worker);
    }

    public ServiceStartDecision ShouldStart(
        in SyntheticConfig config,
        in ServiceCycleStartContext context) =>
        ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);

    public ServiceCaptureResult Capture(
        ref SyntheticFrame frame,
        in SyntheticConfig config,
        in ServiceCaptureContext context) =>
        ServiceCaptureResult.Captured(
            new RuntimeStrategyGeneration(1),
            CommonServiceDecisionCodes.Captured);


    public ServiceActionResult TryExecute(
        in SyntheticAction action,
        in SyntheticConfig config,
        in ServiceActionContext context) =>
        ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(1, 1, 1)));

    private sealed class WorkerDefinition :
        IServiceCycleWorkerDefinition<SyntheticFrame, SyntheticConfig, SyntheticState, SyntheticAction>
    {
        private readonly SyntheticWorkerControl _control;
        internal WorkerDefinition(SyntheticWorkerControl control) => _control = control;
        public SyntheticState CreateState(RuntimeLifecycleGeneration lifecycle) => _control.CreateState();
        public void ReleaseState(ref SyntheticState state) => _control.ReleaseState(ref state);
        public void ReleaseFrame(ref SyntheticFrame frame) => _control.ReleaseFrame(ref frame);
        public WakePolicy Evaluate(
            in SyntheticFrame frame,
            in SyntheticConfig config,
            in ServiceCycleContext context,
            ref SyntheticState state,
            ServiceActionWriter<SyntheticAction> actions) =>
            _control.Evaluate(ref state);
        public void ProjectState(
            in SyntheticState state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output) { }
    }
}

internal sealed class SyntheticWorkerControl
{
    internal SyntheticWorkerControl(int releaseSignalId) => ReleaseSignalId = releaseSignalId;
    internal int ReleaseSignalId { get; }
    internal bool ThrowFromStateFactory;
    internal bool ReturnNullState;
    internal bool ThrowFromStateRelease;
    internal bool ThrowFromFrameRelease;
    internal int StateCreateCount;
    internal int FrameReleaseCount;
    internal int StateReleaseCount;

    internal SyntheticState CreateState()
    {
        StateCreateCount++;
        if (ThrowFromStateFactory) throw new InvalidOperationException("synthetic state construction failure");
        return ReturnNullState ? null! : new SyntheticState();
    }
    internal void ReleaseState(ref SyntheticState state)
    {
        StateReleaseCount++;
        if (ThrowFromStateRelease) throw new InvalidOperationException("synthetic state release failure");
        state = null!;
    }
    internal void ReleaseFrame(ref SyntheticFrame frame)
    {
        FrameReleaseCount++;
        try
        {
            if (ThrowFromFrameRelease) throw new InvalidOperationException("synthetic frame release failure");
            frame = null!;
        }
        finally { SyntheticReleaseSignals.Signal(ReleaseSignalId); }
    }
    internal WakePolicy Evaluate(ref SyntheticState state)
    {
        state.Evaluations++;
        return WakePolicy.Immediate;
    }
}

internal static class SyntheticReleaseSignals
{
    private static readonly ConcurrentDictionary<int, ManualResetEventSlim> Signals = new();
    private static int _nextId;
    internal static int Register(ManualResetEventSlim signal)
    {
        var id = Interlocked.Increment(ref _nextId);
        if (!Signals.TryAdd(id, signal)) throw new InvalidOperationException("Duplicate test signal id.");
        return id;
    }
    internal static void Signal(int id)
    {
        if (Signals.TryRemove(id, out var signal)) signal.Set();
    }
}
