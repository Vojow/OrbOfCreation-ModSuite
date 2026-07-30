using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal enum AutoScribeEvidenceReason
{
    None = 0,
    CollectionUnavailable = 1,
    RecipeRegistryIncomplete = 2,
    RecipeMissing = 3,
    RecipeRelationshipMismatch = 4,
    TargetLevelUnavailable = 5,
    TargetEvidenceMissing = 6,
    TargetEvidenceContradictory = 7,
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
    int RoleOrdinal,
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
    int AutomaticSupply,
    int Deficit,
    AutoScribeEvidenceReason EvidenceReason,
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

    /// <summary>
    /// F4 fail-closed selection: an unknown enabled role blocks the whole publication before cost
    /// rank can select a different, apparently healthy role.
    /// </summary>
    internal bool TryChooseProduction(
        PublicationTable<ScrollRoleKey>? enabledRoles,
        out ScrollRoleCoverage coverage,
        out ScrollRoleCoverage blocked)
    {
        coverage = default;
        blocked = default;
        for (var index = 0; index < Roles.Length; index++)
        {
            var candidate = Roles[index];
            if (!AutoScribeRoleSelection.Contains(enabledRoles, candidate.Role) ||
                candidate.State != ScrollCoverageState.EvidenceUnknown)
                continue;
            blocked = candidate;
            return false;
        }

        var found = false;
        for (var index = 0; index < Roles.Length; index++)
        {
            var candidate = Roles[index];
            if (!candidate.ShouldProduce ||
                !AutoScribeRoleSelection.Contains(enabledRoles, candidate.Role))
                continue;
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

internal static class ScrollCoveragePlanner
{
    internal const string CollectionCategory = "scribe relations";

    internal static ScrollCoveragePlan Build(
        GameWorldState world,
        AutoScribeIdentityProfile profile)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        var categoryClean = IsCategoryClean(world);
        var registryClean = HasCompleteRegistry(world, profile);
        var rows = new ScrollRoleCoverage[profile.Roles.Count];
        for (var index = 0; index < rows.Length; index++)
            rows[index] = BuildRole(world, profile.Roles[index], categoryClean, registryClean);
        return new ScrollCoveragePlan(world.CollectedAtFrame, world.CollectedAtEpoch, rows);
    }

    internal static string DescribeEvidence(
        in ScrollRoleCoverage role)
    {
        var prefix = $"{role.DisplayName} ({role.Role.Value})";
        return role.EvidenceReason switch
        {
            AutoScribeEvidenceReason.CollectionUnavailable =>
                prefix + " is blocked because the Scribe relationship collection was incomplete.",
            AutoScribeEvidenceReason.RecipeRegistryIncomplete =>
                prefix + " is blocked because ScribeCraftingRecipes was not exactly the six audited recipes.",
            AutoScribeEvidenceReason.RecipeMissing =>
                prefix + $" is blocked because recipe {role.RecipeId:D} was absent.",
            AutoScribeEvidenceReason.RecipeRelationshipMismatch =>
                prefix + " is blocked because its live recipe/type/output/level relationship contradicted the audited role.",
            AutoScribeEvidenceReason.TargetLevelUnavailable =>
                prefix + " is blocked because the Scribe maxStartingLevel was not positive.",
            AutoScribeEvidenceReason.TargetEvidenceMissing =>
                prefix + " is blocked because its Scroll target relationship was unavailable.",
            AutoScribeEvidenceReason.TargetEvidenceContradictory =>
                prefix + " is blocked because its Scroll target count contradicted the completeness marker.",
            _ => prefix + " has complete evidence.",
        };
    }

    internal static string DescribeEvidence(
        in AutoScribeRoleDescriptor role,
        AutoScribeEvidenceReason reason)
    {
        var row = new ScrollRoleCoverage(
            role.Ordinal,
            role.Key,
            role.DisplayName,
            role.Scroll.Uuid,
            role.Enchantment.Uuid,
            role.Recipe?.Uuid ?? Guid.Empty,
            role.CraftCostOrder,
            0, 0, 0, 0, 0, 0, 0, 0,
            reason,
            ScrollCoverageState.EvidenceUnknown);
        return DescribeEvidence(in row);
    }

    private static ScrollRoleCoverage BuildRole(
        GameWorldState world,
        in AutoScribeRoleDescriptor role,
        bool categoryClean,
        bool registryClean)
    {
        if (!role.IsProducible)
            return Row(role, 0, AutoScribeEvidenceReason.None, ScrollCoverageState.CoverageOnly);
        if (!categoryClean)
            return Row(
                role, 0, AutoScribeEvidenceReason.CollectionUnavailable,
                ScrollCoverageState.EvidenceUnknown);
        if (!registryClean)
            return Row(
                role, 0, AutoScribeEvidenceReason.RecipeRegistryIncomplete,
                ScrollCoverageState.EvidenceUnknown);

        var recipeId = role.Recipe!.Value.Uuid;
        if (!WorldScribeLookup.TryGetRecipe(world.ScribeRecipes, recipeId, out var recipe))
            return Row(
                role, 0, AutoScribeEvidenceReason.RecipeMissing,
                ScrollCoverageState.EvidenceUnknown);
        if (recipe.RecipeTypeId != KnownEntities.ScribeCrafting.Uuid ||
            recipe.OutputConsumableId != role.Scroll.Uuid ||
            !recipe.UsesQuantityAsLevel)
            return Row(
                role, 0, AutoScribeEvidenceReason.RecipeRelationshipMismatch,
                ScrollCoverageState.EvidenceUnknown);
        if (!recipe.Visible)
            return Row(role, 0, AutoScribeEvidenceReason.None, ScrollCoverageState.Unavailable);

        var targetLevel = TargetLevel(world);
        if (targetLevel <= 0)
            return Row(
                role, targetLevel, AutoScribeEvidenceReason.TargetLevelUnavailable,
                ScrollCoverageState.EvidenceUnknown);
        if (!WorldScribeLookup.TryGetTargetEvidence(
                world.ScrollTargetEvidence,
                role.Scroll.Uuid,
                role.Enchantment.Uuid,
                out var expectedTargets))
            return Row(
                role, targetLevel, AutoScribeEvidenceReason.TargetEvidenceMissing,
                ScrollCoverageState.EvidenceUnknown);

        var targets = 0;
        var uncovered = 0;
        var targetRows = world.ScrollTargets.AsSpan();
        for (var index = 0; index < targetRows.Length; index++)
        {
            var target = targetRows[index];
            if (target.ConsumableId != role.Scroll.Uuid ||
                target.EnchantmentId != role.Enchantment.Uuid)
                continue;
            targets++;
            if (WorldScribeLookup.EnchantmentLevel(
                    world.StructureEnchantments,
                    target.StructureId,
                    role.Enchantment.Uuid) < targetLevel)
                uncovered++;
        }
        if (targets != expectedTargets)
            return Row(
                role, targetLevel, AutoScribeEvidenceReason.TargetEvidenceContradictory,
                ScrollCoverageState.EvidenceUnknown);

        var owned = WorldConsumableCountLookup.CountAtOrAbove(
            world.ConsumableCounts, role.Scroll.Uuid, targetLevel);
        var queued = CountWork(world.ScribeWork, recipeId, targetLevel, automatic: false);
        var automatic = CountWork(world.ScribeWork, recipeId, targetLevel, automatic: true);
        var pending = CountPending(world.ConsumableUsages, role.Scroll.Uuid, targetLevel);
        var carry = MaximumCarryLoad(world, role.Scroll.Uuid);
        var desired = carry > 0 ? Math.Max(uncovered, carry) : uncovered;
        var deficit = Math.Max(0, desired - owned - queued - pending);
        var state = automatic > 0 && deficit > 0
            ? ScrollCoverageState.ExternallyProducing
            : deficit > 0
                ? ScrollCoverageState.ProductionNeeded
                : ScrollCoverageState.Covered;
        return new ScrollRoleCoverage(
            role.Ordinal,
            role.Key,
            role.DisplayName,
            role.Scroll.Uuid,
            role.Enchantment.Uuid,
            recipeId,
            role.CraftCostOrder,
            targetLevel,
            targets,
            Math.Max(0, targets - uncovered),
            owned,
            queued,
            pending,
            automatic,
            deficit,
            AutoScribeEvidenceReason.None,
            state);
    }

    private static ScrollRoleCoverage Row(
        in AutoScribeRoleDescriptor role,
        int targetLevel,
        AutoScribeEvidenceReason reason,
        ScrollCoverageState state) =>
        new(
            role.Ordinal,
            role.Key,
            role.DisplayName,
            role.Scroll.Uuid,
            role.Enchantment.Uuid,
            role.Recipe?.Uuid ?? Guid.Empty,
            role.CraftCostOrder,
            targetLevel,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            reason,
            state);

    private static bool IsCategoryClean(GameWorldState world)
    {
        for (var index = 0; index < world.CollectionCategories.Count; index++)
        {
            var row = world.CollectionCategories[index];
            if (string.Equals(row.Category, CollectionCategory, StringComparison.Ordinal))
                return row.IsClean;
        }
        return false;
    }

    private static bool HasCompleteRegistry(
        GameWorldState world,
        AutoScribeIdentityProfile profile)
    {
        var expected = 0;
        for (var roleIndex = 0; roleIndex < profile.Roles.Count; roleIndex++)
            if (profile.Roles[roleIndex].IsProducible) expected++;
        if (world.ScribeRecipes.Count != expected) return false;
        for (var roleIndex = 0; roleIndex < profile.Roles.Count; roleIndex++)
        {
            var role = profile.Roles[roleIndex];
            if (role.Recipe.HasValue &&
                !WorldScribeLookup.TryGetRecipe(
                    world.ScribeRecipes,
                    role.Recipe.Value.Uuid,
                    out _))
                return false;
        }
        return true;
    }

    private static int TargetLevel(GameWorldState world)
    {
        var rows = world.CraftingRecipeTypes.AsSpan();
        for (var index = 0; index < rows.Length; index++)
            if (rows[index].CraftingRecipeTypeId == KnownEntities.ScribeCrafting.Uuid)
                return rows[index].MaxStartingLevel;
        return 0;
    }

    private static int MaximumCarryLoad(GameWorldState world, Guid scrollId)
    {
        var rows = world.Consumables.AsSpan();
        for (var index = 0; index < rows.Length; index++)
            if (rows[index].ConsumableId == scrollId)
                return Math.Max(0, rows[index].MaximumCarryLoad);
        return 0;
    }

    private static int CountWork(
        PublicationTable<WorldScribeWork> work,
        Guid recipeId,
        int level,
        bool automatic)
    {
        var count = 0;
        for (var index = 0; index < work.Count; index++)
        {
            var row = work[index];
            if (row.RecipeId == recipeId && row.Level >= level &&
                row.IsAutomatic == automatic && !row.IsExpired)
                count++;
        }
        return count;
    }

    private static int CountPending(
        PublicationTable<WorldConsumableUsage> usages,
        Guid scrollId,
        int level)
    {
        if (!WorldConsumableUsageLookup.TryFindRange(
                usages, scrollId, out var start, out var count))
            return 0;
        var pending = 0;
        for (var index = 0; index < count; index++)
        {
            var row = usages[start + index];
            if (row.Pending && row.Level >= level && !row.Expired) pending++;
        }
        return pending;
    }
}
