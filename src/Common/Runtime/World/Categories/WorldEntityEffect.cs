using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>One authored effect as read: who applies it, to what, on which property, and how much.</summary>
internal readonly struct RawEntityEffect
{
    internal RawEntityEffect(
        Guid sourceId,
        Guid targetId,
        string property,
        int modifierType,
        BigDouble modifierAmount)
    {
        SourceId = sourceId;
        TargetId = targetId;
        Property = property;
        ModifierType = modifierType;
        ModifierAmount = modifierAmount;
    }

    internal Guid SourceId { get; }
    internal Guid TargetId { get; }
    internal string Property { get; }
    internal int ModifierType { get; }
    internal BigDouble ModifierAmount { get; }
}

/// <summary>
/// What buying one of an entity does to another entity's named property.
/// </summary>
/// <remarks>
/// <para>
/// The published fact is deliberately the effect, not a verdict about it. Whether "makes a resource
/// cheaper" is worth preferring is a service's policy and belongs to that service; whether a
/// structure carries an effect on a resource's cost at all is a fact about the build's authored
/// content and belongs here.
/// </para>
/// <para>
/// The property travels as the game's own name for it, which is the one departure in this table from
/// the rule that an enum travels as its integer. Two vocabularies reach this table — a resource
/// effect names a member of the game's <c>ModifiableType</c> enum, an upgradeable-object effect
/// carries an authored string from its target type's property record — and the integer of the first
/// is not a representation the second has. The name is what both actually mean, and it is what the
/// game itself compares on the second path.
/// </para>
/// </remarks>
internal readonly struct WorldEntityEffect
{
    internal WorldEntityEffect(
        Guid sourceId,
        Guid targetId,
        string property,
        BigDouble ratioAtOne,
        bool ratioKnown)
    {
        SourceId = sourceId;
        TargetId = targetId;
        Property = property;
        RatioAtOne = ratioAtOne;
        RatioKnown = ratioKnown;
    }

    /// <summary>The entity whose purchase applies the effect.</summary>
    internal Guid SourceId { get; }

    /// <summary>The entity the effect modifies.</summary>
    internal Guid TargetId { get; }

    /// <summary>The game's name for the modified property.</summary>
    internal string Property { get; }

    /// <summary>
    /// What the modifier does to a value of one — above one raises, below one lowers.
    /// </summary>
    /// <remarks>
    /// One number rather than the modifier's type and amount, because the direction is what a
    /// consumer asks about and the arithmetic that answers it is the game's, not the consumer's.
    /// </remarks>
    internal BigDouble RatioAtOne { get; }

    /// <summary>
    /// Whether <see cref="RatioAtOne"/> was computable. False when the build's modifier enum has a
    /// member this suite was not ported against, in which case the ratio is one and means nothing.
    /// </summary>
    internal bool RatioKnown { get; }
}

/// <summary>
/// Range lookup over the effect table, which is keyed by source and then target and property.
/// </summary>
/// <remarks>
/// An entity authors any number of effects, so this cannot use <see cref="WorldLookup"/> — that
/// rejects duplicate identities and duplicates are the point. Same bargain as
/// <see cref="WorldPurchaseCostLookup"/>: sorted so one source's rows are contiguous, answered by a
/// binary search plus a forward walk.
/// </remarks>
internal static class WorldEntityEffectLookup
{
    /// <summary>
    /// The half-open row range belonging to <paramref name="sourceId"/>. Both indices are zero when
    /// the entity authors no effects, which is the reading for most of the registry.
    /// </summary>
    internal static bool TryFindRange(
        PublicationTable<WorldEntityEffect> table,
        Guid sourceId,
        out int start,
        out int count)
    {
        start = 0;
        count = 0;

        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = rows[middle].SourceId.CompareTo(sourceId);
            if (comparison == 0)
            {
                found = middle;
                high = middle - 1;
                continue;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        if (found < 0) return false;

        start = found;
        while (start + count < rows.Length && rows[start + count].SourceId == sourceId) count++;
        return true;
    }
}

/// <summary>The authored effects for every entity, held where a cycle can own them.</summary>
internal sealed class WorldEntityEffectBuffer
{
    private const int InitialCapacity = 64;

    private RawEntityEffect[] _samples = new RawEntityEffect[InitialCapacity];
    private int _count;

    internal int Count => _count;

    internal ref readonly RawEntityEffect this[int index] => ref _samples[index];

    internal void Reset() => _count = 0;

    internal void Append(in RawEntityEffect sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

/// <summary>
/// Turns each collected modifier into what it does to a value of one.
/// </summary>
/// <remarks>
/// The only derivation is <c>ValueModifier.Adjust(1)</c>, which the suite already owns as
/// <see cref="GameValueModifier.Adjust"/>. Doing it here rather than in a consumer is the whole point
/// of the table: the arithmetic is the game's, so it happens once per collection on the worker
/// instead of once per candidate per cycle on the main thread.
/// </remarks>
internal sealed class WorldEntityEffectDeriver
{
    internal PublicationTable<WorldEntityEffect> Build(WorldEntityEffectBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldEntityEffect>.Empty;

        var derived = new WorldEntityEffect[buffer.Count];
        for (var index = 0; index < buffer.Count; index++)
        {
            ref readonly var sample = ref buffer[index];
            var known = Enum.IsDefined(typeof(GameValueModifierType), sample.ModifierType);
            var ratio = known
                ? new GameValueModifier(
                    (GameValueModifierType)sample.ModifierType,
                    sample.ModifierAmount).Adjust(BigDouble.One)
                : BigDouble.One;
            derived[index] = new WorldEntityEffect(
                sample.SourceId, sample.TargetId, sample.Property, ratio, known);
        }

        Array.Sort(derived, EntityEffectComparer.BySourceThenTargetThenProperty);
        return PublicationTable<WorldEntityEffect>.Create(derived, derived.Length);
    }

    private sealed class EntityEffectComparer : IComparer<WorldEntityEffect>
    {
        internal static readonly IComparer<WorldEntityEffect> BySourceThenTargetThenProperty =
            new EntityEffectComparer();

        public int Compare(WorldEntityEffect left, WorldEntityEffect right)
        {
            var bySource = left.SourceId.CompareTo(right.SourceId);
            if (bySource != 0) return bySource;
            var byTarget = left.TargetId.CompareTo(right.TargetId);
            return byTarget != 0
                ? byTarget
                : string.CompareOrdinal(left.Property, right.Property);
        }
    }
}

/// <summary>
/// Reads every structure's authored effects. A second walk of the structure registry, because a row
/// binder returns one fixed-size reading per entity and an effect list is neither.
/// </summary>
/// <remarks>
/// <para>
/// It claims no identities: its rows are keyed by an entity another category already claimed.
/// </para>
/// <para>
/// Structures only. The game reaches these through <c>PersistentEffectDeprecated</c>, which several
/// types own, but the one consumer that exists asks about what a purchase does to the economy and
/// only a structure's purchase does anything. Widening the walk before something reads it would be
/// collecting for its own sake.
/// </para>
/// </remarks>
internal sealed class WorldEntityEffectReader : IWorldCategoryReader
{
    private readonly Type? _structureType;
    private readonly string _unavailable;

    private readonly Func<object, Guid>? _structureId;
    private readonly Func<object, IList?>? _properties;
    private readonly Func<object, IList?>? _resourceEffects;
    private readonly Func<object, IList?>? _objectEffects;

    private readonly Func<object, Guid>? _resourceTarget;
    private readonly Func<object, int>? _resourceProperty;
    private readonly Func<object, int>? _resourceModifierType;
    private readonly Func<object, BigDouble>? _resourceModifierAmount;

    private readonly Func<object, Guid>? _objectTarget;
    private readonly Func<object, string>? _objectProperty;
    private readonly Func<object, bool>? _objectUsesTargetRef;
    private readonly Func<object, int>? _objectModifierType;
    private readonly Func<object, BigDouble>? _objectModifierAmount;

    /// <summary>
    /// The modifiable-property enum's members by value, read once so a row costs an index rather than
    /// a name lookup, and so a build that renumbers the enum cannot silently rename an effect.
    /// </summary>
    private readonly Dictionary<int, string> _resourcePropertyNames = new Dictionary<int, string>();

    internal WorldEntityEffectReader(Type? structureType)
    {
        _structureType = structureType;
        if (structureType is null)
        {
            _unavailable = "the StructureSO type was not found on this build";
            return;
        }

        var bind = new WorldMemberBinding(structureType, "StructureSO");
        _structureId = bind.Call<Guid>("GetGuid");
        _properties = bind.CollectionField("structureProperties");

        var propertyType = bind.CollectionElementType("structureProperties");
        _resourceEffects = NativeAccessorBinder.CollectionField(propertyType, "resourceEffects");
        _objectEffects = NativeAccessorBinder.CollectionField(propertyType, "upgradeableObjectEffects");

        var resourceEffectType =
            NativeAccessorBinder.CollectionElementType(propertyType, "resourceEffects");
        _resourceTarget = NativeAccessorBinder.ReferenceGuid(resourceEffectType, "resource");
        _resourceProperty = NativeAccessorBinder.EnumField(resourceEffectType, "upgradeType");
        _resourceModifierType =
            NativeAccessorBinder.NestedEnumField(resourceEffectType, "modifier", "type");
        _resourceModifierAmount =
            NativeAccessorBinder.NestedField<BigDouble>(resourceEffectType, "modifier", "adjustReal");
        FillPropertyNames(resourceEffectType);

        var objectEffectType =
            NativeAccessorBinder.CollectionElementType(propertyType, "upgradeableObjectEffects");
        _objectTarget = NativeAccessorBinder.ReferenceGuid(objectEffectType, "upgradeableObject");
        _objectProperty = NativeAccessorBinder.Field<string>(objectEffectType, "propertyType");
        _objectUsesTargetRef = NativeAccessorBinder.Field<bool>(objectEffectType, "useTargetRef");
        _objectModifierType =
            NativeAccessorBinder.NestedEnumField(objectEffectType, "modifier", "type");
        _objectModifierAmount =
            NativeAccessorBinder.NestedField<BigDouble>(objectEffectType, "modifier", "adjustReal");

        var missing = Missing();
        _unavailable = missing.Length == 0
            ? bind.Failure
            : $"StructureSO did not expose {missing} on this build";
    }

    public string Category => "entity effects";

    public bool IsAvailable => _structureType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        var buffer = frame.EntityEffects;
        buffer.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var structures = NativeAccessorBinder.StaticList(_structureType, "All");
        if (structures is null)
            return WorldCategoryReport.Missing(Category, "the StructureSO registry was unreadable");

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;

        for (var index = 0; index < structures.Count; index++)
        {
            var structure = structures[index];
            if (structure is null) continue;

            try
            {
                sampled += Read(structure, buffer);
            }
            catch (Exception ex)
            {
                skipped++;
                if (firstFailure.Length == 0)
                    firstFailure = $"reading an effect list threw: {ex.GetBaseException().Message}";
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private int Read(object structure, WorldEntityEffectBuffer buffer)
    {
        var sourceId = _structureId!(structure);
        if (sourceId == Guid.Empty) return 0;

        var properties = _properties!(structure);
        if (properties is null) return 0;

        var appended = 0;
        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            if (property is null) continue;

            appended += ReadResourceEffects(sourceId, property, buffer);
            appended += ReadObjectEffects(sourceId, property, buffer);
        }

        return appended;
    }

    private int ReadResourceEffects(Guid sourceId, object property, WorldEntityEffectBuffer buffer)
    {
        var effects = _resourceEffects!(property);
        if (effects is null) return 0;

        var appended = 0;
        for (var index = 0; index < effects.Count; index++)
        {
            var effect = effects[index];
            if (effect is null) continue;

            var targetId = _resourceTarget!(effect);
            if (targetId == Guid.Empty) continue;
            if (!_resourcePropertyNames.TryGetValue(_resourceProperty!(effect), out var name)) continue;

            buffer.Append(new RawEntityEffect(
                sourceId,
                targetId,
                name,
                _resourceModifierType!(effect),
                _resourceModifierAmount!(effect)));
            appended++;
        }

        return appended;
    }

    /// <summary>
    /// Reads the effects that name their target directly. One that resolves its target through a
    /// class reference instead is skipped: what it modifies is decided at apply time from the
    /// referencing object, so there is no edge here to publish.
    /// </summary>
    private int ReadObjectEffects(Guid sourceId, object property, WorldEntityEffectBuffer buffer)
    {
        var effects = _objectEffects!(property);
        if (effects is null) return 0;

        var appended = 0;
        for (var index = 0; index < effects.Count; index++)
        {
            var effect = effects[index];
            if (effect is null || _objectUsesTargetRef!(effect)) continue;

            var targetId = _objectTarget!(effect);
            if (targetId == Guid.Empty) continue;

            var name = _objectProperty!(effect);
            if (string.IsNullOrEmpty(name)) continue;

            buffer.Append(new RawEntityEffect(
                sourceId,
                targetId,
                name,
                _objectModifierType!(effect),
                _objectModifierAmount!(effect)));
            appended++;
        }

        return appended;
    }

    private void FillPropertyNames(Type? resourceEffectType)
    {
        var field = resourceEffectType?.GetField(
            "upgradeType",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        if (field is null || !field.FieldType.IsEnum) return;
        if (Enum.GetUnderlyingType(field.FieldType) != typeof(int)) return;

        var names = Enum.GetNames(field.FieldType);
        var values = (int[])Enum.GetValues(field.FieldType);
        for (var index = 0; index < names.Length; index++) _resourcePropertyNames[values[index]] = names[index];
    }

    /// <summary>The members that did not bind, named, or empty when they all did.</summary>
    private string Missing()
    {
        var missing = new List<string>();
        if (_structureId is null) missing.Add("GetGuid()");
        if (_properties is null) missing.Add("structureProperties");
        if (_resourceEffects is null) missing.Add("resourceEffects");
        if (_objectEffects is null) missing.Add("upgradeableObjectEffects");
        if (_resourceTarget is null) missing.Add("resourceEffects[].resource");
        if (_resourceProperty is null) missing.Add("resourceEffects[].upgradeType");
        if (_resourceModifierType is null) missing.Add("resourceEffects[].modifier.type");
        if (_resourceModifierAmount is null) missing.Add("resourceEffects[].modifier.adjustReal");
        if (_resourcePropertyNames.Count == 0) missing.Add("the modifiable-property names");
        if (_objectTarget is null) missing.Add("upgradeableObjectEffects[].upgradeableObject");
        if (_objectProperty is null) missing.Add("upgradeableObjectEffects[].propertyType");
        if (_objectUsesTargetRef is null) missing.Add("upgradeableObjectEffects[].useTargetRef");
        if (_objectModifierType is null) missing.Add("upgradeableObjectEffects[].modifier.type");
        if (_objectModifierAmount is null) missing.Add("upgradeableObjectEffects[].modifier.adjustReal");
        return string.Join(", ", missing);
    }
}
