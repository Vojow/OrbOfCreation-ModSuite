using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal sealed class LifecycleFrame
{
    internal LifecycleFrame(int serial) => Serial = serial;
    public int Serial { get; }
    public ulong CapturedLifecycle { get; internal set; }
}

internal sealed class LifecycleConfig
{
    internal LifecycleConfig(int value) => Value = value;
    public int Value { get; }
}

internal sealed class LifecycleState
{
    internal LifecycleState(int serial, ulong lifecycle)
    {
        Serial = serial;
        Lifecycle = lifecycle;
    }

    public int Serial { get; }
    public ulong Lifecycle { get; }
    public int Evaluations { get; internal set; }
}

internal readonly struct LifecycleAction
{
    internal LifecycleAction(int index, ulong lifecycle, LifecyclePayload payload)
    {
        Index = index;
        Lifecycle = lifecycle;
        Payload = payload;
    }

    public int Index { get; }
    public ulong Lifecycle { get; }
    public LifecyclePayload Payload { get; }
}

internal sealed class LifecyclePayload
{
    internal LifecyclePayload(int value) => Value = value;
    public int Value { get; }
}

/// <summary>Generation-safe fixture: every worker definition, frame, and state has a unique serial.</summary>
internal sealed class LifecycleServiceDefinition :
    IServiceCycleDefinition<LifecycleFrame, LifecycleConfig, LifecycleState, LifecycleAction>
{
    private readonly ConcurrentDictionary<ulong, int> _executionCounts = new();
    private readonly List<object> _workerDefinitions = new();
    private readonly List<LifecycleFrame> _frames = new();
    private readonly object _identityGate = new();
    private int _nextWorkerSerial;
    private int _nextFrameSerial;
    private int _workerFactoryFailures;
    private int _frameFactoryFailures;
    private readonly int _runtimeId;

    internal LifecycleServiceDefinition(string id)
    {
        ServiceId = new ServiceId(id);
        _runtimeId = LifecycleWorkerFixtureRuntime.Register();
    }

    public ServiceId ServiceId { get; }
    public WakePolicy DefaultWakePolicy { get; set; } = WakePolicy.Immediate;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; set; } = new(
        new MonotonicDuration(10), new MonotonicDuration(80));
    internal int ActionCount { get; set; } = 1;
    internal int RejectAtIndex { get; set; } = -1;
    internal int FaultAtIndex { get; set; } = -1;
    internal bool ReuseFrame { get; set; }
    internal bool ReuseWorkerDefinition { get; set; }
    internal LifecycleFrame? SharedFrame { get; set; }
    internal LifecycleState? SharedState { get; set; }
    internal IServiceCycleWorkerDefinition<LifecycleFrame, LifecycleConfig, LifecycleState, LifecycleAction>?
        SharedWorkerDefinition { get; set; }
    internal LifecycleFrame? CaptureReplacementFrame { get; set; }
    internal StateReleaseGate? StateReleaseGate { get; set; }
    internal Action? WorkerDefinitionFactoryCallback { get; set; }
    internal Action? FrameFactoryCallback { get; set; }
    internal Action? ShouldStartCallback { get; set; }
    internal Action? CaptureCallback { get; set; }
    internal Action? ActionCallback { get; set; }
    internal int WorkerDefinitionCreateCount => Volatile.Read(ref _nextWorkerSerial);
    internal int FrameCreateCount => Volatile.Read(ref _nextFrameSerial);
    internal int FrameReleaseCount => LifecycleWorkerFixtureRuntime.FrameReleaseCount(_runtimeId);
    internal int ActionExecutionCount => Sum(_executionCounts);
    internal bool IsPayloadAlive(ulong lifecycle) =>
        LifecycleWorkerFixtureRuntime.IsPayloadAlive(_runtimeId, lifecycle);

    internal void FailNextWorkerFactories(int count) => Volatile.Write(ref _workerFactoryFailures, count);
    internal void FailNextFrameFactories(int count) => Volatile.Write(ref _frameFactoryFailures, count);

    internal EvaluationGate BlockEvaluation(ulong lifecycle)
    {
        var gate = new EvaluationGate();
        if (!LifecycleWorkerFixtureRuntime.TryAddGate(_runtimeId, lifecycle, gate))
            throw new InvalidOperationException("The lifecycle already has an evaluation gate.");
        return gate;
    }

    internal int EvaluationCount(ulong lifecycle) =>
        LifecycleWorkerFixtureRuntime.EvaluationCount(_runtimeId, lifecycle);
    internal int ExecutionCount(ulong lifecycle) =>
        _executionCounts.TryGetValue(lifecycle, out var count) ? count : 0;
    internal int StateSerial(ulong lifecycle) =>
        LifecycleWorkerFixtureRuntime.StateSerial(_runtimeId, lifecycle);

    public LifecycleFrame CreateFrame()
    {
        var serial = Interlocked.Increment(ref _nextFrameSerial);
        FrameFactoryCallback?.Invoke();
        if (ConsumeOne(ref _frameFactoryFailures))
            throw new InvalidOperationException("synthetic frame construction failure");
        lock (_identityGate)
        {
            if (SharedFrame is not null) return SharedFrame;
            if (ReuseFrame && _frames.Count != 0) return _frames[0];
            var frame = new LifecycleFrame(serial);
            _frames.Add(frame);
            return frame;
        }
    }

    public IServiceCycleWorkerDefinition<LifecycleFrame, LifecycleConfig, LifecycleState, LifecycleAction>
        CreateWorkerDefinition()
    {
        var serial = Interlocked.Increment(ref _nextWorkerSerial);
        WorkerDefinitionFactoryCallback?.Invoke();
        if (ConsumeOne(ref _workerFactoryFailures))
            throw new InvalidOperationException("synthetic worker-definition construction failure");
        lock (_identityGate)
        {
            if (SharedWorkerDefinition is not null) return SharedWorkerDefinition;
            if (ReuseWorkerDefinition && _workerDefinitions.Count != 0)
            {
                return (IServiceCycleWorkerDefinition<
                    LifecycleFrame, LifecycleConfig, LifecycleState, LifecycleAction>)_workerDefinitions[0];
            }
            var worker = new WorkerDefinition(
                _runtimeId,
                serial,
                ActionCount,
                SharedState);
            if (StateReleaseGate is not null)
                LifecycleWorkerFixtureRuntime.SetStateReleaseGate(_runtimeId, StateReleaseGate);
            _workerDefinitions.Add(worker);
            return worker;
        }
    }

    public ServiceStartDecision ShouldStart(in LifecycleConfig config, in ServiceCycleStartContext context)
    {
        ShouldStartCallback?.Invoke();
        return ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
    }

    public ServiceCaptureResult Capture(
        ref LifecycleFrame frame,
        in LifecycleConfig config,
        in ServiceCaptureContext context)
    {
        CaptureCallback?.Invoke();
        if (CaptureReplacementFrame is not null) frame = CaptureReplacementFrame;
        frame.CapturedLifecycle = context.Lifecycle.Value;
        return ServiceCaptureResult.Captured(new StrategyGeneration(1), CommonServiceDecisionCodes.Captured);
    }

    public ServiceActionResult TryExecute(
        in LifecycleAction action,
        in LifecycleConfig config,
        in ServiceActionContext context)
    {
        ActionCallback?.Invoke();
        if (action.Lifecycle != context.Cycle.Lifecycle.Value)
            throw new InvalidOperationException("A stale action reached the native adapter fixture.");
        _executionCounts.AddOrUpdate(action.Lifecycle, 1, static (_, count) => count + 1);
        if (action.Index == RejectAtIndex)
            return ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
        if (action.Index == FaultAtIndex)
        {
            return ServiceActionResult.Faulted(
                CommonActionResultCodes.AdapterFault,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.PostconditionFailed,
                    new NativeMutationCallOutcome(1, 1, 0)));
        }
        return ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(1, 1, 1)));
    }

    private static int Sum(ConcurrentDictionary<ulong, int> values)
    {
        var total = 0;
        foreach (var pair in values) total += pair.Value;
        return total;
    }

    private static bool ConsumeOne(ref int count)
    {
        while (true)
        {
            var current = Volatile.Read(ref count);
            if (current <= 0) return false;
            if (Interlocked.CompareExchange(ref count, current - 1, current) == current) return true;
        }
    }

    private sealed class WorkerDefinition :
        IServiceCycleWorkerDefinition<LifecycleFrame, LifecycleConfig, LifecycleState, LifecycleAction>
    {
        private readonly int _serial;
        private readonly int _actionCount;
        private readonly int _runtimeId;
        private readonly LifecycleState? _sharedState;

        internal WorkerDefinition(
            int runtimeId,
            int serial,
            int actionCount,
            LifecycleState? sharedState)
        {
            _runtimeId = runtimeId;
            _serial = serial;
            _actionCount = actionCount;
            _sharedState = sharedState;
        }

        public LifecycleState CreateState(LifecycleGeneration lifecycle)
        {
            var state = _sharedState ?? new LifecycleState(_serial, lifecycle.Value);
            LifecycleWorkerFixtureRuntime.RecordState(_runtimeId, lifecycle.Value, _serial);
            return state;
        }

        public void ReleaseState(ref LifecycleState state)
        {
            if (LifecycleWorkerFixtureRuntime.TryGetStateReleaseGate(
                    _runtimeId, out var stateReleaseGate))
            {
                stateReleaseGate.Entered.Set();
                stateReleaseGate.Release.Wait();
            }
            state = null!;
        }
        public void ReleaseFrame(ref LifecycleFrame frame)
        {
            LifecycleWorkerFixtureRuntime.RecordFrameRelease(_runtimeId);
            frame = null!;
        }

        public WakePolicy Evaluate(
            in LifecycleFrame frame,
            in LifecycleConfig config,
            in ServiceCycleContext context,
            ref LifecycleState state,
            ServiceActionWriter<LifecycleAction> actions)
        {
            if (frame.CapturedLifecycle != context.Identity.Lifecycle.Value ||
                (_sharedState is null && state.Lifecycle != context.Identity.Lifecycle.Value))
                throw new InvalidOperationException("Generation stamps diverged in the evaluator fixture.");
            state.Evaluations++;
            LifecycleWorkerFixtureRuntime.RecordEvaluation(_runtimeId, context.Identity.Lifecycle.Value);
            if (LifecycleWorkerFixtureRuntime.TryGetGate(
                    _runtimeId, context.Identity.Lifecycle.Value, out var gate))
            {
                gate.Entered.Set();
                gate.Release.Wait();
            }
            var payload = new LifecyclePayload(_serial);
            LifecycleWorkerFixtureRuntime.RecordPayload(
                _runtimeId,
                context.Identity.Lifecycle.Value,
                payload);
            for (var index = 0; index < _actionCount; index++)
            {
                var action = new LifecycleAction(
                    index,
                    context.Identity.Lifecycle.Value,
                    payload);
                actions.Add(in action);
            }
            return WakePolicy.Immediate;
        }

        public void ProjectState(
            in LifecycleState state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output)
        {
            output.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(state.Serial));
            output.Add(new ServiceProjectionKey(2), ServiceProjectionValue.FromInteger(state.Evaluations));
        }
    }
}

internal sealed class StateReleaseGate : IDisposable
{
    internal ManualResetEventSlim Entered { get; } = new(false);
    internal ManualResetEventSlim Release { get; } = new(false);

    public void Dispose()
    {
        Release.Set();
        Entered.Dispose();
        Release.Dispose();
    }
}

internal sealed class EvaluationGate : IDisposable
{
    internal ManualResetEventSlim Entered { get; } = new(false);
    internal ManualResetEventSlim Release { get; } = new(false);

    public void Dispose()
    {
        Release.Set();
        Entered.Dispose();
        Release.Dispose();
    }
}

internal static class LifecycleWorkerFixtureRuntime
{
    private static readonly ConcurrentDictionary<int, RuntimeData> Instances = new();
    private static int _nextId;

    internal static int Register()
    {
        var id = Interlocked.Increment(ref _nextId);
        if (!Instances.TryAdd(id, new RuntimeData()))
            throw new InvalidOperationException("Duplicate lifecycle fixture runtime id.");
        return id;
    }

    internal static bool TryAddGate(int id, ulong lifecycle, EvaluationGate gate) =>
        Get(id).Gates.TryAdd(lifecycle, gate);
    internal static bool TryGetGate(int id, ulong lifecycle, out EvaluationGate gate) =>
        Get(id).Gates.TryGetValue(lifecycle, out gate!);
    internal static void RecordEvaluation(int id, ulong lifecycle) =>
        Get(id).EvaluationCounts.AddOrUpdate(lifecycle, 1, static (_, count) => count + 1);
    internal static int EvaluationCount(int id, ulong lifecycle) =>
        Get(id).EvaluationCounts.TryGetValue(lifecycle, out var count) ? count : 0;
    internal static void RecordState(int id, ulong lifecycle, int serial) =>
        Get(id).StateSerials[lifecycle] = serial;
    internal static int StateSerial(int id, ulong lifecycle) =>
        Get(id).StateSerials.TryGetValue(lifecycle, out var serial) ? serial : 0;
    internal static void RecordPayload(int id, ulong lifecycle, LifecyclePayload payload) =>
        Get(id).Payloads[lifecycle] = new WeakReference<LifecyclePayload>(payload);
    internal static void RecordFrameRelease(int id) =>
        Interlocked.Increment(ref Get(id).FrameReleaseCount);
    internal static int FrameReleaseCount(int id) => Volatile.Read(ref Get(id).FrameReleaseCount);
    internal static bool IsPayloadAlive(int id, ulong lifecycle) =>
        Get(id).Payloads.TryGetValue(lifecycle, out var reference) &&
        reference.TryGetTarget(out _);
    internal static void SetStateReleaseGate(int id, StateReleaseGate gate) =>
        Get(id).StateReleaseGate = gate;
    internal static bool TryGetStateReleaseGate(int id, out StateReleaseGate gate)
    {
        gate = Get(id).StateReleaseGate!;
        return gate is not null;
    }

    private static RuntimeData Get(int id) =>
        Instances.TryGetValue(id, out var data)
            ? data
            : throw new InvalidOperationException("Unknown lifecycle fixture runtime id.");

    private sealed class RuntimeData
    {
        internal ConcurrentDictionary<ulong, EvaluationGate> Gates { get; } = new();
        internal ConcurrentDictionary<ulong, int> EvaluationCounts { get; } = new();
        internal ConcurrentDictionary<ulong, int> StateSerials { get; } = new();
        internal ConcurrentDictionary<ulong, WeakReference<LifecyclePayload>> Payloads { get; } = new();
        internal StateReleaseGate? StateReleaseGate;
        internal int FrameReleaseCount;
    }
}
