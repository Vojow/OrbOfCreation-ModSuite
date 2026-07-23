namespace OrbAutomata;

internal readonly struct AutoHarvestPairFacts
{
    public AutoHarvestPairFacts(
        AutoHarvestEvidenceState identity,
        AutoHarvestEvidenceState plotVisibility,
        AutoHarvestEvidenceState actionAvailability,
        AutoHarvestEvidenceState prerequisites,
        AutoHarvestEvidenceState readiness,
        AutoHarvestActionSafetyState actionSafety,
        AutoHarvestEvidenceState noDuplicate,
        AutoHarvestEvidenceState actionSlotAvailability)
    {
        Identity = identity;
        PlotVisibility = plotVisibility;
        ActionAvailability = actionAvailability;
        Prerequisites = prerequisites;
        Readiness = readiness;
        ActionSafety = actionSafety;
        NoDuplicate = noDuplicate;
        ActionSlotAvailability = actionSlotAvailability;
    }

    public AutoHarvestEvidenceState Identity { get; }
    public AutoHarvestEvidenceState PlotVisibility { get; }
    public AutoHarvestEvidenceState ActionAvailability { get; }
    public AutoHarvestEvidenceState Prerequisites { get; }
    public AutoHarvestEvidenceState Readiness { get; }
    public AutoHarvestActionSafetyState ActionSafety { get; }
    public AutoHarvestEvidenceState NoDuplicate { get; }
    public AutoHarvestEvidenceState ActionSlotAvailability { get; }
}
