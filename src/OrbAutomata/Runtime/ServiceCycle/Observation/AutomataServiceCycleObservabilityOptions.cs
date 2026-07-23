namespace OrbAutomata;

internal readonly struct AutomataServiceCycleObservabilityOptions
{
    internal AutomataServiceCycleObservabilityOptions(
        AutomataFullTraceOptions fullTrace,
        AutomataDecisionJournalOptions decisionJournal)
    {
        FullTrace = fullTrace;
        DecisionJournal = decisionJournal;
    }

    internal AutomataFullTraceOptions FullTrace { get; }
    internal AutomataDecisionJournalOptions DecisionJournal { get; }
}
