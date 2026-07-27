using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

/// <summary>
/// Owns append order, causal heads, delayed-operation anchors, and emergency ancestry for the
/// semantic stream. Payload translation stays in <see cref="ServiceCycleSemanticRecorder"/>.
/// </summary>
internal sealed partial class ServiceCycleSemanticCausalWriter
{
    private readonly ServiceCycleEventRing _ring;
    private readonly ServiceCycleTraceIdentityMap _identities;
    private readonly ServiceCycleTraceEventId[] _serviceHeads;
    private readonly int _ownerThreadId;

    internal ServiceCycleSemanticCausalWriter(
        ServiceCycleTraceSessionId session,
        int eventCapacity,
        int serviceCapacity,
        IServiceCycleSemanticEventObserver? observer)
    {
        _ring = new ServiceCycleEventRing(session, eventCapacity, observer);
        _identities = new ServiceCycleTraceIdentityMap(serviceCapacity);
        _emergencyCausality = new ServiceCycleEmergencyCausalMap(eventCapacity);
        _serviceHeads = new ServiceCycleTraceEventId[serviceCapacity];
        _queuedCycleAnchors = new ServiceCycleTraceEventId[serviceCapacity];
        _queuedCycles = new ServiceCycleTraceCycleIdentity[serviceCapacity];
        _captureAnchors = new ServiceCycleTraceEventId[serviceCapacity];
        _captures = new ServiceCycleTraceCaptureIdentity[serviceCapacity];
        _captureTerminalAnchors = new ServiceCycleTraceEventId[serviceCapacity];
        _captureTerminalIdentities = new ServiceCycleTraceCaptureIdentity[serviceCapacity];
        _startAnchors = new ServiceCycleTraceEventId[serviceCapacity];
        _startLifecycles = new ulong[serviceCapacity];
        _startConfigurations = new ulong[serviceCapacity];
        _actionAttemptAnchors = new ServiceCycleTraceEventId[serviceCapacity];
        _actionAttempts = new ServiceActionContext[serviceCapacity];
        _retainedEmergencyContexts = new EmergencyStopContext[serviceCapacity];
        _retainedEmergencyEntries = new ServiceCycleTraceEventId[serviceCapacity];
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    internal ServiceCycleTraceSessionId Session => _ring.Session;
    internal int Capacity => _ring.Capacity;
    internal int Count => _ring.Count;
    internal ulong OverwrittenTotal => _ring.OverwrittenTotal;
    internal ServiceCycleTraceDropRange OverwrittenRange => _ring.OverwrittenRange;
    internal ServiceCycleTraceCursor Cursor => _ring.Cursor;
    internal int ServiceCapacity => _serviceHeads.Length;

    internal ServiceCycleTraceIdentityMap Identities
    {
        get
        {
            EnsureOwner();
            return _identities;
        }
    }

    internal void RegisterService(int ordinal, ServiceId service)
    {
        EnsureOwner();
        _identities.Register(ordinal, service);
    }

    internal ServiceCycleEventDrain DrainSince(
        ServiceCycleTraceCursor after,
        Span<ServiceCycleSemanticEvent> destination) => _ring.DrainSince(after, destination);

    internal ServiceCycleEventDrain PullCapture(ServiceCycleTraceCapture capture, int maximumEvents) =>
        capture.Pull(_ring, maximumEvents);

    internal ServiceCycleTraceCycleIdentity TraceCycle(int ordinal, in ServiceCycleIdentity cycle)
    {
        if (!cycle.IsValid) throw new ArgumentException("A valid cycle identity is required.", nameof(cycle));
        _identities.EnsureMatches(ordinal, cycle.Service);
        return new ServiceCycleTraceCycleIdentity(
            _identities.ForRegistrationOrdinal(ordinal),
            cycle.Lifecycle.Value,
            cycle.Config.Value,
            cycle.Strategy.Value,
            cycle.World.Value,
            cycle.Cycle.Value);
    }

    internal ServiceCycleTraceCaptureIdentity TraceCapture(
        int ordinal,
        in ServiceCaptureContext capture)
    {
        if (!capture.Service.IsValid || capture.Lifecycle.Value == 0 || !capture.Config.IsValid ||
            !capture.Capture.IsValid || !capture.Cycle.IsValid)
        {
            throw new ArgumentException("A valid capture identity is required.", nameof(capture));
        }
        _identities.EnsureMatches(ordinal, capture.Service);
        return new ServiceCycleTraceCaptureIdentity(
            _identities.ForRegistrationOrdinal(ordinal),
            capture.Lifecycle.Value,
            capture.Config.Value,
            capture.Capture.Value,
            capture.Cycle.Value);
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Service-cycle semantic recording is owner-thread affine.");
    }
}
