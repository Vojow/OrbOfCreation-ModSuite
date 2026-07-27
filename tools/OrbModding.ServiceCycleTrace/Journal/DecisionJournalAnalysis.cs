using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;

namespace OrbModding.ServiceCycleTrace.Journal;

internal sealed class DecisionJournalAnalysis
{
    private readonly SortedDictionary<(ulong Run, ulong Service), ServiceBuilder> _services = new();

    internal long ConfigurationChanges { get; private set; }
    internal long StrategyChanges { get; private set; }
    internal long EmergencyEntered { get; private set; }
    internal long EmergencyCleared { get; private set; }

    internal void Observe(DecisionJournalRunId run, in DecisionJournalRecord record)
    {
        try
        {
            // A record with no service names the suite: emergency episodes, and the one
            // configuration record and one strategy bulletin every service reads.
            if (!record.Service.IsValid)
            {
                switch (record.Kind)
                {
                    case DecisionJournalRecordKind.ConfigurationChanged:
                        ConfigurationChanges = checked(ConfigurationChanges + 1);
                        break;
                    case DecisionJournalRecordKind.StrategyChanged:
                        StrategyChanges = checked(StrategyChanges + 1);
                        break;
                    case DecisionJournalRecordKind.EmergencyEntered:
                        EmergencyEntered = checked(EmergencyEntered + 1);
                        break;
                    case DecisionJournalRecordKind.EmergencyCleared:
                        EmergencyCleared = checked(EmergencyCleared + 1);
                        break;
                }
                return;
            }

            var key = (run.Value, record.Service.Value);
            if (!_services.TryGetValue(key, out var service))
            {
                service = new ServiceBuilder(run.Value, record.Service.Value);
                _services.Add(key, service);
            }
            service.Observe(in record);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "The retained decision-journal totals exceed the report's numeric bounds.",
                exception);
        }
    }

    internal DecisionJournalAnalysisDocument Complete()
    {
        var services = new DecisionJournalServiceSummary[_services.Count];
        var index = 0;
        foreach (var service in _services.Values)
            services[index++] = service.Complete();
        return new DecisionJournalAnalysisDocument(
            services,
            ConfigurationChanges,
            StrategyChanges,
            EmergencyEntered,
            EmergencyCleared);
    }

    private sealed class ServiceBuilder
    {
        private readonly ulong _service;
        private readonly ulong _run;
        private long _decisionSpans;
        private long _observations;
        private long _captureAttempts;
        private long _terminalCompleted;
        private long _terminalRejected;
        private long _terminalFaulted;
        private long _terminalOrphaned;
        private long _terminalUnavailable;
        private long _plannedActions;
        private long _committedActions;
        private long _nativeCalls;
        private long _mutationAttempts;
        private long _mutationsCommitted;
        private long _faultBearingObservations;
        private long _lifecycleChanges;
        private long _worldGateHolds;

        internal ServiceBuilder(ulong run, ulong service)
        {
            _run = run;
            _service = service;
        }

        internal void Observe(in DecisionJournalRecord record)
        {
            switch (record.Kind)
            {
                case DecisionJournalRecordKind.DecisionSpan:
                    _decisionSpans = checked(_decisionSpans + 1);
                    _observations = checked(_observations + record.RepeatCount);
                    if (record.FirstCycle != 0)
                        _captureAttempts = checked(_captureAttempts + record.RepeatCount);
                    if (record.FaultCategory != 0)
                    {
                        _faultBearingObservations = checked(
                            _faultBearingObservations + record.RepeatCount);
                    }
                    _plannedActions = checked(
                        _plannedActions + checked((long)record.ActionCount * record.RepeatCount));
                    _committedActions = checked(_committedActions + record.CommittedActions);
                    _nativeCalls = checked(_nativeCalls + record.NativeCallsAttempted);
                    _mutationAttempts = checked(_mutationAttempts + record.MutationAttempts);
                    _mutationsCommitted = checked(_mutationsCommitted + record.MutationsCommitted);
                    AddTerminal(record.TerminalDisposition, record.RepeatCount);
                    break;
                case DecisionJournalRecordKind.LifecycleChanged:
                    _lifecycleChanges = checked(_lifecycleChanges + 1);
                    break;
                case DecisionJournalRecordKind.WorldGateHeld:
                    _worldGateHolds = checked(_worldGateHolds + 1);
                    break;
            }
        }

        internal DecisionJournalServiceSummary Complete() => new(
            _run,
            _service,
            _decisionSpans,
            _observations,
            _captureAttempts,
            _terminalCompleted,
            _terminalRejected,
            _terminalFaulted,
            _terminalOrphaned,
            _terminalUnavailable,
            _plannedActions,
            _committedActions,
            _nativeCalls,
            _mutationAttempts,
            _mutationsCommitted,
            _faultBearingObservations,
            _lifecycleChanges,
            _worldGateHolds);

        private void AddTerminal(BatchTerminalDisposition disposition, long count)
        {
            switch (disposition)
            {
                case 0: _terminalUnavailable = checked(_terminalUnavailable + count); break;
                case BatchTerminalDisposition.Completed:
                    _terminalCompleted = checked(_terminalCompleted + count);
                    break;
                case BatchTerminalDisposition.Rejected:
                    _terminalRejected = checked(_terminalRejected + count);
                    break;
                case BatchTerminalDisposition.Faulted:
                    _terminalFaulted = checked(_terminalFaulted + count);
                    break;
                case BatchTerminalDisposition.Orphaned:
                    _terminalOrphaned = checked(_terminalOrphaned + count);
                    break;
            }
        }
    }
}

internal sealed class DecisionJournalAnalysisDocument
{
    private readonly DecisionJournalServiceSummary[] _services;

    internal DecisionJournalAnalysisDocument(
        DecisionJournalServiceSummary[] services,
        long configurationChanges,
        long strategyChanges,
        long emergencyEntered,
        long emergencyCleared)
    {
        _services = services;
        ConfigurationChanges = configurationChanges;
        StrategyChanges = strategyChanges;
        EmergencyEntered = emergencyEntered;
        EmergencyCleared = emergencyCleared;
    }

    internal int ServiceCount => _services.Length;
    internal long ConfigurationChanges { get; }
    internal long StrategyChanges { get; }
    internal long EmergencyEntered { get; }
    internal long EmergencyCleared { get; }
    internal DecisionJournalServiceSummary GetService(int index) => _services[index];
}

internal readonly struct DecisionJournalServiceSummary
{
    internal DecisionJournalServiceSummary(
        ulong run,
        ulong service,
        long decisionSpans,
        long observations,
        long captureAttempts,
        long terminalCompleted,
        long terminalRejected,
        long terminalFaulted,
        long terminalOrphaned,
        long terminalUnavailable,
        long plannedActions,
        long committedActions,
        long nativeCalls,
        long mutationAttempts,
        long mutationsCommitted,
        long faultBearingObservations,
        long lifecycleChanges,
        long worldGateHolds)
    {
        Run = run;
        Service = service;
        DecisionSpans = decisionSpans;
        Observations = observations;
        CaptureAttempts = captureAttempts;
        TerminalCompleted = terminalCompleted;
        TerminalRejected = terminalRejected;
        TerminalFaulted = terminalFaulted;
        TerminalOrphaned = terminalOrphaned;
        TerminalUnavailable = terminalUnavailable;
        PlannedActions = plannedActions;
        CommittedActions = committedActions;
        NativeCalls = nativeCalls;
        MutationAttempts = mutationAttempts;
        MutationsCommitted = mutationsCommitted;
        FaultBearingObservations = faultBearingObservations;
        LifecycleChanges = lifecycleChanges;
        WorldGateHolds = worldGateHolds;
    }

    internal ulong Run { get; }
    internal ulong Service { get; }
    internal long DecisionSpans { get; }
    internal long Observations { get; }
    internal long CaptureAttempts { get; }
    internal long TerminalCompleted { get; }
    internal long TerminalRejected { get; }
    internal long TerminalFaulted { get; }
    internal long TerminalOrphaned { get; }
    internal long TerminalUnavailable { get; }
    internal long PlannedActions { get; }
    internal long CommittedActions { get; }
    internal long NativeCalls { get; }
    internal long MutationAttempts { get; }
    internal long MutationsCommitted { get; }
    internal long FaultBearingObservations { get; }
    internal long LifecycleChanges { get; }

    /// <summary>
    /// How often the world freshness gate began holding this service closed. A hold is counted once
    /// however long it lasts, so a small number beside a service that committed nothing is a stall.
    /// </summary>
    internal long WorldGateHolds { get; }
}
