namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal interface IDecisionJournalRecordSink
{
    bool TryAppend(in DecisionJournalRecord record);
    bool TryFlush();
    void Stop();
}
