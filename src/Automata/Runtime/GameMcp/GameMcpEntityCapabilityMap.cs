#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>
/// The sole MCP category/native-type/capability registry. Read discovery and gameplay admission both
/// consume these descriptors, so a UUID cannot be accepted as one native type by <c>world_get</c> and
/// rejected under a second hand-written type/category map by an action.
/// </summary>
internal static class GameMcpEntityCapabilityMap
{
    private static readonly GameMcpEntityCapabilityDescriptor[] Descriptors = Create();
    private static readonly Dictionary<string, GameMcpEntityCapabilityDescriptor> ByCategory = Index();

    internal static IReadOnlyList<GameMcpEntityCapabilityDescriptor> Entries => Descriptors;

    internal static string ExpectedNativeType(string category)
    {
        if (!ByCategory.TryGetValue(category, out var descriptor))
            throw new InvalidOperationException(
                "MCP world category '" + category + "' has no entity-capability descriptor");
        return descriptor.ExpectedNativeType;
    }

    internal static bool TryCategoryForNativeType(string nativeType, out string category)
    {
        for (var index = 0; index < Descriptors.Length; index++)
        {
            var descriptor = Descriptors[index];
            if (descriptor.ExpectedNativeType.IndexOf('|') >= 0 ||
                !string.Equals(descriptor.ExpectedNativeType, nativeType, StringComparison.Ordinal))
            {
                continue;
            }
            category = descriptor.Category;
            return true;
        }
        category = string.Empty;
        return false;
    }

    internal static bool Contains(
        GameWorldState world,
        Guid target,
        GameMcpCommandKind capability,
        out string reason)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (target == Guid.Empty)
        {
            reason = "a non-empty stable UUID is required";
            return false;
        }

        return capability switch
        {
            GameMcpCommandKind.Purchase => PurchaseTarget(world, target, out reason),
            GameMcpCommandKind.Cast or GameMcpCommandKind.SpellLevel or GameMcpCommandKind.SpellWorkbench =>
                Entity(
                    world.EntityIdentities,
                    world.SpellRecipes,
                    target,
                    "spell-recipes",
                    capability,
                    out reason),
            GameMcpCommandKind.Concept =>
                Entity(
                    world.EntityIdentities,
                    world.AlchemyRecipes,
                    target,
                    "alchemy-recipes",
                    capability,
                    out reason),
            GameMcpCommandKind.Harvest => HarvestTarget(world, target, out reason),
            GameMcpCommandKind.DiscoveryTreeOffer =>
                Entity(
                    world.EntityIdentities,
                    world.DiscoveryTrees,
                    target,
                    "discovery-trees",
                    capability,
                    out reason),
            _ => Unsupported(capability, out reason),
        };
    }

    internal static bool Supports(string category, GameMcpCommandKind capability) =>
        ByCategory.TryGetValue(category, out var descriptor) && descriptor.Supports(capability);

    private static bool PurchaseTarget(GameWorldState world, Guid target, out string reason)
    {
        if (!Supports("structures", GameMcpCommandKind.Purchase) ||
            !Supports("upgrades", GameMcpCommandKind.Purchase))
        {
            reason = "the authoritative capability map does not admit purchase targets";
            return false;
        }
        var structure = WorldLookup.TryFind(world.Structures, target, out _);
        var upgrade = WorldLookup.TryFind(world.Upgrades, target, out _);
        if (structure != upgrade)
        {
            reason = string.Empty;
            return true;
        }
        reason = structure
            ? "Identity " + EntityIdentityFormatter.Format(target, world.EntityIdentities) +
              " ambiguously identifies both a structure and an upgrade"
            : "Identity " + EntityIdentityFormatter.Format(target, world.EntityIdentities) +
              " is absent from published structures and upgrades";
        return false;
    }

    private static bool HarvestTarget(GameWorldState world, Guid target, out string reason)
    {
        if (!Supports("plot-nodes", GameMcpCommandKind.Harvest))
        {
            reason = "the authoritative capability map does not admit harvest targets";
            return false;
        }
        if (!WorldLookup.TryFind(world.PlotNodes, target, out _))
        {
            reason = "Identity " + EntityIdentityFormatter.Format(target, world.EntityIdentities) +
                " is absent from published category plot-nodes";
            return false;
        }
        if (target == KnownEntities.FruitTreePlot.Uuid ||
            target == KnownEntities.TreasureTreePlot.Uuid)
        {
            reason = string.Empty;
            return true;
        }
        reason = "plot " + EntityIdentityFormatter.Format(target, world.EntityIdentities) +
            " is published but has no audited native harvest capability";
        return false;
    }

    private static bool Entity<TRow>(
        EntityIdentityCatalogSnapshot identities,
        PublicationTable<TRow> table,
        Guid target,
        string category,
        GameMcpCommandKind capability,
        out string reason)
        where TRow : struct, IWorldEntity
    {
        if (!Supports(category, capability))
        {
            reason = "category " + category + " does not admit command " + capability;
            return false;
        }
        if (WorldLookup.TryFind(table, target, out _))
        {
            reason = string.Empty;
            return true;
        }
        reason = "Identity " + EntityIdentityFormatter.Format(target, identities) +
            " is absent from published category " + category;
        return false;
    }

    private static bool Unsupported(GameMcpCommandKind capability, out string reason)
    {
        reason = "command " + capability + " is not a gameplay entity capability";
        return false;
    }

    private static Dictionary<string, GameMcpEntityCapabilityDescriptor> Index()
    {
        var result = new Dictionary<string, GameMcpEntityCapabilityDescriptor>(StringComparer.Ordinal);
        for (var index = 0; index < Descriptors.Length; index++)
            result.Add(Descriptors[index].Category, Descriptors[index]);
        return result;
    }

    private static GameMcpEntityCapabilityDescriptor[] Create() => new[]
    {
        D("resources", "ResourceSO"),
        D("structures", "StructureSO", GameMcpCommandKind.Purchase),
        D("upgrades", "UpgradeSO", GameMcpCommandKind.Purchase),
        D("research", "ResearchSO"),
        D("double-variables", "DoubleVariable"),
        D("int-variables", "IntVariable"),
        D("bool-variables", "BoolVariable"),
        D("modifier-variables", "ValueModifierVariable"),
        D("purchase-costs", "StructureSO|UpgradeSO"),
        D("alchemy-recipes", "AlchemyRecipeSO", GameMcpCommandKind.Concept),
        D("alchemy-types", "AlchemyTypeSO"),
        D("spell-recipes", "SpellRecipeSO", GameMcpCommandKind.Cast, GameMcpCommandKind.SpellLevel,
            GameMcpCommandKind.SpellWorkbench),
        D("spell-types", "SpellTypeSO"),
        D("equipment", "EquipmentSO"),
        D("equipment-types", "EquipmentTypeSO"),
        D("resource-types", "ResourceTypeSO"),
        D("crafting-recipe-types", "CraftingRecipeTypeSO"),
        D("crafting-recipes", "CraftingRecipeSO"),
        D("harvest-elements", "HarvestElementSO"),
        D("harvest-resources", "HarvestElementSO"),
        D("time-runes", "TimeRuneSO"),
        D("glyphs", "GlyphSO"),
        D("consumables", "ConsumableSO"),
        D("rituals", "RitualSO"),
        D("achievements", "AchievementSO"),
        D("advancements", "AdvancementSO"),
        D("challenges", "ChallengeSO"),
        D("thought-streams", "ThoughtStreamSO"),
        D("tutorials", "TutorialSO"),
        D("views", "ViewSO"),
        D("plot-node-actions", "PlotNodeActionSO"),
        D("passive-abilities", "PassiveAbilitySO"),
        D("characters", "CharacterSO"),
        D("discovery-trees", "DiscoveryTreeSO", GameMcpCommandKind.DiscoveryTreeOffer),
        D("recipe-books", "RecipeBookSO"),
        D("plot-nodes", "PlotNodeSO", GameMcpCommandKind.Harvest),
        D("plot-actions", "PlotNodeSO|PlotNodeActionSO"),
        D("plot-action-instances", "PlotNodeActionInstance"),
        D("action-queues", "ActionQueueVariable"),
        D("action-queue-slots", "PlotNodeActionInstance"),
        D("spell-slots", "Spell"),
        D("spell-costs", "Spell"),
        D("mastery-experience", "SpellRecipeSO|AlchemyRecipeSO|EquipmentSO"),
        D("concept-recipes", "AlchemyRecipeSO"),
        D("alchemy-instances", "AlchemyInstance"),
        D("alchemy-costs", "AlchemyInstance"),
        D("plot-authoring", "PlotNodeSO"),
        D("plot-phase-descriptors", "PlotNodeSO"),
        D("effect-blocks", "EffectSO"),
        D("entity-requirements", "EntitySO"),
        D("requirement-native-verdicts", "StructureSO|UpgradeSO"),
        D("treasure-pools", "TreasurePoolSO"),
    };

    private static GameMcpEntityCapabilityDescriptor D(
        string category,
        string nativeType,
        params GameMcpCommandKind[] capabilities) =>
        new(category, nativeType, capabilities);
}

internal sealed class GameMcpEntityCapabilityDescriptor
{
    private readonly GameMcpCommandKind[] _capabilities;

    internal GameMcpEntityCapabilityDescriptor(
        string category,
        string expectedNativeType,
        GameMcpCommandKind[] capabilities)
    {
        Category = category ?? throw new ArgumentNullException(nameof(category));
        ExpectedNativeType = expectedNativeType ??
            throw new ArgumentNullException(nameof(expectedNativeType));
        _capabilities = capabilities is null
            ? Array.Empty<GameMcpCommandKind>()
            : (GameMcpCommandKind[])capabilities.Clone();
    }

    internal string Category { get; }
    internal string ExpectedNativeType { get; }
    internal IReadOnlyList<GameMcpCommandKind> Capabilities => _capabilities;

    internal bool Supports(GameMcpCommandKind capability)
    {
        for (var index = 0; index < _capabilities.Length; index++)
            if (_capabilities[index] == capability) return true;
        return false;
    }
}
#endif
