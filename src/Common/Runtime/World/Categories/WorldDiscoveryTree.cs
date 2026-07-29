using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One discovery tree as published — the scalar half. Which choices are currently on offer is a list
/// of identities and is not carried; see <c>docs/runtime-architecture/world-collection.md</c>.
/// </summary>
internal readonly struct WorldDiscoveryTree : IWorldEntity
{
    internal WorldDiscoveryTree(
        Guid treeId,
        int actionMode,
        BigDouble actionTime,
        int rerollsLeft,
        bool usedRerollsLastDiscover,
        Guid selectedChoiceId,
        Guid overrideRerollsId,
        Guid overrideChoicesId,
        int additionalDiscoveryChoices,
        int discoveryBonusLevelCost,
        bool debugMode,
        int totalDiscoveredCount,
        int poolDiscoveredCount,
        bool hasRequiredDiscovery,
        bool hasRemainingDiscovery,
        bool hasCompletedAllDiscoveries)
    {
        TreeId = treeId;
        ActionMode = actionMode;
        ActionTime = actionTime;
        RerollsLeft = rerollsLeft;
        UsedRerollsLastDiscover = usedRerollsLastDiscover;
        SelectedChoiceId = selectedChoiceId;
        OverrideRerollsId = overrideRerollsId;
        OverrideChoicesId = overrideChoicesId;
        AdditionalDiscoveryChoices = additionalDiscoveryChoices;
        DiscoveryBonusLevelCost = discoveryBonusLevelCost;
        DebugMode = debugMode;
        TotalDiscoveredCount = totalDiscoveredCount;
        PoolDiscoveredCount = poolDiscoveredCount;
        HasRequiredDiscovery = hasRequiredDiscovery;
        HasRemainingDiscovery = hasRemainingDiscovery;
        HasCompletedAllDiscoveries = hasCompletedAllDiscoveries;
    }

    internal Guid TreeId { get; }

    public Guid EntityId => TreeId;

    /// <summary>The game's mode enum as its underlying integer; see <see cref="WorldChallenge.State"/>.</summary>
    internal int ActionMode { get; }

    /// <summary>Seconds left on the action in progress.</summary>
    internal BigDouble ActionTime { get; }

    internal int RerollsLeft { get; }

    internal bool UsedRerollsLastDiscover { get; }

    /// <summary>
    /// Which choice the player has selected, or <see cref="Guid.Empty"/> when none is. The game holds
    /// this as a <c>GuidContainer</c>, so it is already an identity rather than a live reference.
    /// </summary>
    internal Guid SelectedChoiceId { get; }

    /// <summary>
    /// The variables that override the reroll and choice counts, when the tree is configured to use
    /// them. Empty when it is not. As with the alchemy type's selected level, the values live in the
    /// global registry and only the edge belongs here.
    /// </summary>
    internal Guid OverrideRerollsId { get; }

    internal Guid OverrideChoicesId { get; }

    /// <summary>
    /// The rest of the tree's runtime state: the counts the game caches about what has been discovered,
    /// and the flags it derives from them.
    /// </summary>
    internal int AdditionalDiscoveryChoices { get; }

    internal int DiscoveryBonusLevelCost { get; }

    internal bool DebugMode { get; }

    internal int TotalDiscoveredCount { get; }

    internal int PoolDiscoveredCount { get; }

    internal bool HasRequiredDiscovery { get; }

    internal bool HasRemainingDiscovery { get; }

    internal bool HasCompletedAllDiscoveries { get; }
}

internal sealed class WorldDiscoveryTreeBinder : WorldPlainBinder<WorldDiscoveryTree>
{
    private Func<object, Guid>? _id;
    private Func<object, int>? _actionMode;
    private Func<object, BigDouble>? _actionTime;
    private Func<object, int>? _rerollsLeft;
    private Func<object, bool>? _usedRerolls;
    private Func<object, Guid>? _selectedChoice;
    private Func<object, Guid>? _overrideRerolls;
    private Func<object, Guid>? _overrideChoices;
    private Func<object, int>? _additionalDiscoveryChoices;
    private Func<object, int>? _discoveryBonusLevelCost;
    private Func<object, bool>? _debugMode;
    private Func<object, int>? _totalDiscoveredCount;
    private Func<object, int>? _poolDiscoveredCount;
    private Func<object, bool>? _hasRequiredDiscovery;
    private Func<object, bool>? _hasRemainingDiscovery;
    private Func<object, bool>? _hasCompletedAllDiscoveries;

    internal override string Category => "discovery trees";

    internal override string TypeName => "DiscoveryTreeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _actionMode = bind.EnumField("actionMode");
        _actionTime = bind.Field<BigDouble>("actionTime");
        _rerollsLeft = bind.Field<int>("rerollsLeft");
        _usedRerolls = bind.Field<bool>("usedRerollsLastDiscover");
        _selectedChoice = bind.ReferenceGuid("selectedChoiceId");
        _overrideRerolls = bind.ReferenceGuid("overrideDiscoveryRerolls");
        _overrideChoices = bind.ReferenceGuid("overrideDiscoveryChoices");
        _additionalDiscoveryChoices = bind.Field<int>("additionalDiscoveryChoices");
        _discoveryBonusLevelCost = bind.Field<int>("discoveryBonusLevelCost");
        _debugMode = bind.Field<bool>("debugMode");
        _totalDiscoveredCount = bind.Field<int>("totalDiscoveredCount");
        _poolDiscoveredCount = bind.Field<int>("poolDiscoveredCount");
        _hasRequiredDiscovery = bind.Field<bool>("hasRequiredDiscovery");
        _hasRemainingDiscovery = bind.Field<bool>("hasRemainingDiscovery");
        _hasCompletedAllDiscoveries = bind.Field<bool>("hasCompletedAllDiscoveries");
        return bind.Failure;
    }

    internal override WorldDiscoveryTree Read(object entity) =>
        new(
            _id!(entity),
            _actionMode!(entity),
            _actionTime!(entity),
            _rerollsLeft!(entity),
            _usedRerolls!(entity),
            _selectedChoice!(entity),
            _overrideRerolls!(entity),
            _overrideChoices!(entity),
            _additionalDiscoveryChoices!(entity),
            _discoveryBonusLevelCost!(entity),
            _debugMode!(entity),
            _totalDiscoveredCount!(entity),
            _poolDiscoveredCount!(entity),
            _hasRequiredDiscovery!(entity),
            _hasRemainingDiscovery!(entity),
            _hasCompletedAllDiscoveries!(entity));
}
