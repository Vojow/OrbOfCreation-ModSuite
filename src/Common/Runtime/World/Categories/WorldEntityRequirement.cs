using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>Which kind of entity a requirement row belongs to.</summary>
/// <remarks>
/// The kind travels because the level a per-level container is checked at is a property of the owner,
/// not of the condition: an upgrade asks about <c>level + queuedLevels + 1</c> and a structure asks
/// about <c>quantity</c>. A consumer that only knew the identity would have to guess which, or search
/// both registries to find out.
/// </remarks>
internal enum WorldRequirementOwnerKind
{
    /// <summary>The owner's registry could not be established. No consumer may treat this as met.</summary>
    Unknown = 0,
    Upgrade = 1,
    Structure = 2,
    AlchemyRecipe = 3,
}

internal enum WorldRequirementProgramKind
{
    NextLevel = 0,
    Usage = 1,
}

/// <summary>How the leaves at one top-level container position combine.</summary>
internal enum WorldRequirementGroupKind
{
    /// <summary>A leaf or an explicit AndRequirement: every row must hold.</summary>
    All = 0,

    /// <summary>An explicit OrRequirement: one row must hold.</summary>
    Any = 1,
}

/// <summary>
/// Which of the game's condition classes a requirement row was read from, as this suite models them.
/// </summary>
/// <remarks>
/// The game distinguishes conditions by class rather than by a discriminator field, so this is a
/// suite-owned classification of the runtime type name rather than a mirror of a game enum. Anything
/// this suite has not been audited against reads as <see cref="Unknown"/>, which is the whole point:
/// an unmodelled condition must make a candidate inadmissible rather than silently absent.
/// </remarks>
internal enum WorldRequirementConditionKind
{
    /// <summary>
    /// A condition class this build has that this suite does not model, or one whose members did not
    /// bind. The row still carries its type name so an operator can name what was found.
    /// </summary>
    Unknown = 0,
    Upgrade = 1,
    Research = 2,
    Structure = 3,
    Spell = 4,
    AlchemyRecipe = 5,
    Ritual = 6,
    Number = 7,
    Generic = 8,

    /// <summary>An authored empty composite's exact Any/All identity value.</summary>
    Literal = 9,
}

/// <summary>
/// One of the two per-level modifiers a requirement threshold scales by, as read.
/// </summary>
/// <remarks>
/// The game's <c>LeveledValue</c> is <c>baseValue</c> plus two <c>ValueModifier</c>s, and the modifier
/// is a plain authored struct — its <c>gc</c> is its own identity, not a pointer at a live variable —
/// so the threshold is a pure function of authored data and the level being bought. That is why these
/// travel as values rather than as an identity into the global modifier registry.
/// </remarks>
internal readonly struct WorldRequirementScaling
{
    internal WorldRequirementScaling(int modifierType, BigDouble amount, int order)
    {
        ModifierType = modifierType;
        Amount = amount;
        Order = order;
    }

    /// <summary>The game's <c>ValueModifierType</c>, as its underlying integer. See D17.</summary>
    internal int ModifierType { get; }

    /// <summary>The original's <c>adjustReal</c>.</summary>
    internal BigDouble Amount { get; }

    internal int Order { get; }
}

/// <summary>
/// One authored condition on one entity's per-level purchase, described by what it compares rather
/// than by whether it currently holds.
/// </summary>
/// <remarks>
/// <para>
/// The game asks <c>prerequisitesPerLevel.Check(level)</c>, which takes a level argument and so cannot
/// be published as a latched boolean the way the whole-entity container's <c>available</c> can. What
/// can be published is the container's contents: which entity is looked at, which comparison is made,
/// and what the threshold is at a given level. Everything the comparison reads is already a published
/// row, so the verdict is arithmetic a worker can do for itself.
/// </para>
/// <para>
/// Rows are facts, not a verdict. A consumer deciding "can I buy one more of this now" wants the
/// boolean; a consumer planning a chain wants to know that this upgrade is waiting on that research
/// reaching level six, which is a fact only the rows carry.
/// </para>
/// <para>
/// The runtime type name travels beside the modelled kind for the same reason it does on
/// <see cref="WorldEffectBlock"/>: when the kind is <see cref="WorldRequirementConditionKind.Unknown"/>
/// the name is the only thing that lets anyone say <em>what</em> was not modelled.
/// </para>
/// </remarks>
internal readonly struct WorldEntityRequirement
{
    internal WorldEntityRequirement(
        Guid ownerId,
        WorldRequirementOwnerKind ownerKind,
        int ordinal,
        WorldRequirementConditionKind kind,
        string conditionTypeName,
        Guid targetId,
        int reqType,
        double baseValue,
        in WorldRequirementScaling perLevel,
        in WorldRequirementScaling modPerLevel,
        WorldRequirementProgramKind program = WorldRequirementProgramKind.NextLevel,
        WorldRequirementGroupKind groupKind = WorldRequirementGroupKind.All,
        int groupOrdinal = -1)
    {
        OwnerId = ownerId;
        OwnerKind = ownerKind;
        Ordinal = ordinal;
        Kind = kind;
        ConditionTypeName = conditionTypeName;
        TargetId = targetId;
        ReqType = reqType;
        BaseValue = baseValue;
        PerLevel = perLevel;
        ModPerLevel = modPerLevel;
        Program = program;
        GroupKind = groupKind;
        GroupOrdinal = groupOrdinal < 0 ? ordinal : groupOrdinal;
    }

    /// <summary>The entity whose next level this condition gates.</summary>
    internal Guid OwnerId { get; }

    internal WorldRequirementOwnerKind OwnerKind { get; }
    internal WorldRequirementProgramKind Program { get; }

    /// <summary>The native fold for the leaves at <see cref="GroupOrdinal"/>.</summary>
    internal WorldRequirementGroupKind GroupKind { get; }

    /// <summary>
    /// The top-level container position this leaf belongs to. The container ANDs positions; rows
    /// sharing one position are the children of an explicit Or/And composite.
    /// </summary>
    internal int GroupOrdinal { get; }

    /// <summary>The condition's position in its owner's container.</summary>
    internal int Ordinal { get; }

    internal WorldRequirementConditionKind Kind { get; }

    /// <summary>The condition's runtime class name, as the game names it.</summary>
    internal string ConditionTypeName { get; }

    /// <summary>
    /// The entity the condition looks at, or <see cref="Guid.Empty"/> when the condition names none —
    /// which for a modelled kind means the reference was never authored.
    /// </summary>
    internal Guid TargetId { get; }

    /// <summary>
    /// Which comparison the condition makes, as the game's own enum integer. Its meaning depends on
    /// <see cref="Kind"/>: the same integer names a different comparison for each condition class.
    /// </summary>
    internal int ReqType { get; }

    /// <summary>The authored threshold before any per-level scaling.</summary>
    internal double BaseValue { get; }

    /// <summary>How the threshold grows with the level being bought.</summary>
    internal WorldRequirementScaling PerLevel { get; }

    /// <summary>How <see cref="PerLevel"/> itself grows, for a threshold that accelerates.</summary>
    internal WorldRequirementScaling ModPerLevel { get; }
}

/// <summary>
/// Range lookup over the requirement table, which is keyed by owner and then position.
/// </summary>
/// <remarks>
/// <see cref="WorldPurchaseCostLookup"/>'s shape and for the same reason: a condition is not an
/// entity, an owner authors several, and <see cref="WorldLookup"/> refuses duplicate identities.
/// <para>
/// A miss means the owner authored no per-level conditions, which is the overwhelmingly common case
/// and is not a degraded reading — an empty container's <c>Check</c> passes unconditionally.
/// </para>
/// </remarks>
internal static class WorldEntityRequirementLookup
{
    internal static bool TryFindRange(
        PublicationTable<WorldEntityRequirement> table,
        Guid ownerId,
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
            var comparison = rows[middle].OwnerId.CompareTo(ownerId);
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
        while (start + count < rows.Length && rows[start + count].OwnerId == ownerId) count++;
        return true;
    }
}

/// <summary>Every authored per-level condition as read, held where a cycle can own them.</summary>
internal sealed class WorldEntityRequirementBuffer
{
    private const int InitialCapacity = 32;

    private WorldEntityRequirement[] _samples = new WorldEntityRequirement[InitialCapacity];
    private int _count;

    internal int Count => _count;

    internal ref readonly WorldEntityRequirement this[int index] => ref _samples[index];

    internal void Reset() => _count = 0;

    internal void Append(in WorldEntityRequirement sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

/// <summary>Publishes the requirement readings, sorted by owner and then position.</summary>
internal static class WorldEntityRequirementDeriver
{
    internal static PublicationTable<WorldEntityRequirement> Build(WorldEntityRequirementBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldEntityRequirement>.Empty;

        var derived = new WorldEntityRequirement[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) derived[index] = buffer[index];

        Array.Sort(derived, 0, derived.Length, RequirementComparer.ByOwnerThenOrdinal);
        return PublicationTable<WorldEntityRequirement>.Create(derived, derived.Length);
    }

    private sealed class RequirementComparer : IComparer<WorldEntityRequirement>
    {
        internal static readonly IComparer<WorldEntityRequirement> ByOwnerThenOrdinal =
            new RequirementComparer();

        public int Compare(WorldEntityRequirement left, WorldEntityRequirement right)
        {
            var byOwner = left.OwnerId.CompareTo(right.OwnerId);
            return byOwner != 0 ? byOwner : left.Ordinal.CompareTo(right.Ordinal);
        }
    }
}

/// <summary>
/// Reads every upgrade's and every structure's per-level prerequisite container.
/// </summary>
/// <remarks>
/// <para>
/// It claims no identities: the rows are keyed by an entity its own category already claimed, and
/// claiming again would report every upgrade as a duplicate of itself.
/// </para>
/// <para>
/// The container's list is <c>[SerializeReference]</c>, so its entries are of whatever class the
/// author picked and there is no element type to bind against. Accessors are therefore compiled per
/// concrete runtime class, on first sight, and kept — which costs one binding pass per condition class
/// per game session rather than per entity. A class whose members do not bind yields a row of kind
/// <see cref="WorldRequirementConditionKind.Unknown"/> rather than none, so an unmodelled condition is
/// visible as a requirement nobody can evaluate instead of as an entity with no requirements.
/// </para>
/// <para>
/// Every read here is a field load. The game's own answer — <c>Container.Check(ConditionInfo)</c> —
/// is not called, and neither is any condition's <c>IsValid</c>: the <c>Visible</c> and
/// <c>Available</c> comparisons reach the no-argument <c>Prerequisites.Container.Check()</c>, which
/// stamps a game id and latches <c>available</c>. That is a write, and collection does not write.
/// See W58.
/// </para>
/// </remarks>
internal sealed class WorldEntityRequirementReader : IWorldCategoryReader
{
    private readonly Type? _upgradeType;
    private readonly Type? _structureType;
    private readonly Type? _alchemyType;
    private readonly string _unavailable;

    private readonly Func<object, Guid>? _upgradeId;
    private readonly Func<object, object?>? _upgradeContainer;
    private readonly Func<object, Guid>? _structureId;
    private readonly Func<object, object?>? _structureContainer;
    private readonly Func<object, object?>? _alchemyUsageContainer;
    private readonly Func<object, IList?>? _conditions;
    private readonly Func<object, IList?>? _alchemyConditions;

    /// <summary>
    /// One compiled accessor set per condition class seen so far. Not on the frame: these are
    /// delegates, and a frame crosses to a worker.
    /// </summary>
    private readonly Dictionary<Type, ConditionAccessors> _accessors = new();

    internal WorldEntityRequirementReader(Type? upgradeType, Type? structureType, Type? alchemyType)
    {
        _upgradeType = upgradeType;
        _structureType = structureType;
        _alchemyType = alchemyType;
        if (upgradeType is null || structureType is null || alchemyType is null)
        {
            _unavailable = upgradeType is null
                ? "the UpgradeSO type was not found on this build"
                : structureType is null
                    ? "the StructureSO type was not found on this build"
                    : "the AlchemyRecipeSO type was not found on this build";
            return;
        }

        var upgrade = new WorldMemberBinding(upgradeType, "UpgradeSO");
        _upgradeId = upgrade.Call<Guid>("GetGuid");
        _upgradeContainer = NativeAccessorBinder.Reference(upgradeType, "prerequisitesPerLevel");

        var structure = new WorldMemberBinding(structureType, "StructureSO");
        _structureId = structure.Call<Guid>("GetGuid");
        _structureContainer = NativeAccessorBinder.Reference(structureType, "prerequisitesPerLevel");
        _alchemyUsageContainer = NativeAccessorBinder.Reference(alchemyType, "usagePrerequisites");

        // Both owners hold the same container type, so the list accessor is bound once against
        // whichever of them declared it rather than twice against two names for one shape.
        var containerType = ContainerTypeOf(upgradeType) ?? ContainerTypeOf(structureType);
        _conditions = NativeAccessorBinder.CollectionField(containerType, "prerequisites");
        var alchemyContainerType = alchemyType.GetField("usagePrerequisites", Instance)?.FieldType;
        _alchemyConditions = NativeAccessorBinder.CollectionField(alchemyContainerType, "prerequisites");

        if (_upgradeContainer is null || _structureContainer is null ||
            _alchemyUsageContainer is null || _conditions is null || _alchemyConditions is null)
        {
            _unavailable = "UpgradeSO and StructureSO did not expose a per-level prerequisite " +
                "container on this build";
            return;
        }

        _unavailable = upgrade.Failure.Length > 0 ? upgrade.Failure : structure.Failure;
    }

    public string Category => "entity requirements";

    public bool IsAvailable =>
        _upgradeType is not null && _structureType is not null && _alchemyType is not null &&
        _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var buffer = frame.EntityRequirements;
        buffer.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var sampled = 0;
        var unmodelled = 0;
        var firstFailure = string.Empty;

        Walk(
            NativeAccessorBinder.StaticList(_upgradeType, "All"),
            WorldRequirementOwnerKind.Upgrade,
            _upgradeId!,
            _upgradeContainer!,
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure);
        WalkKnownIds(
            NativeAccessorBinder.StaticList(_alchemyType, "All"),
            WorldRequirementOwnerKind.AlchemyRecipe,
            WorldRequirementProgramKind.Usage,
            frame.AlchemyRecipes,
            _alchemyUsageContainer!,
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure);
        Walk(
            NativeAccessorBinder.StaticList(_structureType, "All"),
            WorldRequirementOwnerKind.Structure,
            _structureId!,
            _structureContainer!,
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure);

        // An unmodelled condition is counted as skipped even though its row is published. The row
        // exists so the shortfall can be named; the count exists so the pass reports itself as
        // incomplete, which is what puts the subtype's name in front of an operator. The reader runs
        // once per lifecycle and the announcement is deduplicated on its text, so that is one line per
        // run of the game rather than one per pass.
        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, unmodelled, firstFailure);
    }

    private void Walk(
        IList? owners,
        WorldRequirementOwnerKind kind,
        Func<object, Guid> identity,
        Func<object, object?> container,
        WorldEntityRequirementBuffer buffer,
        ref int sampled,
        ref int unmodelled,
        ref string firstFailure)
    {
        if (owners is null)
        {
            if (firstFailure.Length == 0) firstFailure = $"the {kind} registry was unreadable";
            return;
        }

        for (var index = 0; index < owners.Count; index++)
        {
            var owner = owners[index];
            if (owner is null) continue;

            try
            {
                sampled += Read(owner, kind, identity, container, buffer, ref unmodelled, ref firstFailure);
            }
            catch (Exception ex)
            {
                unmodelled++;
                if (firstFailure.Length == 0)
                {
                    firstFailure = "reading a per-level prerequisite threw: " +
                        ex.GetBaseException().Message;
                }
            }
        }
    }

    private int Read(
        object owner,
        WorldRequirementOwnerKind kind,
        Func<object, Guid> identity,
        Func<object, object?> container,
        WorldEntityRequirementBuffer buffer,
        ref int unmodelled,
        ref string firstFailure,
        WorldRequirementProgramKind program = WorldRequirementProgramKind.NextLevel)
    {
        var ownerId = identity(owner);
        if (ownerId == Guid.Empty) return 0;

        var held = container(owner);
        if (held is null) return 0;

        var conditions = _conditions!(held);
        return AppendConditions(
            ownerId, kind, program, conditions, buffer, ref unmodelled, ref firstFailure);
    }

    private void WalkKnownIds(
        IList? owners,
        WorldRequirementOwnerKind kind,
        WorldRequirementProgramKind program,
        WorldSampleBuffer<WorldAlchemyRecipe, WorldAlchemyRecipe> identities,
        Func<object, object?> container,
        WorldEntityRequirementBuffer buffer,
        ref int sampled,
        ref int unmodelled,
        ref string firstFailure)
    {
        if (owners is null || owners.Count != identities.Count)
        {
            if (firstFailure.Length == 0)
                firstFailure = "the AlchemyRecipeSO identity snapshot was incomplete";
            return;
        }

        for (var index = 0; index < owners.Count; index++)
        {
            var owner = owners[index];
            if (owner is null) continue;
            try
            {
                sampled += ReadKnownId(
                    owner, identities[index].EntityId, kind, program, container, buffer,
                    ref unmodelled, ref firstFailure);
            }
            catch (Exception ex)
            {
                var row = UnreadableUsage(identities[index].EntityId, program);
                buffer.Append(in row);
                sampled++;
                unmodelled++;
                if (firstFailure.Length == 0)
                    firstFailure = "reading a usage prerequisite threw: " +
                        ex.GetBaseException().Message;
            }
        }
    }

    private int ReadKnownId(
        object owner,
        Guid ownerId,
        WorldRequirementOwnerKind kind,
        WorldRequirementProgramKind program,
        Func<object, object?> container,
        WorldEntityRequirementBuffer buffer,
        ref int unmodelled,
        ref string firstFailure)
    {
        var held = container(owner);
        if (held is null) return AppendUnreadableUsage(
            ownerId, kind, program, buffer, ref unmodelled, ref firstFailure);
        var conditions = _alchemyConditions!(held);
        if (conditions is null) return AppendUnreadableUsage(
            ownerId, kind, program, buffer, ref unmodelled, ref firstFailure);
        return AppendConditions(
            ownerId, kind, program, conditions, buffer, ref unmodelled, ref firstFailure);
    }

    private static int AppendUnreadableUsage(
        Guid ownerId,
        WorldRequirementOwnerKind ownerKind,
        WorldRequirementProgramKind program,
        WorldEntityRequirementBuffer buffer,
        ref int unmodelled,
        ref string firstFailure)
    {
        var row = UnreadableUsage(ownerId, program, ownerKind);
        buffer.Append(in row);
        unmodelled++;
        if (firstFailure.Length == 0)
            firstFailure = "an AlchemyRecipeSO usage-prerequisite container was unreadable";
        return 1;
    }

    private static WorldEntityRequirement UnreadableUsage(
        Guid ownerId,
        WorldRequirementProgramKind program,
        WorldRequirementOwnerKind ownerKind = WorldRequirementOwnerKind.AlchemyRecipe) =>
        new(
            ownerId,
            ownerKind,
            0,
            WorldRequirementConditionKind.Unknown,
            "UnreadableUsageRequirements",
            Guid.Empty,
            -1,
            0,
            default,
            default,
            program);

    private int AppendConditions(
        Guid ownerId,
        WorldRequirementOwnerKind ownerKind,
        WorldRequirementProgramKind program,
        IList? conditions,
        WorldEntityRequirementBuffer buffer,
        ref int unmodelled,
        ref string firstFailure)
    {
        var appended = 0;
        for (var groupOrdinal = 0; groupOrdinal < (conditions?.Count ?? 0); groupOrdinal++)
        {
            var condition = conditions![groupOrdinal];
            if (condition is null) continue;

            var accessors = AccessorsFor(condition.GetType());
            if (!accessors.IsComposite)
            {
                var row = accessors.Read(
                    ownerId, ownerKind, appended, condition, program,
                    WorldRequirementGroupKind.All, groupOrdinal);
                Append(in row, buffer, ref appended, ref unmodelled, ref firstFailure);
                continue;
            }

            var children = accessors.ReadChildren(condition);
            if (children is null)
            {
                var row = accessors.Unknown(
                    ownerId, ownerKind, appended, program, accessors.GroupKind, groupOrdinal);
                Append(in row, buffer, ref appended, ref unmodelled, ref firstFailure);
                continue;
            }

            if (children.Count == 0)
            {
                // Enumerable.Any(empty) is false; Enumerable.All(empty) is true.
                var row = accessors.EmptyComposite(
                    ownerId, ownerKind, appended, program, groupOrdinal);
                Append(in row, buffer, ref appended, ref unmodelled, ref firstFailure);
                continue;
            }

            for (var childIndex = 0; childIndex < children.Count; childIndex++)
            {
                var child = children[childIndex];
                if (child is null)
                {
                    var nullRow = accessors.Unknown(
                        ownerId, ownerKind, appended, program, accessors.GroupKind, groupOrdinal);
                    Append(in nullRow, buffer, ref appended, ref unmodelled, ref firstFailure);
                    continue;
                }
                var childAccessors = AccessorsFor(child.GetType());
                // This baseline authors no deeper composite in the programs collected here. Publish
                // one named unknown child instead of flattening away its parentheses; the enclosing
                // three-way fold then decides whether another OR arm proves the group met.
                var row = childAccessors.IsComposite
                    ? childAccessors.Unknown(
                        ownerId, ownerKind, appended, program, accessors.GroupKind, groupOrdinal)
                    : childAccessors.Read(
                        ownerId, ownerKind, appended, child, program,
                        accessors.GroupKind, groupOrdinal);
                Append(in row, buffer, ref appended, ref unmodelled, ref firstFailure);
            }
        }

        return appended;
    }

    private static void Append(
        in WorldEntityRequirement row,
        WorldEntityRequirementBuffer buffer,
        ref int appended,
        ref int unmodelled,
        ref string firstFailure)
    {
        if (row.Kind == WorldRequirementConditionKind.Unknown)
        {
            unmodelled++;
            if (firstFailure.Length == 0)
                firstFailure = "this build authors a condition this suite does not model: " +
                    $"{row.ConditionTypeName}. Entities gated by one are never planned.";
        }

        buffer.Append(in row);
        appended++;
    }

    private ConditionAccessors AccessorsFor(Type conditionType)
    {
        if (_accessors.TryGetValue(conditionType, out var cached)) return cached;

        var built = ConditionAccessors.Bind(conditionType);
        _accessors.Add(conditionType, built);
        return built;
    }

    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static Type? ContainerTypeOf(Type owner) =>
        owner.GetField("prerequisitesPerLevel", Instance)?.FieldType;

    /// <summary>
    /// The compiled reads for one concrete condition class, and the kind this suite models it as.
    /// </summary>
    /// <remarks>
    /// The three members are declared on the game's <c>BaseCondition&lt;T, TE&gt;</c>, whose <c>item</c>
    /// and <c>reqType</c> are its own type parameters — so there is no single closed type to bind
    /// against and the accessors are compiled against each concrete subclass instead. The two
    /// composites derive from the non-generic base and bind their own child-list field instead.
    /// </remarks>
    private sealed class ConditionAccessors
    {
        private readonly WorldRequirementConditionKind _kind;
        private readonly string _typeName;
        private readonly Func<object, Guid>? _item;
        private readonly Func<object, int>? _reqType;
        private readonly Func<object, object?>? _value;
        private readonly Func<object, double>? _baseValue;
        private readonly Func<object, int>? _perLevelType;
        private readonly Func<object, BigDouble>? _perLevelAmount;
        private readonly Func<object, int>? _perLevelOrder;
        private readonly Func<object, int>? _modPerLevelType;
        private readonly Func<object, BigDouble>? _modPerLevelAmount;
        private readonly Func<object, int>? _modPerLevelOrder;
        private readonly WorldRequirementGroupKind? _groupKind;
        private readonly Func<object, IList?>? _children;

        private ConditionAccessors(
            WorldRequirementConditionKind kind,
            string typeName,
            Func<object, Guid>? item,
            Func<object, int>? reqType,
            Func<object, object?>? value,
            Func<object, double>? baseValue,
            Func<object, int>? perLevelType,
            Func<object, BigDouble>? perLevelAmount,
            Func<object, int>? perLevelOrder,
            Func<object, int>? modPerLevelType,
            Func<object, BigDouble>? modPerLevelAmount,
            Func<object, int>? modPerLevelOrder,
            WorldRequirementGroupKind? groupKind = null,
            Func<object, IList?>? children = null)
        {
            _kind = kind;
            _typeName = typeName;
            _item = item;
            _reqType = reqType;
            _value = value;
            _baseValue = baseValue;
            _perLevelType = perLevelType;
            _perLevelAmount = perLevelAmount;
            _perLevelOrder = perLevelOrder;
            _modPerLevelType = modPerLevelType;
            _modPerLevelAmount = modPerLevelAmount;
            _modPerLevelOrder = modPerLevelOrder;
            _groupKind = groupKind;
            _children = children;
        }

        internal bool IsComposite => _groupKind.HasValue;
        internal WorldRequirementGroupKind GroupKind => _groupKind ?? WorldRequirementGroupKind.All;

        internal IList? ReadChildren(object condition) => _children?.Invoke(condition);

        internal static ConditionAccessors Bind(Type conditionType)
        {
            var typeName = conditionType.Name;
            var kind = Classify(typeName);

            if (typeName is "OrRequirement" or "AndRequirement")
            {
                var groupKind = typeName == "OrRequirement"
                    ? WorldRequirementGroupKind.Any
                    : WorldRequirementGroupKind.All;
                var children = NativeAccessorBinder.CollectionField(
                    conditionType,
                    typeName == "OrRequirement" ? "orConditions" : "andConditions");
                return new ConditionAccessors(
                    WorldRequirementConditionKind.Unknown,
                    typeName,
                    null, null, null, null, null, null, null, null, null, null,
                    groupKind,
                    children);
            }

            var item = NativeAccessorBinder.ReferenceGuid(conditionType, "item");
            var reqType = NativeAccessorBinder.EnumField(conditionType, "reqType");
            var value = NativeAccessorBinder.Reference(conditionType, "value");
            var thresholdType = conditionType.GetField("value", Instance)?.FieldType;

            var baseValue = NativeAccessorBinder.Field<double>(thresholdType, "baseValue");
            var perLevelType = NativeAccessorBinder.NestedEnumField(thresholdType, "perLevel", "type");
            var perLevelAmount =
                NativeAccessorBinder.NestedField<BigDouble>(thresholdType, "perLevel", "adjustReal");
            var perLevelOrder = NativeAccessorBinder.NestedField<int>(thresholdType, "perLevel", "order");
            var modPerLevelType =
                NativeAccessorBinder.NestedEnumField(thresholdType, "modPerLevel", "type");
            var modPerLevelAmount =
                NativeAccessorBinder.NestedField<BigDouble>(thresholdType, "modPerLevel", "adjustReal");
            var modPerLevelOrder =
                NativeAccessorBinder.NestedField<int>(thresholdType, "modPerLevel", "order");

            // A modelled kind whose members did not bind is not modelled after all. Collapsing the two
            // into one verdict is what keeps every consumer's fail-closed test a single comparison.
            var bound = item is not null && reqType is not null && value is not null &&
                baseValue is not null && perLevelType is not null && perLevelAmount is not null &&
                perLevelOrder is not null && modPerLevelType is not null &&
                modPerLevelAmount is not null && modPerLevelOrder is not null;

            return new ConditionAccessors(
                bound ? kind : WorldRequirementConditionKind.Unknown,
                typeName,
                item,
                reqType,
                value,
                baseValue,
                perLevelType,
                perLevelAmount,
                perLevelOrder,
                modPerLevelType,
                modPerLevelAmount,
                modPerLevelOrder);
        }

        internal WorldEntityRequirement Read(
            Guid ownerId,
            WorldRequirementOwnerKind ownerKind,
            int ordinal,
            object condition,
            WorldRequirementProgramKind program,
            WorldRequirementGroupKind groupKind,
            int groupOrdinal)
        {
            if (_kind == WorldRequirementConditionKind.Unknown)
            {
                return Unknown(ownerId, ownerKind, ordinal, program, groupKind, groupOrdinal);
            }

            var threshold = _value!(condition);
            var perLevel = threshold is null
                ? default(WorldRequirementScaling)
                : new WorldRequirementScaling(
                    _perLevelType!(threshold), _perLevelAmount!(threshold), _perLevelOrder!(threshold));
            var modPerLevel = threshold is null
                ? default(WorldRequirementScaling)
                : new WorldRequirementScaling(
                    _modPerLevelType!(threshold),
                    _modPerLevelAmount!(threshold),
                    _modPerLevelOrder!(threshold));

            return new WorldEntityRequirement(
                ownerId,
                ownerKind,
                ordinal,
                _kind,
                _typeName,
                _item!(condition),
                _reqType!(condition),
                threshold is null ? 0d : _baseValue!(threshold),
                in perLevel,
                in modPerLevel,
                program,
                groupKind,
                groupOrdinal);
        }

        internal WorldEntityRequirement Unknown(
            Guid ownerId,
            WorldRequirementOwnerKind ownerKind,
            int ordinal,
            WorldRequirementProgramKind program,
            WorldRequirementGroupKind groupKind,
            int groupOrdinal) =>
            new(
                ownerId,
                ownerKind,
                ordinal,
                WorldRequirementConditionKind.Unknown,
                _typeName,
                Guid.Empty,
                reqType: -1,
                baseValue: 0d,
                default(WorldRequirementScaling),
                default(WorldRequirementScaling),
                program,
                groupKind,
                groupOrdinal);

        internal WorldEntityRequirement EmptyComposite(
            Guid ownerId,
            WorldRequirementOwnerKind ownerKind,
            int ordinal,
            WorldRequirementProgramKind program,
            int groupOrdinal) =>
            new(
                ownerId,
                ownerKind,
                ordinal,
                WorldRequirementConditionKind.Literal,
                _typeName,
                Guid.Empty,
                reqType: GroupKind == WorldRequirementGroupKind.All ? 1 : 0,
                baseValue: 0d,
                default(WorldRequirementScaling),
                default(WorldRequirementScaling),
                program,
                GroupKind,
                groupOrdinal);

        /// <summary>
        /// The condition classes this suite has been audited against, by the name the game gives them.
        /// </summary>
        /// <remarks>
        /// A name rather than a resolved type, because the list the container holds is
        /// <c>[SerializeReference]</c> and its entries are only ever known by what they turn out to be.
        /// Composite classes are bound separately to their child lists. Everything absent from this
        /// switch and that composite branch is unknown by construction, which is the fail-closed
        /// reading.
        /// </remarks>
        private static WorldRequirementConditionKind Classify(string typeName) => typeName switch
        {
            "UpgradeRequirement" => WorldRequirementConditionKind.Upgrade,
            "ResearchRequirement" => WorldRequirementConditionKind.Research,
            "StructureRequirement" => WorldRequirementConditionKind.Structure,
            "SpellRequirement" => WorldRequirementConditionKind.Spell,
            "AlchemyRecipeRequirement" => WorldRequirementConditionKind.AlchemyRecipe,
            "RitualRequirement" => WorldRequirementConditionKind.Ritual,
            "NumberRequirement" => WorldRequirementConditionKind.Number,
            "GenericRequirement" => WorldRequirementConditionKind.Generic,
            _ => WorldRequirementConditionKind.Unknown,
        };
    }
}
