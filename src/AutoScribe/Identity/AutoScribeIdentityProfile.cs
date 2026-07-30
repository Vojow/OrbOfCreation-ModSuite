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
        ScrollRoleKey key,
        string displayName,
        in AutoScribeNativeIdentity scroll,
        in AutoScribeNativeIdentity enchantment,
        AutoScribeNativeIdentity? recipe)
    {
        Key = key;
        DisplayName = displayName ?? string.Empty;
        Scroll = scroll;
        Enchantment = enchantment;
        Recipe = recipe;
    }

    internal ScrollRoleKey Key { get; }
    internal string DisplayName { get; }
    internal AutoScribeNativeIdentity Scroll { get; }
    internal AutoScribeNativeIdentity Enchantment { get; }
    internal AutoScribeNativeIdentity? Recipe { get; }
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
        BaselineId = baselineId;
        RecipeType = recipeType;
        RecipeRegistry = recipeRegistry;
        ActiveInstances = activeInstances;
        AutomaticInstances = automaticInstances;
        if (roles is null) throw new ArgumentNullException(nameof(roles));
        if (roles.Length == 0)
            throw new ArgumentException("At least one Scroll role is required.", nameof(roles));
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
}

internal interface IAutoScribeIdentityCatalog
{
    bool TryGetProfile(string baselineId, out AutoScribeIdentityProfile profile);
}

internal sealed class AutoScribeIdentityCatalog : IAutoScribeIdentityCatalog
{
    private static readonly AutoScribeIdentityProfile WindowsV1052 =
        CreateWindowsV1052();

    public bool TryGetProfile(string baselineId, out AutoScribeIdentityProfile profile)
    {
        if (string.Equals(
                baselineId,
                GameAssemblyAudit.WindowsV1052BaselineId,
                StringComparison.Ordinal))
        {
            profile = WindowsV1052;
            return true;
        }
        profile = null!;
        return false;
    }

    private static AutoScribeIdentityProfile CreateWindowsV1052()
    {
        var recipeType = Identity(KnownEntities.ScribeCrafting);
        var registry = Identity(KnownEntities.ScribeCraftingRecipes);
        var active = Identity(KnownEntities.ActiveScribeInstances);
        var automatic = Identity(KnownEntities.AutoScribeInstances);
        return new AutoScribeIdentityProfile(
            GameAssemblyAudit.WindowsV1052BaselineId,
            in recipeType,
            in registry,
            in active,
            in automatic,
            new[]
            {
                Role(
                    "scribe.advancement",
                    "Advancement",
                    KnownEntities.ScrollAdvancement,
                    KnownEntities.EnchantAdvancement,
                    KnownEntities.CraftScrollAdvancement),
                Role(
                    "scribe.development",
                    "Development",
                    KnownEntities.ScrollDevelopment,
                    KnownEntities.EnchantDevelopment,
                    KnownEntities.CraftScrollDevelopment),
                Role(
                    "scribe.echo",
                    "Echoing",
                    KnownEntities.ScrollEcho,
                    KnownEntities.EnchantEcho,
                    KnownEntities.CraftScrollEcho),
                Role(
                    "scribe.excellence",
                    "Excellence",
                    KnownEntities.ScrollExcellence,
                    KnownEntities.EnchantExcellence,
                    KnownEntities.CraftScrollExcellence),
                CoverageOnly(
                    "scribe.investment",
                    "Investment",
                    KnownEntities.ScrollInvestment,
                    KnownEntities.EnchantInvestment),
                Role(
                    "scribe.learning",
                    "Learning",
                    KnownEntities.ScrollLearning,
                    KnownEntities.EnchantLearning,
                    KnownEntities.CraftScrollLearning),
                Role(
                    "scribe.power",
                    "Power",
                    KnownEntities.ScrollPower,
                    KnownEntities.EnchantPower,
                    KnownEntities.CraftScrollPower),
                CoverageOnly(
                    "scribe.speed",
                    "Speed",
                    KnownEntities.ScrollSpeed,
                    KnownEntities.EnchantSpeed),
            });
    }

    private static AutoScribeRoleDescriptor Role(
        string key,
        string displayName,
        KnownEntity<ConsumableSOContract> scroll,
        KnownEntity<EnchantmentSOContract> enchantment,
        KnownEntity<CraftingRecipeSOContract> recipe)
    {
        var scrollIdentity = Identity(scroll);
        var enchantmentIdentity = Identity(enchantment);
        var recipeIdentity = Identity(recipe);
        return new AutoScribeRoleDescriptor(
            new ScrollRoleKey(key),
            displayName,
            in scrollIdentity,
            in enchantmentIdentity,
            recipeIdentity);
    }

    private static AutoScribeRoleDescriptor CoverageOnly(
        string key,
        string displayName,
        KnownEntity<ConsumableSOContract> scroll,
        KnownEntity<EnchantmentSOContract> enchantment)
    {
        var scrollIdentity = Identity(scroll);
        var enchantmentIdentity = Identity(enchantment);
        return new AutoScribeRoleDescriptor(
            new ScrollRoleKey(key),
            displayName,
            in scrollIdentity,
            in enchantmentIdentity,
            recipe: null);
    }

    private static AutoScribeNativeIdentity Identity<T>(KnownEntity<T> entity) =>
        new(entity.Uuid, entity.ManagedTypeName);
}
