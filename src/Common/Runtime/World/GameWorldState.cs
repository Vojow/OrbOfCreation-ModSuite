using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One immutable snapshot of everything the suite knows about the game world, published once per
/// collection interval and consumed by every service the same way configuration is.
/// </summary>
/// <remarks>
/// <para>
/// The shape is category-generic on purpose: one bounded table per entity category, each keyed by
/// stable UUID. Adding a category is a row type and a table, not a redesign — which is why the four
/// categories this started with became every category the game keeps per-entity state for, without
/// the shape changing.
/// </para>
/// <para>
/// This exists because roughly ten services are expected to need resource state every tick, and
/// having each capture its own copy costs the main thread once per service. One publication that
/// every worker pins is the same latest-wins, generation-stamped bargain configuration already
/// makes: a consumer that runs before a newer snapshot lands uses the previous one and picks the new
/// one up next cycle, so no cross-service scheduling is introduced.
/// </para>
/// <para>
/// Tables are sorted by identity at build time, so lookups are a binary search rather than a scan —
/// with 80 resources and 222 upgrades in a real save, a linear probe per candidate per cycle is the
/// kind of cost this whole design exists to remove.
/// </para>
/// <para>
/// Public because the worker contract names it: the runtime hands every service the snapshot as an
/// argument, and an internal parameter type cannot appear on a public interface. Its members stay
/// internal, so the suite reads the world and anything outside it sees only an opaque handle.
/// </para>
/// </remarks>
public sealed record GameWorldState
{
    internal PublicationTable<WorldResource> Resources { get; init; } =
        PublicationTable<WorldResource>.Empty;

    internal PublicationTable<WorldStructure> Structures { get; init; } =
        PublicationTable<WorldStructure>.Empty;

    internal PublicationTable<WorldUpgrade> Upgrades { get; init; } =
        PublicationTable<WorldUpgrade>.Empty;

    internal PublicationTable<WorldResearch> Research { get; init; } =
        PublicationTable<WorldResearch>.Empty;

    /// <summary>
    /// The game's global variables, by registry. Every <c>Player</c> and <c>GlobalVariables</c>
    /// accessor is a lookup into one of these three, so they are collected as registries rather than
    /// as a hundred separately declared accessors.
    /// </summary>
    internal PublicationTable<WorldNumberVariable> DoubleVariables { get; init; } =
        PublicationTable<WorldNumberVariable>.Empty;

    internal PublicationTable<WorldNumberVariable> IntVariables { get; init; } =
        PublicationTable<WorldNumberVariable>.Empty;

    internal PublicationTable<WorldBoolVariable> BoolVariables { get; init; } =
        PublicationTable<WorldBoolVariable>.Empty;

    /// <summary>
    /// The global modifier registry. Entities point into it by identity rather than owning a copy,
    /// so a modifier shared by twenty structures is collected once.
    /// </summary>
    internal PublicationTable<WorldModifierVariable> ModifierVariables { get; init; } =
        PublicationTable<WorldModifierVariable>.Empty;

    /// <summary>
    /// What one more level of each entity costs, computed here rather than asked of the game. Keyed by
    /// entity and then resource, with several rows per entity; read it through
    /// <see cref="WorldPurchaseCostLookup"/> rather than <see cref="WorldLookup"/>.
    /// </summary>
    /// <summary>
    /// What each entity's purchase does to other entities' named properties, keyed by the entity
    /// applying the effect; read it through <see cref="WorldEntityEffectLookup"/>.
    /// </summary>
    internal PublicationTable<WorldEntityEffect> EntityEffects { get; init; } =
        PublicationTable<WorldEntityEffect>.Empty;

    internal PublicationTable<WorldPurchaseCost> PurchaseCosts { get; init; } =
        PublicationTable<WorldPurchaseCost>.Empty;

    internal PublicationTable<WorldAlchemyRecipe> AlchemyRecipes { get; init; } =
        PublicationTable<WorldAlchemyRecipe>.Empty;

    internal PublicationTable<WorldAlchemyType> AlchemyTypes { get; init; } =
        PublicationTable<WorldAlchemyType>.Empty;

    internal PublicationTable<WorldSpellRecipe> SpellRecipes { get; init; } =
        PublicationTable<WorldSpellRecipe>.Empty;

    internal PublicationTable<WorldSpellType> SpellTypes { get; init; } =
        PublicationTable<WorldSpellType>.Empty;

    internal PublicationTable<WorldEquipment> Equipment { get; init; } =
        PublicationTable<WorldEquipment>.Empty;

    internal PublicationTable<WorldEquipmentType> EquipmentTypes { get; init; } =
        PublicationTable<WorldEquipmentType>.Empty;

    internal PublicationTable<WorldResourceType> ResourceTypes { get; init; } =
        PublicationTable<WorldResourceType>.Empty;

    internal PublicationTable<WorldCraftingRecipeType> CraftingRecipeTypes { get; init; } =
        PublicationTable<WorldCraftingRecipeType>.Empty;

    internal PublicationTable<WorldHarvestElement> HarvestElements { get; init; } =
        PublicationTable<WorldHarvestElement>.Empty;

    /// <summary>
    /// The resource each harvest element owns. Separate from <see cref="Resources"/> because the game
    /// keeps it out of <c>ResourceSO.All</c> and out of every global aggregate; see
    /// <see cref="RawHarvestResourceSample"/>.
    /// </summary>
    internal PublicationTable<WorldHarvestResource> HarvestResources { get; init; } =
        PublicationTable<WorldHarvestResource>.Empty;

    internal PublicationTable<WorldTimeRune> TimeRunes { get; init; } =
        PublicationTable<WorldTimeRune>.Empty;

    internal PublicationTable<WorldGlyph> Glyphs { get; init; } =
        PublicationTable<WorldGlyph>.Empty;

    internal PublicationTable<WorldConsumable> Consumables { get; init; } =
        PublicationTable<WorldConsumable>.Empty;

    /// <summary>Every native family assigned to each consumable.</summary>
    internal PublicationTable<WorldConsumableType> ConsumableTypes { get; init; } =
        PublicationTable<WorldConsumableType>.Empty;

    /// <summary>Every immediate and held resource cost authored on each consumable.</summary>
    internal PublicationTable<WorldConsumableCost> ConsumableCosts { get; init; } =
        PublicationTable<WorldConsumableCost>.Empty;

    /// <summary>
    /// Pending and engaged native consumable usages, keyed by owning consumable and stable
    /// lifecycle-local usage identity.
    /// </summary>
    internal PublicationTable<WorldConsumableUsage> ConsumableUsages { get; init; } =
        PublicationTable<WorldConsumableUsage>.Empty;

    internal PublicationTable<WorldRitual> Rituals { get; init; } =
        PublicationTable<WorldRitual>.Empty;

    internal PublicationTable<WorldAchievement> Achievements { get; init; } =
        PublicationTable<WorldAchievement>.Empty;

    internal PublicationTable<WorldAdvancement> Advancements { get; init; } =
        PublicationTable<WorldAdvancement>.Empty;

    internal PublicationTable<WorldChallenge> Challenges { get; init; } =
        PublicationTable<WorldChallenge>.Empty;

    internal PublicationTable<WorldThoughtStream> ThoughtStreams { get; init; } =
        PublicationTable<WorldThoughtStream>.Empty;

    internal PublicationTable<WorldTutorial> Tutorials { get; init; } =
        PublicationTable<WorldTutorial>.Empty;

    internal PublicationTable<WorldView> Views { get; init; } =
        PublicationTable<WorldView>.Empty;

    internal PublicationTable<WorldPlotNodeAction> PlotNodeActions { get; init; } =
        PublicationTable<WorldPlotNodeAction>.Empty;

    internal PublicationTable<WorldPassiveAbility> PassiveAbilities { get; init; } =
        PublicationTable<WorldPassiveAbility>.Empty;

    internal PublicationTable<WorldCharacter> Characters { get; init; } =
        PublicationTable<WorldCharacter>.Empty;

    internal PublicationTable<WorldDiscoveryTree> DiscoveryTrees { get; init; } =
        PublicationTable<WorldDiscoveryTree>.Empty;

    internal PublicationTable<WorldRecipeBook> RecipeBooks { get; init; } =
        PublicationTable<WorldRecipeBook>.Empty;

    internal PublicationTable<WorldPlotNode> PlotNodes { get; init; } =
        PublicationTable<WorldPlotNode>.Empty;

    /// <summary>
    /// Every plot-and-action pair: whether the plot offers the action, whether it holds an instance
    /// of it, and what one run would cost. Read through <see cref="WorldPlotActionLookup"/>.
    /// </summary>
    internal PublicationTable<WorldPlotAction> PlotActions { get; init; } =
        PublicationTable<WorldPlotAction>.Empty;

    /// <summary>
    /// Every runtime action instance each plot holds, sorted by plot, then action, then position.
    /// Read through <see cref="WorldPlotActionInstanceLookup"/>, which answers with a range because
    /// a plot may hold several instances of one action.
    /// </summary>
    internal PublicationTable<WorldPlotActionInstance> PlotActionInstances { get; init; } =
        PublicationTable<WorldPlotActionInstance>.Empty;

    /// <summary>
    /// The game's action queues, as occupancy. A plan may be shaped by what the last reading said
    /// fits; whether one more action actually fits is the action boundary's answer, not this table's.
    /// </summary>
    internal PublicationTable<WorldActionQueue> ActionQueues { get; init; } =
        PublicationTable<WorldActionQueue>.Empty;

    /// <summary>
    /// What occupies each slot of the plot-action queue, sorted by queue and then position.
    /// </summary>
    internal PublicationTable<WorldActionQueueSlot> ActionQueueSlots { get; init; } =
        PublicationTable<WorldActionQueueSlot>.Empty;

    /// <summary>
    /// The player's equipped spell loadout, sorted by position. Reached by
    /// <see cref="WorldSpellSlotLookup"/>, because a slot is a position rather than an entity.
    /// </summary>
    internal PublicationTable<WorldSpellSlot> SpellSlots { get; init; } =
        PublicationTable<WorldSpellSlot>.Empty;

    /// <summary>
    /// What each equipped spell costs to cast and to sustain, sorted by slot, then kind, then
    /// resource. Reached by <see cref="WorldSpellCostLookup"/>.
    /// </summary>
    internal PublicationTable<WorldSpellCost> SpellCosts { get; init; } =
        PublicationTable<WorldSpellCost>.Empty;

    /// <summary>
    /// Exact native mastery-XP inputs retained for this lifecycle. Consumers use the monotonic
    /// sequence to process each observation once even when several world generations repeat it.
    /// </summary>
    internal PublicationTable<WorldMasteryExperience> MasteryExperience { get; init; } =
        PublicationTable<WorldMasteryExperience>.Empty;

    /// <summary>The recipes in the native ConceptRecipes registry and their compatible slot types.</summary>
    internal PublicationTable<WorldConceptRecipe> ConceptRecipes { get; init; } =
        PublicationTable<WorldConceptRecipe>.Empty;

    /// <summary>The currently active Concept assignments, keyed by their recipe identities.</summary>
    internal PublicationTable<WorldAlchemyInstance> AlchemyInstances { get; init; } =
        PublicationTable<WorldAlchemyInstance>.Empty;

    /// <summary>
    /// Authored and current Concept drains, keyed by recipe and resource. Prospective quantities are
    /// deliberately absent: the game only answers those from a throwaway native instance.
    /// </summary>
    internal PublicationTable<WorldAlchemyCost> AlchemyCosts { get; init; } =
        PublicationTable<WorldAlchemyCost>.Empty;

    /// <summary>
    /// What each plot's author decided about it. Keyed by plot rather than keyed <em>as</em> a plot,
    /// so the plot's identity stays claimed exactly once.
    /// </summary>
    internal PublicationTable<WorldPlotAuthoring> PlotAuthoring { get; init; } =
        PublicationTable<WorldPlotAuthoring>.Empty;

    /// <summary>Every phase each plot authors, in the order the plot lists them.</summary>
    internal PublicationTable<WorldPlotPhaseDescriptor> PlotPhaseDescriptors { get; init; } =
        PublicationTable<WorldPlotPhaseDescriptor>.Empty;

    /// <summary>
    /// Every authored effect block, described by its shape. What a consumer concludes from the shape
    /// is that consumer's policy.
    /// </summary>
    internal PublicationTable<WorldEffectBlock> EffectBlocks { get; init; } =
        PublicationTable<WorldEffectBlock>.Empty;

    /// <summary>
    /// Every authored condition on an entity's next level, keyed by the entity it gates and read
    /// through <see cref="WorldEntityRequirementLookup"/>.
    /// </summary>
    /// <remarks>
    /// The game's own answer takes a level argument — <c>prerequisitesPerLevel.Check(level)</c> — so
    /// unlike the whole-entity gate it cannot be published as a boolean. The conditions themselves can
    /// be, and everything they compare against is already a row in this same snapshot, which is what
    /// lets a worker reach the verdict without asking the game. An entity with no row here authored no
    /// per-level condition, which is the game's own unconditional pass rather than a gap in the read.
    /// </remarks>
    internal PublicationTable<WorldEntityRequirement> EntityRequirements { get; init; } =
        PublicationTable<WorldEntityRequirement>.Empty;

    internal PublicationTable<WorldTreasurePool> TreasurePools { get; init; } =
        PublicationTable<WorldTreasurePool>.Empty;

    /// <summary>
    /// Unity's fixed timestep, in seconds, as of the collection that produced this snapshot.
    /// </summary>
    /// <remarks>
    /// A property of the tick rather than of any entity, so it sits here rather than being copied
    /// into eighty resource readings. It is on the snapshot at all because the ported rate chain
    /// needs it and <c>Time.fixedDeltaTime</c> is a Unity static that may only be touched on the main
    /// thread — reading it during capture is the only way a worker can have it.
    /// <para>
    /// The other three per-tick globals the rate chain reads are <c>DoubleVariable</c>s and are
    /// already in <see cref="DoubleVariables"/>; only this one has no registry to come from.
    /// </para>
    /// </remarks>
    internal double FixedDeltaTime { get; init; }

    /// <summary>The pump frame whose native readings produced this snapshot.</summary>
    internal long CollectedAtFrame { get; init; }

    /// <summary>
    /// Which run of the game this snapshot describes, as opposed to how new it is.
    /// </summary>
    /// <remarks>
    /// A save load or a reset can leave an entity's identity intact while replacing the object behind
    /// it, which no generation comparison can detect — a generation says the world was re-read, not
    /// that it is the same world. This is the native lifecycle counter the host replaces its runners
    /// on, read during capture and carried through derivation unchanged, so a consumer can compare it
    /// against the lifecycle its own cycle was pinned to. It plays no part in the world-freshness
    /// gate: an epoch is not a frame and never participates in that comparison.
    /// </remarks>
    internal long CollectedAtEpoch { get; init; }

    // Lookups live on WorldLookup rather than here. A member per category would be a forwarding
    // one-liner repeated once per table, and the record is meant to grow to roughly thirty of them.
}

/// <summary>
/// The empty world state, held outside <see cref="GameWorldState"/> because published shapes may
/// not own non-constant static storage — the structural validator rejects it, since a static field on
/// a published type is exactly the shared mutable surface publication is meant to eliminate.
/// </summary>
internal static class GameWorldStateDefaults
{
    /// <summary>
    /// The snapshot every consumer sees before the first collection completes. Empty rather than
    /// absent, so a service that starts early reads "nothing known yet" from the normal lookup path
    /// instead of needing a null check the type system cannot enforce.
    /// </summary>
    internal static readonly GameWorldState Empty = new();
}
