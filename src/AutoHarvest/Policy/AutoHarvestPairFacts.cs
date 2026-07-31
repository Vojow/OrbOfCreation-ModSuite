using OrbModding.Common.Runtime.World;

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
        PlotActionPrerequisiteEvidence prerequisites,
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
    /// What the native prerequisite latch proves and whether the exact action boundary must ask the
    /// game's validator for a current verdict.
    /// </summary>
    public PlotActionPrerequisiteEvidence Prerequisites { get; }

    public AutoHarvestEvidenceState Readiness { get; }
}
