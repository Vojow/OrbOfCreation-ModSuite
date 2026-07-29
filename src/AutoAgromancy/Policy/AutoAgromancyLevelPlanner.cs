using System;
using System.Collections.Generic;

namespace OrbAutomata;

internal enum AutoAgromancyPlanDisposition
{
    Selected = 1,
    LevelOneUnsustainable = 2,
    InvalidSnapshot = 3,
    WorkLimitExceeded = 4,
}

internal readonly struct AutoAgromancyResourceSnapshot
{
    internal AutoAgromancyResourceSnapshot(
        string id,
        string name,
        BigAmount baselineWithoutSelected)
    {
        Id = id ?? string.Empty;
        Name = name ?? string.Empty;
        BaselineWithoutSelected = baselineWithoutSelected;
    }

    internal string Id { get; }
    internal string Name { get; }
    internal BigAmount BaselineWithoutSelected { get; }
}

internal readonly struct AutoAgromancyDrainEntry
{
    internal AutoAgromancyDrainEntry(int resourceIndex, BigAmount drain)
    {
        ResourceIndex = resourceIndex;
        Drain = drain;
    }

    internal int ResourceIndex { get; }
    internal BigAmount Drain { get; }
}

internal readonly struct AutoAgromancyLevelCost
{
    internal AutoAgromancyLevelCost(
        int level,
        IReadOnlyList<AutoAgromancyDrainEntry> drains)
    {
        Level = level;
        Drains = drains ?? throw new ArgumentNullException(nameof(drains));
    }

    internal int Level { get; }
    internal IReadOnlyList<AutoAgromancyDrainEntry> Drains { get; }
}

internal readonly struct AutoAgromancyPlan
{
    internal AutoAgromancyPlan(
        AutoAgromancyPlanDisposition disposition,
        int targetLevel,
        int limitingResourceIndex,
        BigAmount limitingProjectedRate,
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
    internal BigAmount LimitingProjectedRate { get; }
    internal string Reason { get; }
    internal bool HasTarget => Disposition == AutoAgromancyPlanDisposition.Selected;
}

internal static class AutoAgromancyLevelPlanner
{
    internal const int MaximumExactLevels = 4096;

    internal static AutoAgromancyPlan Plan(
        int maximumLevel,
        IReadOnlyList<AutoAgromancyResourceSnapshot> resources,
        IReadOnlyList<AutoAgromancyLevelCost> levels)
    {
        if (resources is null) throw new ArgumentNullException(nameof(resources));
        if (levels is null) throw new ArgumentNullException(nameof(levels));
        if (maximumLevel <= 0)
            return Invalid("maximum native level is not positive");
        if (maximumLevel > MaximumExactLevels)
        {
            return new AutoAgromancyPlan(
                AutoAgromancyPlanDisposition.WorkLimitExceeded,
                0,
                -1,
                default,
                $"maximum native level {maximumLevel} exceeds the exact-search limit");
        }
        if (levels.Count != maximumLevel)
            return Invalid("the exact level-cost set is incomplete");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
        {
            var resource = resources[resourceIndex];
            if (string.IsNullOrWhiteSpace(resource.Id) || !ids.Add(resource.Id))
                return Invalid("resource identities are missing or duplicated");
        }

        var selectedLevel = 0;
        var selectedLimit = -1;
        var selectedRate = default(BigAmount);
        var levelOneLimit = -1;
        var levelOneRate = default(BigAmount);

        for (var levelIndex = 0; levelIndex < levels.Count; levelIndex++)
        {
            var level = levels[levelIndex];
            if (level.Level != levelIndex + 1)
                return Invalid("level-cost entries are not contiguous");

            var projected = new BigAmount[resources.Count];
            for (var resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
                projected[resourceIndex] = resources[resourceIndex].BaselineWithoutSelected;

            var seen = new HashSet<int>();
            for (var drainIndex = 0; drainIndex < level.Drains.Count; drainIndex++)
            {
                var drain = level.Drains[drainIndex];
                if (drain.ResourceIndex < 0 ||
                    drain.ResourceIndex >= resources.Count ||
                    !seen.Add(drain.ResourceIndex) ||
                    drain.Drain.IsNegative)
                {
                    return Invalid("a level contains an invalid resource drain");
                }
                projected[drain.ResourceIndex] =
                    projected[drain.ResourceIndex].Subtract(drain.Drain);
            }

            var sustainable = true;
            var limitingIndex = -1;
            var limitingRate = default(BigAmount);
            for (var resourceIndex = 0; resourceIndex < projected.Length; resourceIndex++)
            {
                var rate = projected[resourceIndex];
                if (limitingIndex < 0 || Compare(rate, limitingRate) < 0)
                {
                    limitingIndex = resourceIndex;
                    limitingRate = rate;
                }
                if (rate.IsNegative) sustainable = false;
            }

            if (levelIndex == 0)
            {
                levelOneLimit = limitingIndex;
                levelOneRate = limitingRate;
            }
            if (!sustainable) continue;
            selectedLevel = level.Level;
            selectedLimit = limitingIndex;
            selectedRate = limitingRate;
        }

        if (selectedLevel == 0)
        {
            return new AutoAgromancyPlan(
                AutoAgromancyPlanDisposition.LevelOneUnsustainable,
                0,
                levelOneLimit,
                levelOneRate,
                "level 1 would make a consumed resource decrease");
        }

        return new AutoAgromancyPlan(
            AutoAgromancyPlanDisposition.Selected,
            selectedLevel,
            selectedLimit,
            selectedRate,
            "highest sustainable exact level selected");
    }

    private static int Compare(BigAmount left, BigAmount right)
    {
        if (left.IsNegative != right.IsNegative)
            return left.IsNegative ? -1 : 1;
        return left.CompareTo(right);
    }

    private static AutoAgromancyPlan Invalid(string reason) =>
        new(AutoAgromancyPlanDisposition.InvalidSnapshot, 0, -1, default, reason);
}
