using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One time rune as published: discovery, level, mastery, and the four records that scale it.</summary>
internal readonly struct WorldTimeRune : IWorldEntity
{
    internal WorldTimeRune(
        Guid timeRuneId,
        bool discovered,
        int level,
        int discRarityLevel,
        BigDouble masteryXp,
        int masteryLevel,
        bool isDiscoverRequired,
        bool seen,
        BigDouble freeUsages,
        BigDouble power,
        BigDouble powerScalingMod,
        BigDouble masteryXpMod,
        WorldDiscoverableDecision discovery = default)
    {
        TimeRuneId = timeRuneId;
        Discovered = discovered;
        Level = level;
        DiscRarityLevel = discRarityLevel;
        MasteryXp = masteryXp;
        MasteryLevel = masteryLevel;
        IsDiscoverRequired = isDiscoverRequired;
        Seen = seen;
        FreeUsages = freeUsages;
        Power = power;
        PowerScalingMod = powerScalingMod;
        MasteryXpMod = masteryXpMod;
        Discovery = discovery;
    }

    internal Guid TimeRuneId { get; }

    public Guid EntityId => TimeRuneId;

    internal bool Discovered { get; }

    internal int Level { get; }

    internal int DiscRarityLevel { get; }

    internal BigDouble MasteryXp { get; }

    internal int MasteryLevel { get; }

    internal bool IsDiscoverRequired { get; }

    internal bool Seen { get; }

    internal BigDouble FreeUsages { get; }

    internal BigDouble Power { get; }

    internal BigDouble PowerScalingMod { get; }

    internal BigDouble MasteryXpMod { get; }

    internal WorldDiscoverableDecision Discovery { get; }
}

internal sealed class WorldTimeRuneBinder : WorldPlainBinder<WorldTimeRune>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _discovered;
    private Func<object, int>? _level;
    private Func<object, int>? _discRarityLevel;
    private Func<object, BigDouble>? _masteryXp;
    private Func<object, int>? _masteryLevel;
    private Func<object, bool>? _isDiscoverRequired;
    private Func<object, bool>? _seen;
    private Func<object, BigDouble>? _freeUsages;
    private Func<object, BigDouble>? _power;
    private Func<object, BigDouble>? _powerScalingMod;
    private Func<object, BigDouble>? _masteryXpMod;
    private WorldDiscoverableBinding? _discovery;

    internal override string Category => "time runes";

    internal override string TypeName => "TimeRuneSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _discovered = bind.Field<bool>("discovered");
        _level = bind.Field<int>("level");
        _discRarityLevel = bind.Field<int>("discRarityLevel");
        _masteryXp = bind.Field<BigDouble>("masteryXp");
        _masteryLevel = bind.Field<int>("masteryLevel");
        _isDiscoverRequired = bind.Field<bool>("isDiscoverRequired");
        _seen = bind.Field<bool>("seen");
        _freeUsages = bind.ModifierRecord("freeUsages");
        _power = bind.ModifierRecord("power");
        _powerScalingMod = bind.ModifierRecord("powerScalingMod");
        _masteryXpMod = bind.ModifierRecord("masteryXpMod");
        _discovery = new WorldDiscoverableBinding(type, TypeName);
        return Join(bind.Failure, _discovery.Failure);
    }

    internal override WorldTimeRune Read(object entity) =>
        new(
            _id!(entity),
            _discovered!(entity),
            _level!(entity),
            _discRarityLevel!(entity),
            _masteryXp!(entity),
            _masteryLevel!(entity),
            _isDiscoverRequired!(entity),
            _seen!(entity),
            _freeUsages!(entity),
            _power!(entity),
            _powerScalingMod!(entity),
            _masteryXpMod!(entity),
            _discovery!.Read(entity));

    private static string Join(string left, string right) =>
        left.Length == 0 ? right : right.Length == 0 ? left : left + "; " + right;
}
