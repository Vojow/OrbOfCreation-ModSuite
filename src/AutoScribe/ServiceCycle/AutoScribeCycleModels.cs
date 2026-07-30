using System;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal readonly struct AutoScribeCycleAction
{
    internal AutoScribeCycleAction(
        Guid recipeId,
        Guid scrollId,
        int level,
        long collectedAtFrame,
        long collectedAtEpoch)
    {
        RecipeId = recipeId;
        ScrollId = scrollId;
        Level = level;
        CollectedAtFrame = collectedAtFrame;
        CollectedAtEpoch = collectedAtEpoch;
    }

    internal Guid RecipeId { get; }
    internal Guid ScrollId { get; }
    internal int Level { get; }
    internal long CollectedAtFrame { get; }
    internal long CollectedAtEpoch { get; }
}

internal struct AutoScribeCycleState
{
    internal LifecycleGeneration Lifecycle;
    internal int DeficientRoles;
    internal int EvidenceUnknownRoles;
    internal int ExternallyProducingRoles;
    internal int CoveredRoles;
    internal int PlannedActions;
    internal int TargetLevel;
}
