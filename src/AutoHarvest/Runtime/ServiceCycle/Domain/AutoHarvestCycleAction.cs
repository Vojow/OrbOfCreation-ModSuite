using System;

namespace OrbAutomata;

/// <summary>
/// One planned harvest submission: which pair, and what the world said about it when the worker
/// chose it.
/// </summary>
/// <remarks>
/// The facts travel with the action because the boundary decides on them. They were derived from the
/// same snapshot the worker ranked against, so re-deriving them from the live game at submission
/// time would be asking a second source the question the plan already answered — and one of those
/// live reads (<c>PlotNodeActionInstance.IsVisible()</c>) reaches
/// <c>Prerequisites.Container.Check()</c>, which writes. The safety verdict travels the same way and
/// for the same reason. What the snapshot cannot answer — the live action queue and the instance to
/// submit into — is still read where the mutation happens.
/// </remarks>
internal readonly struct AutoHarvestCycleAction
{
    // The facts are taken by value, not by reference: a published action's constructor surface may
    // not alias anything the caller still holds, and the structural validator enforces it.
    public AutoHarvestCycleAction(
        AutoHarvestPair pair,
        AutoHarvestPairFacts facts,
        AutoHarvestActionSafetyState safety)
    {
        if (pair is not AutoHarvestPair.FruitTree and not AutoHarvestPair.TreasureTree)
            throw new ArgumentOutOfRangeException(nameof(pair));
        Pair = pair;
        Facts = facts;
        Safety = safety;
    }

    public AutoHarvestPair Pair { get; }

    public AutoHarvestPairFacts Facts { get; }

    /// <summary>What the snapshot said about the pair's authored content when the worker chose it.</summary>
    public AutoHarvestActionSafetyState Safety { get; }
}
