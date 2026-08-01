using System;

namespace OrbAutomata;

/// <summary>
/// One planned harvest submission: which pair, and what the world said about it when the worker
/// chose it.
/// </summary>
/// <remarks>
/// The facts travel with the action because the policy's boundary judgment decides on them: they
/// were derived from the same snapshot the worker ranked against, so a plan already known to be
/// inadmissible stops without touching the game. The safety verdict travels the same way and for the
/// same reason. The mutable native click gates are the opposite case: after the policy judgment the
/// mutation adapter re-reads them live — plot and row visibility, offered membership, one-instance
/// affordability, remaining maximum — because the game enforces them at click time and only the
/// moment of acting can answer them. One of those reads
/// (<c>PlotNodeActionInstance.IsVisible()</c>) reaches <c>Prerequisites.Container.Check()</c>, which
/// writes; that is acceptable at the mutation point and nowhere earlier, which is also why the world
/// snapshot deliberately publishes the latch instead of the read. What the snapshot cannot answer —
/// the live action queue and the instance to submit into — is likewise read where the mutation
/// happens.
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
