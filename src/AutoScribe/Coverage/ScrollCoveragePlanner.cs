using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal enum ScrollUseDirective
{
    BlockUnknown = 0,
    BlockNoCandidate = 1,
    AllowUse = 2,
}

internal enum ScrollCoverageState
{
    EvidenceUnknown = 0,
    CoverageOnly = 1,
    Covered = 2,
    ProductionNeeded = 3,
    ExternallyProducing = 4,
    Unavailable = 5,
}

internal readonly record struct ScrollRoleCoverage(
    ScrollRoleKey Role,
    string DisplayName,
    Guid ScrollId,
    Guid EnchantmentId,
    Guid RecipeId,
    int CraftCostOrder,
    int TargetLevel,
    int ValidTargets,
    int CoveredTargets,
    int OwnedSupply,
    int QueuedSupply,
    int PendingUseSupply,
    int Deficit,
    int StrongestOwnedLevel,
    int UsableCandidates,
    ScrollUseDirective UseDirective,
    ScrollCoverageState State)
{
    internal bool ShouldProduce =>
        State == ScrollCoverageState.ProductionNeeded && RecipeId != Guid.Empty && Deficit > 0;
}

internal sealed class ScrollCoveragePlan
{
    internal ScrollCoveragePlan(long frame, long epoch, ScrollRoleCoverage[] roles)
    {
        CollectedAtFrame = frame;
        CollectedAtEpoch = epoch;
        Roles = roles ?? throw new ArgumentNullException(nameof(roles));
    }

    internal long CollectedAtFrame { get; }
    internal long CollectedAtEpoch { get; }
    internal ScrollRoleCoverage[] Roles { get; }

    internal bool TryFind(Guid scrollId, out ScrollRoleCoverage coverage)
    {
        for (var index = 0; index < Roles.Length; index++)
        {
            if (Roles[index].ScrollId != scrollId) continue;
            coverage = Roles[index];
            return true;
        }
        coverage = default;
        return false;
    }

    internal bool TryChooseProduction(out ScrollRoleCoverage coverage)
    {
        coverage = default;
        var found = false;
        for (var index = 0; index < Roles.Length; index++)
        {
            var candidate = Roles[index];
            if (!candidate.ShouldProduce) continue;
            if (!found ||
                candidate.CraftCostOrder < coverage.CraftCostOrder ||
                (candidate.CraftCostOrder == coverage.CraftCostOrder &&
                 candidate.Role.CompareTo(coverage.Role) < 0))
            {
                coverage = candidate;
                found = true;
            }
        }
        return found;
    }
}

/// <summary>
/// Native-free shared policy used by both Auto Scribe production and Auto Items Scroll admission.
/// Unknown or contradictory relationship evidence always blocks.
/// </summary>
internal static class ScrollCoveragePlanner
{
    internal static ScrollCoveragePlan Build(
        GameWorldState world,
        AutoScribeIdentityProfile profile)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        var rows = new ScrollRoleCoverage[profile.Roles.Count];
        for (var index = 0; index < rows.Length; index++)
            rows[index] = BuildRole(world, profile.Roles[index]);
        return new ScrollCoveragePlan(
            world.CollectedAtFrame,
            world.CollectedAtEpoch,
            rows);
    }

    private static ScrollRoleCoverage BuildRole(
        GameWorldState world,
        in AutoScribeRoleDescriptor role)
    {
        var targetLevel = TargetLevel(world);
        var strongest = WorldConsumableCountLookup.StrongestOwnedLevel(
            world.ConsumableCounts, role.Scroll.Uuid);
        var useTargets = false;
        var usableCandidates = strongest > 0
            ? CountUncovered(world, role, strongest, out useTargets)
            : 0;
        var useDirective = strongest <= 0
            ? ScrollUseDirective.BlockNoCandidate
            : useTargets
                ? usableCandidates > 0
                    ? ScrollUseDirective.AllowUse
                    : ScrollUseDirective.BlockNoCandidate
                : ScrollUseDirective.BlockUnknown;

        if (!role.IsProducible)
            return Row(
                role, 0, 0, 0, 0, 0, strongest, usableCandidates, useDirective,
                ScrollCoverageState.CoverageOnly);

        var recipeId = role.Recipe!.Value.Uuid;
        if (targetLevel <= 0 ||
            !WorldScribeLookup.TryGetRecipe(world.ScribeRecipes, recipeId, out var recipe) ||
            recipe.RecipeTypeId != KnownEntities.ScribeCrafting.Uuid ||
            recipe.OutputConsumableId != role.Scroll.Uuid ||
            !recipe.UsesQuantityAsLevel)
        {
            return Row(
                role, targetLevel, 0, 0, 0, 0, strongest, usableCandidates,
                ScrollUseDirective.BlockUnknown, ScrollCoverageState.EvidenceUnknown);
        }
        if (!recipe.Visible)
            return Row(
                role, targetLevel, 0, 0, 0, 0, strongest, usableCandidates,
                useDirective, ScrollCoverageState.Unavailable);

        var uncovered = CountUncovered(world, role, targetLevel, out var completeTargets);
        if (!completeTargets)
            return Row(
                role, targetLevel, 0, 0, 0, 0, strongest, usableCandidates,
                ScrollUseDirective.BlockUnknown, ScrollCoverageState.EvidenceUnknown);

        var targets = WorldScribeLookup.CountTargets(
            world.ScrollTargets, role.Scroll.Uuid, role.Enchantment.Uuid);
        var covered = Math.Max(0, targets - uncovered);
        var owned = WorldConsumableCountLookup.CountAtOrAbove(
            world.ConsumableCounts, role.Scroll.Uuid, targetLevel);
        var queued = CountManualWorkAtOrAbove(
            world.ScribeWork, recipeId, targetLevel);
        var pending = CountPendingUsesAtOrAbove(
            world.ConsumableUsages, role.Scroll.Uuid, targetLevel);
        var automatic = CountAutomaticWork(world, recipeId, targetLevel);
        var carryTarget = FindMaximumCarryLoad(world, role.Scroll.Uuid);
        var desiredSupply = carryTarget > 0
            ? Math.Max(uncovered, carryTarget)
            : uncovered;
        var deficit = Math.Max(0, desiredSupply - owned - queued - pending);
        var state = automatic > 0 && deficit > 0
            ? ScrollCoverageState.ExternallyProducing
            : deficit > 0
                ? ScrollCoverageState.ProductionNeeded
                : ScrollCoverageState.Covered;
        return new ScrollRoleCoverage(
            role.Key, role.DisplayName, role.Scroll.Uuid, role.Enchantment.Uuid,
            recipeId, role.CraftCostOrder, targetLevel, targets, covered, owned,
            queued, pending, deficit, strongest, usableCandidates, useDirective, state);
    }

    private static ScrollRoleCoverage Row(
        in AutoScribeRoleDescriptor role,
        int targetLevel,
        int targets,
        int covered,
        int owned,
        int queued,
        int strongest,
        int usable,
        ScrollUseDirective directive,
        ScrollCoverageState state) =>
        new(
            role.Key, role.DisplayName, role.Scroll.Uuid, role.Enchantment.Uuid,
            role.Recipe?.Uuid ?? Guid.Empty, role.CraftCostOrder, targetLevel,
            targets, covered, owned, queued, 0, 0, strongest, usable, directive,
            state);

    private static int TargetLevel(GameWorldState world)
    {
        var rows = world.CraftingRecipeTypes.AsSpan();
        for (var index = 0; index < rows.Length; index++)
            if (rows[index].CraftingRecipeTypeId == KnownEntities.ScribeCrafting.Uuid)
                return rows[index].MaxStartingLevel;
        return 0;
    }

    private static int FindMaximumCarryLoad(GameWorldState world, Guid scrollId)
    {
        var rows = world.Consumables.AsSpan();
        for (var index = 0; index < rows.Length; index++)
            if (rows[index].ConsumableId == scrollId)
                return Math.Max(0, rows[index].MaximumCarryLoad);
        return 0;
    }

    private static int CountUncovered(
        GameWorldState world,
        in AutoScribeRoleDescriptor role,
        int level,
        out bool hasEvidence)
    {
        var count = 0;
        var targets = 0;
        if (!WorldScribeLookup.TryGetTargetEvidence(
                world.ScrollTargetEvidence,
                role.Scroll.Uuid,
                role.Enchantment.Uuid,
                out var expectedTargets))
        {
            hasEvidence = false;
            return 0;
        }
        var rows = world.ScrollTargets.AsSpan();
        for (var index = 0; index < rows.Length; index++)
        {
            if (rows[index].ConsumableId != role.Scroll.Uuid ||
                rows[index].EnchantmentId != role.Enchantment.Uuid)
                continue;
            targets++;
            if (WorldScribeLookup.EnchantmentLevel(
                    world.StructureEnchantments,
                    rows[index].StructureId,
                    role.Enchantment.Uuid) < level)
                count++;
        }
        hasEvidence = targets == expectedTargets;
        return count;
    }

    private static int CountAutomaticWork(
        GameWorldState world,
        Guid recipeId,
        int level)
    {
        var count = 0;
        var rows = world.ScribeWork.AsSpan();
        for (var index = 0; index < rows.Length; index++)
            if (rows[index].RecipeId == recipeId &&
                rows[index].Level >= level &&
                rows[index].IsAutomatic &&
                !rows[index].IsExpired)
                count++;
        return count;
    }

    private static int CountManualWorkAtOrAbove(
        PublicationTable<WorldScribeWork> work,
        Guid recipeId,
        int level)
    {
        var count = 0;
        var rows = work.AsSpan();
        for (var index = 0; index < rows.Length; index++)
            if (rows[index].RecipeId == recipeId &&
                rows[index].Level >= level &&
                !rows[index].IsAutomatic &&
                !rows[index].IsExpired)
            {
                count++;
            }
        return count;
    }

    private static int CountPendingUsesAtOrAbove(
        PublicationTable<WorldConsumableUsage> usages,
        Guid consumableId,
        int level)
    {
        if (!WorldConsumableUsageLookup.TryFindRange(
                usages, consumableId, out var start, out var count))
        {
            return 0;
        }

        var pending = 0;
        for (var index = 0; index < count; index++)
        {
            var usage = usages[start + index];
            if (usage.Pending && usage.Level >= level && !usage.Expired) pending++;
        }
        return pending;
    }
}
