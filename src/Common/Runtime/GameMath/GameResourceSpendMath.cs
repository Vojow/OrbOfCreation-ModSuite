using System;

namespace OrbModding.Common.Runtime.GameMath;

/// <summary>
/// Resource spending arithmetic transcribed from the audited game build.
/// </summary>
/// <remarks>
/// <para>
/// <c>ResourceSO.GetTrueSpend(amount)</c> divides by
/// <c>quality.GetValue().AsPercent()</c>. Druidry first multiplies its authored
/// cost by <c>ScalingInfo.GetDrainCostMod().AsPercent()</c>, then applies that
/// resource conversion.
/// </para>
/// <para>
/// These entry points reject invalid or non-positive conversion inputs instead
/// of reproducing a native NaN/infinity. Automation must not authorize a drain
/// when its exact cost cannot be represented.
/// </para>
/// </remarks>
internal static class GameResourceSpendMath
{
    internal static bool TryGetTrueSpend(
        BigDouble amount,
        BigDouble quality,
        out BigDouble spend)
    {
        spend = BigDouble.Zero;
        if (!IsFiniteNonNegative(amount) || !IsFinitePositive(quality))
            return false;

        var qualityPercent = OrbGameMath.AsPercent(quality);
        if (!IsFinitePositive(qualityPercent))
            return false;

        var converted = amount / qualityPercent;
        if (!IsFiniteNonNegative(converted))
            return false;

        spend = converted;
        return true;
    }

    internal static bool TryGetScaledDrain(
        BigDouble baseCost,
        BigDouble drainCostModifier,
        BigDouble quality,
        out BigDouble drain)
    {
        drain = BigDouble.Zero;
        if (!IsFiniteNonNegative(baseCost) ||
            !IsFiniteNonNegative(drainCostModifier))
        {
            return false;
        }

        var multiplier = OrbGameMath.AsPercent(drainCostModifier);
        if (!IsFiniteNonNegative(multiplier))
            return false;

        return TryGetTrueSpend(baseCost * multiplier, quality, out drain);
    }

    private static bool IsFiniteNonNegative(BigDouble value) =>
        !double.IsNaN(value.Mantissa) &&
        !double.IsInfinity(value.Mantissa) &&
        value >= BigDouble.Zero;

    private static bool IsFinitePositive(BigDouble value) =>
        !double.IsNaN(value.Mantissa) &&
        !double.IsInfinity(value.Mantissa) &&
        value > BigDouble.Zero;
}
