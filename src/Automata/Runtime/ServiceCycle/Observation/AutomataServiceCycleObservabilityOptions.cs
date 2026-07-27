namespace OrbAutomata;

internal readonly struct AutomataServiceCycleObservabilityOptions
{
    internal AutomataServiceCycleObservabilityOptions(
        AutomataFullTraceOptions fullTrace,
        AutomataDecisionJournalOptions decisionJournal,
        AutomataHostTraceOptions hostTrace = default,
        bool autoStartDiagnosticSessions = false)
    {
        FullTrace = fullTrace;
        DecisionJournal = decisionJournal;
        HostTrace = hostTrace;
        AutoStartDiagnosticSessions = autoStartDiagnosticSessions;
    }

    internal AutomataFullTraceOptions FullTrace { get; }
    internal AutomataDecisionJournalOptions DecisionJournal { get; }
    internal AutomataHostTraceOptions HostTrace { get; }
    internal bool AutoStartDiagnosticSessions { get; }
}
