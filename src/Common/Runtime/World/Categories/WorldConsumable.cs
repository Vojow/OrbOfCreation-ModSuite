using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One consumable as published — the scalar half. How many are in stock lives in
/// <c>consumableCounts</c>, a list, and is not carried; see <c>docs/runtime-architecture/world-collection.md</c>.
/// </summary>
internal readonly struct WorldConsumable : IWorldEntity
{
    internal WorldConsumable(
        Guid consumableId,
        bool visible,
        bool randomized,
        int quantity,
        int queuedQuantity,
        int gainedSince,
        int maxCreatedLevel,
        BigDouble currentPrepTime,
        BigDouble currentCooldown,
        BigDouble currentCooldownTime,
        in RawConsumableModifiers modifiers,
        double preparationTime,
        bool canBeRandomized,
        bool hasDuration,
        double durationBase,
        bool queueOnStart)
    {
        ConsumableId = consumableId;
        Visible = visible;
        Randomized = randomized;
        Quantity = quantity;
        QueuedQuantity = queuedQuantity;
        GainedSince = gainedSince;
        MaxCreatedLevel = maxCreatedLevel;
        CurrentPrepTime = currentPrepTime;
        CurrentCooldown = currentCooldown;
        CurrentCooldownTime = currentCooldownTime;
        Modifiers = modifiers;
        PreparationTime = preparationTime;
        CanBeRandomized = canBeRandomized;
        HasDuration = hasDuration;
        DurationBase = durationBase;
        QueueOnStart = queueOnStart;
    }

    internal Guid ConsumableId { get; }

    public Guid EntityId => ConsumableId;

    internal bool Visible { get; }

    internal bool Randomized { get; }

    /// <summary>
    /// How many are in stock. The save record stores a <c>consumableCounts</c> list and derives this
    /// on load, but the runtime keeps the total in a plain cached int that <c>GetQuantity()</c>
    /// returns — so the stock count was always a scalar, and reading the save record is what hid it.
    /// </summary>
    internal int Quantity { get; }

    /// <summary>
    /// How many are queued to be made. <c>GetRemainingQuantity()</c> is
    /// <see cref="Quantity"/> minus this.
    /// </summary>
    internal int QueuedQuantity { get; }

    /// <summary>How many have been gained since the counter was last cleared.</summary>
    internal int GainedSince { get; }

    /// <summary>The highest level ever created.</summary>
    internal int MaxCreatedLevel { get; }

    /// <summary>Seconds left preparing, and the cooldown left against the cooldown it started from.</summary>
    internal BigDouble CurrentPrepTime { get; }

    internal BigDouble CurrentCooldown { get; }

    internal BigDouble CurrentCooldownTime { get; }

    internal RawConsumableModifiers Modifiers { get; }

    /// <summary>The rest of the consumable's definition: how it is prepared and whether it has a duration.</summary>
    internal double PreparationTime { get; }

    internal bool CanBeRandomized { get; }

    internal bool HasDuration { get; }

    internal double DurationBase { get; }

    internal bool QueueOnStart { get; }
}

/// <summary>A consumable's cached modifier records — what using one is currently worth.</summary>
internal readonly struct RawConsumableModifiers
{
    internal RawConsumableModifiers(
        BigDouble power,
        BigDouble durationMod,
        BigDouble special,
        BigDouble prepSpeed,
        BigDouble bonusLevels)
    {
        Power = power;
        DurationMod = durationMod;
        Special = special;
        PrepSpeed = prepSpeed;
        BonusLevels = bonusLevels;
    }

    internal BigDouble Power { get; }

    internal BigDouble DurationMod { get; }

    internal BigDouble Special { get; }

    /// <summary>How fast preparation runs, before the player-wide multiplier.</summary>
    internal BigDouble PrepSpeed { get; }

    /// <summary>Levels granted on top of the created level.</summary>
    internal BigDouble BonusLevels { get; }
}

internal sealed class WorldConsumableBinder : WorldPlainBinder<WorldConsumable>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _visible;
    private Func<object, bool>? _randomized;
    private Func<object, int>? _quantity;
    private Func<object, int>? _queuedQuantity;
    private Func<object, int>? _gainedSince;
    private Func<object, int>? _maxCreatedLevel;
    private Func<object, BigDouble>? _prepTime;
    private Func<object, BigDouble>? _cooldown;
    private Func<object, BigDouble>? _cooldownTime;
    private Func<object, BigDouble>? _power;
    private Func<object, BigDouble>? _durationMod;
    private Func<object, BigDouble>? _special;
    private Func<object, BigDouble>? _prepSpeed;
    private Func<object, BigDouble>? _bonusLevels;
    private Func<object, double>? _preparationTime;
    private Func<object, bool>? _canBeRandomized;
    private Func<object, bool>? _hasDuration;
    private Func<object, double>? _durationBase;
    private Func<object, bool>? _queueOnStart;

    internal override string Category => "consumables";

    internal override string TypeName => "ConsumableSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _visible = bind.Field<bool>("visible");
        _randomized = bind.Field<bool>("randomized");
        _quantity = bind.Field<int>("quantity");
        _queuedQuantity = bind.Field<int>("queuedQuantity");
        _gainedSince = bind.Field<int>("gainedSince");
        _maxCreatedLevel = bind.Field<int>("maxCreatedLv");
        _prepTime = bind.Field<BigDouble>("currentPrepTime");
        _cooldown = bind.Field<BigDouble>("currentCooldown");
        _cooldownTime = bind.Field<BigDouble>("currentCooldownTime");
        _power = bind.ModifierRecord("power");
        _durationMod = bind.ModifierRecord("durationMod");
        _special = bind.ModifierRecord("special");
        _prepSpeed = bind.ModifierRecord("prepSpeed");
        _bonusLevels = bind.ModifierRecord("bonusLevels");
        _preparationTime = bind.Field<double>("preparationTime");
        _canBeRandomized = bind.Field<bool>("canBeRandomized");
        _hasDuration = bind.Field<bool>("hasDuration");
        _durationBase = bind.Field<double>("durationBase");
        _queueOnStart = bind.Field<bool>("queueOnStart");
        return bind.Failure;
    }

    internal override WorldConsumable Read(object entity) =>
        new(
            _id!(entity),
            _visible!(entity),
            _randomized!(entity),
            _quantity!(entity),
            _queuedQuantity!(entity),
            _gainedSince!(entity),
            _maxCreatedLevel!(entity),
            _prepTime!(entity),
            _cooldown!(entity),
            _cooldownTime!(entity),
            new RawConsumableModifiers(
                _power!(entity),
                _durationMod!(entity),
                _special!(entity),
                _prepSpeed!(entity),
                _bonusLevels!(entity)),
            _preparationTime!(entity),
            _canBeRandomized!(entity),
            _hasDuration!(entity),
            _durationBase!(entity),
            _queueOnStart!(entity));
}
