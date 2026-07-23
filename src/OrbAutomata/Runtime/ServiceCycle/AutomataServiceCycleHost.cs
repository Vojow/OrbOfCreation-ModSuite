using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

internal sealed class AutomataServiceCycleHost : IDisposable
{
    private readonly Func<long> _readFrameIdentity;
    private readonly IServiceCyclePumpTimingSink? _pumpTiming;
    private readonly SuiteFramePump _pump;
    private readonly int _serviceCapacity;
    private AutomataServiceCycleObservability? _observability;
    private Func<bool>? _disposeClaimedPump;
    private bool _disposed;

    internal AutomataServiceCycleHost(
        ServiceCycleRegistry registry,
        Func<long> readFrameIdentity,
        IServiceCyclePumpTimingSink? pumpTiming,
        ServiceCycleSemanticRecorder? semanticTrace
#if SERVICE_CYCLE_PROFILE
        , ServiceCycleProfileProbe profileProbe
#endif
        )
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        _readFrameIdentity = readFrameIdentity ??
            throw new ArgumentNullException(nameof(readFrameIdentity));
        _pumpTiming = pumpTiming;
        _serviceCapacity = registry.OrdinalCount;
        registry.Seal();
        _pump = new SuiteFramePump(
            registry,
            semanticTrace
#if SERVICE_CYCLE_PROFILE
            , profileProbe
#endif
            );
    }

    internal SuiteFramePump Pump => _pump;
    internal LifecycleGeneration CurrentLifecycle => _pump.CurrentLifecycle;
    internal bool EmergencyStopEngaged => _pump.IsEmergencyStopEngaged;

    internal SuiteFramePumpReport Tick()
    {
        ThrowIfDisposed();
        _observability?.BeforePump();
        var report = _pump.PumpFrame(_readFrameIdentity());
        _pumpTiming?.Observe(in report);
        _observability?.AfterPump();
        return report;
    }

    internal void AttachObservability(
        AutomataServiceCycleObservability observability,
        in AutomataServiceCycleObservabilityOptions options)
    {
        ThrowIfDisposed();
        if (observability is null) throw new ArgumentNullException(nameof(observability));
        if (_observability is not null)
            throw new InvalidOperationException("ServiceCycle observability is already attached.");
        if (_disposeClaimedPump is not null)
            throw new InvalidOperationException("The ServiceCycle pump already has a shutdown owner.");

        observability.Attach(_pump, _serviceCapacity, in options);
        var pumpShutdown = observability.PumpShutdown;
        if (pumpShutdown is not null) ClaimPumpShutdown(pumpShutdown);
        _observability = observability;
    }

    internal void SetEmergencyStop(bool engaged, EmergencyStopReason reason)
    {
        ThrowIfDisposed();
        _pump.SetEmergencyStop(engaged, reason);
    }

    internal bool TryReplaceLifecycle(long nativeGeneration)
    {
        ThrowIfDisposed();
        var lifecycle = ToLifecycle(nativeGeneration);
        return lifecycle.Value > _pump.CurrentLifecycle.Value &&
            _pump.RequestLifecycleReplacement(lifecycle);
    }

    internal void ClaimPumpShutdown(Func<bool> disposeClaimedPump)
    {
        ThrowIfDisposed();
        if (disposeClaimedPump is null)
            throw new ArgumentNullException(nameof(disposeClaimedPump));
        if (_disposeClaimedPump is not null)
            throw new InvalidOperationException("The ServiceCycle pump already has a shutdown owner.");
        _disposeClaimedPump = disposeClaimedPump;
    }

    public void Dispose() => Shutdown();

    internal void Shutdown()
    {
        if (_disposed) return;
        _disposed = true;
        try { _observability?.Dispose(); }
        finally
        {
            if (_disposeClaimedPump?.Invoke() != true) _pump.Dispose();
        }
    }

    internal static LifecycleGeneration ToLifecycle(long generation)
    {
        if (generation <= 0)
            throw new InvalidOperationException(
                "The Automata ServiceCycle host requires a positive native lifecycle generation.");
        return new LifecycleGeneration((ulong)generation);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AutomataServiceCycleHost));
    }
}
