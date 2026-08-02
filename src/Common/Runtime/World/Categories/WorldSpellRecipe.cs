using System;
using System.Collections;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

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
        : this(
            spellRecipeId,
            discovered,
            discRarityLevel,
            masteryXp,
            masteryLevel,
            masteryLevelReady,
            hiddenDiscovery,
            isRequiredDiscovery,
            penaltyUsageCost,
            castSpeed,
            baseCharges,
            repeatInstantEffects,
            spellPowerMod,
            spellCostMod,
            spellCdSpeedMod,
            spellDurationMod,
            spellSpecialMod,
            spellXpMod,
            hasAlertedThisMastery,
            PublicationTable<WorldSpellRecipeGlyph>.Empty,
            PublicationTable<WorldSpellRecipeCost>.Empty,
            false)
    {
    }

    internal WorldSpellRecipe(
        Guid spellRecipeId,
        bool discovered,
        int discRarityLevel,
        BigDouble masteryXp,
        int masteryLevel,
        bool masteryLevelReady,
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
        bool hasAlertedThisMastery,
        PublicationTable<WorldSpellRecipeGlyph> coreGlyphs,
        PublicationTable<WorldSpellRecipeCost> discoveryCosts,
        bool discoveryAffordable)
    {
        SpellRecipeId = spellRecipeId;
        Discovered = discovered;
        DiscRarityLevel = discRarityLevel;
        MasteryXp = masteryXp;
        MasteryLevel = masteryLevel;
        MasteryLevelReady = masteryLevelReady;
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
        CoreGlyphs = coreGlyphs ?? throw new ArgumentNullException(nameof(coreGlyphs));
        DiscoveryCosts = discoveryCosts ?? throw new ArgumentNullException(nameof(discoveryCosts));
        DiscoveryAffordable = discoveryAffordable;
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

    /// <summary>The authored, ordered core-glyph recipe the native resolver consumes.</summary>
    internal PublicationTable<WorldSpellRecipeGlyph> CoreGlyphs { get; }

    /// <summary>The game's exact next discovery price, with the current spendable amount beside it.</summary>
    internal PublicationTable<WorldSpellRecipeCost> DiscoveryCosts { get; }

    /// <summary>The native <c>ResourceCostList.HasEnough()</c> verdict for this discovery price.</summary>
    internal bool DiscoveryAffordable { get; }
}

internal readonly struct WorldSpellRecipeGlyph
{
    internal WorldSpellRecipeGlyph(int position, Guid glyphId)
    {
        Position = position;
        GlyphId = glyphId;
    }

    internal int Position { get; }
    internal Guid GlyphId { get; }
}

internal readonly struct WorldSpellRecipeCost
{
    internal WorldSpellRecipeCost(Guid resourceId, BigDouble cost, BigDouble availableAmount)
    {
        ResourceId = resourceId;
        Cost = cost;
        AvailableAmount = availableAmount;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Cost { get; }
    internal BigDouble AvailableAmount { get; }
}

internal sealed class WorldSpellRecipeBinder : WorldPlainBinder<WorldSpellRecipe>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _discovered;
    private Func<object, int>? _discRarityLevel;
    private Func<object, BigDouble>? _masteryXp;
    private Func<object, int>? _masteryLevel;
    private Func<object, bool>? _masteryLevelReady;
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
    private Func<object, IList?>? _coreGlyphs;
    private Func<object, object?>? _discoveryCost;
    private Func<object, bool>? _costAffordable;
    private Func<object, IList?>? _costEntries;
    private Func<object, Guid>? _glyphId;
    private Func<object, Guid>? _costResourceId;
    private Func<object, BigDouble>? _costValue;
    private Func<object, object?>? _costResource;
    private Func<object, BigDouble>? _resourceAmount;

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
        const System.Reflection.BindingFlags instance =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;
        var glyphMethod = type.GetMethod("GetGlyphRecipe", instance, null, Type.EmptyTypes, null);
        var glyphType = glyphMethod?.ReturnType is { IsGenericType: true } glyphList
            ? glyphList.GetGenericArguments()[0]
            : null;
        _coreGlyphs = bind.CallList("GetGlyphRecipe", glyphType);
        _glyphId = NativeAccessorBinder.Call<Guid>(glyphType, "GetGuid");

        var costMethod = type.GetMethod("GetDiscoverCost", instance, null, Type.EmptyTypes, null);
        var costType = costMethod?.ReturnType;
        _discoveryCost = bind.CallObject("GetDiscoverCost", costType);
        _costAffordable = NativeAccessorBinder.Call<bool>(costType, "HasEnough");
        var entryMethod = costType?.GetMethod("GetEntries", instance, null, Type.EmptyTypes, null);
        var entryType = entryMethod?.ReturnType is { IsGenericType: true } entryList
            ? entryList.GetGenericArguments()[0]
            : null;
        _costEntries = NativeAccessorBinder.CallList(costType, "GetEntries", entryType);
        _costResourceId = NativeAccessorBinder.ReferenceGuid(entryType, "resource");
        _costValue = NativeAccessorBinder.Call<BigDouble>(entryType, "GetValue");
        var resourceType = entryType?.GetField("resource", instance)?.FieldType;
        _costResource = NativeAccessorBinder.Reference(entryType, "resource", resourceType);
        _resourceAmount = NativeAccessorBinder.Call<BigDouble>(resourceType, "GetTrueQuantity");

        if (_glyphId is null || _costAffordable is null || _costEntries is null ||
            _costResourceId is null || _costValue is null || _costResource is null ||
            _resourceAmount is null)
        {
            return bind.Failure.Length == 0
                ? "SpellRecipeSO decision members did not expose their complete nested identity and cost shape on this build"
                : bind.Failure;
        }
        return bind.Failure;
    }

    internal override WorldSpellRecipe Read(object entity)
    {
        var glyphValues = _coreGlyphs!(entity);
        var glyphs = new WorldSpellRecipeGlyph[glyphValues?.Count ?? 0];
        for (var index = 0; index < glyphs.Length; index++)
        {
            var glyph = glyphValues![index];
            glyphs[index] = new WorldSpellRecipeGlyph(
                index,
                glyph is null ? Guid.Empty : _glyphId!(glyph));
        }

        var cost = _discoveryCost!(entity);
        var entries = cost is null ? null : _costEntries!(cost);
        var costs = new WorldSpellRecipeCost[entries?.Count ?? 0];
        for (var index = 0; index < costs.Length; index++)
        {
            var entry = entries![index];
            if (entry is null) continue;
            var resource = _costResource!(entry);
            costs[index] = new WorldSpellRecipeCost(
                _costResourceId!(entry),
                _costValue!(entry),
                resource is null ? default : _resourceAmount!(resource));
        }

        return new WorldSpellRecipe(
            _id!(entity),
            _discovered!(entity),
            _discRarityLevel!(entity),
            _masteryXp!(entity),
            _masteryLevel!(entity),
            _masteryLevelReady!(entity),
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
            _hasAlertedThisMastery!(entity),
            PublicationTable<WorldSpellRecipeGlyph>.Create(glyphs),
            PublicationTable<WorldSpellRecipeCost>.Create(costs),
            cost is not null && _costAffordable!(cost));
    }
}
