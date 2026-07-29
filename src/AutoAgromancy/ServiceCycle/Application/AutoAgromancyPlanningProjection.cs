using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Projects immutable world rows into planner inputs and the matching
/// action-boundary fingerprint. It owns no cycle or trigger state.
/// </summary>
internal static class AutoAgromancyPlanningProjection
{
    internal static bool TryPlan(
        GameWorldState world,
        in WorldHarvestAction pair,
        out AutoAgromancyCompactPlan plan,
        out AutoAgromancyFactFingerprint fingerprint)
    {
        plan = default;
        fingerprint = default;
        WorldHarvestActionLookup.TryFindCosts(
            world.HarvestActionCosts,
            pair.ActionId,
            pair.ElementId,
            WorldHarvestActionCostKind.Base,
            out var baseStart,
            out var baseCount);
        if (!WorldLookup.TryFind(world.HarvestElements, pair.ElementId, out var element))
            return false;

        var resources = new List<AutoAgromancyCompactResource>(baseCount);
        var resourceIndexes = new Dictionary<Guid, int>(baseCount);
        var baseCosts = new AutoAgromancyBaseCost[baseCount];
        for (var index = 0; index < baseCount; index++)
        {
            var cost = world.HarvestActionCosts[baseStart + index];
            if (!resourceIndexes.TryGetValue(cost.ResourceId, out var resourceIndex))
            {
                if (!TryFindResource(world, cost.ResourceId, out var resource))
                    return false;
                resourceIndex = resources.Count;
                resourceIndexes.Add(cost.ResourceId, resourceIndex);
                resources.Add(new AutoAgromancyCompactResource(
                    cost.ResourceId,
                    cost.ResourceId.ToString(),
                    resource.TrueRate,
                    resource.Reading.Quality));
            }
            baseCosts[index] = new AutoAgromancyBaseCost(resourceIndex, cost.Amount);
        }

        if (WorldHarvestActionLookup.TryFindCosts(
                world.HarvestActionCosts,
                pair.ActionId,
                pair.ElementId,
                WorldHarvestActionCostKind.ObservedCurrent,
                out var currentStart,
                out var currentCount))
        {
            for (var index = 0; index < currentCount; index++)
            {
                var current = world.HarvestActionCosts[currentStart + index];
                if (!resourceIndexes.TryGetValue(current.ResourceId, out var resourceIndex))
                    return false;
                var resource = resources[resourceIndex];
                if (!GameResourceSpendMath.TryGetTrueSpend(
                        current.Amount, resource.Quality, out var currentSpend))
                    return false;
                resources[resourceIndex] = new AutoAgromancyCompactResource(
                    resource.ResourceId,
                    resource.Name,
                    resource.BaselineWithoutSelected + currentSpend,
                    resource.Quality);
            }
        }

        var costModifiers = new List<GameValueModifier>();
        var costExponents = new List<GameValueModifier>();
        var speedModifiers = new List<GameValueModifier>();
        var speedExponents = new List<GameValueModifier>();
        AppendModifiers(
            world,
            in pair,
            WorldHarvestActionScalingAxis.Cost,
            costModifiers,
            costExponents);
        AppendModifiers(
            world,
            in pair,
            WorldHarvestActionScalingAxis.Speed,
            speedModifiers,
            speedExponents);

        var scaling = new AutoAgromancyScalingSnapshot(
            pair.HasInstanceScaling,
            pair.ActionCostModifier,
            pair.ActionSpeed,
            element.ActionCostMod,
            element.ActionSpeed);
        plan = AutoAgromancyCompactLevelPlanner.Plan(
            pair.MaximumLevel,
            resources.ToArray(),
            baseCosts,
            in scaling,
            costModifiers.ToArray(),
            costExponents.ToArray(),
            speedModifiers.ToArray(),
            speedExponents.ToArray());
        return TryBuildFingerprint(world, in pair, out fingerprint);
    }

    /// <summary>
    /// Fingerprints only captured facts. The live boundary can reproduce this
    /// after an immediate collection without executing the level planner.
    /// </summary>
    internal static bool TryBuildFingerprint(
        GameWorldState world,
        in WorldHarvestAction pair,
        out AutoAgromancyFactFingerprint fingerprint)
    {
        fingerprint = default;
        if (!WorldLookup.TryFind(world.HarvestElements, pair.ElementId, out var element))
            return false;
        WorldHarvestActionLookup.TryFindCosts(
            world.HarvestActionCosts,
            pair.ActionId,
            pair.ElementId,
            WorldHarvestActionCostKind.Base,
            out var baseStart,
            out var baseCount);

        var hash = new AutoAgromancyFingerprintBuilder();
        hash.Add(pair.ActionId);
        hash.Add(pair.ElementId);
        hash.Add(pair.CurrentLevel);
        hash.Add(pair.MaximumLevel);
        hash.Add(pair.Visible);
        hash.Add(pair.HasInstanceScaling);
        hash.Add(pair.ActionCostModifier);
        hash.Add(pair.ActionSpeed);
        hash.Add(element.ActionCostMod);
        hash.Add(element.ActionSpeed);

        for (var index = 0; index < baseCount; index++)
        {
            var cost = world.HarvestActionCosts[baseStart + index];
            if (!TryFindResource(world, cost.ResourceId, out var resource))
                return false;
            hash.Add((int)cost.Kind);
            hash.Add(cost.Position);
            hash.Add(cost.ResourceId);
            hash.Add(cost.Amount);
            hash.Add(resource.TrueRate);
            hash.Add(resource.Reading.Quality);
        }

        if (WorldHarvestActionLookup.TryFindCosts(
                world.HarvestActionCosts,
                pair.ActionId,
                pair.ElementId,
                WorldHarvestActionCostKind.ObservedCurrent,
                out var currentStart,
                out var currentCount))
        {
            for (var index = 0; index < currentCount; index++)
            {
                var cost = world.HarvestActionCosts[currentStart + index];
                hash.Add((int)cost.Kind);
                hash.Add(cost.Position);
                hash.Add(cost.ResourceId);
                hash.Add(cost.Amount);
            }
        }

        var modifierRows = world.HarvestActionModifiers.AsSpan();
        for (var index = 0; index < modifierRows.Length; index++)
        {
            ref readonly var modifier = ref modifierRows[index];
            if (modifier.ActionId != pair.ActionId ||
                modifier.ElementId != pair.ElementId)
                continue;
            hash.Add((int)modifier.Axis);
            hash.Add((int)modifier.Role);
            hash.Add(modifier.Position);
            hash.Add((int)modifier.Type);
            hash.Add(modifier.Amount);
            hash.Add(modifier.Order);
        }

        fingerprint = new AutoAgromancyFactFingerprint(hash.Value);
        return fingerprint.IsValid;
    }

    private static void AppendModifiers(
        GameWorldState world,
        in WorldHarvestAction pair,
        WorldHarvestActionScalingAxis axis,
        ICollection<GameValueModifier> modifiers,
        ICollection<GameValueModifier> exponents)
    {
        if (!WorldHarvestActionLookup.TryFindModifiers(
                world.HarvestActionModifiers,
                pair.ActionId,
                pair.ElementId,
                axis,
                out var start,
                out var count))
            return;

        for (var index = 0; index < count; index++)
        {
            var row = world.HarvestActionModifiers[start + index];
            var value = new GameValueModifier(row.Type, row.Amount, row.Order);
            if (row.Role == WorldHarvestActionModifierRole.Exponent)
                exponents.Add(value);
            else
                modifiers.Add(value);
        }
    }

    private static bool TryFindResource(
        GameWorldState world,
        Guid resourceId,
        out WorldResource resource)
    {
        if (WorldLookup.TryFind(world.Resources, resourceId, out resource))
            return true;
        if (WorldLookup.TryFind(world.HarvestResources, resourceId, out var harvest))
        {
            resource = harvest.Resource;
            return true;
        }
        resource = default;
        return false;
    }
}

internal sealed class AutoAgromancyFingerprintBuilder
{
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    internal ulong Value { get; private set; } = Offset;

    internal void Add(bool value) => Add(value ? 1 : 0);
    internal void Add(int value) => Add(unchecked((ulong)(uint)value));
    internal void Add(long value) => Add(unchecked((ulong)value));

    internal void Add(Guid value)
    {
        var bytes = value.ToByteArray();
        for (var index = 0; index < bytes.Length; index++) Mix(bytes[index]);
    }

    internal void Add(BigDouble value)
    {
        Add(BitConverter.DoubleToInt64Bits(value.Mantissa));
        Add(value.Exponent);
    }

    private void Add(ulong value)
    {
        for (var shift = 0; shift < 64; shift += 8)
            Mix((byte)(value >> shift));
    }

    private void Mix(byte value)
    {
        Value ^= value;
        Value *= Prime;
        if (Value == 0) Value = Offset;
    }
}
