using System;

namespace OrbModding.Common.Runtime.GameMath;

/// <summary>
/// One resource entry in a cost, ported from the game's <c>ResourceTuple</c>. Identity is the
/// stable resource UUID rather than a native reference, so a cost can cross to a worker thread.
/// </summary>
internal readonly struct GameResourceCost
{
    internal GameResourceCost(Guid resourceId, BigDouble value)
    {
        ResourceId = resourceId;
        Value = value;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Value { get; }

    /// <summary>Ported from <c>ResourceTuple.NewValue</c>: same resource, replaced magnitude.</summary>
    internal GameResourceCost WithValue(BigDouble value) => new(ResourceId, value);
}

/// <summary>
/// The purchase-cost calculation, ported so it can run on a worker thread without allocating.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> This is the measured hot path. The original
/// <c>StructureSO.GetNextCost()</c> is:
/// </para>
/// <code>
/// baseCost.AdjustAsAttribute()
///     .AdjustWith(costPerQuantity.GetModifier()
///         .MultiplyScalar(costScalingMod.AsPercent())
///         .MultiplyScalar(quantity + queuedQuantity))
///     .Multiply(GetNextCostMod().AsPercent())
///     .RoundToTwoSigsEarly();
/// </code>
/// <para>
/// Every one of those transforms is <c>new ResourceCostList(costs.Select(…).ToList())</c> — a LINQ
/// projection, a <c>ToList</c>, and a new object — so a single cost query allocates roughly half a
/// dozen lists, and it is issued per candidate per collect. After the other session removed
/// reflection from capture, cost invocation still cost ~3.6 ms per collect; this is that cost.
/// </para>
/// <para>
/// This port keeps the same sequence and the same arithmetic but rewrites the rows in place in a
/// caller-owned span, so the whole chain allocates nothing.
/// </para>
/// <para>
/// Provenance: <c>Assembly-CSharp.dll</c> SHA-256 <c>5652EBE3…</c>, audited baseline
/// <c>steam-macos-2026-07-13</c>. Valid only for that build; <see cref="SuiteLoadGate"/> refuses to
/// load the suite against any other.
/// </para>
/// </remarks>
internal static class GameCostMath
{
    /// <summary>
    /// Ported from <c>ResourceCostList.AdjustAsAttribute()</c>: scale each entry by its own
    /// resource's attribute-cost modifier.
    /// </summary>
    /// <param name="costs">Rewritten in place.</param>
    /// <param name="attributeCostModPercents">
    /// Per-entry <c>resource.GetAttributeCostMod().AsPercent()</c>, positionally aligned with
    /// <paramref name="costs"/>. Supplied by the caller because it is a per-resource live reading,
    /// which keeps this function pure and worker-safe.
    /// </param>
    internal static void AdjustAsAttribute(
        Span<GameResourceCost> costs,
        ReadOnlySpan<BigDouble> attributeCostModPercents)
    {
        if (attributeCostModPercents.Length != costs.Length)
        {
            throw new ArgumentException(
                "One attribute-cost modifier is required per cost entry.",
                nameof(attributeCostModPercents));
        }

        for (var index = 0; index < costs.Length; index++)
        {
            costs[index] = costs[index].WithValue(costs[index].Value * attributeCostModPercents[index]);
        }
    }

    /// <summary>
    /// Ported from <c>ResourceCostList.AdjustWith(ValueModifier)</c>: run every entry through the
    /// modifier stack.
    /// </summary>
    internal static void AdjustWith(
        Span<GameResourceCost> costs,
        ReadOnlySpan<GameValueModifier> modifiers)
    {
        for (var index = 0; index < costs.Length; index++)
        {
            costs[index] = costs[index].WithValue(
                GameModifierStack.AdjustWith(costs[index].Value, modifiers));
        }
    }

    /// <summary>Ported from <c>ResourceCostList.Multiply(BigDouble)</c>.</summary>
    internal static void Multiply(Span<GameResourceCost> costs, BigDouble factor)
    {
        for (var index = 0; index < costs.Length; index++)
        {
            costs[index] = costs[index].WithValue(costs[index].Value * factor);
        }
    }

    /// <summary>Ported from <c>ResourceCostList.RoundToTwoSigs()</c>.</summary>
    internal static void RoundToTwoSigs(Span<GameResourceCost> costs)
    {
        for (var index = 0; index < costs.Length; index++)
        {
            costs[index] = costs[index].WithValue(OrbGameMath.RoundToTwoSigs(costs[index].Value));
        }
    }

    /// <summary>
    /// Ported from <c>UpgradeSO.GetLeveledCostList()</c> — the cost of an upgrade's next level, which
    /// shares nothing with the structure chain above.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The original:
    /// </para>
    /// <code>
    /// int num = Math.Min(level + queuedLevels, HasFiniteLevels() ? maxLevel - 1 : int.MaxValue);
    /// cachedCost = resourceCost.SetToLevel(resourceCostModPerLevel, num + 1).RoundToTwoSigs();
    /// // SetToLevel(list, n) => n == 1 ? costs : list.MultiplyScalar(n - 1).Adjust(cost)
    /// </code>
    /// <para>
    /// Two things are easy to get wrong. The level passed in is <em>one past</em> the committed level,
    /// and the modifier is scaled by that minus one — so an upgrade with nothing bought and nothing
    /// queued takes the <c>n == 1</c> branch and pays its authored cost untouched. And the rounding is
    /// <see cref="OrbGameMath.RoundToTwoSigs"/>, not the <c>…Early</c> variant the structure chain
    /// ends with, so an upgrade's price is snapped at every magnitude rather than only below 100.
    /// </para>
    /// <para>
    /// Every span is caller-owned. <paramref name="scaledModifiers"/> and
    /// <paramref name="scaledExponents"/> must match their sources in length, and
    /// <paramref name="scratch"/> must be at least as long as the modifiers.
    /// </para>
    /// </remarks>
    /// <param name="level">
    /// <c>min(level + queuedLevels, maxLevel - 1 when finite) + 1</c> — the level being priced.
    /// </param>
    internal static void ComputeLeveledCost(
        Span<GameResourceCost> costs,
        ReadOnlySpan<GameValueModifier> perLevelModifiers,
        ReadOnlySpan<GameValueModifier> perLevelExponents,
        int level,
        Span<GameValueModifier> scaledModifiers,
        Span<GameValueModifier> scaledExponents,
        Span<GameValueModifier> scratch)
    {
        if (level != 1 && perLevelModifiers.Length > 0)
        {
            if (scaledModifiers.Length < perLevelModifiers.Length ||
                scaledExponents.Length < perLevelExponents.Length)
            {
                throw new ArgumentException(
                    "The scaled spans must hold one entry per source modifier.",
                    nameof(scaledModifiers));
            }

            var scalar = new BigDouble(level - 1);
            for (var index = 0; index < perLevelModifiers.Length; index++)
                scaledModifiers[index] = perLevelModifiers[index].MultiplyScalar(scalar);
            for (var index = 0; index < perLevelExponents.Length; index++)
                scaledExponents[index] = perLevelExponents[index].MultiplyScalar(scalar);

            var modifiers = (ReadOnlySpan<GameValueModifier>)scaledModifiers[..perLevelModifiers.Length];
            var exponents = (ReadOnlySpan<GameValueModifier>)scaledExponents[..perLevelExponents.Length];

            for (var index = 0; index < costs.Length; index++)
            {
                costs[index] = costs[index].WithValue(
                    GameModifierStack.AdjustWith(costs[index].Value, modifiers, exponents, scratch));
            }
        }

        RoundToTwoSigs(costs);
    }

    /// <summary>Ported from <c>ResourceCostList.RoundToTwoSigsEarly()</c>.</summary>
    internal static void RoundToTwoSigsEarly(Span<GameResourceCost> costs)
    {
        for (var index = 0; index < costs.Length; index++)
        {
            costs[index] = costs[index].WithValue(OrbGameMath.RoundToTwoSigsEarly(costs[index].Value));
        }
    }

    /// <summary>
    /// Ported from <c>StructureSO.GetNextCost()</c> — the cost of the structure's next level.
    /// </summary>
    /// <param name="costs">
    /// Seeded with the structure's <c>baseCost</c> entries and rewritten in place into the result.
    /// </param>
    /// <param name="attributeCostModPercents">Per-entry attribute-cost modifier, as a percent.</param>
    /// <param name="costPerQuantity">The structure's per-quantity cost modifier.</param>
    /// <param name="costScalingModPercent">The structure's cost-scaling modifier, as a percent.</param>
    /// <param name="committedQuantity">
    /// <c>quantity + queuedQuantity</c>. Queued levels count: they are already bought, so the next
    /// level is priced beyond them. Pricing on owned quantity alone under-prices every structure
    /// with work in flight.
    /// </param>
    /// <param name="nextCostModPercent">The structure's next-cost modifier, as a percent.</param>
    internal static void ComputeNextCost(
        Span<GameResourceCost> costs,
        ReadOnlySpan<BigDouble> attributeCostModPercents,
        in GameValueModifier costPerQuantity,
        BigDouble costScalingModPercent,
        BigDouble committedQuantity,
        BigDouble nextCostModPercent)
    {
        AdjustAsAttribute(costs, attributeCostModPercents);

        // The original scales the per-quantity modifier twice before applying it: once by the
        // cost-scaling percent, then by how many levels are already committed.
        var scaled = costPerQuantity
            .MultiplyScalar(costScalingModPercent)
            .MultiplyScalar(committedQuantity);

        Span<GameValueModifier> single = stackalloc GameValueModifier[1];
        single[0] = scaled;
        AdjustWith(costs, single);

        Multiply(costs, nextCostModPercent);
        RoundToTwoSigsEarly(costs);
    }

    /// <summary>
    /// Ported from <c>StructureSO.GetNextCostMod()</c> — the factor
    /// <see cref="ComputeNextCost"/> takes as <c>nextCostModPercent</c>, once
    /// <see cref="OrbGameMath.AsPercent"/> has been applied to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The original, with its two one-line accessors inlined:
    /// </para>
    /// <code>
    /// Max(passiveCostMod, 100 / costPerQuantity.GetMod().MultiplyScalar(GetNextQuantity()).Adjust(1))
    ///     * (activeCostMod * Player.GetStructureCost().AsPercent()).AsPercent()
    /// </code>
    /// <para>
    /// The <c>Max</c> is a floor, and the term it floors is a reciprocal: the stronger the
    /// per-quantity modifier has grown by this level, the smaller <c>100 / …</c> becomes, so the
    /// passive modifier takes over once scaling would otherwise drive the multiplier below it.
    /// <c>Adjust(1)</c> evaluates the modifier against a base of one, which is what makes the result
    /// a pure factor rather than a cost.
    /// </para>
    /// <para>
    /// Both operands of the <c>Max</c> are on the game's percent scale, where 100 means "unchanged",
    /// which is why the literal is <c>100</c> and not <c>1</c>.
    /// </para>
    /// </remarks>
    /// <param name="passiveCostMod">The structure's <c>passiveCostMod</c> record value.</param>
    /// <param name="activeCostMod">The structure's <c>activeCostMod</c> record value.</param>
    /// <param name="costPerQuantity">The structure's per-quantity cost modifier.</param>
    /// <param name="nextQuantity">
    /// <c>GetBaseLevel() + queuedQuantity</c> — the level this purchase would produce.
    /// </param>
    /// <param name="structureCostPercent">
    /// <c>Player.GetStructureCost()</c> already through <see cref="OrbGameMath.AsPercent"/>, because
    /// it is a frame-wide global read once per collection rather than per structure.
    /// </param>
    internal static BigDouble ComputeNextCostMod(
        BigDouble passiveCostMod,
        BigDouble activeCostMod,
        in GameValueModifier costPerQuantity,
        BigDouble nextQuantity,
        BigDouble structureCostPercent)
    {
        var scaled = costPerQuantity.MultiplyScalar(nextQuantity).Adjust(BigDouble.One);
        return BigDouble.Max(passiveCostMod, (BigDouble)100 / scaled) *
            OrbGameMath.AsPercent(activeCostMod * structureCostPercent);
    }
}
