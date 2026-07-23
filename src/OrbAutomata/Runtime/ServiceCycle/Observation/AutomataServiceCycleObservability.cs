using System;
using BepInEx.Logging;
using OrbModding.Common.Runtime;
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
        bool replayActive,
        ManualLogSource log)
    {
#if SERVICE_CYCLE_PROFILE
        var profile = AutomataServiceCycleProfileController.TryCreate(
            clock,
            replayActive,
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
        in AutomataServiceCycleObservabilityOptions options)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AutomataServiceCycleObservability));
        if (_attached) throw new InvalidOperationException("ServiceCycle observability is already attached.");
        if (pump is null) throw new ArgumentNullException(nameof(pump));

        AutomataFullTraceController? fullTrace = null;
        try
        {
            var fullTraceOptions = options.FullTrace;
            if (fullTraceOptions.Enabled)
            {
                fullTrace = AutomataFullTraceController.TryCreate(
                    pump,
                    serviceCapacity,
                    _clock,
                    in fullTraceOptions);
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

            _fullTrace = fullTrace;
            _decisionJournal = decisionJournal;
            _attached = true;
        }
        catch
        {
            fullTrace?.Dispose();
            throw;
        }
    }

    internal void BeforePump()
    {
        _fullTrace?.BeforePump();
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
        _fullTrace?.Dispose();
    }
}
