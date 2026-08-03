using System;
using BepInEx.Logging;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbAutomata;

/// <summary>The automatic detailed-trace companion to a profiling session.</summary>
internal sealed class AutomataFullTraceController : IDisposable
{
    private readonly FullTraceRuntimeSession _session;
    private readonly IAutomataFullTraceSessionSource _sessions;
    private readonly ManualLogSource _log;
    private string _artifactName = string.Empty;
    private bool _started;
    private bool _startFailed;
    private bool _terminalReported;
    private bool _disposed;

    private AutomataFullTraceController(
        SuiteFramePump pump,
        int serviceCapacity,
        ServiceCycleTraceRoster roster,
        IAutomataFullTraceSessionSource sessions,
        ManualLogSource log)
    {
        _session = new FullTraceRuntimeSession(pump, serviceCapacity, roster);
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    internal static AutomataFullTraceController Create(
        SuiteFramePump pump,
        int serviceCapacity,
        ServiceCycleTraceRoster roster,
        in AutomataFullTraceOptions options,
        ManualLogSource log)
    {
        if (!options.Enabled || options.Sessions is null)
            throw new ArgumentException("Enabled profiling full-trace options are required.", nameof(options));
        return new AutomataFullTraceController(
            pump,
            serviceCapacity,
            roster,
            options.Sessions,
            log);
    }

    internal FullTraceRuntimeSessionSnapshot Snapshot => _session.Snapshot;

    internal void BeforePump() => Tick();

    internal void AfterPump() => Tick();

    internal void StartAutomatically()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AutomataFullTraceController));
        if (_started || _startFailed) return;
        try
        {
            var spec = _sessions.Create();
            _artifactName = spec.ArtifactName;
            _session.Start(spec.Session, spec.SemanticSession, spec.Storage);
            _started = true;
            Tick();
        }
        catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
        {
            _startFailed = true;
            _log.LogAutomataError(
                "Profiling full trace could not start; profiling and gameplay continue: " +
                Describe(exception));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
    }

    private void Tick()
    {
        if (_disposed || !_started || _startFailed) return;
        _session.Tick();
        var snapshot = _session.Snapshot;
        if (_terminalReported || snapshot.State is not (
                FullTraceRuntimeSessionState.Complete or FullTraceRuntimeSessionState.Incomplete))
            return;
        _terminalReported = true;
        if (snapshot.State == FullTraceRuntimeSessionState.Incomplete)
        {
            _log.LogAutomataError(
                "Profiling full trace ended incomplete: " + _artifactName +
                " | reason=" + snapshot.FaultReason +
                " | first missing sequence=" + snapshot.FirstIncompleteSequence + ".");
        }
    }

    private static string Describe(Exception exception)
    {
        var message = exception.GetBaseException().Message?.Trim();
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }
}
