using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

/// <summary>
/// Exact feature evaluator/state/projector port shared by production recording and offline replay
/// execution. It remains over the original gameplay types plus detached state/action records.
/// </summary>
public interface IServiceCycleReplayEvaluatorPort<
    TFrame,
    TConfig,
    TState,
    TAction,
    TStateRecord,
    TActionRecord>
    where TConfig : notnull
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    TState CreateState(LifecycleGeneration lifecycle);
    void ReleaseState(ref TState state);
    void ReleaseFrame(ref TFrame frame);
    TStateRecord CreateStateRecord(in TState state);
    WakePolicy Evaluate(
        in TFrame frame,
        in TConfig config,
        in ServiceCycleContext context,
        ref TState state,
        ServiceCycleReplayActionWriter<TAction, TActionRecord> actions);
    void ProjectState(
        in TState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output);
}

/// <summary>
/// Non-escapable paired writer for an opt-in replayable evaluator. The gameplay action is appended first;
/// only a successful append offers the detached record with that append's actual index.
/// </summary>
public readonly ref struct ServiceCycleReplayActionWriter<TAction, TActionRecord>
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    private readonly ServiceActionWriter<TAction> _gameplay;
    private readonly IServiceCycleReplayActionSink<TActionRecord> _replay;

    internal ServiceCycleReplayActionWriter(
        ServiceActionWriter<TAction> gameplay,
        IServiceCycleReplayActionSink<TActionRecord> replay)
    {
        _gameplay = gameplay;
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
    }

    public int Count => _gameplay.Count;

    public void Add(in TAction action, in TActionRecord record)
    {
        var actualIndex = _gameplay.Count;
        _gameplay.Add(in action);
        _replay.Offer(in record, actualIndex);
    }
}
