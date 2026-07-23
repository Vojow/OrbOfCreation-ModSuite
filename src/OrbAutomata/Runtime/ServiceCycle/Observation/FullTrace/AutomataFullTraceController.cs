using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Control;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbAutomata;

internal sealed class AutomataFullTraceController : IDisposable
{
    private readonly FullTraceRuntimeSession _session;
    private readonly ManualFullTraceControlRegistration _control;
    private readonly IAutomataFullTraceSessionSource _sessions;
    private readonly IMonotonicClock _clock;
    private MonotonicTimestamp _startedAt;
    private TimeSpan _terminalDuration;
    private ManualFullTraceStatus _lastPublished = ManualFullTraceStatus.Idle;
    private string _artifactName = string.Empty;
    private bool _terminalDurationSet;
    private bool _startFailed;
    private bool _disposed;

    private AutomataFullTraceController(
        SuiteFramePump pump,
        int serviceCapacity,
        IMonotonicClock clock,
        IAutomataFullTraceSessionSource sessions,
        ManualFullTraceControlRegistration control)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _session = new FullTraceRuntimeSession(pump, serviceCapacity);
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _control = control ?? throw new ArgumentNullException(nameof(control));
    }

    internal static AutomataFullTraceController? TryCreate(
        SuiteFramePump pump,
        int serviceCapacity,
        IMonotonicClock clock,
        in AutomataFullTraceOptions options)
    {
        if (!options.Enabled || options.Control is null || options.Sessions is null)
            throw new ArgumentException("Enabled full-trace options are required.", nameof(options));
        if (!options.Control.TryRegister(out var control) || control is null) return null;
        try
        {
            return new AutomataFullTraceController(
                pump,
                serviceCapacity,
                clock,
                options.Sessions,
                control);
        }
        catch
        {
            control.Dispose();
            throw;
        }
    }

    internal void BeforePump()
    {
        if (_disposed) return;
        var commandTaken = _control.TryTakeCommand(out var command);
        if (commandTaken) Apply(command);
        if (_startFailed) return;
        if (!commandTaken && !IsActive(_lastPublished.State)) return;
        _session.Tick();
        PublishSession();
    }

    internal void AfterPump()
    {
        if (_disposed || _startFailed || !IsActive(_lastPublished.State)) return;
        _session.Tick();
        PublishSession();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _session.Dispose(); }
        finally { _control.Dispose(); }
    }

    private void Apply(ManualFullTraceCommand command)
    {
        if (command == ManualFullTraceCommand.Stop)
        {
            _session.RequestStop();
            return;
        }
        if (command != ManualFullTraceCommand.Start)
            throw new InvalidOperationException("The manual full-trace command is invalid.");

        _startFailed = false;
        _artifactName = string.Empty;
        _terminalDuration = TimeSpan.Zero;
        _terminalDurationSet = false;
        _startedAt = _clock.Now;
        try
        {
            var spec = _sessions.Create();
            _artifactName = spec.ArtifactName;
            _session.Start(spec.Session, spec.SemanticSession, spec.Storage);
        }
        catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
        {
            _startFailed = true;
            Publish(new ManualFullTraceStatus(
                ManualFullTraceState.Incomplete,
                Elapsed(terminal: true),
                0,
                0,
                0,
                0,
                1,
                false,
                ManualFullTraceResult.InitializationFailed,
                _artifactName));
        }
    }

    private void PublishSession()
    {
        var snapshot = _session.Snapshot;
        var state = MapState(snapshot.State);
        var terminal = state is ManualFullTraceState.Complete or ManualFullTraceState.Incomplete;
        Publish(new ManualFullTraceStatus(
            state,
            state == ManualFullTraceState.Idle ? TimeSpan.Zero : Elapsed(terminal),
            snapshot.AcceptedRecords,
            snapshot.WrittenRecords,
            snapshot.BytesWritten,
            snapshot.SegmentCount,
            snapshot.FirstIncompleteSequence,
            snapshot.ManifestCommitted,
            terminal ? MapResult(in snapshot) : ManualFullTraceResult.None,
            state == ManualFullTraceState.Idle ? string.Empty : _artifactName));
    }

    private TimeSpan Elapsed(bool terminal)
    {
        if (_terminalDurationSet) return _terminalDuration;
        var elapsed = (_clock.Now - _startedAt).ToTimeSpan();
        if (terminal)
        {
            _terminalDuration = elapsed;
            _terminalDurationSet = true;
            return elapsed;
        }
        return TimeSpan.FromTicks(
            elapsed.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond);
    }

    private void Publish(ManualFullTraceStatus status)
    {
        var unchangedExceptAccepted = status.State == _lastPublished.State &&
            status.Duration == _lastPublished.Duration &&
            status.WrittenRecords == _lastPublished.WrittenRecords &&
            status.BytesWritten == _lastPublished.BytesWritten &&
            status.SegmentCount == _lastPublished.SegmentCount &&
            status.FirstIncompleteSequence == _lastPublished.FirstIncompleteSequence &&
            status.ManifestCommitted == _lastPublished.ManifestCommitted &&
            status.Result == _lastPublished.Result &&
            string.Equals(status.ArtifactName, _lastPublished.ArtifactName, StringComparison.Ordinal);
        if (unchangedExceptAccepted) return;
        _lastPublished = status;
        _control.Publish(status);
    }

    private static ManualFullTraceState MapState(FullTraceRuntimeSessionState state) => state switch
    {
        FullTraceRuntimeSessionState.Idle => ManualFullTraceState.Idle,
        FullTraceRuntimeSessionState.Arming => ManualFullTraceState.Arming,
        FullTraceRuntimeSessionState.Recording => ManualFullTraceState.Recording,
        FullTraceRuntimeSessionState.Stopping => ManualFullTraceState.Stopping,
        FullTraceRuntimeSessionState.Complete => ManualFullTraceState.Complete,
        FullTraceRuntimeSessionState.Incomplete => ManualFullTraceState.Incomplete,
        _ => throw new InvalidOperationException("The full-trace session state is invalid."),
    };

    private static bool IsActive(ManualFullTraceState state) =>
        state is ManualFullTraceState.Arming or ManualFullTraceState.Recording or
            ManualFullTraceState.Stopping;

    private static ManualFullTraceResult MapResult(in FullTraceRuntimeSessionSnapshot snapshot)
    {
        if (snapshot.State == FullTraceRuntimeSessionState.Complete)
        {
            return snapshot.TerminalReason switch
            {
                FullTraceTerminalReason.UserStopped => ManualFullTraceResult.UserStopped,
                FullTraceTerminalReason.RuntimeShutdown => ManualFullTraceResult.RuntimeShutdown,
                _ => throw new InvalidOperationException("A complete full trace has no terminal result."),
            };
        }

        return snapshot.FaultReason switch
        {
            BufferedSegmentFaultReason.BufferExhausted => ManualFullTraceResult.BufferExhausted,
            BufferedSegmentFaultReason.SequenceExhausted => ManualFullTraceResult.SequenceExhausted,
            BufferedSegmentFaultReason.InitializationFailed => ManualFullTraceResult.InitializationFailed,
            BufferedSegmentFaultReason.WriteFailed => ManualFullTraceResult.WriteFailed,
            BufferedSegmentFaultReason.CompletionFailed => ManualFullTraceResult.CompletionFailed,
            BufferedSegmentFaultReason.ProducerFailed => ManualFullTraceResult.SemanticFault,
            BufferedSegmentFaultReason.ProducerStopped => ManualFullTraceResult.RuntimeShutdown,
            _ => throw new InvalidOperationException("An incomplete full trace has no failure result."),
        };
    }
}
