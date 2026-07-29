using System;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Decides which of a candidate's published effects make it worth preferring.
/// </summary>
/// <remarks>
/// <para>
/// The world says what a purchase does; this says which of those things Auto Buy cares about. That
/// split is why the effect table publishes a property name and a ratio rather than a verdict — a
/// second consumer wanting "raises my rates" reads the same rows and reaches a different answer.
/// </para>
/// <para>
/// Ported from <c>NativeStructurePriorityClassifier</c>, which asked the game the same questions per
/// candidate, per lifecycle, on the main thread. The property names and the direction of each
/// comparison are reproduced as they were.
/// </para>
/// </remarks>
internal static class AutoBuyEconomicPriorityPolicy
{
    /// <summary>
    /// The resource property whose modifier above one means a resource is worth more per unit.
    /// </summary>
    private const string Quality = "Quality";

    /// <summary>
    /// The resource properties whose modifier below one means an attribute costs less. Both spellings
    /// exist: <c>ModifiableType.AttributeCostMod</c> names the enum member a resource effect carries,
    /// and <c>AttributeCost</c> names the same thing in a resource's authored property record.
    /// </summary>
    private const string AttributeCostMod = "AttributeCostMod";
    private const string AttributeCost = "AttributeCost";

    /// <summary>
    /// The structure properties whose modifier below one means a structure costs less to buy.
    /// </summary>
    private const string Cost = "Cost";
    private const string CostScaling = "CostScaling";

    internal static AutoBuyEconomicPriority Classify(GameWorldState world, Guid candidateId)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (!WorldEntityEffectLookup.TryFindRange(world.EntityEffects, candidateId, out var start, out var count))
            return AutoBuyEconomicPriority.None;

        var priority = AutoBuyEconomicPriority.None;
        for (var index = 0; index < count; index++)
        {
            priority |= Classify(world, world.EntityEffects[start + index]);
            if (priority == (AutoBuyEconomicPriority.CostReduction | AutoBuyEconomicPriority.QualityIncrease))
                break;
        }

        return priority;
    }

    /// <summary>
    /// What one effect is worth, which depends on what it points at as well as on what it names.
    /// </summary>
    /// <remarks>
    /// A cost that falls on a structure and a quality that rises on a resource are both worth
    /// preferring; the same names against the other kind of target are not the same claim, so the
    /// target's table decides which reading applies. An effect whose ratio could not be computed is
    /// worth nothing rather than assumed neutral-or-better.
    /// </remarks>
    private static AutoBuyEconomicPriority Classify(GameWorldState world, in WorldEntityEffect effect)
    {
        if (!effect.RatioKnown) return AutoBuyEconomicPriority.None;

        if (WorldLookup.TryFind(world.Resources, effect.TargetId, out _))
        {
            if (Is(effect.Property, Quality) && effect.RatioAtOne > BigDouble.One)
                return AutoBuyEconomicPriority.QualityIncrease;
            return (Is(effect.Property, AttributeCostMod) || Is(effect.Property, AttributeCost)) &&
                effect.RatioAtOne < BigDouble.One
                ? AutoBuyEconomicPriority.CostReduction
                : AutoBuyEconomicPriority.None;
        }

        if (!WorldLookup.TryFind(world.Structures, effect.TargetId, out _))
            return AutoBuyEconomicPriority.None;

        return (Is(effect.Property, Cost) || Is(effect.Property, CostScaling)) &&
            effect.RatioAtOne < BigDouble.One
            ? AutoBuyEconomicPriority.CostReduction
            : AutoBuyEconomicPriority.None;
    }

    private static bool Is(string property, string expected) =>
        string.Equals(property, expected, StringComparison.Ordinal);
}
