using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace.Control;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbAutomata;

/// <summary>
/// Turns a user's "dump recent events" into one artifact written from the pump's host trace ring.
/// </summary>
internal sealed class AutomataHostTraceController : IDisposable
{
    private readonly SuiteFramePump _pump;
    private readonly int _serviceCapacity;
    private readonly ServiceCycleTraceRoster _roster;
    private readonly IAutomataHostTraceDumpSource _dumps;
    private readonly HostTraceDumpRegistration _control;
    private bool _disposed;

    private AutomataHostTraceController(
        SuiteFramePump pump,
        int serviceCapacity,
        ServiceCycleTraceRoster roster,
        IAutomataHostTraceDumpSource dumps,
        HostTraceDumpRegistration control)
    {
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
        _serviceCapacity = serviceCapacity;
        _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        _dumps = dumps ?? throw new ArgumentNullException(nameof(dumps));
        _control = control ?? throw new ArgumentNullException(nameof(control));
    }

    internal static AutomataHostTraceController? TryCreate(
        SuiteFramePump pump,
        int serviceCapacity,
        ServiceCycleTraceRoster roster,
        in AutomataHostTraceOptions options)
    {
        if (!options.Enabled || options.Control is null || options.Dumps is null)
            throw new ArgumentException("Enabled host-trace options are required.", nameof(options));
        // Nothing to dump without the ring, so the affordance stays unavailable rather than offering
        // a button that writes an empty artifact.
        if (pump.SemanticTrace is null) return null;
        if (!options.Control.TryRegister(out var control) || control is null) return null;
        try
        {
            return new AutomataHostTraceController(pump, serviceCapacity, roster, options.Dumps, control);
        }
        catch
        {
            control.Dispose();
            throw;
        }
    }

    internal void BeforePump()
    {
        if (_disposed || !_control.TryTakeRequest()) return;
        _control.Publish(Dump());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _control.Dispose();
    }

    private HostTraceDumpStatus Dump()
    {
        var source = _pump.SemanticTrace ??
            throw new InvalidOperationException("The host semantic trace is unavailable.");
        try
        {
            var spec = _dumps.Create();
            var outcome = HostTraceDumpWriter.Write(
                source,
                spec.Session,
                spec.Storage,
                _serviceCapacity,
                _roster);
            return outcome.WrittenEvents == 0
                ? HostTraceDumpStatus.Idle
                : new HostTraceDumpStatus(
                    HostTraceDumpState.Written,
                    outcome.WrittenEvents,
                    outcome.BytesWritten,
                    outcome.OverwrittenEvents,
                    spec.ArtifactName);
        }
        catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
        {
            // A dump that cannot be written is a failed bug report, not a failed run.
            return new HostTraceDumpStatus(HostTraceDumpState.Failed, 0, 0, source.OverwrittenTotal, string.Empty);
        }
    }
}
