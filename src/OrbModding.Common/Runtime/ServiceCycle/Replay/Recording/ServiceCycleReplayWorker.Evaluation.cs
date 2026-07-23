using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

public abstract partial class ServiceCycleReplayWorker<
    TFrame,
    TConfig,
    TState,
    TAction,
    TCycleInputRecord,
    TStateRecord,
    TActionRecord>
{
    TState IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction>.CreateState(
        LifecycleGeneration lifecycle) => CreateStateCore(lifecycle);

    void IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction>.ReleaseState(ref TState state) =>
        ReleaseStateCore(ref state);

    void IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction>.ReleaseFrame(ref TFrame frame)
    {
        try
        {
            ReleaseFrameCore(ref frame);
        }
        finally
        {
            _inputBridge?.MarkReleased();
        }
    }

    WakePolicy IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction>.Evaluate(
        in TFrame frame,
        in TConfig config,
        in ServiceCycleContext context,
        ref TState state,
        ServiceActionWriter<TAction> actions)
    {
        var bridge = _inputBridge ?? throw new InvalidOperationException("The replayable worker is not attached.");
        var recorder = _recorder ?? throw new InvalidOperationException("The replayable worker is not attached.");
        recorder.Begin(in context, bridge.TraceServiceKey);
        _pendingActionCount = 0;
        try
        {
            if (bridge.TryTake(in context, out _, out var input))
                recorder.RecordCycleInput(in input);
            else
                recorder.MarkRecordProductionFailed(
                    new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0));

            TryRecordState(recorder, in state, ServiceCycleReplayRecordKind.PreviousState);
            var paired = new ServiceCycleReplayActionWriter<TAction, TActionRecord>(actions, recorder);
            var returnedWake = EvaluateCore(
                in frame,
                in config,
                in context,
                ref state,
                paired);
            _pendingActionCount = actions.Count;
            if (!returnedWake.IsValid)
            {
                recorder.AbortEvaluation(_pendingActionCount);
            }
            else
            {
                var concreteWake = returnedWake.Kind == WakePolicyKind.Default
                    ? _defaultWakePolicy
                    : returnedWake;
                recorder.RecordReturnedWake(concreteWake);
            }
            TryRecordState(recorder, in state, ServiceCycleReplayRecordKind.NextState);
            return returnedWake;
        }
        catch (Exception exception) when (
            exception is not StackOverflowException &&
            !ServiceCycleFatalExceptionPolicy.MustEscape(this, exception))
        {
            _pendingActionCount = actions.Count;
            recorder.AbortEvaluation(_pendingActionCount);
            throw;
        }
    }

    void IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction>.ProjectState(
        in TState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output)
    {
        var recorder = _recorder ?? throw new InvalidOperationException("The replayable worker is not attached.");
        try
        {
            ProjectStateCore(in state, in context, output);
            var exactProjection = output.CaptureSnapshot();
            recorder.SealProvisional(in exactProjection, _pendingActionCount);
        }
        catch (Exception exception) when (
            exception is not StackOverflowException &&
            !ServiceCycleFatalExceptionPolicy.MustEscape(this, exception))
        {
            recorder.AbortProjection(_pendingActionCount);
            throw;
        }
    }

    private void TryRecordState(
        ServiceCycleReplayWorkerRecorder<TCycleInputRecord, TStateRecord, TActionRecord> recorder,
        in TState state,
        ServiceCycleReplayRecordKind kind)
    {
        try
        {
            var record = CreateStateRecordCore(in state);
            if (kind == ServiceCycleReplayRecordKind.PreviousState)
                recorder.RecordPreviousState(in record);
            else
                recorder.RecordNextState(in record);
        }
        catch (Exception exception) when (
            exception is not StackOverflowException &&
            !ServiceCycleFatalExceptionPolicy.MustEscape(this, exception))
        {
            recorder.MarkRecordProductionFailed(new ServiceCycleReplayRecordIdentity(kind, 0));
        }
    }
}
