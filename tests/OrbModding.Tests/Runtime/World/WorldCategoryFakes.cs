using System;
using System.Collections.Generic;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// Stand-ins for the entity categories world collection walks but no test in this file is about.
/// </summary>
/// <remarks>
/// They exist so the collector tests can assert a complete pass without reaching for the shared game
/// stubs, whose registries other test classes populate and which would make a count here depend on
/// what else happens to be running. Each spells out exactly the members its binder requires, so a
/// binder that grows a member fails to bind here before it fails anywhere else.
/// <para>
/// The real member names are checked against the shipped assemblies by the native contract manifest,
/// and the shared stubs cover by-name resolution end to end. These types cover neither; they cover
/// the traversal.
/// </para>
/// </remarks>
internal static class WorldCategoryFakes
{
    /// <summary>Maps each game type name to its stand-in, for a collector test's resolver.</summary>
    internal static IReadOnlyDictionary<string, Type?> ByTypeName { get; } = new Dictionary<string, Type?>(StringComparer.Ordinal)
    {
        ["AlchemyRecipeSO"] = typeof(FakeAlchemyRecipe),
        ["AlchemyTypeSO"] = typeof(FakeAlchemyType),
        ["AlchemyInstanceListVariable"] = typeof(FakeAlchemyInstanceList),
        ["AlchemyRecipeListVariable"] = typeof(FakeAlchemyRecipeList),
        ["SpellRecipeSO"] = typeof(FakeSpellRecipe),
        ["SpellTypeSO"] = typeof(FakeSpellType),
        ["EquipmentSO"] = typeof(FakeEquipment),
        ["EquipmentTypeSO"] = typeof(FakeEquipmentType),
        ["ResourceTypeSO"] = typeof(FakeResourceType),
        ["CraftingRecipeTypeSO"] = typeof(FakeCraftingRecipeType),
        ["HarvestElementSO"] = typeof(FakeHarvestElement),
        ["TimeRuneSO"] = typeof(FakeTimeRune),
        ["GlyphSO"] = typeof(FakeGlyph),
        ["ConsumableSO"] = typeof(FakeConsumable),
        ["RitualSO"] = typeof(FakeRitual),
        ["AchievementSO"] = typeof(FakeAchievement),
        ["AdvancementSO"] = typeof(FakeAdvancement),
        ["ChallengeSO"] = typeof(FakeChallenge),
        ["ThoughtStreamSO"] = typeof(FakeThoughtStream),
        ["TutorialSO"] = typeof(FakeTutorial),
        ["ViewSO"] = typeof(FakeView),
        ["PlotNodeActionSO"] = typeof(FakePlotNodeAction),
        ["PassiveAbilitySO"] = typeof(FakePassiveAbility),
        ["CharacterSO"] = typeof(FakeCharacter),
        ["DiscoveryTreeSO"] = typeof(FakeDiscoveryTree),
        ["RecipeBookSO"] = typeof(FakeRecipeBook),
        ["PlotNodeSO"] = typeof(FakePlotNode),
        ["TreasurePoolSO"] = typeof(FakeTreasurePool),
        ["ValueModifierVariable"] = typeof(FakeModifierVariable),

        // Not a category either: the action queues belong to no per-type registry, so the reader
        // reaches them through the identity registry by uuid.
        ["IdScriptableObject"] = typeof(FakeIdRegistry),
        ["PlotNodeActionInstanceListVariable"] = typeof(FakeActionQueue),
        ["ActionableListVariable"] = typeof(FakeAttributeQueue),

        // Nor is the equipped loadout: Spell belongs to no per-type registry either, so the list
        // holding it is reached by uuid the same way and the slots are read out of that list.
        ["SpellListVariable"] = typeof(FakeSpellLoadout),

        // Not categories either: an effect block's one modifier and one script are read past their
        // counts, and the lists holding them are typed as interfaces, so the two kinds the suite
        // knows how to read are reached by name.
        ["ScalingWeightEffectMod"] = typeof(FakeScalingWeightMod),
        ["TreasurePoolInstantEffect"] = typeof(FakeTreasureEffect),
    };

    /// <summary>Empties every registry, so one test cannot see another's entities.</summary>
    internal static void Clear()
    {
        FakeAlchemyRecipe.All.Clear();
        FakeAlchemyType.All.Clear();
        FakeSpellRecipe.All.Clear();
        FakeSpellType.All.Clear();
        FakeEquipment.All.Clear();
        FakeEquipmentType.All.Clear();
        FakeResourceType.All.Clear();
        FakeCraftingRecipeType.All.Clear();
        FakeHarvestElement.All.Clear();
        FakeTimeRune.All.Clear();
        FakeGlyph.All.Clear();
        FakeConsumable.All.Clear();
        FakeRitual.All.Clear();
        FakeAchievement.All.Clear();
        FakeAdvancement.All.Clear();
        FakeChallenge.All.Clear();
        FakeThoughtStream.All.Clear();
        FakeTutorial.All.Clear();
        FakeView.All.Clear();
        FakePlotNodeAction.All.Clear();
        FakePassiveAbility.All.Clear();
        FakeCharacter.All.Clear();
        FakeDiscoveryTree.All.Clear();
        FakeRecipeBook.All.Clear();
        FakePlotNode.All.Clear();
        FakeTreasurePool.All.Clear();
        FakeModifierVariable.All.Clear();
        FakeIdRegistry.RuntimeLookup.Clear();
    }
}

/// <summary>
/// The game's identity registry, which is how an entity belonging to no per-type <c>All</c> list is
/// reached. Only the action queues need it.
/// </summary>
internal static class FakeIdRegistry
{
    public static readonly Dictionary<Guid, object> RuntimeLookup = new();
}

/// <summary>
/// An action queue: a list of slots, plus the game's own answers about how many are in use.
/// </summary>
internal sealed class FakeActionQueue
{
    public Guid Identity = Guid.NewGuid();
    public List<FakeQueueSlot?> value = new();

    /// <summary>
    /// What the game answers about occupancy when a test wants it to disagree with the slots. The
    /// two agreeing is what the published reading claims, so a test needs to be able to break it.
    /// </summary>
    public int ReportedUsedSpots = -1;

    public Guid GetGuid() => Identity;

    public int GetUsedSpots()
    {
        if (ReportedUsedSpots >= 0) return ReportedUsedSpots;

        var used = 0;
        foreach (var slot in value)
        {
            if (slot is not null && !slot.IsEmpty()) used++;
        }

        return used;
    }

    public bool HasEmptySpot() => GetUsedSpots() < value.Count;
}

/// <summary>
/// The queue whose occupancy is effectively an integer: entries of every kind the game queues, and a
/// variable saying how many of them it admits.
/// </summary>
internal sealed class FakeAttributeQueue
{
    public Guid Identity = Guid.NewGuid();
    public List<object?> value = new();
    public FakeQueueCapacity? maxQueuedItems;

    public Guid GetGuid() => Identity;

    public int GetUsedSpots()
    {
        var used = 0;
        foreach (var entry in value)
        {
            if (entry is not null) used++;
        }

        return used;
    }

    public bool HasEmptySpot() => maxQueuedItems is null || GetUsedSpots() < maxQueuedItems.Maximum;
}

/// <summary>The global variable a queue's maximum lives in, which is a row of its own elsewhere.</summary>
internal sealed class FakeQueueCapacity
{
    public Guid Identity = Guid.NewGuid();
    public int Maximum;

    public Guid GetGuid() => Identity;
}

internal class FakeQueueSlot
{
    public int quantity;
    public bool engaged;
    public FakePlotNode? plot;
    public FakePlotNodeAction? action;

    public bool IsEmpty() => quantity <= 0;

    public bool IsEngaged() => engaged;

    public int GetActualQuantity() => quantity;

    public FakePlotNode? GetElement() => plot;

    public FakePlotNodeAction? GetAction() => action;
}

/// <summary>
/// Something the list will hold that the reader's accessors were not bound against. The game's own
/// list is typed too, so this is the only shape a foreign entry can take — and the one the action
/// boundary already refuses to read.
/// </summary>
internal sealed class FakeForeignQueueSlot : FakeQueueSlot
{
}

/// <summary>
/// The global modifier registry's stand-in. Its value is an inline struct, which is the shape the
/// binder's nested reads require and the only category that has it.
/// </summary>
internal sealed class FakeModifierVariable
{
    public static readonly List<FakeModifierVariable> All = new();

    public Guid Identity = Guid.NewGuid();
    public FakeValueModifier value;

    public Guid GetGuid() => Identity;
}

internal struct FakeValueModifier
{
    public BigDouble adjustReal;
    public FakeModifierKind type;
    public int order;

    internal FakeValueModifier(FakeModifierKind type, double amount, int order)
        : this(type, new BigDouble(amount), order)
    {
    }

    /// <summary>
    /// The same modifier at a magnitude a double cannot hold. The game's own amount is a BigDouble
    /// and this game's modifiers live past 1e308, so a fake that could only be built from a double
    /// could not exercise the range the fold exists for.
    /// </summary>
    internal FakeValueModifier(FakeModifierKind type, BigDouble amount, int order = 0)
    {
        this.type = type;
        adjustReal = amount;
        this.order = order;
    }
}

/// <summary>Positionally identical to the game's ValueModifierType, which is what travels.</summary>
internal enum FakeModifierKind
{
    Raw,
    MultiDiminishing,
    MultiStacking,
    Reduction,
    Exponent,
}

/// <summary>
/// The game nests each category's state enum inside the category. One stand-in enum serves them all:
/// collection reads the underlying integer and never the member names.
/// </summary>
internal enum FakeState
{
    Idle,
    Active,
    Done,
}

/// <summary>
/// A modifier record shaped as the game shapes it: a base value, two modifier sets, a memo, and the
/// flag that decides which of the last two the game answers with. <see cref="GetValueCalls"/> is how
/// the tests prove the collector never calls the accessor.
/// </summary>
internal sealed class FakeModifierRecord
{
    private BigDouble calculatedValue;

    /// <summary>
    /// Whether the memo is out of date. The game sets it when a modifier is added or removed and
    /// clears it in Calculate(), so a record that never carries a modifier is never dirty — and its
    /// memo is what the game answers with for the rest of the session.
    /// </summary>
    private bool calculationDirty;

    /// <summary>What the collector folds from when the record is dirty. Named as the game names it.</summary>
    public double baseValue;

    public Dictionary<Guid, FakeValueModifier> passiveModifiers = new();

    public Dictionary<Guid, FakeValueModifier> activeModifiers = new();

    internal FakeModifierRecord(double value)
    {
        baseValue = value;
        calculatedValue = value;
    }

    /// <summary>
    /// With <paramref name="activeCount"/> modifiers that are all their type's identity, so the
    /// active-set count the collector publishes moves while the value does not — which is exactly the
    /// pair of facts the count tests want to vary independently.
    /// </summary>
    internal FakeModifierRecord(double value, int activeCount)
        : this(value)
    {
        for (var index = 0; index < activeCount; index++)
        {
            activeModifiers[Guid.NewGuid()] = default;
        }

        if (activeCount > 0) calculationDirty = true;
    }

    internal int GetValueCalls { get; private set; }

    /// <summary>Whether the memo is out of date, so a test can state the shape it built.</summary>
    internal bool IsCalculationDirty => calculationDirty;

    /// <summary>Sets the memo away from a recomputation, the way a save load or a purchase does.</summary>
    internal FakeModifierRecord WithStaleCache(double cached)
    {
        calculatedValue = cached;
        return this;
    }

    /// <summary>
    /// Marks the memo out of date, the way the game does whenever a modifier is added or removed. A
    /// record carrying a modifier without this is a shape the game cannot produce.
    /// </summary>
    internal FakeModifierRecord Dirty()
    {
        calculationDirty = true;
        return this;
    }

    public BigDouble GetValue()
    {
        GetValueCalls++;
        return calculatedValue;
    }
}

internal sealed class FakeAlchemyRecipe
{
    public static readonly List<FakeAlchemyRecipe> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool discovered;
    public int maxLevel;
    public int advancementLevel;
    public int discRarityLevel;
    public BigDouble masteryXp;
    public int masteryLevel;
    public BigDouble recipeTime;

    public Guid GetGuid() => Identity;
    public bool isRequiredDiscovery;
    public bool isCompletionRecipe;
    public bool isAdvancementRecipe;
    public double completionTime;
    public bool isDebugAlchemy;
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord speed = new(0d);
    public FakeModifierRecord drainCostMod = new(0d);
    public FakeModifierRecord special = new(0d);
    public FakeModifierRecord timeReqMod = new(0d);
    public FakeModifierRecord timeScalingMod = new(0d);
    public FakeModifierRecord masteryXpRate = new(0d);
    public FakeModifierRecord effectLevels = new(0d);
    public FakeModifierRecord overdrivePower = new(0d);
    public FakeModifierRecord overdriveSpeed = new(0d);
    public FakeModifierRecord overdriveDrainCostMod = new(0d);
    public FakeModifierRecord overdriveXpRate = new(0d);
    public FakeModifierRecord freeUsageSlots = new(0d);
    public FakeModifierRecord maxUsageSlots = new(0d);
    public BigDouble cachedCompletionTime;
    public BigDouble cachedRequiredXp;
    public FakeSpellCostList drainCost = new();
    public FakeAlchemyType coreType = new();

    public FakeAlchemyType GetCoreType() => coreType;

    public int GetMaxUsageSlots()
    {
        if (coreType.maxUsageByMastery) return masteryLevel + 1;
        var maximum = maxUsageSlots.GetValue().ToDouble();
        return maximum < 0 ? int.MaxValue : (int)Math.Floor(maximum);
    }
}

internal sealed class FakeAlchemyRecipeList
{
    public List<FakeAlchemyRecipe> value = new();
}

internal sealed class FakeAlchemyInstanceList
{
    public List<FakeAlchemyInstance> value = new();

    public bool CanAddInstance(FakeAlchemyRecipe recipe) => true;
}

internal sealed class FakeAlchemyInstance
{
    public FakeAlchemyInstance(FakeAlchemyRecipe recipe)
    {
        reference = recipe;
    }

    public FakeAlchemyRecipe reference;
    public int quantity;
    public int queuedQuantity;
    public FakeAlchemyDrain resourceDrain = new();

    public FakeAlchemyRecipe get_reference() => reference;
}

internal sealed class FakeAlchemyDrain
{
    public BigDouble ratio = new(1d);
    public FakeSpellCostList current = new();

    public BigDouble GetRatio() => ratio;
    public FakeSpellCostList GetCurrentDrain() => current;
}

internal sealed class FakeAlchemyType
{
    public static readonly List<FakeAlchemyType> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool maxUsageByMastery;
    public FakeModifierRecord level = new(0d);
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord speed = new(0d);
    public FakeModifierRecord special = new(0d);
    public FakeModifierRecord drainCostMod = new(0d);
    public FakeModifierRecord experienceRate = new(0d);
    public FakeModifierRecord overdrivePower = new(0d);
    public FakeModifierRecord overdriveSpeed = new(0d);
    public FakeModifierRecord overdriveDrainCostMod = new(0d);
    public FakeModifierRecord overdriveXpRate = new(0d);
    public FakeModifierRecord timeReqMod = new(0d);
    public FakeModifierRecord timeScalingMod = new(0d);
    public FakeModifierRecord freeUsageSlots = new(0d);
    public FakeModifierRecord effectLevels = new(0d);
    public FakeReferencedEntity? selectedLevel;

    public Guid GetGuid() => Identity;
}

/// <summary>
/// Something a reference edge can point at. Answers <c>GetGuid()</c> like every game entity does.
/// </summary>
internal sealed class FakeReferencedEntity
{
    public Guid Identity = Guid.NewGuid();

    public Guid GetGuid() => Identity;
}

/// <summary>The game's other reference shape: an identity already held as one.</summary>
internal sealed class FakeGuidContainer
{
    private Guid _guid;

    internal FakeGuidContainer()
    {
    }

    internal FakeGuidContainer(Guid guid) => _guid = guid;
}

/// <summary>
/// The equipped loadout, as a list variable with a uuid. Holes are real: the game lets a player leave
/// a position unfilled, and the position still counts toward the index a cast is addressed by.
/// </summary>
internal sealed class FakeSpellLoadout
{
    public Guid Identity = Guid.NewGuid();
    public List<FakeSpell?> value = new();

    public Guid GetGuid() => Identity;
}

/// <summary>One equipped spell, answering exactly what the loadout reader asks it.</summary>
internal sealed class FakeSpell
{
    public FakeSpellRecipe? spellReference;
    public bool empty;
    public bool casting;
    public bool readyingCast;
    public bool attuning;
    public bool channeled;
    public bool toggled;
    public bool chargeable;
    public bool castReady = true;
    public bool chargeAvailable = true;
    public bool resourcesCovered = true;
    public int currentCharges;
    public int maximumCharges;
    public BigDouble cooldownRemaining;
    public FakeSpellCostList cost = new();
    public FakeSpellCostList drainCost = new();

    public FakeSpellRecipe? get_reference() => spellReference;

    public bool IsEmpty() => empty;
    public bool IsCasting() => casting;
    public bool IsReadyingCast() => readyingCast;
    public bool IsAttuning() => attuning;
    public bool IsChanneled() => channeled;
    public bool IsToggledSpell() => toggled;
    public bool CanCharge() => chargeable;
    public bool CanCast() => castReady;
    public bool IsChargeAvailable() => chargeAvailable;
    public bool HasEnoughResources() => resourcesCovered;
    public int GetCurrSpellCharges() => currentCharges;
    public int GetMaxSpellCharges() => maximumCharges;
    public BigDouble GetCooldownTimeRemaining() => cooldownRemaining;
    public FakeSpellCostList GetCost() => cost;
    public FakeSpellCostList GetDrainCost() => drainCost;
}

/// <summary>What a spell answers when asked its price, in the game's own cost-list shape.</summary>
internal sealed class FakeSpellCostList
{
    public List<FakeSpellCostEntry> costs = new();

    internal FakeSpellCostList With(Guid resource, double amount)
    {
        costs.Add(new FakeSpellCostEntry(resource, amount));
        return this;
    }
}

/// <summary>
/// One priced resource. The magnitude is the BigDouble field, not the serialized double beside it.
/// </summary>
internal struct FakeSpellCostEntry
{
    public FakeReferencedEntity resource;
    public BigDouble valueBig;

    internal FakeSpellCostEntry(Guid resourceId, double amount)
    {
        resource = new FakeReferencedEntity { Identity = resourceId };
        valueBig = new BigDouble(amount, 0);
    }
}

internal sealed class FakeSpellRecipe
{
    public static readonly List<FakeSpellRecipe> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool discovered;
    public int discRarityLevel;
    public BigDouble masteryExperience;
    public int masteryLevel;
    public bool readyToLevel;
    public bool hiddenDiscovery;
    public bool isRequiredDiscovery;
    public int penaltyUsageCost;
    public double castSpeed;
    public int baseCharges;
    public bool repeatInstantEffects;
    public FakeModifierRecord spellPowerMod = new(0d);
    public FakeModifierRecord spellCostMod = new(0d);
    public FakeModifierRecord spellCdSpeedMod = new(0d);
    public FakeModifierRecord spellDurationMod = new(0d);
    public FakeModifierRecord spellSpecialMod = new(0d);
    public FakeModifierRecord spellXpMod = new(0d);
    public bool hasAlertedThisMastery;

    public Guid GetGuid() => Identity;

    public bool IsReadyToLevelMastery() => readyToLevel;
}

internal sealed class FakeSpellType
{
    public static readonly List<FakeSpellType> All = new();

    public Guid Identity = Guid.NewGuid();
    public int typeLevel;
    public BigDouble typeXp;
    public double typeXpRequiredBase;
    public double augmentPowerMod;
    public bool hasNoLevels;
    public bool isElemental;
    public bool isLoadoutUnique;
    public bool hasNotTypeSignificance;
    public bool isVisible;
    public bool debugMode;
    public FakeModifierRecord typeXpMod = new(0d);
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord cooldownSpeed = new(0d);
    public FakeModifierRecord cooldownTime = new(0d);
    public FakeModifierRecord costMod = new(0d);
    public FakeModifierRecord drainCostMod = new(0d);
    public FakeModifierRecord durationMod = new(0d);
    public FakeModifierRecord elementalResonance = new(0d);
    public FakeModifierRecord augmentResonance = new(0d);
    public FakeModifierRecord maxStacksMod = new(0d);
    public FakeModifierRecord scalingMod = new(0d);
    public FakeModifierRecord usageCostReduction = new(0d);
    public FakeModifierRecord bonusCritRate = new(0d);
    public FakeModifierRecord critEffectMod = new(0d);
    public FakeModifierRecord critDurationMod = new(0d);
    public FakeModifierRecord bonusDoubleCastRate = new(0d);
    public FakeModifierRecord doubleCastEffectMod = new(0d);
    public FakeModifierRecord chargeTimeMod = new(0d);
    public FakeModifierRecord chargeEffectMod = new(0d);
    public FakeModifierRecord chargeSpecialMod = new(0d);
    public FakeModifierRecord bonusFlashRate = new(0d);
    public FakeModifierRecord flashEffectMod = new(0d);

    public Guid GetGuid() => Identity;
}

internal sealed class FakeEquipment
{
    public static readonly List<FakeEquipment> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool isCreated;
    public int discRarityLevel;
    public BigDouble masteryXp;
    public int masteryLevel;
    public bool isRequiredDiscovery;
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord baseLevel = new(0d);
    public FakeModifierRecord experienceRateMod = new(0d);
    public int equippedLevel;
    public int attuningLevel;
    public double attunementTimeLeft;
    public BigDouble baseXpRate;

    public Guid GetGuid() => Identity;
}

internal sealed class FakeEquipmentType
{
    public static readonly List<FakeEquipmentType> All = new();

    public Guid Identity = Guid.NewGuid();
    public int level;
    public int freeLevels;
    public int baseUsage;
    public FakeModifierRecord masteryLevel = new(0d);
    public FakeModifierRecord maxTypeSlots = new(0d);
    public FakeModifierRecord powerMod = new(0d);
    public FakeModifierRecord experienceRateMod = new(0d);

    public Guid GetGuid() => Identity;
}

internal sealed class FakeResourceType
{
    public static readonly List<FakeResourceType> All = new();

    public Guid Identity = Guid.NewGuid();
    public int level;
    public int freeLevels;
    public bool specialHidden;
    public bool ignoreAudit;
    public bool ignoreEffects;
    public bool auditHasMaxQuantity;
    public FakeModifierRecord rateMod = new(0d);
    public FakeModifierRecord maxQuantityMod = new(0d);
    public FakeModifierRecord maxQuantityRateMod = new(0d);
    public FakeModifierRecord qualityMod = new(0d);
    public FakeModifierRecord gainRateMod = new(0d);
    public FakeModifierRecord drainMod = new(0d);
    public FakeModifierRecord lossPercentMod = new(0d);
    public FakeModifierRecord restMod = new(0d);
    public FakeModifierRecord splashRate = new(0d);
    public FakeModifierRecord splashRateMaxPercent = new(0d);
    public FakeModifierRecord splashRateInterest = new(0d);
    public FakeModifierRecord splashRateMissing = new(0d);
    public FakeModifierRecord splashRateLifetime = new(0d);
    public FakeModifierRecord rawMaxQuantity = new(0d);

    public Guid GetGuid() => Identity;
    public FakeModifierRecord attributeCostMod = new(0d);
    public FakeModifierRecord reservationMod = new(0d);
    public FakeModifierRecord reverberateMod = new(0d);
    public FakeModifierRecord reverberateTimeMod = new(0d);
    public FakeModifierRecord replenishRatio = new(0d);
    public FakeModifierRecord replenishTimeMod = new(0d);
    public FakeModifierRecord decayRatio = new(0d);
    public FakeModifierRecord decayTimeMod = new(0d);
}

internal sealed class FakeCraftingRecipeType
{
    public static readonly List<FakeCraftingRecipeType> All = new();

    public Guid Identity = Guid.NewGuid();
    public int startingLevel;
    public int maxStartingLevel;
    public string craftVerb;
    public bool isLevelType;
    public bool initiated;
    public double magnitudeLoss;
    public double magnitudeTime;
    public FakeModifierRecord magnitudeIncrement = new(0d);
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord speed = new(0d);
    public FakeModifierRecord costMod = new(0d);
    public FakeModifierRecord costIncrementMod = new(0d);
    public FakeModifierRecord efficiencyMod = new(0d);
    public FakeModifierRecord autoPenaltyMod = new(0d);
    public FakeModifierRecord multiPenaltyMod = new(0d);

    public Guid GetGuid() => Identity;
}

/// <summary>
/// A resource. Shared rather than nested in one test class because the harvest element owns one, and
/// the collector reads that one with exactly the member list it reads every other resource with.
/// </summary>
internal sealed class FakeResource
{
    public static readonly List<FakeResource> All = new();

    public Guid Identity = Guid.NewGuid();
    public BigDouble Quantity;
    public BigDouble Rate;
    public bool Visible = true;
    public bool ThrowOnRate;
    public FakeModifierRecord maxQuantity = new(-1d);
    public BigDouble lifetimeQuantity;
    public BigDouble discoveryTime;
    public FakeModifierRecord quality = new(100d);
    public FakeModifierRecord gainRate = new(100d);
    public FakeModifierRecord drain = new(0d);
    public FakeModifierRecord reservationMod = new(100d);
    public FakeModifierRecord usage = new(0d);
    public FakeModifierRecord rate = new(0d);
    public FakeModifierRecord rateSplash = new(0d);
    public FakeModifierRecord rateMaxPercent = new(0d);
    public FakeModifierRecord rateInterestPercent = new(0d);
    public FakeModifierRecord rateMissingPercent = new(0d);
    public FakeModifierRecord rateLifetimePercent = new(0d);
    public FakeModifierRecord lossPercent = new(0d);
    public FakeModifierRecord displayRate = new(0d);
    public BigDouble calcRarityValue;
    public double baseLoss = 0.5d;
    public long appliedLevels;
    public FakeReferencedEntity? levelVariable;
    public double rarityValue;
    public double rarityValueEnd;
    public double restEngageTime;
    public bool pauseLossOnChange;
    public bool canOverflow;
    public bool noOverflowRubberBand;
    public bool bandwidthResource;
    public bool invertedResource;
    public bool excludeFromGlobals;
    public bool startVisible;
    public BigDouble appliedMaxQuantity;
    public int quantitySoftCapOrder;
    public int quantitySoftCapMagnitude;
    public double quantitySoftCapRatio;
    public bool debugResource;
    public double currentLossRate;
    public BigDouble lastReservation;
    public BigDouble debouncedReplenish;
    public BigDouble debouncedReverberate;
    public BigDouble debouncedDecay;
    public bool firstIncrement;
    public FakeModifierRecord maxQuantityRate = new(0d);
    public FakeModifierRecord maxQuantityFunctional = new(0d);
    public FakeModifierRecord restingRateMod = new(0d);
    public FakeModifierRecord attributeCostMod = new(100d);
    public FakeModifierRecord decayRatio = new(0d);
    public FakeModifierRecord decayTimeMod = new(0d);
    public FakeModifierRecord replenishRatio = new(0d);
    public FakeModifierRecord replenishTimeMod = new(0d);
    public FakeModifierRecord reverberateMod = new(0d);
    public FakeModifierRecord reverberateTimeMod = new(0d);
    public FakeModifierRecord rallyThreshold = new(0d);
    public FakeModifierRecord rallyMod = new(0d);
    public FakeModifierRecord usageDrainPenalty = new(0d);
    public bool inLossMode;
    public bool inRestMode;
    public bool inRallyMode;

    public Guid GetGuid() => Identity;

    public BigDouble GetQuantity() => Quantity;

    public BigDouble GetTrueRate() =>
        ThrowOnRate ? throw new InvalidOperationException("rate unavailable") : Rate;

    public bool IsVisible() => Visible;
}

internal sealed class FakeHarvestElement
{
    public static readonly List<FakeHarvestElement> All = new();

    public Guid Identity = Guid.NewGuid();
    public BigDouble masteryXp;
    public int masteryLevel;
    public double harvestTime;
    public double growthTime;
    public double rarityValue;
    public double initialMaxQuantity;
    public double requiredXpToLevel;
    public FakeModifierRecord instances = new(0d);
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord harvestSpeedMod = new(0d);
    public FakeModifierRecord drainCostMod = new(0d);
    public FakeModifierRecord autoGenerationMod = new(0d);
    public FakeModifierRecord experienceRateMod = new(0d);
    public FakeModifierRecord actionXpRate = new(0d);
    public FakeModifierRecord actionPower = new(0d);
    public FakeModifierRecord actionSpeed = new(0d);
    public FakeModifierRecord actionCostMod = new(0d);
    public BigDouble harvestRate;
    public BigDouble lastOutputRate;
    /// <summary>
    /// Private, created by the element rather than registered — the shape that keeps it out of the
    /// resource registry and makes reading it through its owner the only path.
    /// </summary>
    private FakeResource harvestResource = new();

    internal FakeResource Resource => harvestResource;


    public Guid GetGuid() => Identity;
}

internal sealed class FakeTimeRune
{
    public static readonly List<FakeTimeRune> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool discovered;
    public int level;
    public int discRarityLevel;
    public BigDouble masteryXp;
    public int masteryLevel;
    public bool isDiscoverRequired;
    public bool seen;
    public FakeModifierRecord freeUsages = new(0d);
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord powerScalingMod = new(0d);
    public FakeModifierRecord masteryXpMod = new(0d);

    public Guid GetGuid() => Identity;
}

internal sealed class FakeGlyph
{
    public static readonly List<FakeGlyph> All = new();

    public Guid Identity = Guid.NewGuid();
    public int level;
    public int freeLevels;
    public int discRarityLevel;
    public bool discovered;

    public Guid GetGuid() => Identity;
    public bool discoverable;
    public bool discoveryRequired;
    public bool augmentsSpells;
    public bool requiresDuration;
    public bool requiresToggleable;
    public int masteryReqCount;
    public FakeModifierRecord freeUsages = new(0d);
    public FakeModifierRecord freeLoadoutUsages = new(0d);
    public FakeModifierRecord maxUsages = new(0d);
}

internal sealed class FakeConsumable
{
    public static readonly List<FakeConsumable> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool visible;
    public bool randomized;
    public int quantity;
    public int queuedQuantity;
    public int gainedSince;
    public int maxCreatedLv;
    public BigDouble currentPrepTime;
    public BigDouble currentCooldown;
    public BigDouble currentCooldownTime;
    public ValueModifierRecord power = new(new BigDouble(0.0, 0));
    public ValueModifierRecord durationMod = new(new BigDouble(0.0, 0));
    public ValueModifierRecord special = new(new BigDouble(0.0, 0));
    public ValueModifierRecord prepSpeed = new(new BigDouble(0.0, 0));
    public ValueModifierRecord bonusLevels = new(new BigDouble(0.0, 0));
    public List<FakeConsumableType> consumableTypes = new();
    public List<FakeConsumableUsage> consumableUsages = new();
    public List<FakeConsumableCount> consumableCounts = new();
    public FakeConsumableCostList consumeCost = new();
    public FakeConsumableCostList usageCost = new();
    public int maximumCarryLoad = 100;

    public Guid GetGuid() => Identity;
    public int GetMaximumCarryLoad() => maximumCarryLoad;
    public double preparationTime;
    public bool canBeRandomized;
    public bool hasDuration;
    public double durationBase;
    public bool queueOnStart;
}

internal sealed class FakeConsumableType
{
    public Guid Identity = Guid.NewGuid();
    public Guid GetGuid() => Identity;
}

internal sealed class FakeConsumableCostList
{
    public List<FakeConsumableCost> costs = new();
}

internal sealed class FakeConsumableCost
{
    internal FakeConsumableCost(Guid resourceId, double amount)
    {
        resource = new FakeConsumableResource { Identity = resourceId };
        valueBig = new BigDouble(amount);
    }

    public FakeConsumableResource resource;
    public BigDouble valueBig;
}

internal sealed class FakeConsumableResource
{
    public Guid Identity = Guid.NewGuid();
    public Guid GetGuid() => Identity;
}

internal sealed class FakeConsumableUsage
{
    public Guid Identity = Guid.NewGuid();
    public bool en;
    public BigDouble dr;
    public BigDouble maxDr;
    public FakeConsumableScalingInfo baseSi = new();
    public Guid GetGuid() => Identity;
}

internal sealed class FakeConsumableScalingInfo
{
    public int Level = 1;
    public int GetLevelInt() => Level;
}

internal sealed class FakeConsumableCount
{
    public int Level = 1;
    public int Quantity;
    public int fr;
    public int GetLevel() => Level;
    public int GetQuantity() => Quantity;
}

internal sealed class FakeRitual
{
    public static readonly List<FakeRitual> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool discovered;
    public bool inBattle;
    public int reachedLevel;
    public int lastReachedLevel;
    public int selectedLevel;
    public int wavesCompleted;
    public int discRarityLevel;
    public int critLevel;
    public int echoLevel;
    public int chainLevel;

    /// <summary>Left null unless a test fills it, so the null-reads-as-zero path stays exercised.</summary>
    public List<object>? ritualInstances;

    public List<object> durationRewardBlocks = new();
    public BigDouble battleTotalWeight;
    public ValueModifierRecord power = new(new BigDouble(0.0, 0));
    public ValueModifierRecord speed = new(new BigDouble(0.0, 0));
    public ValueModifierRecord special = new(new BigDouble(0.0, 0));
    public ValueModifierRecord durationMod = new(new BigDouble(0.0, 0));
    public ValueModifierRecord echoRating = new(new BigDouble(0.0, 0));
    public ValueModifierRecord echoPower = new(new BigDouble(0.0, 0));
    public ValueModifierRecord critRating = new(new BigDouble(0.0, 0));
    public ValueModifierRecord critPower = new(new BigDouble(0.0, 0));
    public ValueModifierRecord critDurationMod = new(new BigDouble(0.0, 0));
    public ValueModifierRecord chainLengthBonus = new(new BigDouble(0.0, 0));
    public ValueModifierRecord chainPower = new(new BigDouble(0.0, 0));
    public ValueModifierRecord completionCostMod = new(new BigDouble(0.0, 0));
    public ValueModifierRecord completionRateMod = new(new BigDouble(0.0, 0));

    public Guid GetGuid() => Identity;
    public bool hideEndScreenResults;
    public bool isDiscoverRequired;
    public bool forceLevel;
    public int forceLevelValue;
    public int baseWaves;
    public int maxWaves;
    public double baseWeight;
    public int minimumEffectLevel;
}

internal sealed class FakeAchievement
{
    public static readonly List<FakeAchievement> All = new();

    public Guid Identity = Guid.NewGuid();
    public int level;
    public bool seen;
    public bool logProgress;
    public string steamApiName;
    public int maxLevels;
    public int achievementStrength;

    public Guid GetGuid() => Identity;
}

internal sealed class FakeAdvancement
{
    public static readonly List<FakeAdvancement> All = new();

    public Guid Identity = Guid.NewGuid();
    public BigDouble levels;
    public BigDouble xp;
    public bool isPersistent;
    public double baseRequiredXp;
    public FakeModifierRecord power = new(0d);

    public Guid GetGuid() => Identity;
}

internal sealed class FakeChallenge
{
    public static readonly List<FakeChallenge> All = new();

    public Guid Identity = Guid.NewGuid();
    public int level;
    public FakeState state;
    public bool hasBeenSeen;
    public bool rewardQueued;
    public int maxLevel;
    public int weight;
    public double difficulty;
    public double baseReward;

    public Guid GetGuid() => Identity;
}

internal sealed class FakeThoughtStream
{
    public static readonly List<FakeThoughtStream> All = new();

    public Guid Identity = Guid.NewGuid();
    public FakeState state;

    public Guid GetGuid() => Identity;
}

internal sealed class FakeTutorial
{
    public static readonly List<FakeTutorial> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool isCompleted;

    public Guid GetGuid() => Identity;
}

internal sealed class FakeView
{
    public static readonly List<FakeView> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool active;
    public bool alwaysActive;

    public Guid GetGuid() => Identity;
    public bool IsAvailable() => active || alwaysActive;
}

internal sealed class FakePlotNodeAction
{
    public static readonly List<FakePlotNodeAction> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool hasBeenUsed;
    public bool isGrowingAction;
    public bool showExitTooltip;
    public int elementCost;
    public bool useSizeModForCost;
    public bool useAnyStateForCost;
    public bool parallelAction;
    public double baseTime;
    public bool useSpaceUsageForTimeMult;
    public bool ignoreNodeYield;
    public bool useParentSize;
    public bool useParentQuality;
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord speed = new(0d);
    public FakeModifierRecord costMod = new(0d);
    public FakeModifierRecord growthSizeMod = new(0d);
    public FakeModifierRecord refundRating = new(0d);
    public List<FakePlotNode> sizeModNodes = new();
    public FakePrerequisites prerequisites = new();
    public FakeCostType elementCostType;
    public FakePlotPhase elementCostExitPhase;
    public FakeResourceCostList actionDrain = new();
    public List<object> actionEffects = new();
    public List<FakeEffectBlock> completeEffects = new();

    public Guid GetGuid() => Identity;
}

/// <summary>Positionally identical to the game's PlotNodeActionSO.CostType.</summary>
internal enum FakeCostType
{
    OnStart,
    OnExitPhase,
}

internal sealed class FakeResourceCostList
{
    public List<object> costs = new();
}

/// <summary>An authored effect block, read for its shape rather than its contents.</summary>
internal sealed class FakeEffectBlock
{
    public FakePrerequisites prerequisites = new();
    public List<object> effectMods = new();
    public List<object> effectScripts = new();
}

/// <summary>An effect modifier that scales what it applies by an authored weight.</summary>
internal sealed class FakeScalingWeightMod
{
    public FakeScalingWeightRef scalingWeightRef = new();
}

internal sealed class FakeScalingWeightRef
{
    public FakeScalingWeight? scalingWeight;
}

internal sealed class FakeScalingWeight
{
    public Guid Identity = Guid.NewGuid();

    public Guid GetGuid() => Identity;
}

/// <summary>An effect script that pays a treasure out of a pool.</summary>
internal sealed class FakeTreasureEffect
{
    public FakeTreasurePool? treasurePool;
    public string effectType = string.Empty;
    public double effectValue;
    public FakeFilterMod filterScaling = new();
}

internal sealed class FakeFilterMod
{
    public FakeFilterType listType;
    public List<object> listContents = new();
}

/// <summary>Positionally identical to the game's FilterEffectMod.FilterType.</summary>
internal enum FakeFilterType
{
    BlackList,
    WhiteList,
}

internal sealed class FakePrerequisites
{
    public bool available;
    public List<object> prerequisites = new();
}

internal sealed class FakePassiveAbility
{
    public static readonly List<FakePassiveAbility> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool muted;
    public bool touched;
    public bool hidden;
    public bool silent;
    public bool global;
    public bool startOnCooldown;
    public bool ignoreReactionCooldown;
    public double reactionTokenCost;
    public double maxTokens;
    public double minTokenForEffect;
    public bool tokenIndividuateDuration;
    public bool applyWhileRecharging;
    public bool expireAttachedStatusEffect;
    public FakeModifierRecord tokenRate = new(0d);

    public Guid GetGuid() => Identity;
}

internal sealed class FakeCharacter
{
    public static readonly List<FakeCharacter> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool discovered;
    public double numberSlain;

    public Guid GetGuid() => Identity;
    public bool floats;
}

internal sealed class FakeDiscoveryTree
{
    public static readonly List<FakeDiscoveryTree> All = new();

    public Guid Identity = Guid.NewGuid();
    public BigDouble actionTime;
    public int rerollsLeft;
    public bool usedRerollsLastDiscover;
    public FakeState actionMode;
    public FakeGuidContainer selectedChoiceId = new();
    public FakeReferencedEntity? overrideDiscoveryRerolls;
    public FakeReferencedEntity? overrideDiscoveryChoices;

    public Guid GetGuid() => Identity;
    public int additionalDiscoveryChoices;
    public int discoveryBonusLevelCost;
    public bool debugMode;
    public int totalDiscoveredCount;
    public int poolDiscoveredCount;
    public bool hasRequiredDiscovery;
    public bool hasRemainingDiscovery;
    public bool hasCompletedAllDiscoveries;
}

internal sealed class FakeRecipeBook
{
    public static readonly List<FakeRecipeBook> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool Available;

    public Guid GetGuid() => Identity;
    public bool IsAvailable() => Available;
}

internal sealed class FakePlotNode
{
    public static readonly List<FakePlotNode> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool visible;
    public BigDouble currentTime;
    public BigDouble nextErraticTime;
    public BigDouble sizeLevel;
    public BigDouble masteryXp;
    public int masteryLevel;

    public Guid GetGuid() => Identity;
    public bool noMastery;
    public bool noSizeDisplay;
    public bool useVisibilityPrereq;
    public bool hasErraticGrowth;
    public bool debugMode;
    public int erraticQuantity;
    public FakeModifierRecord actionQuantityUsageMain = new(0d);
    public FakeModifierRecord actionQuantityUsageAny = new(0d);
    public FakeModifierRecord actionXpRate = new(0d);
    public FakeModifierRecord yieldMod = new(0d);
    public FakeModifierRecord specialMod = new(0d);
    public FakeModifierRecord actionSpeed = new(0d);
    public FakeModifierRecord actionCostMod = new(0d);
    public FakeModifierRecord growingSpeed = new(0d);
    public FakeModifierRecord restingSpeed = new(0d);
    public FakeModifierRecord sizeMod = new(0d);
    public FakeModifierRecord qualityMod = new(0d);
    public FakeModifierRecord recoverySizeMod = new(0d);
    public FakeModifierRecord naturalGrowth = new(0d);
    public FakeModifierRecord naturalGrowthPower = new(0d);
    public int lastQuantity;

    public FakePlotNodeAction? autoAction;

    public List<FakePhaseInfo> phaseInfos = new() { new FakePhaseInfo { phase = FakePlotPhase.Idle } };
    public List<FakePhaseInstance> phaseInstances = new();

    public List<FakePlotNodeAction> availableActions = new();
    private List<FakePlotActionInstance> actionInstances = new();

    public List<FakePlotActionInstance> GetActionInstances() => actionInstances;

    /// <summary>Puts <paramref name="quantity"/> of the node into <paramref name="phase"/>.</summary>
    internal FakePlotNode With(FakePlotPhase phase, int quantity)
    {
        if (phaseInfos.TrueForAll(info => info.phase != phase))
            phaseInfos.Add(new FakePhaseInfo { phase = phase });
        phaseInstances.Add(new FakePhaseInstance { phase = phase, timers = new FakeTimerList { q = quantity } });
        return this;
    }

    internal FakePlotNode Offering(FakePlotNodeAction action)
    {
        availableActions.Add(action);
        return this;
    }

    /// <summary>
    /// Adds a running instance whose reference has already been resolved, which is the shape the game
    /// holds after anything has asked the reference for its guid.
    /// </summary>
    internal FakePlotNode Running(FakePlotNodeAction action, int quantity = 1, bool engaged = false)
    {
        actionInstances.Add(new FakePlotActionInstance
        {
            refObj = FakeIdObjectRef.Memoised(action.Identity),
            quantity = quantity,
            engaged = engaged,
        });
        return this;
    }

    /// <summary>
    /// Adds an instance whose reference names nothing that resolves, which is what a plot holding an
    /// action from a build that no longer ships it looks like.
    /// </summary>
    internal FakePlotNode RunningSomethingUnknown()
    {
        actionInstances.Add(new FakePlotActionInstance { refObj = FakeIdObjectRef.Unresolvable(), quantity = 1 });
        return this;
    }

    /// <summary>
    /// Adds a running instance whose reference is still only the serialized string, which is the
    /// shape it has straight off a save load.
    /// </summary>
    internal FakePlotNode RunningUnresolved(FakePlotNodeAction action)
    {
        actionInstances.Add(new FakePlotActionInstance
        {
            refObj = FakeIdObjectRef.Serialized(action.Identity),
        });
        return this;
    }
}

internal sealed class FakePlotActionInstance
{
    public FakeIdObjectRef refObj;
    public int quantity;
    public bool engaged;

    public bool IsEmpty() => quantity <= 0;

    public bool IsEngaged() => engaged;

    public int GetActualQuantity() => quantity;
}

/// <summary>
/// Shaped like the game's IdObjectRef: a serialized string, and a guid memoised from it the first
/// time anything asks. Nothing in this assembly reads <c>_guid</c> by name — the collector does, by
/// reflection, which is exactly what the two factories below are here to exercise.
/// </summary>
internal struct FakeIdObjectRef
{
    public string idStr;
#pragma warning disable CS0414
    private Guid _guid;
#pragma warning restore CS0414

    internal static FakeIdObjectRef Memoised(Guid identity) =>
        new() { idStr = identity.ToString(), _guid = identity };

    internal static FakeIdObjectRef Serialized(Guid identity) =>
        new() { idStr = identity.ToString(), _guid = Guid.Empty };

    internal static FakeIdObjectRef Unresolvable() =>
        new() { idStr = string.Empty, _guid = Guid.Empty };
}

/// <summary>Positionally identical to the game's PlotNodeSO.PlotNodePhases.</summary>
internal enum FakePlotPhase
{
    Idle,
    Growing,
    Resting,
}

internal sealed class FakePhaseInfo
{
    public FakePlotPhase phase;
    public double phaseTime;
    public FakeTimerType processType;
    public FakePlotPhase exitPhase;
}

/// <summary>Positionally identical to the game's TimerList.TimerType.</summary>
internal enum FakeTimerType
{
    Single,
    Parallel,
}

internal sealed class FakePhaseInstance
{
    public FakePlotPhase phase;
    public FakeTimerList timers = new();
}

internal sealed class FakeTimerList
{
    public int q;
    public List<BigDouble> ds = new();
}

internal sealed class FakeTreasurePool
{
    public static readonly List<FakeTreasurePool> All = new();

    public Guid Identity = Guid.NewGuid();
    public int treasuresFound;
    public BigDouble partialTreasureReward;

    public Guid GetGuid() => Identity;
    public bool forceLevel;
    public int treasureLevel;
    public bool calculatedTreasureLevel;
}
