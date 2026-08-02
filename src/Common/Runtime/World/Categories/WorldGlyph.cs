using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One glyph as published.</summary>
internal readonly struct WorldGlyph : IWorldEntity
{
    internal WorldGlyph(Guid glyphId, int level, int freeLevels, int discoveryRarityLevel, bool discovered,
        bool discoverable,
        bool discoveryRequired,
        bool augmentsSpells,
        bool requiresDuration,
        bool requiresToggleable,
        int masteryReqCount,
        BigDouble freeUsages,
        BigDouble freeLoadoutUsages,
        BigDouble maxUsages,
        bool available = false,
        int maximumUsages = 0,
        WorldDiscoverableDecision discovery = default)
    {
        GlyphId = glyphId;
        Level = level;
        FreeLevels = freeLevels;
        DiscoveryRarityLevel = discoveryRarityLevel;
        Discovered = discovered;
        Discoverable = discoverable;
        DiscoveryRequired = discoveryRequired;
        AugmentsSpells = augmentsSpells;
        RequiresDuration = requiresDuration;
        RequiresToggleable = requiresToggleable;
        MasteryReqCount = masteryReqCount;
        FreeUsages = freeUsages;
        FreeLoadoutUsages = freeLoadoutUsages;
        MaxUsages = maxUsages;
        Available = available;
        MaximumUsages = maximumUsages;
        Discovery = discovery;
    }

    internal Guid GlyphId { get; }

    public Guid EntityId => GlyphId;

    internal int Level { get; }

    /// <summary>Levels granted rather than bought.</summary>
    internal int FreeLevels { get; }

    internal int DiscoveryRarityLevel { get; }

    internal bool Discovered { get; }

    /// <summary>The rest of the glyph's runtime state: what it may be applied to, and its usage grants.</summary>
    internal bool Discoverable { get; }

    internal bool DiscoveryRequired { get; }

    internal bool AugmentsSpells { get; }

    internal bool RequiresDuration { get; }

    internal bool RequiresToggleable { get; }

    internal int MasteryReqCount { get; }

    internal BigDouble FreeUsages { get; }

    internal BigDouble FreeLoadoutUsages { get; }

    internal BigDouble MaxUsages { get; }

    /// <summary>The native progression verdict used by the glyph picker now.</summary>
    internal bool Available { get; }

    /// <summary>The native picker clamp for this glyph, after active modifiers.</summary>
    internal int MaximumUsages { get; }

    internal WorldDiscoverableDecision Discovery { get; }
}

internal sealed class WorldGlyphBinder : WorldPlainBinder<WorldGlyph>
{
    private Func<object, Guid>? _id;
    private Func<object, int>? _level;
    private Func<object, int>? _freeLevels;
    private Func<object, int>? _discRarityLevel;
    private Func<object, bool>? _discovered;
    private Func<object, bool>? _discoverable;
    private Func<object, bool>? _discoveryRequired;
    private Func<object, bool>? _augmentsSpells;
    private Func<object, bool>? _requiresDuration;
    private Func<object, bool>? _requiresToggleable;
    private Func<object, int>? _masteryReqCount;
    private Func<object, BigDouble>? _freeUsages;
    private Func<object, BigDouble>? _freeLoadoutUsages;
    private Func<object, BigDouble>? _maxUsages;
    private Func<object, bool>? _available;
    private Func<object, int>? _maximumUsages;
    private WorldDiscoverableBinding? _discovery;

    internal override string Category => "glyphs";

    internal override string TypeName => "GlyphSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _level = bind.Field<int>("level");
        _freeLevels = bind.Field<int>("freeLevels");
        _discRarityLevel = bind.Field<int>("discRarityLevel");
        _discovered = bind.Field<bool>("discovered");
        _discoverable = bind.Field<bool>("discoverable");
        _discoveryRequired = bind.Field<bool>("discoveryRequired");
        _augmentsSpells = bind.Field<bool>("augmentsSpells");
        _requiresDuration = bind.Field<bool>("requiresDuration");
        _requiresToggleable = bind.Field<bool>("requiresToggleable");
        _masteryReqCount = bind.Field<int>("masteryReqCount");
        _freeUsages = bind.ModifierRecord("freeUsages");
        _freeLoadoutUsages = bind.ModifierRecord("freeLoadoutUsages");
        _maxUsages = bind.ModifierRecord("maxUsages");
        _available = bind.Call<bool>("IsAvailable");
        _maximumUsages = bind.Call<int>("GetMaxUsages");
        _discovery = new WorldDiscoverableBinding(type, TypeName);
        return Join(bind.Failure, _discovery.Failure);
    }

    internal override WorldGlyph Read(object entity) =>
        new(
            _id!(entity),
            _level!(entity),
            _freeLevels!(entity),
            _discRarityLevel!(entity),
            _discovered!(entity),
            _discoverable!(entity),
            _discoveryRequired!(entity),
            _augmentsSpells!(entity),
            _requiresDuration!(entity),
            _requiresToggleable!(entity),
            _masteryReqCount!(entity),
            _freeUsages!(entity),
            _freeLoadoutUsages!(entity),
            _maxUsages!(entity),
            _available!(entity),
            _maximumUsages!(entity),
            _discovery!.Read(entity));

    private static string Join(string left, string right) =>
        left.Length == 0 ? right : right.Length == 0 ? left : left + "; " + right;
}
