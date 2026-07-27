namespace OrbModding.Common.Runtime.GameMath;

/// <summary>
/// A threshold that scales with the level it is asked about, ported from
/// <c>Requirements.LeveledValue</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Provenance.</b> <c>Requirements.LeveledValue.AtLevel(long)</c> and the two
/// <c>ValueModifier.SumSequenceAdjust</c> overloads in <c>Assembly-CSharp.dll</c>, SHA-256
/// <c>5652EBE3…</c> (audited baseline <c>steam-macos-2026-07-13</c>). See <see cref="OrbGameMath"/>
/// for why the suite owns this arithmetic rather than asking the game for it.
/// </para>
/// <para>
/// The inputs are authored data and one level number, and nothing at runtime can move them: a
/// <c>ValueModifier</c> inside a <c>LeveledValue</c> is a plain serialized struct whose identity is
/// its own rather than a pointer at a live variable. So a threshold is a pure function, and the whole
/// per-level requirement question reduces to comparing it against a value the snapshot already
/// publishes.
/// </para>
/// <para>
/// Every entry point answers <see langword="false"/> rather than throwing where the original throws.
/// The original's sequence sum has no closed form for a <c>Reduction</c> or <c>Exponent</c> modifier
/// and says so with an exception; a threshold that cannot be computed must make its condition
/// unevaluable, which makes its owner inadmissible, rather than take down the pass that asked.
/// </para>
/// </remarks>
internal static class GameLeveledValue
{
    /// <summary>
    /// Ported from <c>LeveledValue.AtLevel(long)</c>: the threshold at
    /// <paramref name="level"/>.
    /// </summary>
    /// <remarks>
    /// The three branches are the original's. At or below level nought the authored base value stands
    /// on its own; with no second modifier the first is simply scaled by the level and applied; with
    /// one, the first is summed as a sequence over <c>level - 1</c> terms before being applied. The
    /// off-by-one in that last argument is the original's and is what makes level one yield the first
    /// term rather than the second.
    /// </remarks>
    internal static bool TryAtLevel(
        BigDouble baseValue,
        in GameValueModifier perLevel,
        in GameValueModifier modPerLevel,
        long level,
        out BigDouble value)
    {
        if (level <= 0)
        {
            value = baseValue;
            return true;
        }

        if (modPerLevel.IsEmpty())
        {
            value = perLevel.MultiplyScalar(level).Adjust(baseValue);
            return true;
        }

        if (!TrySumSequenceAdjust(in modPerLevel, in perLevel, level - 1, out var scaled))
        {
            value = default;
            return false;
        }

        value = scaled.Adjust(baseValue);
        return true;
    }

    /// <summary>
    /// Ported from <c>ValueModifier.SumSequenceAdjust(ValueModifier, BigDouble)</c>: one modifier
    /// strengthened by summing another over <paramref name="n"/> extra terms.
    /// </summary>
    /// <remarks>
    /// The switch is on the <em>target's</em> type, not on the summing modifier's — an additive target
    /// has its amount summed directly, a multiplicative one is raised to the power of the summed unit
    /// series. Reading it the other way round produces plausible numbers on every input and correct
    /// ones on none.
    /// </remarks>
    internal static bool TrySumSequenceAdjust(
        in GameValueModifier summing,
        in GameValueModifier target,
        BigDouble n,
        out GameValueModifier result)
    {
        switch (target.Type)
        {
            case GameValueModifierType.Raw:
            case GameValueModifierType.MultiDiminishing:
            case GameValueModifierType.Reduction:
                if (!TrySumSequence(in summing, target.Amount, n, out var amount)) break;
                result = target.WithAmount(amount);
                return true;

            case GameValueModifierType.MultiStacking:
            case GameValueModifierType.Exponent:
                if (!TrySumSequence(in summing, BigDouble.One, n, out var exponent)) break;
                result = target.WithAmount(BigDouble.Pow(target.Amount, exponent));
                return true;

            default:
                result = target;
                return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Ported from <c>ValueModifier.SumSequence.Adjust(BigDouble, BigDouble)</c>: applying this
    /// modifier nought through <paramref name="n"/> times and summing every result.
    /// </summary>
    /// <remarks>
    /// A negative term count sums to nought and an empty modifier degenerates to
    /// <c>value * (n + 1)</c>, both as written. The two unimplemented cases are the original's own:
    /// it throws for <c>Reduction</c> and <c>Exponent</c>, which have no closed form here, and this
    /// reports them rather than inventing one.
    /// </remarks>
    internal static bool TrySumSequence(
        in GameValueModifier modifier,
        BigDouble value,
        BigDouble n,
        out BigDouble sum)
    {
        if (n < 0)
        {
            sum = BigDouble.Zero;
            return true;
        }

        if (modifier.IsEmpty())
        {
            sum = value * (n + 1);
            return true;
        }

        switch (modifier.Type)
        {
            case GameValueModifierType.Raw:
                sum = OrbGameMath.SumArithmeticSequenceDiff(value, modifier.Amount, n);
                return true;

            case GameValueModifierType.MultiDiminishing:
                sum = OrbGameMath.SumArithmeticSequenceDiff(value, value * modifier.Amount, n);
                return true;

            case GameValueModifierType.MultiStacking:
                sum = OrbGameMath.SumGeometricSequence(value, modifier.Amount, n);
                return true;

            default:
                sum = default;
                return false;
        }
    }
}
