namespace OrbAutomata;

/// <summary>
/// What a harvest pair looks like to the policy: everything the decision rests on that the shared
/// world snapshot can answer.
/// </summary>
/// <remarks>
/// Two things the policy also weighs are deliberately absent, for the same reason. The live action
/// queue changes with every action anyone takes, including this service's own. The action's audited
/// safety is a property of the build's authored content, which no publication carries and which the
/// suite re-verifies structurally rather than trusting a cached verdict. Both are checked where the
/// mutation happens — see <see cref="AutoHarvestPolicy.EvaluateSubmission"/>.
/// </remarks>
internal readonly struct AutoHarvestPairFacts
{
    public AutoHarvestPairFacts(
        AutoHarvestEvidenceState identity,
        AutoHarvestEvidenceState plotVisibility,
        AutoHarvestEvidenceState actionAvailability,
        AutoHarvestEvidenceState prerequisites,
        AutoHarvestEvidenceState readiness)
    {
        Identity = identity;
        PlotVisibility = plotVisibility;
        ActionAvailability = actionAvailability;
        Prerequisites = prerequisites;
        Readiness = readiness;
    }

    public AutoHarvestEvidenceState Identity { get; }
    public AutoHarvestEvidenceState PlotVisibility { get; }
    public AutoHarvestEvidenceState ActionAvailability { get; }

    /// <summary>
    /// Whether the game has confirmed the action's prerequisites. Verified is a verdict the game
    /// reached; anything else is the lack of one, because the native latch behind this cannot say
    /// whether it has ever been evaluated — see <c>RawPlotAction.PrerequisitesConfirmed</c>.
    /// </summary>
    public AutoHarvestEvidenceState Prerequisites { get; }

    public AutoHarvestEvidenceState Readiness { get; }
}
