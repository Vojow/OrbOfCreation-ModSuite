using System;
using System.Collections;

namespace OrbModding.Common.Runtime.World;

[Flags]
internal enum WorldTimeRuneArchetype
{
    None = 0,
    Tempo = 1 << 0,
    Scaling = 1 << 1,
    Investment = 1 << 2,
    Meta = 1 << 3,
    Unique = 1 << 4,
    Global = 1 << 5,
}

/// <summary>One time rune as published: discovery, level, mastery, and the four records that scale it.</summary>
internal readonly struct WorldTimeRune : IWorldEntity
{
    internal WorldTimeRune(
        Guid timeRuneId,
        string label,
        WorldTimeRuneArchetype archetypes,
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
        BigDouble masteryXpMod)
    {
        TimeRuneId = timeRuneId;
        Label = label ?? string.Empty;
        Archetypes = archetypes;
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
    }

    internal Guid TimeRuneId { get; }

    public Guid EntityId => TimeRuneId;

    internal string Label { get; }

    internal WorldTimeRuneArchetype Archetypes { get; }

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
}

internal sealed class WorldTimeRuneBinder : WorldPlainBinder<WorldTimeRune>
{
    private static readonly Guid TempoTypeId = new("fe1b6c9f-a827-422e-8bc7-9da640409d02");
    private static readonly Guid ScalingTypeId = new("92fd86b6-f652-460d-8d2f-9d03f29b5431");
    private static readonly Guid InvestmentTypeId = new("e61bc5cc-deaa-467b-81de-002ac0373f7a");
    private static readonly Guid MetaTypeId = new("a282e50a-5409-454e-8557-b2987d67d78f");
    private static readonly Guid UniqueTypeId = new("dafb5a1e-6da7-47bd-a00d-c9e18c96108a");
    private static readonly Guid GlobalTypeId = new("3f8ccba2-0481-4401-b269-978600cb0208");
    private Func<object, Guid>? _id;
    private Func<object, string>? _label;
    private Func<object, IList?>? _timeRuneTypes;
    private Func<object, Guid>? _timeRuneTypeId;
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

    internal override string Category => "time runes";

    internal override string TypeName => "TimeRuneSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _label = bind.Call<string>("GetName");
        var typeElement = bind.CollectionElementType("timeRuneTypes");
        _timeRuneTypes = bind.CollectionField("timeRuneTypes");
        _timeRuneTypeId = bind.Elements(typeElement, "TimeRuneSO.timeRuneTypes[]")
            .Call<Guid>("GetGuid");
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
        return bind.Failure;
    }

    internal override WorldTimeRune Read(object entity) =>
        new(
            _id!(entity),
            _label!(entity),
            ReadArchetypes(entity),
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
            _masteryXpMod!(entity));

    private WorldTimeRuneArchetype ReadArchetypes(object entity)
    {
        var result = WorldTimeRuneArchetype.None;
        var types = _timeRuneTypes!(entity);
        if (types is null) return result;
        for (var index = 0; index < types.Count; index++)
        {
            var item = types[index];
            if (item is null) continue;
            var id = _timeRuneTypeId!(item);
            if (id == TempoTypeId) result |= WorldTimeRuneArchetype.Tempo;
            else if (id == ScalingTypeId) result |= WorldTimeRuneArchetype.Scaling;
            else if (id == InvestmentTypeId) result |= WorldTimeRuneArchetype.Investment;
            else if (id == MetaTypeId) result |= WorldTimeRuneArchetype.Meta;
            else if (id == UniqueTypeId) result |= WorldTimeRuneArchetype.Unique;
            else if (id == GlobalTypeId) result |= WorldTimeRuneArchetype.Global;
        }
        return result;
    }
}
