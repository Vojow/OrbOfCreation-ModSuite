using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One resource type as published: the levels it carries, the flags that decide how it is audited and displayed, and how loaded each of the records it pushes onto its resources is.</summary>
/// <remarks>
/// The counted records are <c>OrderedMultiplierRecord</c>s and <c>MergingModifierRecord</c>s, and
/// neither is a value at all. They are distributors: they hold modifiers and push them, transformed,
/// into the member records registered with <c>AddRecord</c>. An alchemy type's <c>power</c> pushes
/// into every one of its recipes' <c>power</c>, and it is that recipe-level
/// <c>ValueModifierRecord</c> — already collected, cached value and all — that carries the result.
/// <para>
/// So the distributed effect is not missing from the snapshot; it arrives on the members. What is
/// absent is the distributor's own total, the <c>Adjust(100)</c> its tooltip shows. That is pure
/// arithmetic over its two modifier dictionaries, so it is computable rather than blocked — but it
/// needs the modifiers themselves, which are variable-size and deferred. The active count is what a
/// fixed-size row can carry today, and it is the game's own <c>HasActiveElements()</c>.
/// </para>
/// </remarks>
internal readonly struct WorldResourceType : IWorldEntity
{
    internal WorldResourceType(
        Guid resourceTypeId,
        int level,
        int freeLevels,
        bool specialHidden,
        bool ignoreAudit,
        bool ignoreEffects,
        bool auditHasMaxQuantity,
        int rateModModifiers,
        int maxQuantityModModifiers,
        int maxQuantityRateModModifiers,
        int qualityModModifiers,
        int gainRateModModifiers,
        int drainModModifiers,
        int lossPercentModModifiers,
        int restModModifiers,
        int splashRateModifiers,
        int splashRateMaxPercentModifiers,
        int splashRateInterestModifiers,
        int splashRateMissingModifiers,
        int splashRateLifetimeModifiers,
        int rawMaxQuantityModifiers,
        int attributeCostModModifiers,
        int reservationModModifiers,
        int reverberateModModifiers,
        int reverberateTimeModModifiers,
        int replenishRatioModifiers,
        int replenishTimeModModifiers,
        int decayRatioModifiers,
        int decayTimeModModifiers)
    {
        ResourceTypeId = resourceTypeId;
        Level = level;
        FreeLevels = freeLevels;
        SpecialHidden = specialHidden;
        IgnoreAudit = ignoreAudit;
        IgnoreEffects = ignoreEffects;
        AuditHasMaxQuantity = auditHasMaxQuantity;
        RateModModifiers = rateModModifiers;
        MaxQuantityModModifiers = maxQuantityModModifiers;
        MaxQuantityRateModModifiers = maxQuantityRateModModifiers;
        QualityModModifiers = qualityModModifiers;
        GainRateModModifiers = gainRateModModifiers;
        DrainModModifiers = drainModModifiers;
        LossPercentModModifiers = lossPercentModModifiers;
        RestModModifiers = restModModifiers;
        SplashRateModifiers = splashRateModifiers;
        SplashRateMaxPercentModifiers = splashRateMaxPercentModifiers;
        SplashRateInterestModifiers = splashRateInterestModifiers;
        SplashRateMissingModifiers = splashRateMissingModifiers;
        SplashRateLifetimeModifiers = splashRateLifetimeModifiers;
        RawMaxQuantityModifiers = rawMaxQuantityModifiers;
        AttributeCostModModifiers = attributeCostModModifiers;
        ReservationModModifiers = reservationModModifiers;
        ReverberateModModifiers = reverberateModModifiers;
        ReverberateTimeModModifiers = reverberateTimeModModifiers;
        ReplenishRatioModifiers = replenishRatioModifiers;
        ReplenishTimeModModifiers = replenishTimeModModifiers;
        DecayRatioModifiers = decayRatioModifiers;
        DecayTimeModModifiers = decayTimeModModifiers;
    }

    internal Guid ResourceTypeId { get; }

    public Guid EntityId => ResourceTypeId;

    /// <summary>Levels bought and levels granted.</summary>
    internal int Level { get; }

    internal int FreeLevels { get; }

    /// <summary>Whether the type is hidden as a special, skipped by the audit, or has its effects ignored.</summary>
    internal bool SpecialHidden { get; }

    internal bool IgnoreAudit { get; }

    internal bool IgnoreEffects { get; }

    /// <summary>Whether the audit treats members of this type as capped.</summary>
    internal bool AuditHasMaxQuantity { get; }

    internal int RateModModifiers { get; }

    internal int MaxQuantityModModifiers { get; }

    internal int MaxQuantityRateModModifiers { get; }

    internal int QualityModModifiers { get; }

    internal int GainRateModModifiers { get; }

    internal int DrainModModifiers { get; }

    internal int LossPercentModModifiers { get; }

    internal int RestModModifiers { get; }

    internal int SplashRateModifiers { get; }

    internal int SplashRateMaxPercentModifiers { get; }

    internal int SplashRateInterestModifiers { get; }

    internal int SplashRateMissingModifiers { get; }

    internal int SplashRateLifetimeModifiers { get; }

    internal int RawMaxQuantityModifiers { get; }

    /// <summary>
    /// The rest of what this type pushes onto its resources: the attribute cost and reservation it
    /// adjusts, and the reverberate, replenish and decay pairs it contributes to.
    /// </summary>
    internal int AttributeCostModModifiers { get; }

    internal int ReservationModModifiers { get; }

    internal int ReverberateModModifiers { get; }

    internal int ReverberateTimeModModifiers { get; }

    internal int ReplenishRatioModifiers { get; }

    internal int ReplenishTimeModModifiers { get; }

    internal int DecayRatioModifiers { get; }

    internal int DecayTimeModModifiers { get; }
}

internal sealed class WorldResourceTypeBinder : WorldPlainBinder<WorldResourceType>
{
    private Func<object, Guid>? _id;
    private Func<object, int>? _level;
    private Func<object, int>? _freeLevels;
    private Func<object, bool>? _specialHidden;
    private Func<object, bool>? _ignoreAudit;
    private Func<object, bool>? _ignoreEffects;
    private Func<object, bool>? _auditHasMaxQuantity;
    private Func<object, int>? _rateModModifiers;
    private Func<object, int>? _maxQuantityModModifiers;
    private Func<object, int>? _maxQuantityRateModModifiers;
    private Func<object, int>? _qualityModModifiers;
    private Func<object, int>? _gainRateModModifiers;
    private Func<object, int>? _drainModModifiers;
    private Func<object, int>? _lossPercentModModifiers;
    private Func<object, int>? _restModModifiers;
    private Func<object, int>? _splashRateModifiers;
    private Func<object, int>? _splashRateMaxPercentModifiers;
    private Func<object, int>? _splashRateInterestModifiers;
    private Func<object, int>? _splashRateMissingModifiers;
    private Func<object, int>? _splashRateLifetimeModifiers;
    private Func<object, int>? _rawMaxQuantityModifiers;
    private Func<object, int>? _attributeCostModModifiers;
    private Func<object, int>? _reservationModModifiers;
    private Func<object, int>? _reverberateModModifiers;
    private Func<object, int>? _reverberateTimeModModifiers;
    private Func<object, int>? _replenishRatioModifiers;
    private Func<object, int>? _replenishTimeModModifiers;
    private Func<object, int>? _decayRatioModifiers;
    private Func<object, int>? _decayTimeModModifiers;

    internal override string Category => "resource types";

    internal override string TypeName => "ResourceTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _level = bind.Field<int>("level");
        _freeLevels = bind.Field<int>("freeLevels");
        _specialHidden = bind.Field<bool>("specialHidden");
        _ignoreAudit = bind.Field<bool>("ignoreAudit");
        _ignoreEffects = bind.Field<bool>("ignoreEffects");
        _auditHasMaxQuantity = bind.Field<bool>("auditHasMaxQuantity");
        _rateModModifiers = bind.NestedCollectionCount("rateMod", "activeModifiers");
        _maxQuantityModModifiers = bind.NestedCollectionCount("maxQuantityMod", "activeModifiers");
        _maxQuantityRateModModifiers = bind.NestedCollectionCount("maxQuantityRateMod", "activeModifiers");
        _qualityModModifiers = bind.NestedCollectionCount("qualityMod", "activeModifiers");
        _gainRateModModifiers = bind.NestedCollectionCount("gainRateMod", "activeModifiers");
        _drainModModifiers = bind.NestedCollectionCount("drainMod", "activeModifiers");
        _lossPercentModModifiers = bind.NestedCollectionCount("lossPercentMod", "activeModifiers");
        _restModModifiers = bind.NestedCollectionCount("restMod", "activeModifiers");
        _splashRateModifiers = bind.NestedCollectionCount("splashRate", "activeModifiers");
        _splashRateMaxPercentModifiers = bind.NestedCollectionCount("splashRateMaxPercent", "activeModifiers");
        _splashRateInterestModifiers = bind.NestedCollectionCount("splashRateInterest", "activeModifiers");
        _splashRateMissingModifiers = bind.NestedCollectionCount("splashRateMissing", "activeModifiers");
        _splashRateLifetimeModifiers = bind.NestedCollectionCount("splashRateLifetime", "activeModifiers");
        _rawMaxQuantityModifiers = bind.NestedCollectionCount("rawMaxQuantity", "activeModifiers");
        _attributeCostModModifiers = bind.NestedCollectionCount("attributeCostMod", "activeModifiers");
        _reservationModModifiers = bind.NestedCollectionCount("reservationMod", "activeModifiers");
        _reverberateModModifiers = bind.NestedCollectionCount("reverberateMod", "activeModifiers");
        _reverberateTimeModModifiers = bind.NestedCollectionCount("reverberateTimeMod", "activeModifiers");
        _replenishRatioModifiers = bind.NestedCollectionCount("replenishRatio", "activeModifiers");
        _replenishTimeModModifiers = bind.NestedCollectionCount("replenishTimeMod", "activeModifiers");
        _decayRatioModifiers = bind.NestedCollectionCount("decayRatio", "activeModifiers");
        _decayTimeModModifiers = bind.NestedCollectionCount("decayTimeMod", "activeModifiers");
        return bind.Failure;
    }

    internal override WorldResourceType Read(object entity) =>
        new(
            _id!(entity),
            _level!(entity),
            _freeLevels!(entity),
            _specialHidden!(entity),
            _ignoreAudit!(entity),
            _ignoreEffects!(entity),
            _auditHasMaxQuantity!(entity),
            _rateModModifiers!(entity),
            _maxQuantityModModifiers!(entity),
            _maxQuantityRateModModifiers!(entity),
            _qualityModModifiers!(entity),
            _gainRateModModifiers!(entity),
            _drainModModifiers!(entity),
            _lossPercentModModifiers!(entity),
            _restModModifiers!(entity),
            _splashRateModifiers!(entity),
            _splashRateMaxPercentModifiers!(entity),
            _splashRateInterestModifiers!(entity),
            _splashRateMissingModifiers!(entity),
            _splashRateLifetimeModifiers!(entity),
            _rawMaxQuantityModifiers!(entity),
            _attributeCostModModifiers!(entity),
            _reservationModModifiers!(entity),
            _reverberateModModifiers!(entity),
            _reverberateTimeModModifiers!(entity),
            _replenishRatioModifiers!(entity),
            _replenishTimeModModifiers!(entity),
            _decayRatioModifiers!(entity),
            _decayTimeModModifiers!(entity));
}
