using System;
using OrbModding.Common.Runtime.GameMath;

namespace OrbAutomata;

internal readonly struct AutoAgromancyCompactResource
{
    internal AutoAgromancyCompactResource(
        Guid resourceId,
        string name,
        BigDouble baselineWithoutSelected,
        BigDouble quality)
    {
        ResourceId = resourceId;
        Name = name ?? string.Empty;
        BaselineWithoutSelected = baselineWithoutSelected;
        Quality = quality;
    }

    internal Guid ResourceId { get; }
    internal string Name { get; }
    internal BigDouble BaselineWithoutSelected { get; }
    internal BigDouble Quality { get; }
}

/// <summary>
/// One authored base-cost tuple in native order. Duplicate resource identities
/// remain separate so BigDouble rounding follows ResourceCostList.
/// </summary>
internal readonly struct AutoAgromancyBaseCost
{
    internal AutoAgromancyBaseCost(int resourceIndex, BigDouble amount)
    {
        ResourceIndex = resourceIndex;
        Amount = amount;
    }

    internal int ResourceIndex { get; }
    internal BigDouble Amount { get; }
}

internal readonly struct AutoAgromancyScalingSnapshot
{
    internal AutoAgromancyScalingSnapshot(
        bool hasInstanceScaling,
        BigDouble actionCostModifier,
        BigDouble actionSpeed,
        BigDouble elementActionCostModifier,
        BigDouble elementActionSpeed)
    {
        HasInstanceScaling = hasInstanceScaling;
        ActionCostModifier = actionCostModifier;
        ActionSpeed = actionSpeed;
        ElementActionCostModifier = elementActionCostModifier;
        ElementActionSpeed = elementActionSpeed;
    }

    internal bool HasInstanceScaling { get; }
    internal BigDouble ActionCostModifier { get; }
    internal BigDouble ActionSpeed { get; }
    internal BigDouble ElementActionCostModifier { get; }
    internal BigDouble ElementActionSpeed { get; }
}

internal readonly struct AutoAgromancyCompactPlan
{
    internal AutoAgromancyCompactPlan(
        AutoAgromancyPlanDisposition disposition,
        int targetLevel,
        int limitingResourceIndex,
        BigDouble limitingProjectedRate,
        string reason)
    {
        Disposition = disposition;
        TargetLevel = targetLevel;
        LimitingResourceIndex = limitingResourceIndex;
        LimitingProjectedRate = limitingProjectedRate;
        Reason = reason ?? string.Empty;
    }

    internal AutoAgromancyPlanDisposition Disposition { get; }
    internal int TargetLevel { get; }
    internal int LimitingResourceIndex { get; }
    internal BigDouble LimitingProjectedRate { get; }
    internal string Reason { get; }
    internal bool HasTarget => Disposition == AutoAgromancyPlanDisposition.Selected;
}

/// <summary>
/// Exact level search over compact, immutable native inputs.
/// </summary>
internal static class AutoAgromancyCompactLevelPlanner
{
    internal static AutoAgromancyCompactPlan Plan(
        int maximumLevel,
        ReadOnlySpan<AutoAgromancyCompactResource> resources,
        ReadOnlySpan<AutoAgromancyBaseCost> baseCosts,
        in AutoAgromancyScalingSnapshot scaling,
        ReadOnlySpan<GameValueModifier> costPerInstance,
        ReadOnlySpan<GameValueModifier> speedPerInstance) =>
        Plan(
            maximumLevel,
            resources,
            baseCosts,
            in scaling,
            costPerInstance,
            ReadOnlySpan<GameValueModifier>.Empty,
            speedPerInstance,
            ReadOnlySpan<GameValueModifier>.Empty);

    internal static AutoAgromancyCompactPlan Plan(
        int maximumLevel,
        ReadOnlySpan<AutoAgromancyCompactResource> resources,
        ReadOnlySpan<AutoAgromancyBaseCost> baseCosts,
        in AutoAgromancyScalingSnapshot scaling,
        ReadOnlySpan<GameValueModifier> costPerInstance,
        ReadOnlySpan<GameValueModifier> costExponents,
        ReadOnlySpan<GameValueModifier> speedPerInstance,
        ReadOnlySpan<GameValueModifier> speedExponents)
    {
        if (maximumLevel <= 0)
            return Invalid("maximum native level is not positive");
        if (maximumLevel > AutoAgromancyLevelPlanner.MaximumExactLevels)
        {
            return new AutoAgromancyCompactPlan(
                AutoAgromancyPlanDisposition.WorkLimitExceeded,
                0,
                -1,
                BigDouble.Zero,
                $"maximum native level {maximumLevel} exceeds the exact-search limit");
        }
        if (!Validate(
                resources,
                baseCosts,
                costPerInstance,
                costExponents,
                speedPerInstance,
                speedExponents))
            return Invalid("the compact cost snapshot is invalid");

        var projected = new BigDouble[resources.Length];
        var scratch = new GameValueModifier[Math.Max(
            checked((costPerInstance.Length * 2) + costExponents.Length),
            checked((speedPerInstance.Length * 2) + speedExponents.Length))];
        var selectedLevel = 0;
        var selectedLimit = -1;
        var selectedRate = BigDouble.Zero;
        var levelOneLimit = -1;
        var levelOneRate = BigDouble.Zero;

        for (var level = 1; level <= maximumLevel; level++)
        {
            if (!GameHarvestActionScalingMath.TryGetDrainCostModifier(
                    level,
                    scaling.HasInstanceScaling,
                    scaling.ActionCostModifier,
                    scaling.ActionSpeed,
                    scaling.ElementActionCostModifier,
                    scaling.ElementActionSpeed,
                    costPerInstance,
                    costExponents,
                    speedPerInstance,
                    speedExponents,
                    scratch,
                    out var drainCostModifier))
            {
                return Invalid($"level {level} scaling is invalid");
            }

            for (var resourceIndex = 0; resourceIndex < resources.Length; resourceIndex++)
                projected[resourceIndex] = resources[resourceIndex].BaselineWithoutSelected;

            for (var costIndex = 0; costIndex < baseCosts.Length; costIndex++)
            {
                var cost = baseCosts[costIndex];
                var resource = resources[cost.ResourceIndex];
                if (!GameResourceSpendMath.TryGetScaledDrain(
                        cost.Amount,
                        drainCostModifier,
                        resource.Quality,
                        out var drain))
                {
                    return Invalid($"level {level} resource conversion is invalid");
                }
                projected[cost.ResourceIndex] =
                    projected[cost.ResourceIndex] - drain;
            }

            var sustainable = true;
            var limitingIndex = -1;
            var limitingRate = BigDouble.Zero;
            for (var resourceIndex = 0; resourceIndex < projected.Length; resourceIndex++)
            {
                var rate = projected[resourceIndex];
                if (!IsFinite(rate))
                    return Invalid($"level {level} projected rate is invalid");
                if (limitingIndex < 0 || rate < limitingRate)
                {
                    limitingIndex = resourceIndex;
                    limitingRate = rate;
                }
                if (rate < BigDouble.Zero) sustainable = false;
            }

            if (level == 1)
            {
                levelOneLimit = limitingIndex;
                levelOneRate = limitingRate;
            }
            if (!sustainable) continue;
            selectedLevel = level;
            selectedLimit = limitingIndex;
            selectedRate = limitingRate;
        }

        if (selectedLevel == 0)
        {
            return new AutoAgromancyCompactPlan(
                AutoAgromancyPlanDisposition.LevelOneUnsustainable,
                0,
                levelOneLimit,
                levelOneRate,
                "level 1 would make a consumed resource decrease");
        }

        return new AutoAgromancyCompactPlan(
            AutoAgromancyPlanDisposition.Selected,
            selectedLevel,
            selectedLimit,
            selectedRate,
            "highest sustainable exact level selected");
    }

    private static bool Validate(
        ReadOnlySpan<AutoAgromancyCompactResource> resources,
        ReadOnlySpan<AutoAgromancyBaseCost> baseCosts,
        ReadOnlySpan<GameValueModifier> costPerInstance,
        ReadOnlySpan<GameValueModifier> costExponents,
        ReadOnlySpan<GameValueModifier> speedPerInstance,
        ReadOnlySpan<GameValueModifier> speedExponents)
    {
        for (var left = 0; left < resources.Length; left++)
        {
            var resource = resources[left];
            if (resource.ResourceId == Guid.Empty ||
                !IsFinite(resource.BaselineWithoutSelected) ||
                !IsFinitePositive(resource.Quality))
            {
                return false;
            }
            for (var right = left + 1; right < resources.Length; right++)
            {
                if (resource.ResourceId == resources[right].ResourceId)
                    return false;
            }
        }

        for (var index = 0; index < baseCosts.Length; index++)
        {
            var cost = baseCosts[index];
            if (cost.ResourceIndex < 0 ||
                cost.ResourceIndex >= resources.Length ||
                !IsFiniteNonNegative(cost.Amount))
            {
                return false;
            }
        }

        return ValidateModifiers(costPerInstance) &&
            ValidateModifiers(costExponents) &&
            ValidateModifiers(speedPerInstance) &&
            ValidateModifiers(speedExponents);
    }

    private static bool ValidateModifiers(ReadOnlySpan<GameValueModifier> modifiers)
    {
        for (var index = 0; index < modifiers.Length; index++)
        {
            var modifier = modifiers[index];
            if (!Enum.IsDefined(typeof(GameValueModifierType), modifier.Type) ||
                !IsFinite(modifier.Amount))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsFinite(BigDouble value) =>
        !double.IsNaN(value.Mantissa) &&
        !double.IsInfinity(value.Mantissa);

    private static bool IsFiniteNonNegative(BigDouble value) =>
        IsFinite(value) && value >= BigDouble.Zero;

    private static bool IsFinitePositive(BigDouble value) =>
        IsFinite(value) && value > BigDouble.Zero;

    private static AutoAgromancyCompactPlan Invalid(string reason) =>
        new(
            AutoAgromancyPlanDisposition.InvalidSnapshot,
            0,
            -1,
            BigDouble.Zero,
            reason);
}
