using System;
using System.Collections.Generic;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>
/// Finite artifact-derived clock program. Owner and named worker threads consume independent exact
/// schedules, so worker scheduling cannot change timestamps or hide extra/missing runtime reads.
/// </summary>
internal sealed partial class ServiceCycleReplayClockScript : IMonotonicClock, IServiceCycleStateFactoryGate
{
    private readonly ServiceCycleTraceDocument _semantic;
    private readonly ServiceCycleReplayProductionArtifactPlan? _plan;
    private readonly int _serviceCount;
    private readonly int[] _artifactKeys;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<string, ReadSchedule> _workers = new(StringComparer.Ordinal);
    private readonly object _workerGate = new();
    private readonly object _stateFactoryGate = new();
    private readonly TimeSpan _stateFactoryTimeout;
    private MonotonicTimestamp[] _owner = Array.Empty<MonotonicTimestamp>();
    private int _ownerCursor;
    private int _nextStartOrdinal;
    private bool _emergency;

    internal ServiceCycleReplayClockScript(ServiceCycleTraceDocument semantic, int serviceCount)
        : this(semantic, DenseKeys(serviceCount)) { }

    internal ServiceCycleReplayClockScript(ServiceCycleTraceDocument semantic, int[] artifactKeys)
    {
        _semantic = semantic ?? throw new ArgumentNullException(nameof(semantic));
        _artifactKeys = artifactKeys is null
            ? throw new ArgumentNullException(nameof(artifactKeys))
            : (int[])artifactKeys.Clone();
        if (_artifactKeys.Length <= 0) throw new ArgumentOutOfRangeException(nameof(artifactKeys));
        for (var index = 0; index < _artifactKeys.Length; index++)
            if (_artifactKeys[index] <= 0) throw new ArgumentOutOfRangeException(nameof(artifactKeys));
        _serviceCount = _artifactKeys.Length;
    }

    internal ServiceCycleReplayClockScript(
        ServiceCycleReplayProductionArtifactPlan plan,
        TimeSpan stateFactoryTimeout)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        if (stateFactoryTimeout <= TimeSpan.Zero || stateFactoryTimeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(stateFactoryTimeout));
        _semantic = plan.Semantic;
        _serviceCount = plan.Capacity;
        _artifactKeys = DenseKeys(_serviceCount);
        _stateFactoryTimeout = stateFactoryTimeout;
    }

    void IServiceCycleStateFactoryGate.EnterStateFactory()
    {
        if (_stateFactoryTimeout == default) return;
        if (!Monitor.TryEnter(_stateFactoryGate, _stateFactoryTimeout))
            throw new TimeoutException("Replay state-factory serialization exceeded its worker boundary.");
    }

    void IServiceCycleStateFactoryGate.ExitStateFactory()
    {
        if (_stateFactoryTimeout != default) Monitor.Exit(_stateFactoryGate);
    }

    public MonotonicTimestamp Now
    {
        get
        {
            if (Environment.CurrentManagedThreadId == _ownerThreadId)
            {
                if ((uint)_ownerCursor >= (uint)_owner.Length)
                    throw new InvalidOperationException("Replay consumed an unrecorded owner-thread clock read.");
                return _owner[_ownerCursor++];
            }
            var name = Thread.CurrentThread.Name ?? string.Empty;
            lock (_workerGate)
            {
                if (!_workers.TryGetValue(name, out var schedule) || !schedule.TryTake(out var value))
                    throw new InvalidOperationException("Replay consumed an unrecorded worker clock read.");
                return value;
            }
        }
    }

    internal void RegisterWorker(
        ServiceId serviceId,
        LifecycleGeneration lifecycle,
        int traceServiceKey)
    {
        if (!serviceId.IsValid)
            throw new ArgumentException("A valid replay service identity is required.", nameof(serviceId));
        if (lifecycle.Value == 0)
            throw new ArgumentException("A valid replay lifecycle is required.", nameof(lifecycle));
        var workerName = ServiceCycleWorkerIdentity.Create(serviceId, lifecycle);
        lock (_workerGate)
        {
            if (_workers.TryGetValue(workerName, out var existing))
            {
                if (existing.ArtifactTraceServiceKey != traceServiceKey || existing.Lifecycle != lifecycle)
                    throw new InvalidOperationException("A replay worker identity was registered inconsistently.");
                return;
            }
        }
        var values = _plan is null
            ? ReadWorkerSchedule(traceServiceKey, lifecycle)
            : _plan.CopyWorkerSchedule(traceServiceKey, lifecycle.Value);
        lock (_workerGate)
            _workers.Add(workerName, new ReadSchedule(values, traceServiceKey, lifecycle));
    }

    internal bool IsComplete
    {
        get
        {
            if (_ownerCursor != _owner.Length) return false;
            lock (_workerGate)
                foreach (var schedule in _workers.Values)
                    if (!schedule.IsComplete) return false;
            return true;
        }
    }

    internal int? IncompleteArtifactTraceServiceKey
    {
        get
        {
            lock (_workerGate)
            {
                foreach (var pair in _workers)
                {
                    if (pair.Value.IsComplete) continue;
                    return pair.Value.ArtifactTraceServiceKey;
                }
            }
            return null;
        }
    }

    private void SetOwner(params MonotonicTimestamp[] values)
    {
        EnsureOwnerComplete();
        _owner = values;
        _ownerCursor = 0;
    }

    private void EnsureOwnerComplete()
    {
        if (_ownerCursor != _owner.Length)
            throw new InvalidOperationException("Replay did not consume every scripted owner-thread clock read.");
    }

    private sealed class ReadSchedule
    {
        private readonly MonotonicTimestamp[] _values;
        private readonly int _artifactTraceServiceKey;
        private readonly LifecycleGeneration _lifecycle;
        private int _cursor;

        internal ReadSchedule(
            MonotonicTimestamp[] values,
            int artifactTraceServiceKey,
            LifecycleGeneration lifecycle)
        {
            _values = values;
            _artifactTraceServiceKey = artifactTraceServiceKey;
            _lifecycle = lifecycle;
        }
        internal int ArtifactTraceServiceKey => _artifactTraceServiceKey;
        internal LifecycleGeneration Lifecycle => _lifecycle;
        internal bool IsComplete => _cursor == _values.Length;
        internal bool TryTake(out MonotonicTimestamp value)
        {
            if ((uint)_cursor >= (uint)_values.Length)
            {
                value = default;
                return false;
            }
            value = _values[_cursor++];
            return true;
        }
    }
}
