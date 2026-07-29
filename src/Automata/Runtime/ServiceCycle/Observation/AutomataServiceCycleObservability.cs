using System;
using BepInEx.Logging;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Control;
#endif

namespace OrbAutomata;

internal sealed class AutomataServiceCycleObservability : IDisposable
{
    private readonly IMonotonicClock _clock;
    private readonly ManualLogSource _log;
#if SERVICE_CYCLE_PROFILE
    private readonly AutomataServiceCycleProfileController? _profile;
    private readonly ServiceCycleProfileProbe _profileProbe;
#endif
    private AutomataFullTraceController? _fullTrace;
    private AutomataHostTraceController? _hostTrace;
    private AutomataDecisionJournalController? _decisionJournal;
    private bool _attached;
    private bool _disposed;

    private AutomataServiceCycleObservability(
        IMonotonicClock clock,
        ManualLogSource log
#if SERVICE_CYCLE_PROFILE
        , AutomataServiceCycleProfileController? profile
#endif
        )
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
#if SERVICE_CYCLE_PROFILE
        _profile = profile;
        _profileProbe = profile?.Probe ?? new ServiceCycleProfileProbe();
#endif
    }

    internal static AutomataServiceCycleObservability Create(
        IMonotonicClock clock,
        bool traceActive,
        ManualLogSource log)
    {
#if SERVICE_CYCLE_PROFILE
        var profile = AutomataServiceCycleProfileController.TryCreate(
            clock,
            traceActive,
            log,
            PerformanceProfileControlRegistry.Shared);
        return new AutomataServiceCycleObservability(clock, log, profile);
#else
        return new AutomataServiceCycleObservability(clock, log);
#endif
    }

#if SERVICE_CYCLE_PROFILE
    internal ServiceCycleProfileProbe ProfileProbe => _profileProbe;
#endif

    internal Func<bool>? PumpShutdown =>
        _decisionJournal is null ? null : _decisionJournal.DisposeWithPump;

    internal void Attach(
        SuiteFramePump pump,
        int serviceCapacity,
        ServiceCycleTraceRoster roster,
        in AutomataServiceCycleObservabilityOptions options)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AutomataServiceCycleObservability));
        if (_attached) throw new InvalidOperationException("ServiceCycle observability is already attached.");
        if (pump is null) throw new ArgumentNullException(nameof(pump));

        SweepRunFolders();
        AutomataFullTraceController? fullTrace = null;
        try
        {
            var fullTraceOptions = options.FullTrace;
            if (fullTraceOptions.Enabled)
            {
                fullTrace = AutomataFullTraceController.TryCreate(
                    pump,
                    serviceCapacity,
                    roster,
                    _clock,
                    in fullTraceOptions);
            }

            var hostTraceOptions = options.HostTrace;
            if (hostTraceOptions.Enabled)
            {
                _hostTrace = AutomataHostTraceController.TryCreate(
                    pump,
                    serviceCapacity,
                    roster,
                    in hostTraceOptions);
            }

            AutomataDecisionJournalController? decisionJournal = null;
            var journalOptions = options.DecisionJournal;
            if (journalOptions.Enabled)
            {
                decisionJournal = AutomataDecisionJournalController.TryCreate(
                    pump,
                    in journalOptions,
                    _log);
            }

            if (options.AutoStartDiagnosticSessions)
            {
                fullTrace?.StartAutomatically();
#if SERVICE_CYCLE_PROFILE
                _profile?.StartAutomatically();
#endif
            }

            _fullTrace = fullTrace;
            _decisionJournal = decisionJournal;
            _attached = true;
        }
        catch
        {
            fullTrace?.Dispose();
            _hostTrace?.Dispose();
            _hostTrace = null;
            throw;
        }
    }

    /// <summary>
    /// The journal's own cap governs the journal directory only. Run folders are the other half of
    /// the suite's disk use, and nothing else deletes them, so a launch prunes the oldest before it
    /// can add one more.
    /// </summary>
    private void SweepRunFolders()
    {
        var removed = AutomataTraceRunRoot.SweepRunFolders();
        if (removed == 0) return;
        _log.LogAutomataInfo(
            "ServiceCycle trace retention removed " + removed +
            " old capture folder(s); the newest " + AutomataTraceRunRoot.RetainedRunFolders +
            " are kept.");
    }

    internal void BeforePump()
    {
        _fullTrace?.BeforePump();
        _hostTrace?.BeforePump();
        _decisionJournal?.Tick();
    }

    internal void AfterPump()
    {
        _fullTrace?.AfterPump();
#if SERVICE_CYCLE_PROFILE
        _profile?.AfterPump();
#endif
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
#if SERVICE_CYCLE_PROFILE
        _profile?.Dispose();
#endif
        _hostTrace?.Dispose();
        _fullTrace?.Dispose();
    }
}
