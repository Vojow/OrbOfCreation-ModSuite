namespace OrbAutomata;

internal readonly struct AutomataServiceCycleObservabilityOptions
{
    internal AutomataServiceCycleObservabilityOptions(
        AutomataFullTraceOptions fullTrace,
        AutomataDecisionJournalOptions decisionJournal,
        bool autoStartDiagnosticSessions = false)
    {
        FullTrace = fullTrace;
        DecisionJournal = decisionJournal;
        AutoStartDiagnosticSessions = autoStartDiagnosticSessions;
    }

    internal AutomataFullTraceOptions FullTrace { get; }
    internal AutomataDecisionJournalOptions DecisionJournal { get; }
    internal bool AutoStartDiagnosticSessions { get; }
}
