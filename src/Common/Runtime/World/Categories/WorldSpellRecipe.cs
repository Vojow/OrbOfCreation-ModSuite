using System;
using System.Reflection;

namespace OrbModding.Common.Runtime.World;

/// <summary>One spell recipe as published: discovery, its mastery track, and the six cached records that say what a cast of it is worth.</summary>
internal readonly struct WorldSpellRecipe : IWorldEntity
{
    internal WorldSpellRecipe(
        Guid spellRecipeId,
        bool discovered,
        int discRarityLevel,
        BigDouble masteryXp,
        int masteryLevel,
        bool masteryLevelReady,
        bool masteryLevelAffordable,
        bool hiddenDiscovery,
        bool isRequiredDiscovery,
        int penaltyUsageCost,
        double castSpeed,
        int baseCharges,
        bool repeatInstantEffects,
        BigDouble spellPowerMod,
        BigDouble spellCostMod,
        BigDouble spellCdSpeedMod,
        BigDouble spellDurationMod,
        BigDouble spellSpecialMod,
        BigDouble spellXpMod,
        bool hasAlertedThisMastery)
    {
        SpellRecipeId = spellRecipeId;
        Discovered = discovered;
        DiscRarityLevel = discRarityLevel;
        MasteryXp = masteryXp;
        MasteryLevel = masteryLevel;
        MasteryLevelReady = masteryLevelReady;
        MasteryLevelAffordable = masteryLevelAffordable;
        HiddenDiscovery = hiddenDiscovery;
        IsRequiredDiscovery = isRequiredDiscovery;
        PenaltyUsageCost = penaltyUsageCost;
        CastSpeed = castSpeed;
        BaseCharges = baseCharges;
        RepeatInstantEffects = repeatInstantEffects;
        SpellPowerMod = spellPowerMod;
        SpellCostMod = spellCostMod;
        SpellCdSpeedMod = spellCdSpeedMod;
        SpellDurationMod = spellDurationMod;
        SpellSpecialMod = spellSpecialMod;
        SpellXpMod = spellXpMod;
        HasAlertedThisMastery = hasAlertedThisMastery;
    }

    internal Guid SpellRecipeId { get; }

    public Guid EntityId => SpellRecipeId;

    internal bool Discovered { get; }

    internal int DiscRarityLevel { get; }

    internal BigDouble MasteryXp { get; }

    internal int MasteryLevel { get; }

    /// <summary>
    /// Whether the mastery track has banked enough experience for the next level to be bought.
    /// </summary>
    /// <remarks>
    /// The game's own answer, <c>IsReadyToLevelMastery()</c>, rather than a comparison this suite
    /// makes: the experience threshold lives inside a container the snapshot does not publish, so
    /// there is nothing to compare <see cref="MasteryXp"/> against. W58 named the shortfall and W59
    /// closes it. The call reads and writes nothing, which is what lets capture make it.
    /// </remarks>
    internal bool MasteryLevelReady { get; }

    /// <summary>
    /// Whether the game's current level-cost list says the next mastery level is affordable.
    /// </summary>
    /// <remarks>
    /// This is planning evidence, not mutation authority. The action boundary asks the same native
    /// cost again immediately before spending because resources may move after publication.
    /// </remarks>
    internal bool MasteryLevelAffordable { get; }

    internal bool HiddenDiscovery { get; }

    internal bool IsRequiredDiscovery { get; }

    internal int PenaltyUsageCost { get; }

    internal double CastSpeed { get; }

    internal int BaseCharges { get; }

    internal bool RepeatInstantEffects { get; }

    internal BigDouble SpellPowerMod { get; }

    internal BigDouble SpellCostMod { get; }

    internal BigDouble SpellCdSpeedMod { get; }

    internal BigDouble SpellDurationMod { get; }

    internal BigDouble SpellSpecialMod { get; }

    internal BigDouble SpellXpMod { get; }

    internal bool HasAlertedThisMastery { get; }
}

internal sealed class WorldSpellRecipeBinder : WorldPlainBinder<WorldSpellRecipe>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _discovered;
    private Func<object, int>? _discRarityLevel;
    private Func<object, BigDouble>? _masteryXp;
    private Func<object, int>? _masteryLevel;
    private Func<object, bool>? _masteryLevelReady;
    private MethodInfo? _getLevelCost;
    private Func<object, bool>? _levelCostHasEnough;
    private Func<object, bool>? _hiddenDiscovery;
    private Func<object, bool>? _isRequiredDiscovery;
    private Func<object, int>? _penaltyUsageCost;
    private Func<object, double>? _castSpeed;
    private Func<object, int>? _baseCharges;
    private Func<object, bool>? _repeatInstantEffects;
    private Func<object, BigDouble>? _spellPowerMod;
    private Func<object, BigDouble>? _spellCostMod;
    private Func<object, BigDouble>? _spellCdSpeedMod;
    private Func<object, BigDouble>? _spellDurationMod;
    private Func<object, BigDouble>? _spellSpecialMod;
    private Func<object, BigDouble>? _spellXpMod;
    private Func<object, bool>? _hasAlertedThisMastery;

    internal override string Category => "spell recipes";

    internal override string TypeName => "SpellRecipeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _discovered = bind.Field<bool>("discovered");
        _discRarityLevel = bind.Field<int>("discRarityLevel");
        _masteryXp = bind.Field<BigDouble>("masteryExperience");
        _masteryLevel = bind.Field<int>("masteryLevel");
        _masteryLevelReady = bind.Call<bool>("IsReadyToLevelMastery");
        _getLevelCost = type.GetMethod(
            "GetLevelCost",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        var levelCost = bind.Elements(_getLevelCost?.ReturnType, "SpellRecipeSO.GetLevelCost()");
        _levelCostHasEnough = levelCost.Call<bool>("HasEnough");
        _hiddenDiscovery = bind.Field<bool>("hiddenDiscovery");
        _isRequiredDiscovery = bind.Field<bool>("isRequiredDiscovery");
        _penaltyUsageCost = bind.Field<int>("penaltyUsageCost");
        _castSpeed = bind.Field<double>("castSpeed");
        _baseCharges = bind.Field<int>("baseCharges");
        _repeatInstantEffects = bind.Field<bool>("repeatInstantEffects");
        _spellPowerMod = bind.ModifierRecord("spellPowerMod");
        _spellCostMod = bind.ModifierRecord("spellCostMod");
        _spellCdSpeedMod = bind.ModifierRecord("spellCdSpeedMod");
        _spellDurationMod = bind.ModifierRecord("spellDurationMod");
        _spellSpecialMod = bind.ModifierRecord("spellSpecialMod");
        _spellXpMod = bind.ModifierRecord("spellXpMod");
        _hasAlertedThisMastery = bind.Field<bool>("hasAlertedThisMastery");
        return bind.Failure;
    }

    internal override WorldSpellRecipe Read(object entity)
    {
        var levelCost = _getLevelCost!.Invoke(entity, null);
        var affordable = levelCost is not null && _levelCostHasEnough!(levelCost);
        return new(
            _id!(entity),
            _discovered!(entity),
            _discRarityLevel!(entity),
            _masteryXp!(entity),
            _masteryLevel!(entity),
            _masteryLevelReady!(entity),
            affordable,
            _hiddenDiscovery!(entity),
            _isRequiredDiscovery!(entity),
            _penaltyUsageCost!(entity),
            _castSpeed!(entity),
            _baseCharges!(entity),
            _repeatInstantEffects!(entity),
            _spellPowerMod!(entity),
            _spellCostMod!(entity),
            _spellCdSpeedMod!(entity),
            _spellDurationMod!(entity),
            _spellSpecialMod!(entity),
            _spellXpMod!(entity),
            _hasAlertedThisMastery!(entity));
    }
}
