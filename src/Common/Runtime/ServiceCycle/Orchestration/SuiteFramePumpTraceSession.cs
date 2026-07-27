using System;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed class SuiteFramePumpTraceSession
{
    private readonly ServiceCycleRegistry _registry;
    private readonly int _serviceCapacity;
    private ServiceCycleSemanticRuntimeTraceMultiplexer? _dispatch;
    private ServiceCycleSemanticRuntimeTrace? _host;
    private ServiceCycleSemanticRuntimeTrace? _manual;
    private bool _hostInvalidated;

    internal SuiteFramePumpTraceSession(
        ServiceCycleRegistry registry,
        int serviceCapacity,
        ServiceCycleSemanticRecorder? hostRecorder)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _serviceCapacity = serviceCapacity;
        if (hostRecorder is not { Enabled: true }) return;
        _host = ServiceCycleSemanticTraceBinder.Create(
            hostRecorder,
            registry,
            serviceCapacity);
        RebuildDispatch();
    }

    internal bool HasHostTrace => _host is not null;
    internal ServiceCycleSemanticRuntimeTraceMultiplexer? Dispatch => _dispatch;
    internal ServiceCycleSemanticTraceSource? HostTraceSource => _host?.Source;

    internal ServiceCycleSemanticTraceCloseResult TryCloseHostTraceAtSettledBoundary()
    {
        if (_hostInvalidated)
        {
            _host = null;
            RebuildDispatch();
            return ServiceCycleSemanticTraceCloseResult.Invalidated;
        }
        if (_host is null) return ServiceCycleSemanticTraceCloseResult.Closed;
        for (var ordinal = 0; ordinal < _serviceCapacity; ordinal++)
        {
            var slot = _registry.GetSlot(ordinal);
            if (slot.IsDisposed)
            {
                _host = null;
                RebuildDispatch();
                return ServiceCycleSemanticTraceCloseResult.Invalidated;
            }
            if (!slot.IsBetweenCycles) return ServiceCycleSemanticTraceCloseResult.Pending;
        }
        _host = null;
        RebuildDispatch();
        return ServiceCycleSemanticTraceCloseResult.Closed;
    }

    internal void DiscardHostTrace()
    {
        _host = null;
        RebuildDispatch();
        _hostInvalidated = false;
    }

    internal bool TryAttachManual(
        ServiceCycleSemanticRecorder recorder,
        bool emergencyEngaged,
        out ServiceCycleSemanticRuntimeTrace? attached)
    {
        if (_manual is not null)
            throw new InvalidOperationException("A manual semantic trace is already attached.");
        if (recorder is null) throw new ArgumentNullException(nameof(recorder));
        if (!recorder.Enabled || recorder.ServiceCapacity != _serviceCapacity)
            throw new ArgumentException(
                "The enabled manual recorder must match the registry topology.",
                nameof(recorder));

        attached = null;
        if (emergencyEngaged ||
            !ServiceCycleSemanticTraceBinder.IsSettled(_registry, _serviceCapacity))
            return false;

        var trace = ServiceCycleSemanticTraceBinder.Create(
            recorder,
            _registry,
            _serviceCapacity);
        _manual = trace;
        RebuildDispatch();
        attached = trace;
        return true;
    }

    internal bool TryDetachManual(ServiceCycleSemanticRuntimeTrace attached)
    {
        EnsureManualAttached(attached);
        if (!ServiceCycleSemanticTraceBinder.IsSettled(_registry, _serviceCapacity)) return false;
        _manual = null;
        RebuildDispatch();
        return true;
    }

    internal void DiscardManual(ServiceCycleSemanticRuntimeTrace attached)
    {
        EnsureManualAttached(attached);
        _manual = null;
        RebuildDispatch();
    }

    internal void InvalidateHostTrace() => _hostInvalidated = true;

    private void EnsureManualAttached(ServiceCycleSemanticRuntimeTrace attached)
    {
        if (!ReferenceEquals(_manual, attached))
            throw new InvalidOperationException("The requested manual semantic trace is not attached.");
    }

    private void RebuildDispatch()
    {
        _dispatch = _host is not null
            ? new ServiceCycleSemanticRuntimeTraceMultiplexer(_host, _manual)
            : _manual is not null
                ? new ServiceCycleSemanticRuntimeTraceMultiplexer(_manual)
                : null;
    }
}
