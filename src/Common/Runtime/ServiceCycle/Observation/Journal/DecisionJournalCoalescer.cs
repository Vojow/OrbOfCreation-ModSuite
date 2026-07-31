using System;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal sealed class DecisionJournalCoalescer : IDecisionJournalObservationSink
{
    private readonly IDecisionJournalRecordSink _sink;
    private readonly DecisionJournalRecord[] _open;
    private readonly bool[] _hasOpen;
    private readonly MonotonicDuration _checkpointInterval;
    private MonotonicTimestamp _nextCheckpoint;
    private MonotonicTimestamp _lastObservedAt;
    private bool _stopped;

    internal DecisionJournalCoalescer(
        int serviceCapacity,
        IDecisionJournalRecordSink sink,
        MonotonicDuration checkpointInterval,
        MonotonicTimestamp startedAt)
    {
        if (serviceCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(serviceCapacity));
        if (checkpointInterval.Ticks <= 0) throw new ArgumentOutOfRangeException(nameof(checkpointInterval));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _open = new DecisionJournalRecord[serviceCapacity];
        _hasOpen = new bool[serviceCapacity];
        _checkpointInterval = checkpointInterval;
        _nextCheckpoint = AddSaturated(startedAt, checkpointInterval);
        _lastObservedAt = startedAt;
    }

    public bool IsFaulted { get; private set; }
    internal bool IsStopped => _stopped;

    public void Observe(in DecisionJournalObservation observation)
    {
        if (IsFaulted) return;
        EnsureRunning(observation.LastObservedAt);
        var index = ServiceIndex(observation.Service);
        var next = DecisionJournalRecord.Decision(in observation);
        if (!_hasOpen[index])
        {
            _open[index] = next;
            _hasOpen[index] = true;
            return;
        }
        if (_open[index].CanCoalesceWith(in next))
        {
            _open[index] = _open[index].Coalesce(in next);
            return;
        }
        if (!AppendOpen(index)) return;
        _open[index] = next;
        _hasOpen[index] = true;
    }

    public void ObserveTransition(in DecisionJournalRecord transition)
    {
        if (IsFaulted) return;
        DecisionJournalRecordValidation.Validate(in transition);
        if (transition.Kind == DecisionJournalRecordKind.DecisionSpan)
            throw new ArgumentException("A transition record is required.", nameof(transition));
        EnsureRunning(new MonotonicTimestamp(transition.LastTimestampTicks));
        if (transition.Service.IsValid)
        {
            if (!AppendOpen(ServiceIndex(transition.Service))) return;
        }
        else if (!AppendAllOpen())
        {
            return;
        }
        if (!_sink.TryAppend(in transition)) IsFaulted = true;
    }

    public void BreakServiceSpan(
        ServiceCycleTraceServiceId service,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted) return;
        EnsureRunning(observedAt);
        AppendOpen(ServiceIndex(service));
    }

    public void Advance(MonotonicTimestamp now)
    {
        if (IsFaulted) return;
        EnsureRunning(now);
        if (now < _nextCheckpoint) return;
        if (AppendAllOpen() && !_sink.TryFlush()) IsFaulted = true;
        _nextCheckpoint = AddSaturated(now, _checkpointInterval);
    }

    public void Stop(MonotonicTimestamp now)
    {
        if (_stopped) return;
        if (now < _lastObservedAt) throw new ArgumentOutOfRangeException(nameof(now));
        if (!IsFaulted && AppendAllOpen() && !_sink.TryFlush()) IsFaulted = true;
        _sink.Stop();
        _stopped = true;
        _lastObservedAt = now;
    }

    private bool AppendAllOpen()
    {
        for (var index = 0; index < _open.Length; index++)
            if (!AppendOpen(index)) return false;
        return true;
    }

    private bool AppendOpen(int index)
    {
        if (!_hasOpen[index]) return true;
        if (!_sink.TryAppend(in _open[index]))
        {
            IsFaulted = true;
            return false;
        }
        _open[index] = default;
        _hasOpen[index] = false;
        return true;
    }

    private int ServiceIndex(ServiceCycleTraceServiceId service)
    {
        if (!service.IsValid || service.Value > (ulong)_open.Length)
            throw new ArgumentOutOfRangeException(nameof(service));
        return checked((int)service.Value - 1);
    }

    private void EnsureRunning(MonotonicTimestamp observedAt)
    {
        if (_stopped) throw new InvalidOperationException("The decision journal is stopped.");
        if (observedAt < _lastObservedAt)
            throw new ArgumentOutOfRangeException(nameof(observedAt), "Journal observations must be monotonic.");
        _lastObservedAt = observedAt;
    }

    private static MonotonicTimestamp AddSaturated(
        MonotonicTimestamp timestamp,
        MonotonicDuration duration) =>
        duration.Ticks > long.MaxValue - timestamp.Ticks
            ? new MonotonicTimestamp(long.MaxValue)
            : new MonotonicTimestamp(timestamp.Ticks + duration.Ticks);
}
