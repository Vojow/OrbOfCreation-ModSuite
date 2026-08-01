using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>Which kind of entity a requirement row belongs to.</summary>
/// <remarks>
/// The kind travels because the level a per-level container is checked at is a property of the owner,
/// not of the condition: an upgrade asks about <c>level + queuedLevels + 1</c>, a structure asks
/// about <c>quantity</c>, and Research supplies its native effective requirement level. A consumer
/// that only knew the identity would have to guess which, or search every registry to find out.
/// </remarks>
internal enum WorldRequirementOwnerKind
{
    /// <summary>The owner's registry could not be established. No consumer may treat this as met.</summary>
    Unknown = 0,
    Upgrade = 1,
    Structure = 2,
    PrerequisiteLink = 3,
    Research = 4,
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
    PrerequisiteLink = 9,
}

internal enum WorldRequirementNodeKind
{
    Leaf = 0,
    Group = 1,
}

internal enum WorldRequirementOperator
{
    None = 0,
    And = 1,
    Or = 2,
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
/// One authored condition or group on one entity's per-level purchase, described by what it compares
/// rather than by whether it currently holds.
/// </summary>
/// <remarks>
/// <para>
/// Upgrade and structure pipelines ask <c>prerequisitesPerLevel.Check(level)</c>; Research asks
/// <c>levelPrerequisites.Check(GetRequirementLevel())</c>. Those parameterized calls cannot be
/// published as reusable latches the way a whole-entity <c>available</c> can. The containers' contents
/// publish as the durable explanation. A separate row carries the safe parameterized native answer
/// at one exact level as a same-generation differential oracle.
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
        in WorldRequirementScaling modPerLevel)
        : this(
            ownerId,
            ownerKind,
            containerIndex: 0,
            ordinal,
            parentOrdinal: -1,
            depth: 0,
            WorldRequirementNodeKind.Leaf,
            WorldRequirementOperator.None,
            kind,
            conditionTypeName,
            targetId,
            reqType,
            baseValue,
            in perLevel,
            in modPerLevel)
    {
    }

    internal WorldEntityRequirement(
        Guid ownerId,
        WorldRequirementOwnerKind ownerKind,
        int containerIndex,
        int ordinal,
        int parentOrdinal,
        int depth,
        WorldRequirementNodeKind nodeKind,
        WorldRequirementOperator @operator,
        WorldRequirementConditionKind kind,
        string conditionTypeName,
        Guid targetId,
        int reqType,
        double baseValue,
        in WorldRequirementScaling perLevel,
        in WorldRequirementScaling modPerLevel)
    {
        OwnerId = ownerId;
        OwnerKind = ownerKind;
        ContainerIndex = containerIndex;
        Ordinal = ordinal;
        ParentOrdinal = parentOrdinal;
        Depth = depth;
        NodeKind = nodeKind;
        Operator = @operator;
        Kind = kind;
        ConditionTypeName = conditionTypeName;
        TargetId = targetId;
        ReqType = reqType;
        BaseValue = baseValue;
        PerLevel = perLevel;
        ModPerLevel = modPerLevel;
    }

    /// <summary>The entity whose next level this condition gates.</summary>
    internal Guid OwnerId { get; }

    internal WorldRequirementOwnerKind OwnerKind { get; }

    /// <summary>
    /// Zero for an entity's per-level container; otherwise the exact tier index on a
    /// <c>PrerequisiteLinkSO</c>. It is not a level guessed from list position.
    /// </summary>
    internal int ContainerIndex { get; }

    /// <summary>The condition's position in its owner's container.</summary>
    internal int Ordinal { get; }

    /// <summary>The enclosing explicit group, or -1 for a child of the container's implicit AND.</summary>
    internal int ParentOrdinal { get; }

    internal int Depth { get; }

    internal WorldRequirementNodeKind NodeKind { get; }

    internal WorldRequirementOperator Operator { get; }

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

    internal static bool TryFindContainerRange(
        PublicationTable<WorldEntityRequirement> table,
        Guid ownerId,
        int containerIndex,
        out int start,
        out int count)
    {
        if (!TryFindRange(table, ownerId, out var ownerStart, out var ownerCount))
        {
            start = 0;
            count = 0;
            return false;
        }

        var rows = table.AsSpan();
        start = ownerStart;
        var ownerEnd = ownerStart + ownerCount;
        while (start < ownerEnd && rows[start].ContainerIndex < containerIndex) start++;
        if (start >= ownerEnd || rows[start].ContainerIndex != containerIndex)
        {
            start = 0;
            count = 0;
            return false;
        }

        count = 0;
        while (start + count < ownerEnd && rows[start + count].ContainerIndex == containerIndex)
            count++;
        return true;
    }
}

/// <summary>Every authored per-level condition and group as read, held where a cycle can own them.</summary>
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
            if (byOwner != 0) return byOwner;
            var byContainer = left.ContainerIndex.CompareTo(right.ContainerIndex);
            return byContainer != 0 ? byContainer : left.Ordinal.CompareTo(right.Ordinal);
        }
    }
}

/// <summary>The volatile native gates around one authored prerequisite-link tier.</summary>
internal readonly struct WorldPrerequisiteLinkTier
{
    internal WorldPrerequisiteLinkTier(
        Guid linkId,
        int tierIndex,
        bool activeEnabled,
        bool passiveEnabled,
        long evaluatedFrame,
        long collectedFrame)
    {
        LinkId = linkId;
        TierIndex = tierIndex;
        ActiveEnabled = activeEnabled;
        PassiveEnabled = passiveEnabled;
        EvaluatedFrame = evaluatedFrame;
        CollectedFrame = collectedFrame;
    }

    internal Guid LinkId { get; }
    internal int TierIndex { get; }
    internal bool ActiveEnabled { get; }
    internal bool PassiveEnabled { get; }
    internal long EvaluatedFrame { get; }
    internal long CollectedFrame { get; }
    internal bool EvaluatedThisFrame => EvaluatedFrame == CollectedFrame;
}

internal static class WorldPrerequisiteLinkTierLookup
{
    internal static bool TryFind(
        PublicationTable<WorldPrerequisiteLinkTier> table,
        Guid linkId,
        int tierIndex,
        out WorldPrerequisiteLinkTier row)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            ref readonly var candidate = ref rows[middle];
            var comparison = candidate.LinkId.CompareTo(linkId);
            if (comparison == 0) comparison = candidate.TierIndex.CompareTo(tierIndex);
            if (comparison == 0)
            {
                row = candidate;
                return true;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        row = default;
        return false;
    }
}

internal sealed class WorldPrerequisiteLinkTierBuffer
{
    private WorldPrerequisiteLinkTier[] _samples = new WorldPrerequisiteLinkTier[64];
    private int _count;

    internal int Count => _count;
    internal ref readonly WorldPrerequisiteLinkTier this[int index] => ref _samples[index];
    internal void Reset() => _count = 0;

    internal void Append(in WorldPrerequisiteLinkTier sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

internal static class WorldPrerequisiteLinkTierDeriver
{
    internal static PublicationTable<WorldPrerequisiteLinkTier> Build(
        WorldPrerequisiteLinkTierBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldPrerequisiteLinkTier>.Empty;

        var rows = new WorldPrerequisiteLinkTier[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) rows[index] = buffer[index];
        Array.Sort(rows, static (left, right) =>
        {
            var byLink = left.LinkId.CompareTo(right.LinkId);
            return byLink != 0 ? byLink : left.TierIndex.CompareTo(right.TierIndex);
        });
        return PublicationTable<WorldPrerequisiteLinkTier>.Create(rows, rows.Length);
    }
}

/// <summary>
/// The game's own parameterized per-level prerequisite verdict for one structure or upgrade,
/// captured beside the authored graph in the same world generation.
/// </summary>
/// <remarks>
/// <c>Container.Check(ConditionInfo)</c> is the safe oracle: unlike the parameterless overload it
/// neither stamps a frame nor latches <c>available</c>. Keeping its exact input level is essential;
/// the two owner families intentionally ask different questions.
/// </remarks>
internal readonly struct WorldRequirementNativeVerdict
{
    internal WorldRequirementNativeVerdict(
        Guid entityId,
        WorldRequirementOwnerKind ownerKind,
        long checkLevel,
        bool met)
    {
        EntityId = entityId;
        OwnerKind = ownerKind;
        CheckLevel = checkLevel;
        Met = met;
    }

    internal Guid EntityId { get; }
    internal WorldRequirementOwnerKind OwnerKind { get; }
    internal long CheckLevel { get; }
    internal bool Met { get; }
}

internal static class WorldRequirementNativeVerdictLookup
{
    internal static bool TryFind(
        PublicationTable<WorldRequirementNativeVerdict> table,
        Guid entityId,
        out WorldRequirementNativeVerdict row)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = rows[middle].EntityId.CompareTo(entityId);
            if (comparison == 0)
            {
                row = rows[middle];
                return true;
            }
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        row = default;
        return false;
    }
}

internal sealed class WorldRequirementNativeVerdictBuffer
{
    private WorldRequirementNativeVerdict[] _samples = new WorldRequirementNativeVerdict[256];
    private int _count;

    internal int Count => _count;
    internal ref readonly WorldRequirementNativeVerdict this[int index] => ref _samples[index];
    internal void Reset() => _count = 0;

    internal void Append(in WorldRequirementNativeVerdict sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

internal static class WorldRequirementNativeVerdictDeriver
{
    internal static PublicationTable<WorldRequirementNativeVerdict> Build(
        WorldRequirementNativeVerdictBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldRequirementNativeVerdict>.Empty;

        var rows = new WorldRequirementNativeVerdict[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) rows[index] = buffer[index];
        Array.Sort(rows, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return PublicationTable<WorldRequirementNativeVerdict>.Create(rows, rows.Length);
    }
}

/// <summary>
/// Reads the live active and passive cache gates around prerequisite-link tiers without calling the
/// native <c>IsEnabled()</c>, whose passive branch can latch prerequisite state.
/// </summary>
internal sealed class WorldPrerequisiteLinkTierReader : IWorldCategoryReader
{
    private readonly Type? _linkType;
    private readonly Func<IList?>? _links;
    private readonly Func<object, Guid>? _identity;
    private readonly Func<object, IList?>? _tiers;
    private readonly Func<object, bool>? _activeEnabled;
    private readonly Func<object, bool>? _passiveEnabled;
    private readonly Func<object, long>? _evaluatedFrame;
    private readonly Func<long>? _currentFrame;
    private readonly string _unavailable;

    internal WorldPrerequisiteLinkTierReader(Type? linkType, Type? gameManagerType)
    {
        _linkType = linkType;
        if (linkType is null || gameManagerType is null)
        {
            _unavailable = linkType is null
                ? "the PrerequisiteLinkSO type was not found on this build"
                : "the GameManager type was not found on this build";
            return;
        }

        var link = new WorldMemberBinding(linkType, "PrerequisiteLinkSO");
        _links = NativeAccessorBinder.StaticListAccessor(linkType, "All");
        _identity = link.Call<Guid>("GetGuid");
        _tiers = NativeAccessorBinder.CollectionField(linkType, "linkTiers");
        var definitionType = linkType.GetNestedType(
            "LinkDefinition", BindingFlags.Public | BindingFlags.NonPublic);
        _activeEnabled = NativeAccessorBinder.Field<bool>(definitionType, "isActiveEnabled");
        _passiveEnabled = NativeAccessorBinder.Field<bool>(definitionType, "isPassiveEnabled");
        _evaluatedFrame = NativeAccessorBinder.Field<long>(definitionType, "currentFrame");
        _currentFrame = NativeAccessorBinder.StaticField<long>(gameManagerType, "currentFrame");

        _unavailable = _links is null || _identity is null || _tiers is null ||
            _activeEnabled is null || _passiveEnabled is null || _evaluatedFrame is null ||
            _currentFrame is null
                ? "PrerequisiteLinkSO tiers did not expose their complete live gate state on this build"
                : link.Failure;
    }

    public string Category => "prerequisite link states";
    public bool IsAvailable => _linkType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var buffer = frame.PrerequisiteLinkTiers;
        buffer.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var links = _links!();
        if (links is null) return WorldCategoryReport.Missing(Category, "the link registry was null");

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;
        var collectedFrame = _currentFrame!();
        for (var linkIndex = 0; linkIndex < links.Count; linkIndex++)
        {
            var link = links[linkIndex];
            if (link is null) continue;
            try
            {
                var linkId = _identity!(link);
                var tiers = _tiers!(link);
                if (linkId == Guid.Empty || tiers is null) continue;
                for (var tierIndex = 0; tierIndex < tiers.Count; tierIndex++)
                {
                    var tier = tiers[tierIndex];
                    if (tier is null) continue;
                    var row = new WorldPrerequisiteLinkTier(
                        linkId,
                        tierIndex,
                        _activeEnabled!(tier),
                        _passiveEnabled!(tier),
                        _evaluatedFrame!(tier),
                        collectedFrame);
                    buffer.Append(in row);
                    sampled++;
                }
            }
            catch (Exception ex)
            {
                skipped++;
                if (firstFailure.Length == 0)
                    firstFailure = "reading a prerequisite-link live gate threw: " +
                        ex.GetBaseException().Message;
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
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
/// Authored graph reads are field loads performed once per lifecycle. The paired
/// <see cref="WorldRequirementNativeVerdictReader"/> invokes exactly the parameterized
/// <c>Container.Check(ConditionInfo)</c> overload on every world capture as a same-generation
/// differential oracle. It neither stamps nor latches. The no-argument <c>Check()</c>, and
/// whole-entity visibility or availability predicates which can reach it, remain forbidden here
/// because they write cached prerequisite state. See W58.
/// </para>
/// </remarks>
internal sealed class WorldEntityRequirementReader : IWorldCategoryReader
{
    private const int MaximumGraphDepth = 32;

    private readonly Type? _upgradeType;
    private readonly Type? _structureType;
    private readonly Type? _researchType;
    private readonly Type? _prerequisiteLinkType;
    private readonly string _unavailable;

    private readonly Func<object, Guid>? _upgradeId;
    private readonly Func<object, object?>? _upgradeContainer;
    private readonly Func<object, int>? _upgradeLevel;
    private readonly Func<object, int>? _upgradeQueuedLevels;
    private readonly Func<object, Guid>? _structureId;
    private readonly Func<object, object?>? _structureContainer;
    private readonly Func<object, int>? _structureQuantity;
    private readonly Func<object, Guid>? _researchId;
    private readonly Func<object, object?>? _researchContainer;
    private readonly Func<object, int>? _researchRequirementLevel;
    private readonly Func<object, Guid>? _prerequisiteLinkId;
    private readonly Func<object, IList?>? _prerequisiteLinkTiers;
    private readonly Func<object, object?>? _prerequisiteLinkTierContainer;
    private readonly Func<object, IList?>? _conditions;
    private readonly Func<object, long, bool>? _nativeCheck;

    /// <summary>
    /// One compiled accessor set per condition class seen so far. Not on the frame: these are
    /// delegates, and a frame crosses to a worker.
    /// </summary>
    private readonly Dictionary<Type, ConditionAccessors> _accessors = new();
    private readonly List<NativeVerdictSource> _nativeVerdictSources = new();

    internal WorldEntityRequirementReader(
        Type? upgradeType,
        Type? structureType,
        Type? researchType,
        Type? prerequisiteLinkType)
    {
        _upgradeType = upgradeType;
        _structureType = structureType;
        _researchType = researchType;
        _prerequisiteLinkType = prerequisiteLinkType;
        if (upgradeType is null || structureType is null || researchType is null ||
            prerequisiteLinkType is null)
        {
            _unavailable = upgradeType is null
                ? "the UpgradeSO type was not found on this build"
                : structureType is null
                    ? "the StructureSO type was not found on this build"
                    : researchType is null
                        ? "the ResearchSO type was not found on this build"
                        : "the PrerequisiteLinkSO type was not found on this build";
            return;
        }

        var upgrade = new WorldMemberBinding(upgradeType, "UpgradeSO");
        _upgradeId = upgrade.Call<Guid>("GetGuid");
        _upgradeContainer = NativeAccessorBinder.Reference(upgradeType, "prerequisitesPerLevel");
        _upgradeLevel = upgrade.Call<int>("GetPurchaseLevel");
        _upgradeQueuedLevels = NativeAccessorBinder.Field<int>(upgradeType, "queuedLevels");

        var structure = new WorldMemberBinding(structureType, "StructureSO");
        _structureId = structure.Call<Guid>("GetGuid");
        _structureContainer = NativeAccessorBinder.Reference(structureType, "prerequisitesPerLevel");
        _structureQuantity = NativeAccessorBinder.Field<int>(structureType, "quantity");

        var research = new WorldMemberBinding(researchType, "ResearchSO");
        _researchId = research.Call<Guid>("GetGuid");
        _researchContainer = NativeAccessorBinder.Reference(researchType, "levelPrerequisites");
        _researchRequirementLevel = research.Call<int>("GetRequirementLevel");

        var link = new WorldMemberBinding(prerequisiteLinkType, "PrerequisiteLinkSO");
        _prerequisiteLinkId = link.Call<Guid>("GetGuid");
        _prerequisiteLinkTiers = NativeAccessorBinder.CollectionField(prerequisiteLinkType, "linkTiers");
        var linkDefinitionType = prerequisiteLinkType.GetNestedType(
            "LinkDefinition", BindingFlags.Public | BindingFlags.NonPublic);
        _prerequisiteLinkTierContainer =
            NativeAccessorBinder.Reference(linkDefinitionType, "prerequisites");

        // Both owners and prerequisite-link tiers hold the same container type, so the list accessor
        // is bound once against whichever owner declared it rather than once per native surface.
        var containerType = ContainerTypeOf(upgradeType) ?? ContainerTypeOf(structureType);
        _conditions = NativeAccessorBinder.CollectionField(containerType, "prerequisites");
        _nativeCheck = NativeAccessorBinder.CallWithConstructedLongArgument<bool>(
            containerType,
            "Check",
            "Requirements.ConditionInfo");

        if (_upgradeContainer is null || _structureContainer is null ||
            _upgradeLevel is null || _upgradeQueuedLevels is null ||
            _structureQuantity is null ||
            _researchId is null || _researchContainer is null ||
            _researchRequirementLevel is null ||
            _prerequisiteLinkId is null || _prerequisiteLinkTiers is null ||
            _prerequisiteLinkTierContainer is null || _conditions is null)
        {
            _unavailable = "UpgradeSO, StructureSO, ResearchSO, and PrerequisiteLinkSO did not " +
                "expose the complete prerequisite graph on this build";
            return;
        }
        if (_nativeCheck is null)
        {
            _unavailable = "Prerequisites.Container.Check(Requirements.ConditionInfo) or its " +
                "exact Int64 constructor was unavailable on this build";
            return;
        }

        _unavailable = upgrade.Failure.Length > 0
            ? upgrade.Failure
            : structure.Failure.Length > 0
                ? structure.Failure
                : research.Failure.Length > 0
                    ? research.Failure
                    : link.Failure;
    }

    public string Category => "entity requirements";

    public bool IsAvailable =>
        _upgradeType is not null && _structureType is not null &&
        _researchType is not null && _prerequisiteLinkType is not null &&
        _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var buffer = frame.EntityRequirements;
        buffer.Reset();
        _nativeVerdictSources.Clear();
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
        WalkPrerequisiteLinks(
            NativeAccessorBinder.StaticList(_prerequisiteLinkType, "All"),
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure);
        Walk(
            NativeAccessorBinder.StaticList(_researchType, "All"),
            WorldRequirementOwnerKind.Research,
            _researchId!,
            _researchContainer!,
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

    internal WorldCategoryReport CollectNativeVerdicts(
        HashSet<Guid> claimed,
        GameWorldCycleFrame frame)
    {
        var destination = frame.RequirementNativeVerdicts;
        destination.Reset();
        if (!IsAvailable)
            return WorldCategoryReport.Missing("requirement native verdicts", _unavailable);

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;
        for (var index = 0; index < _nativeVerdictSources.Count; index++)
        {
            var source = _nativeVerdictSources[index];
            try
            {
                var level = source.OwnerKind switch
                {
                    WorldRequirementOwnerKind.Upgrade =>
                        checked((long)_upgradeLevel!(source.Owner) +
                            _upgradeQueuedLevels!(source.Owner) + 1L),
                    WorldRequirementOwnerKind.Research =>
                        _researchRequirementLevel!(source.Owner),
                    _ => _structureQuantity!(source.Owner),
                };
                var row = new WorldRequirementNativeVerdict(
                    source.EntityId,
                    source.OwnerKind,
                    level,
                    _nativeCheck!(source.Container, level));
                destination.Append(in row);
                sampled++;
            }
            catch (Exception exception)
            {
                skipped++;
                if (firstFailure.Length == 0)
                    firstFailure = "reading a live parameterized prerequisite verdict threw: " +
                        exception.GetBaseException().Message;
            }
        }
        return new WorldCategoryReport(
            "requirement native verdicts",
            WorldCategoryOutcome.Collected,
            sampled,
            skipped,
            firstFailure);
    }

    private void WalkPrerequisiteLinks(
        IList? links,
        WorldEntityRequirementBuffer buffer,
        ref int sampled,
        ref int unmodelled,
        ref string firstFailure)
    {
        if (links is null)
        {
            if (firstFailure.Length == 0)
                firstFailure = "the PrerequisiteLink registry was unreadable";
            return;
        }

        for (var index = 0; index < links.Count; index++)
        {
            var link = links[index];
            if (link is null) continue;
            try
            {
                var ownerId = _prerequisiteLinkId!(link);
                if (ownerId == Guid.Empty) continue;
                var tiers = _prerequisiteLinkTiers!(link);
                var tierCount = tiers?.Count ?? 0;
                for (var tier = 0; tier < tierCount; tier++)
                {
                    var definition = tiers![tier];
                    if (definition is null) continue;
                    var held = _prerequisiteLinkTierContainer!(definition);
                    if (held is null) continue;
                    sampled += ReadContainer(
                        ownerId,
                        WorldRequirementOwnerKind.PrerequisiteLink,
                        tier,
                        held,
                        publishContainerRoot: true,
                        buffer,
                        ref unmodelled,
                        ref firstFailure);
                }
            }
            catch (Exception ex)
            {
                unmodelled++;
                if (firstFailure.Length == 0)
                {
                    firstFailure = "reading a prerequisite-link tier threw: " +
                        ex.GetBaseException().Message;
                }
            }
        }
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
                sampled += Read(
                    owner,
                    kind,
                    identity,
                    container,
                    buffer,
                    ref unmodelled,
                    ref firstFailure);
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
        ref string firstFailure)
    {
        var ownerId = identity(owner);
        if (ownerId == Guid.Empty) return 0;

        var held = container(owner);
        if (held is null) return 0;
        _nativeVerdictSources.Add(new NativeVerdictSource(ownerId, kind, owner, held));

        return ReadContainer(
            ownerId,
            kind,
            containerIndex: 0,
            held,
            publishContainerRoot: false,
            buffer,
            ref unmodelled,
            ref firstFailure);
    }

    private readonly struct NativeVerdictSource
    {
        internal NativeVerdictSource(
            Guid entityId,
            WorldRequirementOwnerKind ownerKind,
            object owner,
            object container)
        {
            EntityId = entityId;
            OwnerKind = ownerKind;
            Owner = owner;
            Container = container;
        }

        internal Guid EntityId { get; }
        internal WorldRequirementOwnerKind OwnerKind { get; }
        internal object Owner { get; }
        internal object Container { get; }
    }

    private int ReadContainer(
        Guid ownerId,
        WorldRequirementOwnerKind kind,
        int containerIndex,
        object held,
        bool publishContainerRoot,
        WorldEntityRequirementBuffer buffer,
        ref int unmodelled,
        ref string firstFailure)
    {
        var conditions = _conditions!(held);
        var count = conditions?.Count ?? 0;
        var appended = 0;
        var nextOrdinal = publishContainerRoot ? 1 : 0;
        var parentOrdinal = publishContainerRoot ? 0 : -1;
        var depth = publishContainerRoot ? 1 : 0;
        if (publishContainerRoot)
        {
            var root = new WorldEntityRequirement(
                ownerId,
                kind,
                containerIndex,
                ordinal: 0,
                parentOrdinal: -1,
                depth: 0,
                WorldRequirementNodeKind.Group,
                WorldRequirementOperator.And,
                WorldRequirementConditionKind.Unknown,
                "Prerequisites.Container",
                Guid.Empty,
                reqType: -1,
                baseValue: 0d,
                default(WorldRequirementScaling),
                default(WorldRequirementScaling));
            buffer.Append(in root);
        }
        for (var index = 0; index < count; index++)
        {
            var condition = conditions![index];
            if (condition is null) continue;

            appended += ReadCondition(
                ownerId,
                kind,
                containerIndex,
                condition,
                parentOrdinal,
                depth,
                ref nextOrdinal,
                buffer,
                ref unmodelled,
                ref firstFailure);
        }

        return appended;
    }

    private int ReadCondition(
        Guid ownerId,
        WorldRequirementOwnerKind kind,
        int containerIndex,
        object condition,
        int parentOrdinal,
        int depth,
        ref int nextOrdinal,
        WorldEntityRequirementBuffer buffer,
        ref int unmodelled,
        ref string firstFailure)
    {
        if (depth >= MaximumGraphDepth)
        {
            var exceededOrdinal = nextOrdinal++;
            var unknown = new WorldEntityRequirement(
                ownerId,
                kind,
                containerIndex,
                exceededOrdinal,
                parentOrdinal,
                depth,
                WorldRequirementNodeKind.Leaf,
                WorldRequirementOperator.None,
                WorldRequirementConditionKind.Unknown,
                "RequirementGraphDepthExceeded",
                Guid.Empty,
                reqType: -1,
                baseValue: 0d,
                default(WorldRequirementScaling),
                default(WorldRequirementScaling));
            unmodelled++;
            if (firstFailure.Length == 0)
            {
                firstFailure = $"a prerequisite graph exceeded {MaximumGraphDepth} nested nodes. " +
                    "Entities gated by one are never planned.";
            }
            buffer.Append(in unknown);
            return 1;
        }

        var accessors = AccessorsFor(condition.GetType());
        var ordinal = nextOrdinal++;
        var row = accessors.Read(
            ownerId,
            kind,
            containerIndex,
            ordinal,
            parentOrdinal,
            depth,
            condition);
        if (row.Kind == WorldRequirementConditionKind.Unknown &&
            row.NodeKind == WorldRequirementNodeKind.Leaf)
        {
            unmodelled++;
            if (firstFailure.Length == 0)
            {
                firstFailure = "this build authors a condition this suite does not model: " +
                    $"{row.ConditionTypeName}. Entities gated by one are never planned.";
            }
        }

        buffer.Append(in row);
        var appended = 1;
        if (row.NodeKind != WorldRequirementNodeKind.Group) return appended;

        var children = accessors.Children(condition);
        var childCount = children?.Count ?? 0;
        for (var index = 0; index < childCount; index++)
        {
            var child = children![index];
            if (child is null) continue;
            appended += ReadCondition(
                ownerId,
                kind,
                containerIndex,
                child,
                ordinal,
                depth + 1,
                ref nextOrdinal,
                buffer,
                ref unmodelled,
                ref firstFailure);
        }

        return appended;
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
    /// against and the accessors are compiled against each concrete subclass instead. Composites have
    /// their own explicit child-list binding and are the only non-leaf nodes.
    /// </remarks>
    private sealed class ConditionAccessors
    {
        private readonly WorldRequirementConditionKind _kind;
        private readonly string _typeName;
        private readonly WorldRequirementNodeKind _nodeKind;
        private readonly WorldRequirementOperator _operator;
        private readonly Func<object, IList?>? _children;
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

        private ConditionAccessors(
            WorldRequirementConditionKind kind,
            string typeName,
            WorldRequirementNodeKind nodeKind,
            WorldRequirementOperator @operator,
            Func<object, IList?>? children,
            Func<object, Guid>? item,
            Func<object, int>? reqType,
            Func<object, object?>? value,
            Func<object, double>? baseValue,
            Func<object, int>? perLevelType,
            Func<object, BigDouble>? perLevelAmount,
            Func<object, int>? perLevelOrder,
            Func<object, int>? modPerLevelType,
            Func<object, BigDouble>? modPerLevelAmount,
            Func<object, int>? modPerLevelOrder)
        {
            _kind = kind;
            _typeName = typeName;
            _nodeKind = nodeKind;
            _operator = @operator;
            _children = children;
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
        }

        internal static ConditionAccessors Bind(Type conditionType)
        {
            var typeName = conditionType.Name;
            var @operator = typeName switch
            {
                "AndRequirement" => WorldRequirementOperator.And,
                "OrRequirement" => WorldRequirementOperator.Or,
                _ => WorldRequirementOperator.None,
            };
            if (@operator != WorldRequirementOperator.None)
            {
                var children = NativeAccessorBinder.CollectionField(
                    conditionType,
                    @operator == WorldRequirementOperator.And ? "andConditions" : "orConditions");
                return new ConditionAccessors(
                    WorldRequirementConditionKind.Unknown,
                    typeName,
                    children is null ? WorldRequirementNodeKind.Leaf : WorldRequirementNodeKind.Group,
                    children is null ? WorldRequirementOperator.None : @operator,
                    children,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            var kind = Classify(typeName);

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
                WorldRequirementNodeKind.Leaf,
                WorldRequirementOperator.None,
                null,
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

        internal IList? Children(object condition) => _children?.Invoke(condition);

        internal WorldEntityRequirement Read(
            Guid ownerId,
            WorldRequirementOwnerKind ownerKind,
            int containerIndex,
            int ordinal,
            int parentOrdinal,
            int depth,
            object condition)
        {
            if (_nodeKind == WorldRequirementNodeKind.Group)
            {
                return new WorldEntityRequirement(
                    ownerId,
                    ownerKind,
                    containerIndex,
                    ordinal,
                    parentOrdinal,
                    depth,
                    _nodeKind,
                    _operator,
                    WorldRequirementConditionKind.Unknown,
                    _typeName,
                    Guid.Empty,
                    reqType: -1,
                    baseValue: 0d,
                    default(WorldRequirementScaling),
                    default(WorldRequirementScaling));
            }

            if (_kind == WorldRequirementConditionKind.Unknown)
            {
                return new WorldEntityRequirement(
                    ownerId,
                    ownerKind,
                    containerIndex,
                    ordinal,
                    parentOrdinal,
                    depth,
                    _nodeKind,
                    _operator,
                    WorldRequirementConditionKind.Unknown,
                    _typeName,
                    Guid.Empty,
                    reqType: -1,
                    baseValue: 0d,
                    default(WorldRequirementScaling),
                    default(WorldRequirementScaling));
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
                containerIndex,
                ordinal,
                parentOrdinal,
                depth,
                _nodeKind,
                _operator,
                _kind,
                _typeName,
                _item!(condition),
                _reqType!(condition),
                threshold is null ? 0d : _baseValue!(threshold),
                in perLevel,
                in modPerLevel);
        }

        /// <summary>
        /// The condition classes this suite has been audited against, by the name the game gives them.
        /// </summary>
        /// <remarks>
        /// A name rather than a resolved type, because the list the container holds is
        /// <c>[SerializeReference]</c> and its entries are only ever known by what they turn out to be.
        /// Composite classes are classified before this switch. Everything else absent from it is
        /// unknown by construction, which is the fail-closed reading.
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
            "PrerequisiteLinkRequirement" => WorldRequirementConditionKind.PrerequisiteLink,
            _ => WorldRequirementConditionKind.Unknown,
        };
    }
}

/// <summary>
/// Refreshes only the played-state verdicts for the lifecycle-cached requirement owners. It never
/// traverses the authored requirement graph; the paired structural reader owns that traversal and the
/// owner/container references are invalidated by the same lifecycle epoch that refreshes the graph.
/// </summary>
internal sealed class WorldRequirementNativeVerdictReader : IWorldCategoryReader
{
    private readonly WorldEntityRequirementReader _authoring;

    internal WorldRequirementNativeVerdictReader(WorldEntityRequirementReader authoring) =>
        _authoring = authoring ?? throw new ArgumentNullException(nameof(authoring));

    public string Category => "requirement native verdicts";
    public bool IsAvailable => _authoring.IsAvailable;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame) =>
        _authoring.CollectNativeVerdicts(claimed, frame);
}
