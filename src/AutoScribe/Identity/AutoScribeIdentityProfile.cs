using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct AutoScribeNativeIdentity
{
    internal AutoScribeNativeIdentity(Guid uuid, string expectedType)
    {
        if (uuid == Guid.Empty)
            throw new ArgumentException("A native identity UUID is required.", nameof(uuid));
        if (string.IsNullOrWhiteSpace(expectedType))
            throw new ArgumentException("An expected native type is required.", nameof(expectedType));
        Uuid = uuid;
        ExpectedType = expectedType;
    }

    internal Guid Uuid { get; }
    internal string ExpectedType { get; }
}

internal readonly struct AutoScribeRoleDescriptor
{
    internal AutoScribeRoleDescriptor(
        int ordinal,
        ScrollRoleKey key,
        string displayName,
        in AutoScribeNativeIdentity scroll,
        in AutoScribeNativeIdentity enchantment,
        AutoScribeNativeIdentity? recipe,
        int craftCostOrder)
    {
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
        if (craftCostOrder < 0) throw new ArgumentOutOfRangeException(nameof(craftCostOrder));
        Ordinal = ordinal;
        Key = key;
        DisplayName = displayName ?? string.Empty;
        Scroll = scroll;
        Enchantment = enchantment;
        Recipe = recipe;
        CraftCostOrder = craftCostOrder;
    }

    internal int Ordinal { get; }
    internal ScrollRoleKey Key { get; }
    internal string DisplayName { get; }
    internal AutoScribeNativeIdentity Scroll { get; }
    internal AutoScribeNativeIdentity Enchantment { get; }
    internal AutoScribeNativeIdentity? Recipe { get; }
    internal int CraftCostOrder { get; }
    internal bool IsProducible => Recipe.HasValue;
}

internal sealed class AutoScribeIdentityProfile
{
    internal AutoScribeIdentityProfile(
        string baselineId,
        in AutoScribeNativeIdentity recipeType,
        in AutoScribeNativeIdentity recipeRegistry,
        in AutoScribeNativeIdentity activeInstances,
        in AutoScribeNativeIdentity automaticInstances,
        AutoScribeRoleDescriptor[] roles)
    {
        if (string.IsNullOrWhiteSpace(baselineId))
            throw new ArgumentException("A baseline identity is required.", nameof(baselineId));
        if (roles is null) throw new ArgumentNullException(nameof(roles));
        if (roles.Length == 0)
            throw new ArgumentException("At least one Scroll role is required.", nameof(roles));

        for (var index = 0; index < roles.Length; index++)
        {
            if (roles[index].Ordinal != index)
                throw new ArgumentException("Role ordinals must be dense and stable.", nameof(roles));
            if (!roles[index].IsProducible) continue;
            for (var previous = 0; previous < index; previous++)
            {
                if (roles[previous].IsProducible &&
                    roles[previous].CraftCostOrder == roles[index].CraftCostOrder)
                    throw new ArgumentException(
                        "Producible Scroll roles require unique craft-cost ranks.",
                        nameof(roles));
            }
        }

        BaselineId = baselineId;
        RecipeType = recipeType;
        RecipeRegistry = recipeRegistry;
        ActiveInstances = activeInstances;
        AutomaticInstances = automaticInstances;
        Roles = PublicationTable<AutoScribeRoleDescriptor>.Create(roles, roles.Length);
    }

    internal string BaselineId { get; }
    internal AutoScribeNativeIdentity RecipeType { get; }
    internal AutoScribeNativeIdentity RecipeRegistry { get; }
    internal AutoScribeNativeIdentity ActiveInstances { get; }
    internal AutoScribeNativeIdentity AutomaticInstances { get; }
    internal PublicationTable<AutoScribeRoleDescriptor> Roles { get; }

    internal bool TryFindByScroll(Guid scrollId, out AutoScribeRoleDescriptor role)
    {
        for (var index = 0; index < Roles.Count; index++)
        {
            if (Roles[index].Scroll.Uuid != scrollId) continue;
            role = Roles[index];
            return true;
        }
        role = default;
        return false;
    }

    internal bool TryFindByRecipe(Guid recipeId, out AutoScribeRoleDescriptor role)
    {
        for (var index = 0; index < Roles.Count; index++)
        {
            if (Roles[index].Recipe?.Uuid != recipeId) continue;
            role = Roles[index];
            return true;
        }
        role = default;
        return false;
    }

    internal bool TryFind(ScrollRoleKey key, out AutoScribeRoleDescriptor role)
    {
        for (var index = 0; index < Roles.Count; index++)
        {
            if (Roles[index].Key != key) continue;
            role = Roles[index];
            return true;
        }
        role = default;
        return false;
    }

    internal bool TryFindOrdinal(int ordinal, out AutoScribeRoleDescriptor role)
    {
        if ((uint)ordinal < (uint)Roles.Count)
        {
            role = Roles[ordinal];
            return true;
        }
        role = default;
        return false;
    }
}

internal sealed class AutoScribeIdentityCatalog
{
    private static readonly AutoScribeIdentityProfile WindowsV1052 =
        Create(GameAssemblyAudit.WindowsV1052BaselineId);
    private static readonly AutoScribeIdentityProfile MacV1052 =
        Create(GameAssemblyAudit.MacV1052BaselineId);

    internal bool TryGetProfile(string baselineId, out AutoScribeIdentityProfile profile)
    {
        if (string.Equals(
                baselineId,
                GameAssemblyAudit.WindowsV1052BaselineId,
                StringComparison.Ordinal))
        {
            profile = WindowsV1052;
            return true;
        }
        if (string.Equals(
                baselineId,
                GameAssemblyAudit.MacV1052BaselineId,
                StringComparison.Ordinal))
        {
            profile = MacV1052;
            return true;
        }
        profile = null!;
        return false;
    }

    internal static AutoScribeIdentityProfile Audited => WindowsV1052;

    private static AutoScribeIdentityProfile Create(string baselineId)
    {
        var recipeType = Identity(KnownEntities.ScribeCrafting);
        var registry = Identity(KnownEntities.ScribeCraftingRecipes);
        var active = Identity(KnownEntities.ActiveScribeInstances);
        var automatic = Identity(KnownEntities.AutoScribeInstances);
        return new AutoScribeIdentityProfile(
            baselineId,
            in recipeType,
            in registry,
            in active,
            in automatic,
            new[]
            {
                Role(0, "scribe.advancement", "Advancement", KnownEntities.ScrollAdvancement,
                    KnownEntities.EnchantAdvancement, KnownEntities.CraftScrollAdvancement, 0),
                Role(1, "scribe.development", "Development", KnownEntities.ScrollDevelopment,
                    KnownEntities.EnchantDevelopment, KnownEntities.CraftScrollDevelopment, 4),
                Role(2, "scribe.echo", "Echoing", KnownEntities.ScrollEcho,
                    KnownEntities.EnchantEcho, KnownEntities.CraftScrollEcho, 5),
                Role(3, "scribe.excellence", "Excellence", KnownEntities.ScrollExcellence,
                    KnownEntities.EnchantExcellence, KnownEntities.CraftScrollExcellence, 3),
                CoverageOnly(4, "scribe.investment", "Investment", KnownEntities.ScrollInvestment,
                    KnownEntities.EnchantInvestment),
                Role(5, "scribe.learning", "Learning", KnownEntities.ScrollLearning,
                    KnownEntities.EnchantLearning, KnownEntities.CraftScrollLearning, 2),
                Role(6, "scribe.power", "Power", KnownEntities.ScrollPower,
                    KnownEntities.EnchantPower, KnownEntities.CraftScrollPower, 1),
                CoverageOnly(7, "scribe.speed", "Speed", KnownEntities.ScrollSpeed,
                    KnownEntities.EnchantSpeed),
            });
    }

    private static AutoScribeRoleDescriptor Role(
        int ordinal,
        string key,
        string displayName,
        KnownEntity<ConsumableSOContract> scroll,
        KnownEntity<EnchantmentSOContract> enchantment,
        KnownEntity<CraftingRecipeSOContract> recipe,
        int craftCostOrder)
    {
        var scrollIdentity = Identity(scroll);
        var enchantmentIdentity = Identity(enchantment);
        var recipeIdentity = Identity(recipe);
        return new AutoScribeRoleDescriptor(
            ordinal,
            new ScrollRoleKey(key),
            displayName,
            in scrollIdentity,
            in enchantmentIdentity,
            recipeIdentity,
            craftCostOrder);
    }

    private static AutoScribeRoleDescriptor CoverageOnly(
        int ordinal,
        string key,
        string displayName,
        KnownEntity<ConsumableSOContract> scroll,
        KnownEntity<EnchantmentSOContract> enchantment)
    {
        var scrollIdentity = Identity(scroll);
        var enchantmentIdentity = Identity(enchantment);
        return new AutoScribeRoleDescriptor(
            ordinal,
            new ScrollRoleKey(key),
            displayName,
            in scrollIdentity,
            in enchantmentIdentity,
            recipe: null,
            craftCostOrder: int.MaxValue);
    }

    private static AutoScribeNativeIdentity Identity<T>(KnownEntity<T> entity) =>
        new(entity.Uuid, entity.ManagedTypeName);
}
