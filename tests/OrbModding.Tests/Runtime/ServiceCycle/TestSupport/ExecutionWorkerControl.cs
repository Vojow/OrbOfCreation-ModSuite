using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal sealed class ExecutionWorkerControl
{
    private int _stateFactoryFailures;
    private int _evaluationFaults;
    private int _projectionFaults;
    private int _stateCreateCount;
    private int _stateReleaseCount;
    private int _evaluationCount;

    internal ExecutionWorkerControl(int signalId) => SignalId = signalId;
    internal int SignalId { get; }
    internal int ActionCount;
    internal int PartialActionCountBeforeFault;
    internal WakePolicy EvaluationWake = WakePolicy.Immediate;
    internal ActionPayload? Payload = new(1);
    internal bool MeasureAppendAllocations;
    internal bool DuplicateProjectionKey;
    internal long LastAppendAllocatedBytes;
    internal int LastEvaluationConfig;
    internal int StateCreateCount => Volatile.Read(ref _stateCreateCount);
    internal int StateReleaseCount => Volatile.Read(ref _stateReleaseCount);
    internal int EvaluationCount => Volatile.Read(ref _evaluationCount);

    internal void FailNextEvaluations(int count) => Volatile.Write(ref _evaluationFaults, count);
    internal void FailNextProjections(int count) => Volatile.Write(ref _projectionFaults, count);
    internal void FailNextStateFactories(int count) => Volatile.Write(ref _stateFactoryFailures, count);

    internal ExecutionState CreateState()
    {
        Interlocked.Increment(ref _stateCreateCount);
        if (ConsumeOne(ref _stateFactoryFailures)) throw new InvalidOperationException("synthetic state factory fault");
        return new ExecutionState();
    }

    internal void ReleaseState(ref ExecutionState state)
    {
        Interlocked.Increment(ref _stateReleaseCount);
        state = null!;
    }

    internal WakePolicy Evaluate(
        in ExecutionFrame frame,
        in ExecutionConfig config,
        ref ExecutionState state,
        ServiceActionWriter<ExecutionAction> actions)
    {
        var signals = ExecutionWorkerSignals.Get(SignalId);
        LastEvaluationConfig = config.Value;
        state.Evaluations++;
        Interlocked.Increment(ref _evaluationCount);
        signals.EvaluationEntered?.Set();
        signals.EvaluationRelease?.Wait();
        var shouldFault = ConsumeOne(ref _evaluationFaults);
        var count = shouldFault ? PartialActionCountBeforeFault : ActionCount;
        var before = MeasureAppendAllocations ? GC.GetAllocatedBytesForCurrentThread() : 0;
        var payload = Payload ?? new ActionPayload(-1);
        for (var index = 0; index < count; index++)
        {
            var action = new ExecutionAction(index, payload);
            actions.Add(in action);
        }
        signals.ActionsAppended?.Set();
        signals.ActionsRelease?.Wait();
        if (MeasureAppendAllocations)
            LastAppendAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (shouldFault) throw new InvalidOperationException("synthetic evaluation fault");
        return EvaluationWake;
    }

    internal void Project(in ExecutionState state, ServiceStateProjectionBuilder output)
    {
        output.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(state.Evaluations));
        if (DuplicateProjectionKey)
            output.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(-1));
        if (ConsumeOne(ref _projectionFaults)) throw new InvalidOperationException("synthetic projection fault");
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
}

internal sealed class ExecutionWorkerDefinition :
    IServiceCycleWorkerDefinition<ExecutionFrame, ExecutionConfig, ExecutionState, ExecutionAction>
{
    private readonly ExecutionWorkerControl _control;

    internal ExecutionWorkerDefinition(ExecutionWorkerControl control) => _control = control;

    public ExecutionState CreateState(LifecycleGeneration lifecycle) => _control.CreateState();
    public void ReleaseState(ref ExecutionState state) => _control.ReleaseState(ref state);

    public void ReleaseFrame(ref ExecutionFrame frame)
    {
        frame = null!;
        ExecutionWorkerSignals.Release(_control.SignalId);
    }

    public WakePolicy Evaluate(
        in ExecutionFrame frame,
        in ExecutionConfig config,
        in ServiceCycleContext context,
        ref ExecutionState state,
        ServiceActionWriter<ExecutionAction> actions) =>
        _control.Evaluate(in frame, in config, ref state, actions);

    public void ProjectState(
        in ExecutionState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) => _control.Project(in state, output);
}
