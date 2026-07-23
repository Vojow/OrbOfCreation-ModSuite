namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;

public static class DecisionJournalStatusSources
{
    public static IDecisionJournalStatusSource Shared => DecisionJournalStatusRegistry.Shared;
}
