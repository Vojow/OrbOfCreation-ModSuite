using System;
using System.Collections.Generic;

public sealed class StructureTypeSO : UpgradeableObject
{
    private List<StructureSO> structures = StructureSO.All;

    public void RegisterStructure(StructureSO structure) => structures.Add(structure);
    public List<StructureSO> GetAllStructures() => structures;

    // Purpose-built malformed fixtures can replace the authored membership without reflecting the
    // private game field themselves.
    public void SetStructuresForTests(List<StructureSO> value) => structures = value;
}

public sealed class SpellTypeSO : IdScriptableObject
{
    public static List<SpellTypeSO> All = new List<SpellTypeSO>();
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
    public ValueModifierRecord typeXpMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord cooldownSpeed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord cooldownTime = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord costMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord drainCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord durationMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord elementalResonance = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord augmentResonance = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord maxStacksMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord scalingMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord usageCostReduction = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord bonusCritRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord critEffectMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord critDurationMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord bonusDoubleCastRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord doubleCastEffectMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord chargeTimeMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord chargeEffectMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord chargeSpecialMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord bonusFlashRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord flashEffectMod = new ValueModifierRecord(new BigDouble(0.0, 0));
}


public sealed class EquipmentTypeSO : IdScriptableObject
{
    public static List<EquipmentTypeSO> All = new List<EquipmentTypeSO>();
    public int level;
    public int freeLevels;
    public int baseUsage;
    public ValueModifierRecord masteryLevel = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord maxTypeSlots = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ModifierRecord powerMod = new ModifierRecord();
    public ModifierRecord experienceRateMod = new ModifierRecord();
}


public sealed class ResourceTypeSO : IdScriptableObject
{
    public static List<ResourceTypeSO> All = new List<ResourceTypeSO>();
    public int level;
    public int freeLevels;
    public bool specialHidden;
    public bool ignoreAudit;
    public bool ignoreEffects;
    public bool auditHasMaxQuantity;
    public ModifierRecord rateMod = new ModifierRecord();
    public ModifierRecord maxQuantityMod = new ModifierRecord();
    public ModifierRecord maxQuantityRateMod = new ModifierRecord();
    public ModifierRecord qualityMod = new ModifierRecord();
    public ModifierRecord gainRateMod = new ModifierRecord();
    public ModifierRecord drainMod = new ModifierRecord();
    public ModifierRecord lossPercentMod = new ModifierRecord();
    public ModifierRecord restMod = new ModifierRecord();
    public ModifierRecord splashRate = new ModifierRecord();
    public ModifierRecord splashRateMaxPercent = new ModifierRecord();
    public ModifierRecord splashRateInterest = new ModifierRecord();
    public ModifierRecord splashRateMissing = new ModifierRecord();
    public ModifierRecord splashRateLifetime = new ModifierRecord();
    public ModifierRecord rawMaxQuantity = new ModifierRecord();
    public ModifierRecord attributeCostMod = new ModifierRecord();
    public ModifierRecord reservationMod = new ModifierRecord();
    public ModifierRecord reverberateMod = new ModifierRecord();
    public ModifierRecord reverberateTimeMod = new ModifierRecord();
    public ModifierRecord replenishRatio = new ModifierRecord();
    public ModifierRecord replenishTimeMod = new ModifierRecord();
    public ModifierRecord decayRatio = new ModifierRecord();
    public ModifierRecord decayTimeMod = new ModifierRecord();
}


/// <summary>Its two levels are the same numbers as the others, under the game's own names.</summary>
public sealed class CraftingRecipeTypeSO : IdScriptableObject
{
    public static List<CraftingRecipeTypeSO> All = new List<CraftingRecipeTypeSO>();
    public int startingLevel;
    public int maxStartingLevel;
    public string craftVerb;
    public bool isLevelType;
    public bool initiated;
    public double magnitudeLoss;
    public double magnitudeTime;
    public ValueModifierRecord magnitudeIncrement = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ModifierRecord power = new ModifierRecord();
    public ModifierRecord speed = new ModifierRecord();
    public ModifierRecord costMod = new ModifierRecord();
    public ModifierRecord costIncrementMod = new ModifierRecord();
    public ModifierRecord efficiencyMod = new ModifierRecord();
    public ModifierRecord autoPenaltyMod = new ModifierRecord();
    public ModifierRecord multiPenaltyMod = new ModifierRecord();
}

public sealed class CraftingRecipeListVariable : GenericListVariable<CraftingRecipeSO>
{
}

public class AbstractRefInstance<T> where T : IdScriptableObject
{
    public T reference = null!;
    public T get_reference() => reference;
    public bool IsEmpty() => reference is null;
    public Guid GetGuidReference() => reference.GetGuid();
}

public sealed class CraftingRecipeSO : IdScriptableObject
{
    public static List<CraftingRecipeSO> All = new List<CraftingRecipeSO>();
    public List<CraftingRecipeTypeSO> craftingTypes = new List<CraftingRecipeTypeSO>();
    public ResourceCostList recipeCost = new ResourceCostList();
    public ResourceCostList generatedResources = new ResourceCostList();
    public List<PersistentEffectBlock> engagementEffects = new List<PersistentEffectBlock>();
    public List<InstantEffectBlock> completeEffects = new List<InstantEffectBlock>();
    public bool useQuantityAsLevel;
    public double timeToComplete;
    public bool visible;
    public bool BuyAllowed = true;
    public int MaximumAffordableLevel = int.MaxValue;
    public BigDouble StartingQuantity = BigDouble.One;
    public ResourceCostList TotalCost = new ResourceCostList();
    public CraftingRecipeTypeSO MainType = new CraftingRecipeTypeSO
    {
        maxStartingLevel = 1,
        isLevelType = true,
    };
    public bool InstantCraftEnabled;
    public ConsumableSO? InstantOutput;
    public bool ThrowAfterPurchase;
    public bool ThrowDuringConstruction;
    public bool ThrowAfterInitiation;
    public bool ThrowAfterInstantAdmission;
    public int PurchaseCalls;
    public int VisibilityCalls;
    public int StartingQuantityCalls;
    public int CanBuyCalls;

    public bool IsVisible()
    {
        VisibilityCalls++;
        return visible;
    }
    public bool CanBuyAt(BigDouble quantity)
    {
        CanBuyCalls++;
        return BuyAllowed &&
            quantity > BigDouble.Zero &&
            quantity.ToDouble() <= MaximumAffordableLevel &&
            TotalCost.HasEnough();
    }
    public BigDouble GetStartingQuantity()
    {
        StartingQuantityCalls++;
        return StartingQuantity;
    }
    public ResourceCostList GetTotalCost(BigDouble previousQuantity, BigDouble purchasedQuantity) =>
        TotalCost;
    public CraftingRecipeTypeSO GetMainType() => MainType;

    public void PurchaseQuantity(BigDouble purchasedQuantity, BigDouble previousQuantity)
    {
        PurchaseCalls++;
        TotalCost.PerformCost();
        if (MainType.isLevelType)
            MainType.maxStartingLevel = Math.Max(
                MainType.maxStartingLevel,
                purchasedQuantity.ToInt() + previousQuantity.ToInt());
        if (ThrowAfterPurchase)
            throw new InvalidOperationException("injected failure after purchase");
    }
}

public sealed class CraftingInstanceListVariable : GenericListVariable<CraftingInstance>
{
    public bool isAutoList;
}

public sealed class CraftingInstance : AbstractRefInstance<CraftingRecipeSO>
{
    public BigDouble Quantity = BigDouble.One;
    public bool Automatic;
    public bool Expired;
    public bool Initiated;

    public CraftingInstance()
    {
    }

    public CraftingInstance(CraftingRecipeSO recipe, BigDouble quantity)
    {
        reference = recipe;
        Quantity = quantity;
        if (recipe.ThrowDuringConstruction)
            throw new InvalidOperationException("injected failure during construction");
    }

    public void Initiate()
    {
        Initiated = true;
        if (reference.ThrowAfterInitiation)
            throw new InvalidOperationException("injected failure after initiation");
    }

    public bool CheckInstantCraft() => reference.InstantCraftEnabled;

    public void InstantCraft()
    {
        if (reference.InstantOutput is not null)
        {
            var level = Quantity.ToInt();
            var count = reference.InstantOutput.consumableCounts.Find(row => row.Level == level);
            if (count is null)
            {
                count = new ConsumableCount { Level = level };
                reference.InstantOutput.consumableCounts.Add(count);
            }
            count.Quantity++;
        }
        if (reference.ThrowAfterInstantAdmission)
            throw new InvalidOperationException("injected failure after instant admission");
    }

    public BigDouble GetQuantity() => Quantity;
    public bool IsAuto() => Automatic;
    public bool IsExpired() => Expired;
}

public sealed class EnchantmentSO : IdScriptableObject
{
    public sealed class EnchantTable
    {
        public List<EnchantmentInstance> enchantments = new List<EnchantmentInstance>();
    }

    public sealed class EnchantItemScript : IInstantEffectScript
    {
        public EnchantmentSO enchantment = new EnchantmentSO();
        public Targeting.TargetReference targetReference = new Targeting.TargetReference();
    }
}

public sealed class EnchantmentInstance : AbstractRefInstance<EnchantmentSO>
{
    public int Level = 1;
    public int GetLevel() => Level;
}


public sealed class HarvestElementSO : IdScriptableObject
{
    public static List<HarvestElementSO> All = new List<HarvestElementSO>();
    public BigDouble masteryXp;
    public int masteryLevel;
    public double harvestTime;
    public double growthTime;
    public double rarityValue;
    public double initialMaxQuantity;
    public double requiredXpToLevel;
    public ValueModifierRecord instances = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord harvestSpeedMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord drainCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord autoGenerationMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord experienceRateMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionXpRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionPower = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionSpeed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    private BigDouble harvestRate;
    private BigDouble lastOutputRate;

    /// <summary>
    /// Private in the game, created by the element rather than registered, which is why the resource
    /// registry cannot reach it and it is read through its owner.
    /// </summary>
    private ResourceSO harvestResource = new ResourceSO();
}


public sealed class TimeRuneSO : IdScriptableObject
{
    public static List<TimeRuneSO> All = new List<TimeRuneSO>();
    public bool discovered;
    public int level;
    public int discRarityLevel;
    public BigDouble masteryXp;
    public int masteryLevel;
    public bool isDiscoverRequired;
    public bool seen;
    public ValueModifierRecord freeUsages = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord powerScalingMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord masteryXpMod = new ValueModifierRecord(new BigDouble(0.0, 0));
}


public sealed class GlyphSO : IdScriptableObject, ITooltipable
{
    public static List<GlyphSO> All = new List<GlyphSO>();
    public string DisplayName = string.Empty;
    public string Description = string.Empty;
    public int level;
    public int freeLevels;
    public int discRarityLevel;
    public bool discovered;
    public bool discoverable;
    public bool discoveryRequired;
    public bool augmentsSpells;
    public bool requiresDuration;
    public bool requiresToggleable;
    public int masteryReqCount;
    public ValueModifierRecord freeUsages = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord freeLoadoutUsages = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord maxUsages = new ValueModifierRecord(new BigDouble(0.0, 0));

    public string GetName() => DisplayName;
    public string GetDisplayType() => "Glyph";
    public UnityEngine.Sprite GetIcon() => new UnityEngine.Sprite();
    public UnityEngine.Color GetColor() => default;
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => Description;
    public List<TooltipNode> GetTooltipNodes() => new List<TooltipNode>();
    public List<TooltipNode> GetAltTooltipNodes() => new List<TooltipNode>();
}

public sealed class ConsumableTypeSO : IdScriptableObject
{
    public string DisplayName = string.Empty;

    public string GetName() => DisplayName;
}

public sealed partial class ConsumableSO : IdScriptableObject
{
    public static List<ConsumableSO> All = new List<ConsumableSO>();
    public string DisplayName = string.Empty;
    public UnityEngine.Sprite Icon = new UnityEngine.Sprite();
    public bool visible;
    public bool randomized;
    public int maxCreatedLv;
    public BigDouble currentPrepTime;
    public BigDouble currentCooldown;
    public BigDouble currentCooldownTime;
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord durationMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord special = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord prepSpeed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord bonusLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public List<ConsumableTypeSO> consumableTypes = new List<ConsumableTypeSO>();
    public List<InstantEffectBlock> onUseEffects = new List<InstantEffectBlock>();
    public ResourceCostList consumeCost = new ResourceCostList();
    public ResourceCostList usageCost = new ResourceCostList();
    public List<ConsumableUsage> consumableUsages = new List<ConsumableUsage>();
    public List<ConsumableCount> consumableCounts = new List<ConsumableCount>();

    /// <summary>Private in the game too, which is exactly why the save record hid the stock count.</summary>
    private int quantity;
    private int queuedQuantity;
    private int gainedSince;
    public bool FireAllowed = true;
    public bool SelectionNoOp;
    public int MaximumCarryLoad;

    public void SetStock(int quantityN, int queuedN, int gainedSinceN)
    {
        quantity = quantityN;
        queuedQuantity = queuedN;
        gainedSince = gainedSinceN;
    }

    public bool CanFire() =>
        FireAllowed &&
        quantity > 0 &&
        currentCooldown <= BigDouble.Zero &&
        consumeCost.HasEnough();

    public bool IsVisible() => visible;

    public string GetName() => DisplayName;

    public UnityEngine.Sprite GetIcon() => Icon;

    public void SelectAndFire()
    {
        if (!CanFire() || !Inventory.CanUseConsumable()) return;
        if (SelectionNoOp) return;

        var accepted = Math.Min(GlobalVariables.GetMultiBuy().AsInt(), quantity);
        queuedQuantity += accepted;
        if (accepted > 0)
        {
            quantity--;
            consumeCost.PerformCost();
            if (hasDuration)
            {
                consumableUsages.Add(new ConsumableUsage
                {
                    en = false,
                    dr = new BigDouble(durationBase),
                    maxDr = new BigDouble(durationBase),
                });
            }
        }
        Inventory.BeginPreparing();
    }

    public void SetRandomization(bool randomizationN) => randomized = randomizationN;

    public bool IsRandomized() => canBeRandomized && randomized;

    public int GetQuantity() => quantity;

    public int GetQueued() => queuedQuantity;

    public int GetMaximumCarryLoad() => MaximumCarryLoad;

    public ConsumableCount GetStrongest()
    {
        ConsumableCount? strongest = null;
        foreach (var count in consumableCounts)
            if (count.Quantity > 0 && (strongest is null || count.Level > strongest.Level))
                strongest = count;
        return strongest ?? new ConsumableCount();
    }

    public int GetStrongestLevel() => GetStrongest().GetLevel();

    public ScalingInfo GetCountScalingInfo(ConsumableCount count) =>
        new ScalingInfo { Level = count.GetLevel() };

    public double preparationTime;
    public bool canBeRandomized;
    public bool hasDuration;
    public double durationBase;
    private bool queueOnStart;
}

public sealed class ConsumableCount
{
    public int Level = 1;
    public int Quantity;
    public int fr;

    public int GetLevel() => Level;
    public int GetQuantity() => Quantity;
}

public sealed class ConsumableUsage
{
    public Guid Identity = Guid.NewGuid();
    public ScalingInfo baseSi = new ScalingInfo();
    public bool en;
    public BigDouble dr;
    public BigDouble maxDr;

    public Guid GetGuid() => Identity;
}

public sealed class ScalingInfo
{
    public int Level = 1;

    public int GetLevelInt() => Level;
    public static ScalingInfo Basic(BigDouble level) =>
        new ScalingInfo { Level = level.ToInt() };
}

public interface IInstantEffectScript
{
}

public sealed class UnexpectedInstantEffectScript : IInstantEffectScript
{
}

public sealed class RequestTargetEffectScript : IInstantEffectScript
{
    public Targeting.TargetSelectOptions targetOptions = new Targeting.TargetSelectOptions();
}

namespace Targeting
{
    public enum TargetReferenceTypes
    {
        Target = 0,
    }

    public sealed class TargetReference
    {
        public TargetReferenceTypes refType;
    }

    public interface ITargetable
    {
    }

    public class BaseTargetSelection
    {
    }

    public sealed class TargetStructure : BaseTargetSelection
    {
        public List<ITargetable> Candidates = new List<ITargetable>();

        public List<ITargetable> GetRandomList(ScalingInfo scaling) =>
            new List<ITargetable>(Candidates);
    }

    public sealed class TargetSelectOptions
    {
        public BaseTargetSelection Targeting = new TargetStructure();

        public BaseTargetSelection GetTargeting() => Targeting;
        public TargetReferenceTypes GetTargetRefType() => TargetReferenceTypes.Target;
    }
}

public sealed class Inventory
{
    public static bool Preparing { get; set; }

    public static bool CanUseConsumable() => !Preparing;

    public static void BeginPreparing() => Preparing = true;
}

public sealed class RitualSO : IdScriptableObject
{
    public static List<RitualSO> All = new List<RitualSO>();
    public bool discovered;
    public bool inBattle;
    public int lastReachedLevel;
    public int reachedLevel;
    public int wavesCompleted;
    public int selectedLevel;
    public int discRarityLevel;

    /// <summary>
    /// Left null by default on purpose: the game leaves these null before first use, and the count
    /// accessor has to read that as zero rather than throw.
    /// </summary>
    public List<object>? ritualInstances;

    public List<object> durationRewardBlocks = new List<object>();
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord speed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord special = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord durationMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord echoRating = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord echoPower = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord critRating = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord critPower = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord critDurationMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord chainLengthBonus = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord chainPower = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord completionCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord completionRateMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    /// <summary>Private in the game, as the run tiers are.</summary>
    private BigDouble battleTotalWeight;

    private int critLevel;
    private int echoLevel;
    private int chainLevel;

    public void SetRunState(int critLevelN, int echoLevelN, int chainLevelN, BigDouble totalWeight)
    {
        critLevel = critLevelN;
        echoLevel = echoLevelN;
        chainLevel = chainLevelN;
        battleTotalWeight = totalWeight;
    }
    public bool hideEndScreenResults;
    public bool isDiscoverRequired;
    public bool forceLevel;
    public int forceLevelValue;
    public int baseWaves;
    public int maxWaves;
    public double baseWeight;
    public int minimumEffectLevel;
}

public sealed class AchievementSO : IdScriptableObject
{
    public static List<AchievementSO> All = new List<AchievementSO>();
    public int level;
    public bool seen;
    public bool logProgress;
    public string steamApiName;
    public int maxLevels;
    public int achievementStrength;
}


public sealed class AdvancementSO : IdScriptableObject
{
    public static List<AdvancementSO> All = new List<AdvancementSO>();
    public BigDouble levels;
    public BigDouble xp;
    public bool isPersistent;
    public double baseRequiredXp;
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
}


public sealed class ChallengeSO : IdScriptableObject
{
    public enum ChallengeState
    {
        None,
        Active,
        Completed,
    }

    public static List<ChallengeSO> All = new List<ChallengeSO>();
    public int level;
    public ChallengeState state;
    public bool hasBeenSeen;
    public bool rewardQueued;
    public int maxLevel;
    public int weight;
    public double difficulty;
    public double baseReward;
}


public sealed class ThoughtStreamSO : IdScriptableObject
{
    public enum StreamState
    {
        Idle,
        Running,
    }

    public static List<ThoughtStreamSO> All = new List<ThoughtStreamSO>();
    public StreamState state;
}


public sealed class TutorialSO : IdScriptableObject
{
    public static List<TutorialSO> All = new List<TutorialSO>();
    public bool isCompleted;
}


public sealed class PlotNodeActionSO : IdScriptableObject
{
    /// <summary>How an action charges its element cost. The game nests this here.</summary>
    public enum CostType
    {
        OnStart,
        OnExitPhase,
    }

    public static List<PlotNodeActionSO> All = new List<PlotNodeActionSO>();
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
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord speed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord costMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord growthSizeMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord refundRating = new ValueModifierRecord(new BigDouble(0.0, 0));

    // What the action's author decided it costs: on what, on leaving which phase, and out of which
    // resources. The suite reads the two enums and the length of the drain, never the drain itself.
    public CostType elementCostType;
    public PlotNodePhases elementCostExitPhase;
    public ResourceCostList actionDrain = new ResourceCostList();

    // Which other nodes the action takes its size multiplier from, and the prerequisites that gate
    // it. Collection reads the list's length and latch; the Auto Harvest action boundary calls the
    // exact container Check() after current UUID/type/lifecycle resolution; the native action-row
    // visibility method reaches that same Check() again.
    public List<PlotNodeSO> sizeModNodes = new List<PlotNodeSO>();
    public Prerequisites.Container prerequisites = new Prerequisites.Container();

    /// <summary>What the action applies for as long as it runs, as against on completing.</summary>
    public List<PersistentEffectBlock> actionEffects = new List<PersistentEffectBlock>();

    /// <summary>What completing one run of the action applies.</summary>
    public List<InstantEffectBlock> completeEffects = new List<InstantEffectBlock>();
}

/// <summary>
/// An authored effect block. The suite reads its shape — which class, how many modifiers, how many
/// scripts, and what the one modifier and the one script name — and never applies any of it.
/// </summary>
public class EffectBlock
{
    public Prerequisites.Container prerequisites = new Prerequisites.Container();
    public List<object> effectMods = new List<object>();
    public BigDouble NecessaryDrainRatio = BigDouble.One;

    private BigDouble GetEffectNecessaryDrainRatio() => NecessaryDrainRatio;
}

public class InstantEffectBlock : EffectBlock
{
    public List<IInstantEffectScript> effectScripts = new List<IInstantEffectScript>();
}

public partial class ConsumableSO
{
    public sealed class ConsumableGainEffect : IInstantEffectScript
    {
        public ConsumableSO consumable = new ConsumableSO();
    }
}

public class PersistentEffectBlock : EffectBlock
{
}

/// <summary>The weight a completion effect scales its payout by.</summary>
public sealed class ScalingWeightSO : IdScriptableObject
{
    public static List<ScalingWeightSO> All = new List<ScalingWeightSO>();
}

/// <summary>The game's serialized edge to a scaling weight.</summary>
public sealed class ScalingWeightRef
{
    public ScalingWeightSO? scalingWeight;
}

/// <summary>An effect modifier that scales what it modifies by an authored weight.</summary>
public sealed class ScalingWeightEffectMod
{
    public ScalingWeightRef scalingWeightRef = new ScalingWeightRef();
}

/// <summary>How an effect filters the scalings it applies to.</summary>
public sealed class FilterEffectMod
{
    public enum FilterType
    {
        BlackList,
        WhiteList,
    }

    public FilterType listType;
    public List<object> listContents = new List<object>();
}


public sealed class PassiveAbilitySO : IdScriptableObject
{
    public static List<PassiveAbilitySO> All = new List<PassiveAbilitySO>();
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
    public ValueModifierRecord tokenRate = new ValueModifierRecord(new BigDouble(0.0, 0));
}


public sealed class CharacterSO : IdScriptableObject
{
    public static List<CharacterSO> All = new List<CharacterSO>();
    public bool discovered;

    /// <summary>A double in the game too: the count runs past what an int holds.</summary>
    public double numberSlain;
    public bool floats;
}

public interface IHasGuid
{
    Guid GetGuid();
}

public interface IDiscoverable : IHasGuid
{
    ResourceCostList GetDiscoverCost();
    bool IsDiscoverVisible();
    bool CanDiscover();
    bool IsDiscovered();
    bool IsDiscoverRequired();
    void Discover();
}

public sealed class DiscoveryTestItemSO : IdScriptableObject, IDiscoverable
{
    public ResourceCostList discoverCost = new ResourceCostList();
    public bool discoverVisible = true;
    public bool canDiscover = true;
    public bool discovered;
    public bool required;
    public bool suppressDiscover;
    public bool throwBeforeDiscover;
    public bool throwAfterDiscover;
    public int discoverCalls;

    ResourceCostList IDiscoverable.GetDiscoverCost() => discoverCost;
    bool IDiscoverable.IsDiscoverVisible() => discoverVisible;
    bool IDiscoverable.CanDiscover() => canDiscover;
    bool IDiscoverable.IsDiscovered() => discovered;
    bool IDiscoverable.IsDiscoverRequired() => required;
    Guid IHasGuid.GetGuid() => GetGuid();

    void IDiscoverable.Discover()
    {
        discoverCalls++;
        if (throwBeforeDiscover)
            throw new InvalidOperationException("injected failure before discovery");
        if (!suppressDiscover) discovered = true;
        if (throwAfterDiscover)
            throw new InvalidOperationException("injected failure after discovery");
    }
}

public sealed class DiscoveryTreeSO : IdScriptableObject
{
    public enum DiscoveryTreeModes
    {
        Idle,
        Crafting,
        Choice,
    }

    public static List<DiscoveryTreeSO> All = new List<DiscoveryTreeSO>();
    public DiscoveryTreeModes actionMode;
    public BigDouble actionTime;
    public int rerollsLeft;
    public bool usedRerollsLastDiscover;
    public List<GuidContainer> currentChoiceIds = new List<GuidContainer>();
    public List<GuidContainer> nextExcludedIds = new List<GuidContainer>();

    /// <summary>An identity the game already holds as one, rather than a live reference.</summary>
    public GuidContainer selectedChoiceId = new GuidContainer();

    public IntVariable overrideDiscoveryRerolls;
    public IntVariable overrideDiscoveryChoices;
    public int additionalDiscoveryChoices;
    public int discoveryBonusLevelCost;
    public bool debugMode;
    private int totalDiscoveredCount;
    private int poolDiscoveredCount;
    private bool hasRequiredDiscovery;
    private bool hasRemainingDiscovery = true;
    private bool hasCompletedAllDiscoveries;

    public ResourceCostList nextItemCost = new ResourceCostList();
    public List<IDiscoverable> allDiscoverableItems = new List<IDiscoverable>();
    public int maximumRerolls = 1;
    public bool visible = true;
    public bool immediateRequired;
    public bool suppressInitiate;
    public bool suppressSelect;
    public bool suppressConfirm;
    public bool suppressReroll;
    public bool throwAfterInitiate;
    public bool throwAfterSelect;
    public bool throwAfterConfirmReset;
    public bool throwAfterReroll;
    public bool throwAfterSelectionClear;
    public bool driftInitiateEvidence;
    public bool driftSelectEvidence;
    public bool driftConfirmEvidence;
    public bool driftRerollEvidence;
    public int initiateCalls;
    public int selectCalls;
    public int confirmCalls;
    public int rerollCalls;

    public bool IsVisible() => visible;
    public bool IsInIdleMode() => actionMode == DiscoveryTreeModes.Idle;
    public bool IsInCraftingMode() => actionMode == DiscoveryTreeModes.Crafting;
    public bool IsInChoiceMode() => actionMode == DiscoveryTreeModes.Choice;
    public bool HasCurrentlyRemMainPoolDiscoveries() => hasRemainingDiscovery;
    public bool HasImmediateRequiredDiscover() => immediateRequired;
    public int GetCurrentRerolls() => rerollsLeft;
    public int GetMaxRerolls() => maximumRerolls;
    public ResourceCostList GetNextItemCost() => nextItemCost;

    public IDiscoverable? GetItemFromGuid(Guid guid)
    {
        for (var index = 0; index < allDiscoverableItems.Count; index++)
            if (allDiscoverableItems[index].GetGuid() == guid) return allDiscoverableItems[index];
        return null;
    }

    public void InitiateCraftingMode()
    {
        initiateCalls++;
        if (!suppressInitiate)
        {
            if (!usedRerollsLastDiscover)
                rerollsLeft = Math.Min(rerollsLeft + 1, maximumRerolls);
            usedRerollsLastDiscover = false;
            actionMode = DiscoveryTreeModes.Crafting;
            actionTime = BigDouble.Zero;
            if (driftInitiateEvidence)
            {
                actionTime = new BigDouble(7, 0);
                rerollsLeft = 99;
                usedRerollsLastDiscover = true;
                currentChoiceIds.Add(new GuidContainer(Guid.NewGuid()));
                totalDiscoveredCount += 3;
                poolDiscoveredCount += 2;
            }
        }
        if (throwAfterInitiate)
            throw new InvalidOperationException("injected failure after initiate");
    }

    public void SelectItemId(Guid guid)
    {
        selectCalls++;
        if (!suppressSelect)
        {
            selectedChoiceId = new GuidContainer(guid);
            if (driftSelectEvidence)
            {
                actionMode = DiscoveryTreeModes.Idle;
                actionTime = new BigDouble(8, 0);
                currentChoiceIds.Clear();
                nextExcludedIds.Add(new GuidContainer(Guid.NewGuid()));
                rerollsLeft += 4;
                usedRerollsLastDiscover = !usedRerollsLastDiscover;
                totalDiscoveredCount += 2;
                poolDiscoveredCount += 1;
            }
        }
        if (throwAfterSelect || (guid == Guid.Empty && throwAfterSelectionClear))
            throw new InvalidOperationException("injected failure after selection");
    }

    public void DiscoverSelectedItem()
    {
        confirmCalls++;
        var selected = selectedChoiceId.guid;
        if (selected == Guid.Empty) return;
        var item = GetItemFromGuid(selected);
        if (item is null) return;
        if (!suppressConfirm)
        {
            totalDiscoveredCount++;
            if (!item.IsDiscoverRequired()) poolDiscoveredCount++;
            actionMode = DiscoveryTreeModes.Idle;
            actionTime = BigDouble.Zero;
            currentChoiceIds.Clear();
            selectedChoiceId = new GuidContainer();
        }
        if (throwAfterConfirmReset)
            throw new InvalidOperationException("injected failure after confirm reset");
        item.Discover();
        if (driftConfirmEvidence)
        {
            actionMode = DiscoveryTreeModes.Choice;
            actionTime = new BigDouble(9, 0);
            currentChoiceIds.Add(new GuidContainer(selected));
            selectedChoiceId = new GuidContainer(selected);
            totalDiscoveredCount += 4;
            poolDiscoveredCount += 3;
            rerollsLeft += 2;
            usedRerollsLastDiscover = !usedRerollsLastDiscover;
        }
    }

    public void RerollChoices()
    {
        rerollCalls++;
        if (actionMode != DiscoveryTreeModes.Choice || rerollsLeft <= 0) return;
        if (!suppressReroll)
        {
            nextExcludedIds = new List<GuidContainer>(currentChoiceIds);
            currentChoiceIds = new List<GuidContainer>();
            rerollsLeft--;
            usedRerollsLastDiscover = true;
            actionMode = DiscoveryTreeModes.Crafting;
            actionTime = BigDouble.Zero;
            if (driftRerollEvidence)
            {
                actionTime = new BigDouble(10, 0);
                rerollsLeft += 5;
                usedRerollsLastDiscover = false;
                currentChoiceIds.Add(new GuidContainer(Guid.NewGuid()));
                nextExcludedIds.Clear();
                totalDiscoveredCount += 2;
                poolDiscoveredCount += 1;
            }
        }
        if (throwAfterReroll)
            throw new InvalidOperationException("injected failure after reroll");
    }
}

public sealed class RecipeBookSO : IdScriptableObject
{
    public static List<RecipeBookSO> All = new List<RecipeBookSO>();
    public bool available;

    public bool IsAvailable() => available;
}

public sealed class PlotNodeSO : IdScriptableObject
{
    public static List<PlotNodeSO> All = new List<PlotNodeSO>();
    public bool visible;
    public BigDouble currentTime;
    public BigDouble nextErraticTime;
    public BigDouble sizeLevel;
    public BigDouble masteryXp;
    public int masteryLevel;
    public bool noMastery;
    public bool noSizeDisplay;
    public bool useVisibilityPrereq;
    public bool hasErraticGrowth;
    public bool debugMode;
    public int erraticQuantity;
    public ValueModifierRecord actionQuantityUsageMain = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionQuantityUsageAny = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionXpRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord yieldMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord specialMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionSpeed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord growingSpeed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord restingSpeed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord sizeMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord qualityMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord recoverySizeMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord naturalGrowth = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord naturalGrowthPower = new ValueModifierRecord(new BigDouble(0.0, 0));
    private int lastQuantity;

    /// <summary>The action the node runs by itself, which most nodes do not author.</summary>
    public PlotNodeActionSO autoAction;

    // The phases the node authors and the instances holding how many are in each. The game reaches
    // these through GetPhaseInstance(), which lazily creates a missing instance — a write — so both
    // lists are shaped as the game shapes them and read directly.
    public List<PlotNodePhaseInfo> phaseInfos = new List<PlotNodePhaseInfo>
    {
        new PlotNodePhaseInfo { phase = PlotNodePhases.Idle },
    };

    public List<PlotNodePhaseInstance> phaseInstances = new List<PlotNodePhaseInstance>();

    // The actions this node offers and the runtime instances of them it currently holds. Both are
    // walked directly: the game's GetActionInstances() is a plain getter, but the pairing question
    // needs the list either way.
    public List<PlotNodeActionSO> availableActions = new List<PlotNodeActionSO>();
    private List<PlotNodeActionInstance> actionInstances = new List<PlotNodeActionInstance>();

    public bool IsVisible() => visible;

    public List<PlotNodeActionInstance> GetActionInstances() => actionInstances;
}

/// <summary>
/// One running action on a plot. Its identity travels as the serialized reference the game holds,
/// which is a struct with a memoised guid beside the string it was parsed from.
/// </summary>
public sealed class PlotNodeActionInstance
{
    public IdObjectRef refObj;
    public int quantity;
    public PlotNodeSO? plotNodeRefObj;
    private PlotNodeActionSO? action;
    private bool engaged;
    public bool EnoughForOneInstance = true;
    public int MaximumInstances = int.MaxValue;
    public int MaximumRemainingInstances = int.MaxValue;

    public PlotNodeActionInstance()
    {
    }

    public PlotNodeActionInstance(PlotNodeActionSO action)
    {
        refObj = new IdObjectRef(action);
        this.action = action;
    }

    public PlotNodeActionInstance(PlotNodeSO plot, PlotNodeActionSO action)
        : this(action) => plotNodeRefObj = plot;

    public bool IsEmpty() => quantity <= 0;

    public bool IsEngaged() => engaged;

    public int GetActualQuantity() => quantity;

    public PlotNodeSO? GetElement() => plotNodeRefObj;

    public PlotNodeActionSO? GetAction() => action;

    /// <summary>The native row asks the exact action prerequisite container again.</summary>
    public bool IsVisible() => action?.prerequisites.Check() == true;

    public bool HasEnoughForOneInstance() => EnoughForOneInstance;

    public int GetMaximumRemInstances() => MaximumRemainingInstances;

    public int GetMaximumInstances() => MaximumInstances;

    /// <summary>
    /// Models the shipped existing-row path: it clamps against the absolute maximum, not the
    /// remaining maximum, and performs no affordability check.
    /// </summary>
    public void PlayerChangeInstanceQuantity(int change) =>
        quantity = Math.Max(0, Math.Min(quantity + change, GetMaximumInstances()));

    public void Engage() => engaged = true;
}

public struct IdObjectRef
{
    public string idStr;
    private Guid _guid;

    public IdObjectRef(PlotNodeActionSO action)
    {
        idStr = action.GetGuid().ToString();
        _guid = action.GetGuid();
    }
}

/// <summary>The game nests this inside PlotNodeSO; the suite reads it by name and not by nesting.</summary>
public enum PlotNodePhases
{
    Idle,
    Growing,
    Resting,
}

public sealed class PlotNodePhaseInfo
{
    public PlotNodePhases phase;
    public double phaseTime;
    public TimerList.TimerType processType;
    public PlotNodePhases exitPhase;
}

public sealed class PlotNodePhaseInstance
{
    public PlotNodePhases phase;
    public TimerList timers = new TimerList();

    public PlotNodePhaseInstance()
    {
    }

    public PlotNodePhaseInstance(PlotNodePhases phaseN, int quantity)
    {
        phase = phaseN;
        timers.q = quantity;
    }
}

/// <summary>
/// The game's timer bag. Only the count matters to collection, and the game holds it in a plain
/// field that GetCount() returns unchanged.
/// </summary>
public sealed class TimerList
{
    /// <summary>How a phase's timers run. The game nests this here; the suite reads its integer.</summary>
    public enum TimerType
    {
        Single,
        Parallel,
    }

    public int q;
    public List<BigDouble> ds = new List<BigDouble>();

    public int GetCount() => q;
}

public sealed class TreasurePoolSO : IdScriptableObject
{
    /// <summary>
    /// What one completed run draws out of a pool. The game nests this inside the pool type, which is
    /// why a consumer that wants to know it is a treasure payout compares the class name.
    /// </summary>
    public sealed class TreasurePoolInstantEffect : IInstantEffectScript
    {
        public TreasurePoolSO? treasurePool;
        public string effectType = string.Empty;
        public double effectValue;
        public FilterEffectMod filterScaling = new FilterEffectMod();
    }

    public static List<TreasurePoolSO> All = new List<TreasurePoolSO>();
    public int treasuresFound;
    public BigDouble partialTreasureReward;
    public bool forceLevel;
    private int treasureLevel;
    private bool calculatedTreasureLevel;
}
