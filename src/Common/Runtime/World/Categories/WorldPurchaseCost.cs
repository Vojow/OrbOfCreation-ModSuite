using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>One authored cost entry, as read: which entity, which resource, how much before scaling.</summary>
internal readonly struct RawPurchaseCost
{
    internal RawPurchaseCost(Guid entityId, Guid resourceId, BigDouble baseValue)
    {
        EntityId = entityId;
        ResourceId = resourceId;
        BaseValue = baseValue;
    }

    internal Guid EntityId { get; }
    internal Guid ResourceId { get; }
    internal BigDouble BaseValue { get; }
}

/// <summary>
/// What the next level and the current native purchase group cost in one resource, computed rather
/// than asked for.
/// </summary>
/// <remarks>
/// This is what `GetPurchaseCost()` returns, without the call. The game's own implementation rebuilds
/// the whole cost list from scratch on every ask — six LINQ projections and six allocations per
/// candidate — and Auto Buy asks four hundred times a cycle. Owning the chain means computing the
/// next level and the bounded succession a native group may buy once per entity per collection, on
/// the worker. The grouped amount is a sum of independently priced and rounded levels, never a
/// multiplication of the first price.
/// </remarks>
internal readonly struct WorldPurchaseCost
{
    internal WorldPurchaseCost(Guid entityId, Guid resourceId, BigDouble amount)
        : this(entityId, resourceId, amount, exactGroupedLevels: 1, amount)
    {
    }

    internal WorldPurchaseCost(
        Guid entityId,
        Guid resourceId,
        BigDouble amount,
        int exactGroupedLevels,
        BigDouble exactGroupedAmount)
    {
        EntityId = entityId;
        ResourceId = resourceId;
        Amount = amount;
        ExactGroupedLevels = exactGroupedLevels;
        ExactGroupedAmount = exactGroupedAmount;
    }

    /// <summary>The structure or upgrade being priced.</summary>
    internal Guid EntityId { get; }

    /// <summary>The resource this part of the price is paid in.</summary>
    internal Guid ResourceId { get; }

    internal BigDouble Amount { get; }

    /// <summary>The live native group size priced through the complete rising cost curve.</summary>
    internal int ExactGroupedLevels { get; }

    /// <summary>
    /// The exact sum charged for <see cref="ExactGroupedLevels"/> successive levels, including each
    /// level's own rounding. For a bounded upgrade this includes only levels remaining below its cap.
    /// </summary>
    internal BigDouble ExactGroupedAmount { get; }
}

/// <summary>The shared bound and lookup for native purchase-group counts.</summary>
internal static class WorldPurchaseGrouping
{
    internal const int MaximumLevels = 100;

    internal static int Read(
        PublicationTable<WorldNumberVariable> variables,
        Guid variableId)
    {
        if (!WorldLookup.TryFind(variables, variableId, out var variable))
            return 1;

        return Math.Max(1, Math.Min(MaximumLevels, (int)variable.Value.ToDouble()));
    }
}

/// <summary>
/// Range lookup over the purchase-cost table, which is keyed by entity and then resource.
/// </summary>
/// <remarks>
/// A cost is the one published fact that is not one row per entity, so it cannot use
/// <see cref="WorldLookup"/> — that rejects duplicate identities, and duplicates are the point here.
/// The table is sorted by (entity, resource) instead, so an entity's entries are contiguous and a
/// binary search for its first row plus a forward walk answers "what does this cost" in
/// <c>O(log n + k)</c>.
/// </remarks>
internal static class WorldPurchaseCostLookup
{
    /// <summary>
    /// The half-open row range belonging to <paramref name="entityId"/>. Both indices are zero when
    /// the entity has no published cost, which is the honest reading of an entity whose cost could
    /// not be collected — not a cost of nothing.
    /// </summary>
    internal static bool TryFindRange(
        PublicationTable<WorldPurchaseCost> table,
        Guid entityId,
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
            var comparison = rows[middle].EntityId.CompareTo(entityId);
            if (comparison == 0)
            {
                // Keep going left: the search must land on the entity's *first* row, or the forward
                // walk below starts in the middle of the range and reports a partial cost.
                found = middle;
                high = middle - 1;
                continue;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        if (found < 0) return false;

        start = found;
        while (start + count < rows.Length && rows[start + count].EntityId == entityId) count++;
        return true;
    }
}

/// <summary>
/// The authored cost entries for every entity, held where a cycle can own them.
/// </summary>
/// <remarks>
/// Not a <see cref="WorldSampleBuffer{TSample, TRow}"/>, because that one holds one row per entity
/// and refuses duplicates. Everything else about it is the same bargain: reused across cycles, grown
/// by doubling, and read only by the deriver.
/// <para>
/// It holds readings and nothing else. The derive scratch lives on
/// <see cref="WorldPurchaseCostDeriver"/> instead, because one of those scratch arrays is a
/// <c>BigDouble[]</c> — and an array of a game value type is a runtime boundary a service frame may
/// not hold, however audited the element type is on its own.
/// </para>
/// </remarks>
internal sealed class WorldPurchaseCostBuffer
{
    private const int InitialCapacity = 64;

    private RawPurchaseCost[] _samples = new RawPurchaseCost[InitialCapacity];
    private int _count;

    internal int Count => _count;

    internal ref readonly RawPurchaseCost this[int index] => ref _samples[index];

    internal void Reset() => _count = 0;

    internal void Append(in RawPurchaseCost sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

/// <summary>
/// Runs the two ported cost chains over every entity's collected entries.
/// </summary>
/// <remarks>
/// <para>
/// Cross-table by necessity. A structure needs its own modifiers, the per-resource attribute-cost
/// modifier, the per-quantity modifier out of the global registry and the frame-wide structure-cost
/// global; an upgrade needs its level, its ceiling and the per-level modifier list collected
/// alongside. That is why this is not a <see cref="WorldRowDeriver{TSample, TRow}"/>, which sees one
/// buffer.
/// </para>
/// <para>
/// Built per cycle, like <see cref="WorldResourceDeriver"/> and for the same reason: it closes over
/// that cycle's globals, and a shared instance would let the next collection rewrite terms a worker
/// is still deriving against. Its scratch is therefore allocated per cycle too — a handful of small
/// arrays against the one published table W1 already accepts.
/// </para>
/// </remarks>
internal sealed class WorldPurchaseCostDeriver
{
    private readonly PublicationTable<WorldStructure> _structures;
    private readonly PublicationTable<WorldUpgrade> _upgrades;
    private readonly PublicationTable<WorldResource> _resources;
    private readonly PublicationTable<WorldModifierVariable> _modifiers;
    private readonly WorldLevelCostModifierBuffer _levelModifiers;
    private readonly WorldFrameGlobals _globals;
    private readonly int _structureGroupedLevels;

    private GameResourceCost[] _scratch = new GameResourceCost[8];
    private BigDouble[] _attributeMods = new BigDouble[8];
    private BigDouble[] _nextAmounts = new BigDouble[8];
    private BigDouble[] _groupedAmounts = new BigDouble[8];
    private GameValueModifier[] _perLevel = new GameValueModifier[8];
    private GameValueModifier[] _perLevelExponents = new GameValueModifier[8];
    private GameValueModifier[] _scaled = new GameValueModifier[8];
    private GameValueModifier[] _scaledExponents = new GameValueModifier[8];
    private GameValueModifier[] _combineScratch = new GameValueModifier[8];

    internal WorldPurchaseCostDeriver(
        PublicationTable<WorldStructure> structures,
        PublicationTable<WorldUpgrade> upgrades,
        PublicationTable<WorldResource> resources,
        PublicationTable<WorldModifierVariable> modifiers,
        WorldLevelCostModifierBuffer levelModifiers,
        in WorldFrameGlobals globals,
        int structureGroupedLevels)
    {
        _structures = structures ?? throw new ArgumentNullException(nameof(structures));
        _upgrades = upgrades ?? throw new ArgumentNullException(nameof(upgrades));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _modifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers));
        _levelModifiers = levelModifiers ?? throw new ArgumentNullException(nameof(levelModifiers));
        _globals = globals;
        _structureGroupedLevels = Math.Max(
            1, Math.Min(WorldPurchaseGrouping.MaximumLevels, structureGroupedLevels));
    }

    /// <summary>
    /// Prices every entity in <paramref name="buffer"/>, publishing the results sorted by entity and
    /// then resource.
    /// </summary>
    /// <remarks>
    /// An entity whose chain cannot be completed publishes no cost at all rather than a cost computed
    /// without one of its terms. A wrong price is worse than an absent one: a consumer can see the
    /// absence and fall back, and cannot see a silently-cheap candidate.
    /// </remarks>
    internal PublicationTable<WorldPurchaseCost> Build(WorldPurchaseCostBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldPurchaseCost>.Empty;

        var derived = new WorldPurchaseCost[buffer.Count];
        var written = 0;
        var index = 0;
        while (index < buffer.Count)
        {
            // Entries for one entity are contiguous: the reader appends them as it walks that
            // entity's own cost list, and never interleaves two entities.
            var entityId = buffer[index].EntityId;
            var end = index;
            while (end < buffer.Count && buffer[end].EntityId == entityId) end++;

            var length = end - index;
            if (TryComputeEntity(buffer, entityId, index, length, out var groupedLevels))
            {
                for (var offset = 0; offset < length; offset++)
                {
                    derived[written++] = new WorldPurchaseCost(
                        entityId,
                        buffer[index + offset].ResourceId,
                        _nextAmounts[offset],
                        groupedLevels,
                        _groupedAmounts[offset]);
                }
            }

            index = end;
        }

        if (written == 0) return PublicationTable<WorldPurchaseCost>.Empty;

        Array.Sort(derived, 0, written, PurchaseCostComparer.ByEntityThenResource);
        return PublicationTable<WorldPurchaseCost>.Create(derived, written);
    }

    /// <summary>
    /// Prices one entity, whichever kind it is. The two chains share nothing but their inputs' shape,
    /// so the only thing dispatched on here is which registry the identity belongs to.
    /// </summary>
    private bool TryComputeEntity(
        WorldPurchaseCostBuffer buffer,
        Guid entityId,
        int start,
        int length,
        out int groupedLevels)
    {
        if (WorldLookup.TryFind(_structures, entityId, out var structure))
        {
            groupedLevels = _structureGroupedLevels;
            return TryFillAttributeMods(buffer, start, length) &&
                TryPriceStructure(buffer, start, in structure, length, groupedLevels);
        }

        groupedLevels = 1;
        return WorldLookup.TryFind(_upgrades, entityId, out var upgrade) &&
            TryPriceUpgrade(buffer, start, in upgrade, entityId, length, groupedLevels);
    }

    private bool TryPriceStructure(
        WorldPurchaseCostBuffer buffer,
        int start,
        in WorldStructure structure,
        int length,
        int groupedLevels)
    {
        var reading = structure.Reading;
        if (!WorldLookup.TryFind(_modifiers, reading.CostPerQuantityId, out var variable)) return false;

        var costPerQuantity = AsModifier(in variable);
        if (costPerQuantity is not { } modifier) return false;

        var committed = reading.Level + reading.QueuedLevels;
        ClearGroupedAmounts(length);
        for (var level = 0; level < groupedLevels; level++)
        {
            FillCosts(buffer, start, length);
            var levelCommitted = committed + level;
            var nextCostMod = GameCostMath.ComputeNextCostMod(
                reading.Modifiers.PassiveCostMod,
                reading.Modifiers.ActiveCostMod,
                in modifier,
                levelCommitted,
                _globals.StructureCostPercent);

            GameCostMath.ComputeNextCost(
                new Span<GameResourceCost>(_scratch, 0, length),
                new ReadOnlySpan<BigDouble>(_attributeMods, 0, length),
                in modifier,
                OrbGameMath.AsPercent(reading.Modifiers.CostScalingMod),
                levelCommitted,
                OrbGameMath.AsPercent(nextCostMod));
            Accumulate(level, length);
        }

        return true;
    }

    /// <summary>
    /// Prices one upgrade, which grows by a modifier list rather than by a single modifier and rounds
    /// with <c>RoundToTwoSigs</c> rather than the <c>…Early</c> variant.
    /// </summary>
    /// <remarks>
    /// An upgrade with no per-level modifiers still prices: <c>SetToLevel</c>'s scaling branch simply
    /// does not run, and the authored cost is published rounded. That is the game's own behaviour for
    /// a flat-cost upgrade, not a degraded reading, so it is not treated as a failure.
    /// </remarks>
    private bool TryPriceUpgrade(
        WorldPurchaseCostBuffer buffer,
        int start,
        in WorldUpgrade upgrade,
        Guid entityId,
        int length,
        int groupedLevels)
    {
        var committed = upgrade.CommittedLevel;

        // The game caps the level it prices at one below the maximum for a finite upgrade, so a
        // maxed-out upgrade keeps quoting the last level's price rather than one that cannot be
        // bought. HasFiniteLevels() is maxLevel > 0.
        if (upgrade.IsBounded && committed > upgrade.Reading.MaxLevel - 1)
            committed = upgrade.Reading.MaxLevel - 1;
        if (committed < 0) committed = 0;

        var modifiers = FillLevelModifiers(entityId, out var exponents);
        Grow(ref _scaled, modifiers);
        Grow(ref _scaledExponents, exponents);
        Grow(ref _combineScratch, modifiers);

        var pricedLevels = upgrade.IsBounded
            ? Math.Min(groupedLevels, upgrade.RemainingLevels)
            : groupedLevels;
        ClearGroupedAmounts(length);
        for (var level = 0; level < Math.Max(1, pricedLevels); level++)
        {
            FillCosts(buffer, start, length);
            GameCostMath.ComputeLeveledCost(
                new Span<GameResourceCost>(_scratch, 0, length),
                new ReadOnlySpan<GameValueModifier>(_perLevel, 0, modifiers),
                new ReadOnlySpan<GameValueModifier>(_perLevelExponents, 0, exponents),
                committed + level + 1,
                new Span<GameValueModifier>(_scaled, 0, modifiers),
                new Span<GameValueModifier>(_scaledExponents, 0, exponents),
                new Span<GameValueModifier>(_combineScratch, 0, modifiers));
            Accumulate(level, length);
        }

        if (pricedLevels == 0)
            Array.Clear(_groupedAmounts, 0, length);
        return true;
    }

    private void ClearGroupedAmounts(int length)
    {
        if (_nextAmounts.Length < length) _nextAmounts = new BigDouble[length];
        if (_groupedAmounts.Length < length) _groupedAmounts = new BigDouble[length];
        Array.Clear(_groupedAmounts, 0, length);
    }

    private void Accumulate(int level, int length)
    {
        for (var index = 0; index < length; index++)
        {
            var amount = _scratch[index].Value;
            if (level == 0) _nextAmounts[index] = amount;
            _groupedAmounts[index] += amount;
        }
    }

    /// <summary>
    /// Copies one upgrade's per-level modifiers into scratch, splitting the two lists apart. Entries
    /// whose type this suite was not ported against are dropped rather than defaulted, for the same
    /// reason <see cref="AsModifier"/> fails closed.
    /// </summary>
    private int FillLevelModifiers(Guid entityId, out int exponents)
    {
        var modifiers = 0;
        exponents = 0;

        for (var index = 0; index < _levelModifiers.Count; index++)
        {
            ref readonly var entry = ref _levelModifiers[index];
            if (entry.EntityId != entityId) continue;
            if (!Enum.IsDefined(typeof(GameValueModifierType), entry.ModifierType)) continue;

            var modifier = new GameValueModifier(
                (GameValueModifierType)entry.ModifierType, entry.Amount, entry.Order);
            if (entry.IsExponent)
            {
                Grow(ref _perLevelExponents, exponents + 1);
                _perLevelExponents[exponents++] = modifier;
            }
            else
            {
                Grow(ref _perLevel, modifiers + 1);
                _perLevel[modifiers++] = modifier;
            }
        }

        return modifiers;
    }

    private static void Grow(ref GameValueModifier[] buffer, int required)
    {
        if (buffer.Length < required) Array.Resize(ref buffer, Math.Max(required, buffer.Length * 2));
    }

    /// <summary>Copies one entity's authored entries into the arithmetic scratch.</summary>
    private void FillCosts(WorldPurchaseCostBuffer buffer, int start, int length)
    {
        if (_scratch.Length < length)
        {
            _scratch = new GameResourceCost[length];
            _attributeMods = new BigDouble[length];
            _nextAmounts = new BigDouble[length];
            _groupedAmounts = new BigDouble[length];
        }

        for (var offset = 0; offset < length; offset++)
        {
            ref readonly var entry = ref buffer[start + offset];
            _scratch[offset] = new GameResourceCost(entry.ResourceId, entry.BaseValue);
        }
    }

    /// <summary>
    /// Resolves the per-resource attribute-cost modifier the structure chain multiplies by, the way
    /// the game resolves it. Upgrades do not go through it at all — their chain is
    /// <c>SetToLevel</c>, which never adjusts as an attribute — so this is not a shared precondition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The modifier is not the record. <c>ResourceSO.GetAttributeCostMod()</c> is
    /// </para>
    /// <code>
    /// attributeCostMod / BigDouble.Pow(quality.AsPercent(), Player.GetAttributeQualityBonus())
    /// </code>
    /// <para>
    /// — a quality discount divided out of the authored record — and
    /// <c>ResourceCostList.AdjustAsAttribute()</c> multiplies each entry by that quotient's percent,
    /// not by the record's. Reading the numerator alone published every structure's price times the
    /// discount the player had already earned, which on a developed save is a factor of 1e133. The
    /// bonus is zero before any research grants it, so the two readings agree on a fresh game and
    /// diverge without bound on an old one.
    /// </para>
    /// <para>
    /// Both zeroes withhold the price rather than publishing one. A zero <c>attributeCostMod</c> is
    /// not a free resource: the authored value is 100 — parity — so a zero is a reading this chain
    /// cannot price honestly, and multiplying by it would make the whole entity free. A zero quality
    /// is the same refusal from the other side: it is the base of the power, so it divides the price
    /// by nothing and publishes an infinity. The memo rule removed the reading that manufactured such
    /// a zero, since a never-calculated record the game will recompute now folds rather than reading
    /// as the zero it deserialises to; the guard stays because a zero the game itself holds is still
    /// a zero this chain must not multiply by. See W5.
    /// </para>
    /// </remarks>
    private bool TryFillAttributeMods(WorldPurchaseCostBuffer buffer, int start, int length)
    {
        for (var offset = 0; offset < length; offset++)
        {
            ref readonly var entry = ref buffer[start + offset];
            if (!WorldLookup.TryFind(_resources, entry.ResourceId, out var resource)) return false;

            var attributeCostMod = resource.Reading.Modifiers.AttributeCostMod;
            if (attributeCostMod == BigDouble.Zero) return false;

            var quality = resource.Reading.Quality;
            if (quality == BigDouble.Zero) return false;

            // AsPercent wraps the quotient, not the numerator: the original applies it to what
            // GetAttributeCostMod() returns, and AsPercent shifts an exponent rather than dividing,
            // so moving it inside would not be the same number.
            var qualityDiscount = BigDouble.Pow(
                OrbGameMath.AsPercent(quality), _globals.AttributeQualityBonus);
            if (qualityDiscount == BigDouble.Zero) return false;

            _attributeMods[offset] = OrbGameMath.AsPercent(attributeCostMod / qualityDiscount);
        }

        return true;
    }

    /// <summary>
    /// The published modifier as the arithmetic type, or <see langword="null"/> when the build's enum
    /// has a member this suite was not ported against.
    /// </summary>
    /// <remarks>
    /// Failing closed rather than defaulting to <c>Raw</c>: an unknown member means the modifier does
    /// something the port does not model, and treating it as addition would price the entity
    /// confidently and wrongly.
    /// </remarks>
    private static GameValueModifier? AsModifier(in WorldModifierVariable variable) =>
        Enum.IsDefined(typeof(GameValueModifierType), variable.ModifierType)
            ? new GameValueModifier(
                (GameValueModifierType)variable.ModifierType, variable.Amount, variable.Order)
            : null;

    private sealed class PurchaseCostComparer : IComparer<WorldPurchaseCost>
    {
        internal static readonly IComparer<WorldPurchaseCost> ByEntityThenResource =
            new PurchaseCostComparer();

        public int Compare(WorldPurchaseCost left, WorldPurchaseCost right)
        {
            var byEntity = left.EntityId.CompareTo(right.EntityId);
            return byEntity != 0 ? byEntity : left.ResourceId.CompareTo(right.ResourceId);
        }
    }
}

/// <summary>
/// Reads every structure's authored cost list. A second walk of the structure registry, because a
/// row binder returns one fixed-size reading per entity and a cost list is neither.
/// </summary>
/// <remarks>
/// <para>
/// It claims no identities: its rows are keyed by an entity another category already claimed, and
/// claiming again would report every structure as a duplicate of itself.
/// </para>
/// <para>
/// It shares <c>frame.PurchaseCosts</c> with <see cref="WorldUpgradeCostReader"/> and does not reset
/// it — the collector does that once, before either runs, because a reader that reset a buffer it
/// shares would silently discard the other's rows depending on traversal order.
/// </para>
/// </remarks>
internal sealed class WorldPurchaseCostReader : IWorldCategoryReader
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _structureType;
    private readonly string _unavailable;

    private readonly Func<object, object?>? _baseCost;
    private readonly Func<object, Guid>? _structureId;
    private readonly Func<object, IList?>? _entries;
    private readonly Func<object, Guid>? _entryResource;
    private readonly Func<object, BigDouble>? _entryValue;

    internal WorldPurchaseCostReader(Type? structureType, Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));

        _structureType = structureType;
        if (structureType is null)
        {
            _unavailable = "the StructureSO type was not found on this build";
            return;
        }

        var bind = new WorldMemberBinding(structureType, "StructureSO");
        _structureId = bind.Call<Guid>("GetGuid");
        _baseCost = NativeAccessorBinder.Reference(structureType, "baseCost");

        var costListType = structureType.GetField("baseCost", Instance)?.FieldType;
        var entryType = NativeAccessorBinder.CollectionElementType(costListType, "costs");

        _entries = NativeAccessorBinder.CollectionField(costListType, "costs");
        _entryResource = NativeAccessorBinder.ReferenceGuid(entryType, "resource");

        // valueBig, not the serialized `value` double: the game keeps the magnitude in the BigDouble
        // and the double is only what Unity writes to disk.
        _entryValue = NativeAccessorBinder.Field<BigDouble>(entryType, "valueBig");

        _unavailable = _baseCost is null || _structureId is null || _entries is null ||
            _entryResource is null || _entryValue is null
            ? "StructureSO did not expose its authored cost list on this build"
            : bind.Failure;
    }

    public string Category => "structure costs";

    public bool IsAvailable => _structureType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var buffer = frame.PurchaseCosts;
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
                    firstFailure = $"reading a cost list threw: {ex.GetBaseException().Message}";
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private int Read(object structure, WorldPurchaseCostBuffer buffer)
    {
        var entityId = _structureId!(structure);
        if (entityId == Guid.Empty) return 0;

        var costList = _baseCost!(structure);
        if (costList is null) return 0;

        var entries = _entries!(costList);
        if (entries is null) return 0;

        var appended = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null) continue;

            var resourceId = _entryResource!(entry);
            if (resourceId == Guid.Empty) continue;

            buffer.Append(new RawPurchaseCost(entityId, resourceId, _entryValue!(entry)));
            appended++;
        }

        return appended;
    }
}
