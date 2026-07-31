using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;

/// <summary>
/// Owner-thread, allocation-free rolling projection over the same assembled observations consumed
/// by the decision journal. Every registered service remains present even before it acts.
/// </summary>
internal sealed class ServiceActionOutcomeWindowProjection :
    IDecisionJournalObservationSink,
    IServiceActionOutcomeWindowSource
{
    internal const int DefaultWindowCapacityPerService = 32;
    private readonly ServiceActionOutcomeService[] _services;
    private readonly ServiceActionOutcomeDelta[] _window;
    private readonly int[] _next;
    private readonly int[] _counts;
    private readonly long[] _planned;
    private readonly long[] _committed;
    private readonly long[] _skipped;
    private readonly long[] _rejected;
    private readonly long[] _faulted;
    private readonly ServiceActionOutcomeBoundary[] _lastBoundary;
    private long _revision;

    private ServiceActionOutcomeWindowProjection(
        ServiceActionOutcomeService[] services,
        int windowCapacityPerService)
    {
        if (services is null || services.Length == 0)
            throw new ArgumentException("At least one registered service is required.", nameof(services));
        if (windowCapacityPerService <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowCapacityPerService));
        _services = services;
        WindowCapacityPerService = windowCapacityPerService;
        _window = new ServiceActionOutcomeDelta[checked(services.Length * windowCapacityPerService)];
        _next = new int[services.Length];
        _counts = new int[services.Length];
        _planned = new long[services.Length];
        _committed = new long[services.Length];
        _skipped = new long[services.Length];
        _rejected = new long[services.Length];
        _faulted = new long[services.Length];
        _lastBoundary = new ServiceActionOutcomeBoundary[services.Length];
    }

    internal static ServiceActionOutcomeWindowProjection Create(
        ServiceCycleRegistry registry,
        int windowCapacityPerService = DefaultWindowCapacityPerService)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        var count = registry.OrdinalCount;
        if (count <= 0)
            throw new InvalidOperationException("The action-outcome window requires registered services.");
        var services = new ServiceActionOutcomeService[count];
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var slot = registry.GetSlot(ordinal);
            services[ordinal] = new ServiceActionOutcomeService(
                slot.ServiceId,
                slot.ActionDispatchPolicy.Shape);
        }
        return new ServiceActionOutcomeWindowProjection(services, windowCapacityPerService);
    }

    public int ServiceCount => _services.Length;
    public int WindowCapacityPerService { get; }
    public long Revision => _revision;
    public bool IsFaulted => false;

    public ServiceActionOutcomeWindowCopyResult CopyTo(
        Span<ServiceActionOutcomeSnapshot> destination)
    {
        var written = Math.Min(_services.Length, destination.Length);
        for (var index = 0; index < written; index++)
        {
            var service = _services[index];
            destination[index] = new ServiceActionOutcomeSnapshot(
                service.Service,
                service.Shape,
                _counts[index],
                _planned[index],
                _committed[index],
                _skipped[index],
                _rejected[index],
                _faulted[index],
                _lastBoundary[index]);
        }
        return new ServiceActionOutcomeWindowCopyResult(_services.Length, written, _revision);
    }

    public void Observe(in DecisionJournalObservation observation)
    {
        var index = ServiceIndex(observation.Service);
        var terminal = observation.Terminal;
        var delta = new ServiceActionOutcomeDelta(
            terminal.IsPresent ? terminal.ActionCount : 0,
            terminal.IsPresent ? terminal.CommittedCount : 0,
            terminal.IsPresent ? terminal.SkippedCount : 0,
            terminal.IsPresent && terminal.Disposition == BatchTerminalDisposition.Rejected ? 1 : 0,
            (terminal.IsPresent && terminal.Disposition == BatchTerminalDisposition.Faulted) ||
                observation.Fault.IsValid ? 1 : 0,
            Boundary(in observation));
        Add(index, in delta);
    }

    public void ObserveTransition(in DecisionJournalRecord transition)
    {
        DecisionJournalRecordValidation.Validate(in transition);
        var boundary = TransitionBoundary(in transition);
        if (!boundary.IsPresent) return;
        var delta = new ServiceActionOutcomeDelta(0, 0, 0, 0, 0, boundary);
        if (transition.Service.IsValid)
        {
            Add(ServiceIndex(transition.Service), in delta);
            return;
        }
        if (transition.Kind is not (DecisionJournalRecordKind.EmergencyEntered or
            DecisionJournalRecordKind.EmergencyCleared)) return;
        for (var index = 0; index < _services.Length; index++) Add(index, in delta);
    }

    public void BreakServiceSpan(
        ServiceCycleTraceServiceId service,
        MonotonicTimestamp observedAt)
    {
        _ = ServiceIndex(service);
        _ = observedAt;
    }

    public void Advance(MonotonicTimestamp now) => _ = now;
    public void Stop(MonotonicTimestamp now) => _ = now;

    private void Add(int serviceIndex, in ServiceActionOutcomeDelta delta)
    {
        var slot = checked(serviceIndex * WindowCapacityPerService + _next[serviceIndex]);
        if (_counts[serviceIndex] == WindowCapacityPerService)
        {
            var removed = _window[slot];
            _planned[serviceIndex] -= removed.Planned;
            _committed[serviceIndex] -= removed.Committed;
            _skipped[serviceIndex] -= removed.Skipped;
            _rejected[serviceIndex] -= removed.Rejected;
            _faulted[serviceIndex] -= removed.Faulted;
        }
        else
        {
            _counts[serviceIndex]++;
        }

        _window[slot] = delta;
        _planned[serviceIndex] = checked(_planned[serviceIndex] + delta.Planned);
        _committed[serviceIndex] = checked(_committed[serviceIndex] + delta.Committed);
        _skipped[serviceIndex] = checked(_skipped[serviceIndex] + delta.Skipped);
        _rejected[serviceIndex] = checked(_rejected[serviceIndex] + delta.Rejected);
        _faulted[serviceIndex] = checked(_faulted[serviceIndex] + delta.Faulted);
        _next[serviceIndex] = (_next[serviceIndex] + 1) % WindowCapacityPerService;
        _lastBoundary[serviceIndex] = FindLastBoundary(serviceIndex);
        _revision = checked(_revision + 1);
    }

    private ServiceActionOutcomeBoundary FindLastBoundary(int serviceIndex)
    {
        var count = _counts[serviceIndex];
        for (var offset = 1; offset <= count; offset++)
        {
            var local = (_next[serviceIndex] - offset + WindowCapacityPerService) %
                WindowCapacityPerService;
            var boundary = _window[serviceIndex * WindowCapacityPerService + local].Boundary;
            if (boundary.IsPresent) return boundary;
        }
        return default;
    }

    private int ServiceIndex(ServiceCycleTraceServiceId service)
    {
        if (!service.IsValid || service.Value > (ulong)_services.Length)
            throw new ArgumentOutOfRangeException(nameof(service));
        return checked((int)service.Value - 1);
    }

    private static ServiceActionOutcomeBoundary Boundary(
        in DecisionJournalObservation observation)
    {
        var terminal = observation.Terminal;
        if (terminal.IsPresent)
        {
            return terminal.Disposition switch
            {
                BatchTerminalDisposition.Rejected when
                    terminal.ResultCode == CommonActionResultCodes.EmergencyStop =>
                    new ServiceActionOutcomeBoundary(
                        ServiceActionOutcomeBoundaryKind.EmergencyStopped,
                        terminal.ResultCode.Value),
                BatchTerminalDisposition.Rejected => new ServiceActionOutcomeBoundary(
                    ServiceActionOutcomeBoundaryKind.Rejected,
                    terminal.ResultCode.Value),
                BatchTerminalDisposition.Faulted => new ServiceActionOutcomeBoundary(
                    ServiceActionOutcomeBoundaryKind.Faulted,
                    terminal.ResultCode.Value,
                    observation.Fault.Category),
                BatchTerminalDisposition.Orphaned => new ServiceActionOutcomeBoundary(
                    ServiceActionOutcomeBoundaryKind.LifecycleChanged,
                    terminal.ResultCode.Value),
                BatchTerminalDisposition.Completed when terminal.CommittedCount > 0 =>
                    new ServiceActionOutcomeBoundary(
                        ServiceActionOutcomeBoundaryKind.Committed,
                        terminal.ResultCode.Value),
                BatchTerminalDisposition.Completed when terminal.SkippedCount > 0 =>
                    new ServiceActionOutcomeBoundary(
                        ServiceActionOutcomeBoundaryKind.Skipped,
                        CommonActionResultCodes.Skipped.Value),
                _ => WaitingBoundary(in observation),
            };
        }
        if (observation.Fault.IsValid)
        {
            return new ServiceActionOutcomeBoundary(
                ServiceActionOutcomeBoundaryKind.Faulted,
                observation.Fault.Code.Value,
                observation.Fault.Category);
        }
        return WaitingBoundary(in observation);
    }

    private static ServiceActionOutcomeBoundary WaitingBoundary(
        in DecisionJournalObservation observation)
    {
        var code = observation.CaptureDecisionCode != 0 &&
            observation.CaptureDecisionCode != CommonServiceDecisionCodes.Captured.Value
            ? observation.CaptureDecisionCode
            : observation.StartDecisionCode != 0 &&
                observation.StartDecisionCode != CommonServiceDecisionCodes.Ready.Value
                ? observation.StartDecisionCode
                : 0;
        return observation.HasWake || observation.HasProjection || code != 0
            ? new ServiceActionOutcomeBoundary(ServiceActionOutcomeBoundaryKind.Waiting, code)
            : default;
    }

    private static ServiceActionOutcomeBoundary TransitionBoundary(
        in DecisionJournalRecord transition) => transition.Kind switch
    {
        DecisionJournalRecordKind.LifecycleChanged => new ServiceActionOutcomeBoundary(
            ServiceActionOutcomeBoundaryKind.LifecycleChanged,
            transition.TransitionCode),
        DecisionJournalRecordKind.WorldGateHeld => new ServiceActionOutcomeBoundary(
            ServiceActionOutcomeBoundaryKind.WorldGateHeld,
            transition.TransitionCode),
        DecisionJournalRecordKind.EmergencyEntered => new ServiceActionOutcomeBoundary(
            ServiceActionOutcomeBoundaryKind.EmergencyStopped,
            transition.TransitionCode),
        DecisionJournalRecordKind.EmergencyCleared => new ServiceActionOutcomeBoundary(
            ServiceActionOutcomeBoundaryKind.Waiting,
            transition.TransitionCode),
        _ => default,
    };

    private readonly struct ServiceActionOutcomeService
    {
        internal ServiceActionOutcomeService(ServiceId service, ServiceShape shape)
        {
            if (!service.IsValid) throw new ArgumentException("A valid service identity is required.", nameof(service));
            if (shape is not (ServiceShape.Source or ServiceShape.Ordinary))
                throw new ArgumentOutOfRangeException(nameof(shape));
            Service = service;
            Shape = shape;
        }

        internal ServiceId Service { get; }
        internal ServiceShape Shape { get; }
    }

    private readonly struct ServiceActionOutcomeDelta
    {
        internal ServiceActionOutcomeDelta(
            int planned,
            int committed,
            int skipped,
            int rejected,
            int faulted,
            ServiceActionOutcomeBoundary boundary)
        {
            Planned = planned;
            Committed = committed;
            Skipped = skipped;
            Rejected = rejected;
            Faulted = faulted;
            Boundary = boundary;
        }

        internal int Planned { get; }
        internal int Committed { get; }
        internal int Skipped { get; }
        internal int Rejected { get; }
        internal int Faulted { get; }
        internal ServiceActionOutcomeBoundary Boundary { get; }
    }
}
