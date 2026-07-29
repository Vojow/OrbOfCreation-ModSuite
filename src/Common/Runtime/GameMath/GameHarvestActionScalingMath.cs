using System;

namespace OrbModding.Common.Runtime.GameMath;

/// <summary>
/// Native-free projection of <c>HarvestActionInstance.GetScalingInfo(level)</c>
/// for the drain-cost value used by Druidry.
/// </summary>
/// <remarks>
/// The native graph is mutable and allocation-heavy, but the drain result only
/// depends on four resolved modifier-record values plus the authored cost and
/// speed instance-scaling lists. Those inputs can be flattened into immutable
/// world rows and evaluated on a worker.
/// </remarks>
internal static class GameHarvestActionScalingMath
{
    internal static bool TryGetDrainCostModifier(
        int level,
        bool hasInstanceScaling,
        BigDouble actionCostModifier,
        BigDouble actionSpeed,
        BigDouble elementActionCostModifier,
        BigDouble elementActionSpeed,
        ReadOnlySpan<GameValueModifier> costPerInstance,
        ReadOnlySpan<GameValueModifier> speedPerInstance,
        Span<GameValueModifier> scratch,
        out BigDouble drainCostModifier) =>
        TryGetDrainCostModifier(
            level,
            hasInstanceScaling,
            actionCostModifier,
            actionSpeed,
            elementActionCostModifier,
            elementActionSpeed,
            costPerInstance,
            ReadOnlySpan<GameValueModifier>.Empty,
            speedPerInstance,
            ReadOnlySpan<GameValueModifier>.Empty,
            scratch,
            out drainCostModifier);

    internal static bool TryGetDrainCostModifier(
        int level,
        bool hasInstanceScaling,
        BigDouble actionCostModifier,
        BigDouble actionSpeed,
        BigDouble elementActionCostModifier,
        BigDouble elementActionSpeed,
        ReadOnlySpan<GameValueModifier> costPerInstance,
        ReadOnlySpan<GameValueModifier> costExponents,
        ReadOnlySpan<GameValueModifier> speedPerInstance,
        ReadOnlySpan<GameValueModifier> speedExponents,
        Span<GameValueModifier> scratch,
        out BigDouble drainCostModifier)
    {
        drainCostModifier = BigDouble.Zero;
        if (level <= 0 ||
            !IsFiniteNonNegative(actionCostModifier) ||
            !IsFiniteNonNegative(actionSpeed) ||
            !IsFiniteNonNegative(elementActionCostModifier) ||
            !IsFiniteNonNegative(elementActionSpeed))
        {
            return false;
        }

        if (!TryGetInstancePercent(
                level,
                hasInstanceScaling,
                costPerInstance,
                costExponents,
                scratch,
                out var instanceCost) ||
            !TryGetInstancePercent(
                level,
                hasInstanceScaling,
                speedPerInstance,
                speedExponents,
                scratch,
                out var instanceSpeed))
        {
            return false;
        }

        // ScalingInfo.Combine(element): value.AsPercent() * current.
        var drain = OrbGameMath.AsPercent(elementActionCostModifier) *
            actionCostModifier;
        var speed = OrbGameMath.AsPercent(elementActionSpeed) * actionSpeed;

        // HarvestActionInstance.GetScalingInfo(level), in native call order.
        drain *= instanceCost;
        speed *= instanceSpeed;

        // ScalingInfo.GetFullSpeedScaling().
        drain *= OrbGameMath.AsPercent(speed);
        if (!IsFiniteNonNegative(drain))
            return false;

        drainCostModifier = drain;
        return true;
    }

    private static bool TryGetInstancePercent(
        int level,
        bool hasInstanceScaling,
        ReadOnlySpan<GameValueModifier> modifiers,
        ReadOnlySpan<GameValueModifier> exponents,
        Span<GameValueModifier> scratch,
        out BigDouble percent)
    {
        if (!hasInstanceScaling)
        {
            percent = new BigDouble(level);
            return true;
        }

        var required = exponents.Length == 0
            ? modifiers.Length
            : checked((modifiers.Length * 2) + exponents.Length);
        if (scratch.Length < required)
        {
            percent = BigDouble.Zero;
            return false;
        }

        var scalar = new BigDouble(Math.Max(level - 1, 0));
        for (var index = 0; index < modifiers.Length; index++)
            scratch[index] = modifiers[index].MultiplyScalar(scalar);
        for (var index = 0; index < exponents.Length; index++)
            scratch[modifiers.Length + index] =
                exponents[index].MultiplyScalar(scalar);

        percent = exponents.Length == 0
            ? GameModifierStack.AdjustWith(
                BigDouble.One,
                scratch.Slice(0, modifiers.Length))
            : GameModifierStack.AdjustWith(
                BigDouble.One,
                scratch.Slice(0, modifiers.Length),
                scratch.Slice(modifiers.Length, exponents.Length),
                scratch.Slice(modifiers.Length + exponents.Length, modifiers.Length));
        return IsFiniteNonNegative(percent);
    }

    private static bool IsFiniteNonNegative(BigDouble value) =>
        !double.IsNaN(value.Mantissa) &&
        !double.IsInfinity(value.Mantissa) &&
        value >= BigDouble.Zero;
}
