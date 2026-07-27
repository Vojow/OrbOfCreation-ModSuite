using System;
using System.Collections.Concurrent;
using System.Threading;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal sealed class ExecutionWorkerSignals
{
    private static readonly ConcurrentDictionary<int, ExecutionWorkerSignals> Signals = new();
    private static int _nextId;

    private ExecutionWorkerSignals(int id) => Id = id;

    internal int Id { get; }
    internal ManualResetEventSlim? EvaluationEntered;
    internal ManualResetEventSlim? EvaluationRelease;
    internal ManualResetEventSlim? ActionsAppended;
    internal ManualResetEventSlim? ActionsRelease;

    internal static ExecutionWorkerSignals Create()
    {
        var signals = new ExecutionWorkerSignals(Interlocked.Increment(ref _nextId));
        if (!Signals.TryAdd(signals.Id, signals)) throw new InvalidOperationException("Duplicate test signal id.");
        return signals;
    }

    internal static ExecutionWorkerSignals Get(int id) => Signals[id];
}
