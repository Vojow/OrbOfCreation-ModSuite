using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One research entry as published. Nothing is derived: research state is what the game persists,
/// and the one number the game itself composes — queued levels including the one in flight — is
/// carried here as the game composes it.
/// </summary>
internal readonly struct WorldResearch : IWorldEntity
{
    internal WorldResearch(
        Guid researchId,
        int level,
        int queuedLevels,
        int researchStage,
        int selfBonusLevels,
        int maxLevel,
        double researchTime,
        bool isDeveloping,
        bool isActive,
        bool flagged,
        bool available,
        bool hiddenLevel,
        int levelVisibilityRange,
        int requiredStagesCached,
        BigDouble requiredTimeCached,
        int requirementsAdjustModifiers,
        in RawResearchModifiers modifiers)
    {
        ResearchId = researchId;
        Level = level;
        QueuedLevels = queuedLevels;
        ResearchStage = researchStage;
        SelfBonusLevels = selfBonusLevels;
        MaxLevel = maxLevel;
        ResearchTime = researchTime;
        IsDeveloping = isDeveloping;
        IsActive = isActive;
        Flagged = flagged;
        Available = available;
        HiddenLevel = hiddenLevel;
        LevelVisibilityRange = levelVisibilityRange;
        RequiredStagesCached = requiredStagesCached;
        RequiredTimeCached = requiredTimeCached;
        RequirementsAdjustModifiers = requirementsAdjustModifiers;
        Modifiers = modifiers;
    }

    internal Guid ResearchId { get; }

    public Guid EntityId => ResearchId;

    internal int Level { get; }

    /// <summary>
    /// Levels queued, not counting the one in flight. The game's <c>GetQueuedLevels()</c> adds one
    /// when <see cref="IsDeveloping"/>, which is a composition a consumer can make and this row
    /// deliberately does not bake in — the two numbers answer different questions.
    /// </summary>
    internal int QueuedLevels { get; }

    /// <summary>How far through the current level's stages the entry has progressed.</summary>
    internal int ResearchStage { get; }

    internal int SelfBonusLevels { get; }

    /// <summary>The level ceiling before <c>MaxLevelCap</c> is applied.</summary>
    internal int MaxLevel { get; }

    /// <summary>Seconds one level takes at the base rate.</summary>
    internal double ResearchTime { get; }

    /// <summary>Whether a level is currently developing.</summary>
    internal bool IsDeveloping { get; }

    /// <summary>Whether the entry is running rather than paused.</summary>
    internal bool IsActive { get; }

    /// <summary>The player's own marker on this entry.</summary>
    internal bool Flagged { get; }

    /// <summary>Whether prerequisites currently permit developing.</summary>
    internal bool Available { get; }

    /// <summary>Whether the game hides this research's level, and how far around it levels are shown.</summary>
    internal bool HiddenLevel { get; }

    internal int LevelVisibilityRange { get; }

    /// <summary>
    /// The stage count and time the game last computed for the current level. Cached rather than
    /// persisted, so they are absent from the save record and present here.
    /// </summary>
    internal int RequiredStagesCached { get; }

    internal BigDouble RequiredTimeCached { get; }

    /// <summary>
    /// How many active modifiers adjust this research's requirements. <c>requirementsAdjust</c> is a
    /// plain <c>ModifierRecord</c> with no cached value of its own, so the count — the game's
    /// <c>HasActiveElements()</c> — is what there is to read.
    /// </summary>
    internal int RequirementsAdjustModifiers { get; }

    internal RawResearchModifiers Modifiers { get; }
}

/// <summary>A research entry's cached modifier records.</summary>
internal readonly struct RawResearchModifiers
{
    internal RawResearchModifiers(
        BigDouble bonusLevels,
        BigDouble baseLevels,
        BigDouble power,
        BigDouble maxLevelCap,
        BigDouble leewayPoints)
    {
        BonusLevels = bonusLevels;
        BaseLevels = baseLevels;
        Power = power;
        MaxLevelCap = maxLevelCap;
        LeewayPoints = leewayPoints;
    }

    /// <summary>Levels granted from elsewhere, and levels the entry starts with.</summary>
    internal BigDouble BonusLevels { get; }

    internal BigDouble BaseLevels { get; }

    /// <summary>Percent scaling on what one level does.</summary>
    internal BigDouble Power { get; }

    /// <summary>How far the level ceiling has been raised, and how much requirement slack is allowed.</summary>
    internal BigDouble MaxLevelCap { get; }

    internal BigDouble LeewayPoints { get; }
}

/// <summary>
/// Research entries. Nothing is derived, so the reading is the row.
/// </summary>
/// <remarks>
/// Progress state is read as fields rather than through the game's predicates, all three of which are
/// nothing but the field: <c>IsDeveloping() => isDeveloping</c> and <c>IsActive() => isActive</c>.
/// <c>IsAvailable()</c> is the exception — it walks a prerequisite graph — and is the last call in
/// this binder still waiting on a port.
/// </remarks>
internal sealed class WorldResearchBinder : WorldPlainBinder<WorldResearch>
{
    private Func<object, Guid>? _id;
    private Func<object, int>? _level;
    private Func<object, int>? _queuedLevels;
    private Func<object, int>? _researchStage;
    private Func<object, int>? _selfBonusLevels;
    private Func<object, int>? _maxLevel;
    private Func<object, double>? _researchTime;
    private Func<object, bool>? _isDeveloping;
    private Func<object, bool>? _isActive;
    private Func<object, bool>? _flagged;
    private Func<object, bool>? _available;
    private Func<object, bool>? _hiddenLevel;
    private Func<object, int>? _levelVisibilityRange;
    private Func<object, int>? _requiredStagesCached;
    private Func<object, BigDouble>? _requiredTimeCached;
    private Func<object, int>? _requirementsAdjust;
    private Func<object, BigDouble>? _bonusLevels;
    private Func<object, BigDouble>? _baseLevels;
    private Func<object, BigDouble>? _power;
    private Func<object, BigDouble>? _maxLevelCap;
    private Func<object, BigDouble>? _leewayPoints;

    internal override string Category => "research";

    internal override string TypeName => "ResearchSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _level = bind.Field<int>("level");
        _queuedLevels = bind.Field<int>("queuedLevels");
        _researchStage = bind.Field<int>("researchStage");
        _selfBonusLevels = bind.Field<int>("selfBonusLevels");
        _maxLevel = bind.Field<int>("maxLevel");
        _researchTime = bind.Field<double>("researchTime");
        _isDeveloping = bind.Field<bool>("isDeveloping");
        _isActive = bind.Field<bool>("isActive");
        _flagged = bind.Field<bool>("flagged");
        _available = bind.Call<bool>("IsAvailable");
        _hiddenLevel = bind.Field<bool>("hiddenLevel");
        _levelVisibilityRange = bind.Field<int>("levelVisibilityRange");
        _requiredStagesCached = bind.Field<int>("requiredStagesCached");
        _requiredTimeCached = bind.Field<BigDouble>("requiredTimeCached");

        // A plain ModifierRecord with no cached value of its own; the active count is the fact.
        _requirementsAdjust = bind.NestedCollectionCount("requirementsAdjust", "activeModifiers");
        _bonusLevels = bind.ModifierRecord("bonusLevels");
        _baseLevels = bind.ModifierRecord("baseLevels");
        _power = bind.ModifierRecord("power");
        _maxLevelCap = bind.ModifierRecord("maxLevelCap");
        _leewayPoints = bind.ModifierRecord("leewayPoints");
        return bind.Failure;
    }

    internal override WorldResearch Read(object entity) =>
        new(
            _id!(entity),
            _level!(entity),
            _queuedLevels!(entity),
            _researchStage!(entity),
            _selfBonusLevels!(entity),
            _maxLevel!(entity),
            _researchTime!(entity),
            _isDeveloping!(entity),
            _isActive!(entity),
            _flagged!(entity),
            _available!(entity),
            _hiddenLevel!(entity),
            _levelVisibilityRange!(entity),
            _requiredStagesCached!(entity),
            _requiredTimeCached!(entity),
            _requirementsAdjust!(entity),
            new RawResearchModifiers(
                _bonusLevels!(entity),
                _baseLevels!(entity),
                _power!(entity),
                _maxLevelCap!(entity),
                _leewayPoints!(entity)));
}
