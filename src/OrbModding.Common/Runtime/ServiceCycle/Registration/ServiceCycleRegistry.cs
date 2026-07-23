using System;
using System.Collections.Generic;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using RuntimeLifecycleGeneration = OrbModding.Common.Runtime.LifecycleGeneration;

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

/// <summary>Owner-thread, fixed-capacity, explicit service-cycle composition registry.</summary>
public sealed partial class ServiceCycleRegistry : IDisposable
{
    private readonly int _ownerThreadId;
    private readonly IServiceCycleSlot[] _slots;
    private readonly Dictionary<ServiceId, IServiceCycleSlot> _byServiceId;
    private readonly IMonotonicClock _clock;
    private readonly bool _measureWorkerAllocations;
    private readonly IServiceCycleWorkerStarter? _workerStarter;
    private readonly IServiceCycleWorkerExitObserver? _workerExitObserver;
    private readonly ServiceResourceClaimLedger _resourceClaims;
    private int _activeCount;
    private int _nextOrdinal;
    private long _nextRegistrationToken;
    private long _nextLifecycleReconciliationEpoch;
    private bool _sealed;
    private bool _pumpClaimed;
    private int _pumpCallbackDepth;
    private bool _disposed;
    private bool _reconcilingLifecycle;
    private bool _constructingRunner;
    private RuntimeLifecycleGeneration _lifecycle;
    private bool _hasLifecycle;

    public ServiceCycleRegistry(int capacity, IMonotonicClock? clock = null)
        : this(capacity, clock, false) { }

    public ServiceCycleRegistry(
        int capacity,
        RuntimeLifecycleGeneration lifecycle,
        IMonotonicClock? clock = null)
        : this(capacity, clock, false)
    {
        if (lifecycle.Value == 0)
            throw new ArgumentException("A valid initial lifecycle generation is required.", nameof(lifecycle));
        _lifecycle = lifecycle;
        _hasLifecycle = true;
    }

    internal ServiceCycleRegistry(
        int capacity,
        IMonotonicClock? clock,
        bool measureWorkerAllocations,
        IServiceCycleWorkerStarter? workerStarter = null,
        IServiceCycleWorkerExitObserver? workerExitObserver = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        _clock = clock ?? new StopwatchMonotonicClock();
        _measureWorkerAllocations = measureWorkerAllocations;
        _workerStarter = workerStarter;
        _workerExitObserver = workerExitObserver;
        _slots = new IServiceCycleSlot[capacity];
        _byServiceId = new Dictionary<ServiceId, IServiceCycleSlot>(capacity);
        _resourceClaims = new ServiceResourceClaimLedger(capacity);
    }

    public int Capacity => _slots.Length;
    public int Count => _activeCount;
    public int OrdinalCount => _nextOrdinal;
    public bool IsSealed => _sealed;
    public RuntimeLifecycleGeneration CurrentLifecycle => _lifecycle;
    internal IMonotonicClock Clock => _clock;

    internal long LifecyclePositionTransitionCount
    {
        get
        {
            long total = 0;
            for (var ordinal = 0; ordinal < _nextOrdinal; ordinal++)
                total = checked(total + _slots[ordinal].LifecyclePositionTransitionCount);
            return total;
        }
    }

    private void AssertOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Service-cycle registration must remain on its owning main thread.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ServiceCycleRegistry));
    }

    private void ThrowIfPumpCallback()
    {
        if (_pumpCallbackDepth != 0)
            throw new InvalidOperationException(
                "Service-cycle registration and disposal cannot mutate composition from a pump callback.");
    }

    private void ThrowIfRunnerConstruction()
    {
        if (_reconcilingLifecycle || _constructingRunner)
            throw new InvalidOperationException(
                "Service-cycle composition cannot mutate from a runner construction callback.");
    }
}
