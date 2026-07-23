using System;
using OrbModding.Common.Runtime;
using RuntimeLifecycleGeneration = OrbModding.Common.Runtime.LifecycleGeneration;

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

public sealed partial class ServiceCycleRegistry
{
    internal bool RequestLifecycle(RuntimeLifecycleGeneration generation)
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        ThrowIfRunnerConstruction();
        if (generation.Value == 0)
            throw new ArgumentException("A valid lifecycle generation is required.", nameof(generation));
        if (!_hasLifecycle)
        {
            _lifecycle = generation;
            _hasLifecycle = true;
        }
        else if (generation.Value <= _lifecycle.Value)
        {
            return false;
        }
        else
        {
            _lifecycle = generation;
        }

        for (var ordinal = 0; ordinal < _nextOrdinal; ordinal++)
            _slots[ordinal].RequestLifecycle(generation);
        return true;
    }

    internal int ReconcileLifecycle(MonotonicTimestamp now)
        => ReconcileLifecycle(now, NextLifecycleReconciliationEpoch());

    internal long NextLifecycleReconciliationEpoch()
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        ThrowIfRunnerConstruction();
        return checked(++_nextLifecycleReconciliationEpoch);
    }

    internal int ReconcileLifecycle(MonotonicTimestamp now, long reconciliationEpoch)
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        if (reconciliationEpoch <= 0)
            throw new ArgumentOutOfRangeException(nameof(reconciliationEpoch));
        if (_reconcilingLifecycle || _constructingRunner)
            throw new InvalidOperationException("Lifecycle reconciliation cannot be entered recursively.");
        _reconcilingLifecycle = true;
        try
        {
            var changed = 0;
            for (var ordinal = 0; ordinal < _nextOrdinal; ordinal++)
            {
                if (_slots[ordinal].ReconcileLifecycle(now, reconciliationEpoch)) changed++;
            }
            return changed;
        }
        finally
        {
            _reconcilingLifecycle = false;
        }
    }
}
