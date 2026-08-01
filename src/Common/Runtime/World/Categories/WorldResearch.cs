using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One research entry as published. Persisted state is read directly; base and effective
/// requirement levels are results returned by the game's own evaluators. The only suite-derived
/// field is their explicitly labeled difference.
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
        bool visible,
        bool complete,
        bool canDevelop,
        bool withinDevelopRange,
        bool meetsLevelRequirements,
        bool stillHasLeeway,
        bool belowArtificialMaxLevel,
        bool belowMaxInvestmentLevel,
        int purchasedLevels,
        int baseLevel,
        int bonusLevel,
        int totalLevel,
        int artificialMaxLevel,
        bool hiddenLevel,
        int levelVisibilityRange,
        int requiredStagesCached,
        BigDouble requiredTimeCached,
        int baseRequirementLevel,
        int effectiveRequirementLevel,
        PublicationTable<WorldResearchRequirementAdjustment> requirementAdjustments,
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
        Visible = visible;
        Complete = complete;
        CanDevelop = canDevelop;
        WithinDevelopRange = withinDevelopRange;
        MeetsLevelRequirements = meetsLevelRequirements;
        StillHasLeeway = stillHasLeeway;
        BelowArtificialMaxLevel = belowArtificialMaxLevel;
        BelowMaxInvestmentLevel = belowMaxInvestmentLevel;
        PurchasedLevels = purchasedLevels;
        BaseLevel = baseLevel;
        BonusLevel = bonusLevel;
        TotalLevel = totalLevel;
        ArtificialMaxLevel = artificialMaxLevel;
        HiddenLevel = hiddenLevel;
        LevelVisibilityRange = levelVisibilityRange;
        RequiredStagesCached = requiredStagesCached;
        RequiredTimeCached = requiredTimeCached;
        BaseRequirementLevel = baseRequirementLevel;
        EffectiveRequirementLevel = effectiveRequirementLevel;
        RequirementAdjustments = requirementAdjustments;
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

    /// <summary>Read-only native ResearchSO evaluator results from the same collector generation.</summary>
    internal bool Visible { get; }

    internal bool Complete { get; }

    internal bool CanDevelop { get; }

    internal bool WithinDevelopRange { get; }

    internal bool MeetsLevelRequirements { get; }

    internal bool StillHasLeeway { get; }

    internal bool BelowArtificialMaxLevel { get; }

    internal bool BelowMaxInvestmentLevel { get; }

    /// <summary>The game's distinct level accessors; completion uses BaseLevel, not TotalLevel.</summary>
    internal int PurchasedLevels { get; }

    internal int BaseLevel { get; }

    internal int BonusLevel { get; }

    internal int TotalLevel { get; }

    internal int ArtificialMaxLevel { get; }

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
    /// The level supplied to the native requirement evaluator before and after its exact
    /// <c>GetRequirementLevelMod().Adjust(...)</c> fold. Challenge effects are persistent/passive,
    /// so projecting only <c>activeModifiers.Count</c> hid their adjustment while the native tooltip
    /// and evaluator both applied it.
    /// </summary>
    internal int BaseRequirementLevel { get; }

    internal int EffectiveRequirementLevel { get; }

    internal int RequirementLevelAdjustment => EffectiveRequirementLevel - BaseRequirementLevel;

    /// <summary>
    /// Every direct modifier on this research's <c>requirementsAdjust</c> record, including whether
    /// it is passive and the stable identity/type of its native tooltip source when available.
    /// Research-type adjustments are already reflected in <see cref="EffectiveRequirementLevel"/>
    /// by the native evaluator; their authored records are not direct members of this research and
    /// are deliberately not misattributed here.
    /// </summary>
    internal PublicationTable<WorldResearchRequirementAdjustment> RequirementAdjustments { get; }

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
/// Research entries. Read-only native evaluators supply visibility, completion, development gates,
/// distinct level values, and requirement levels from one main-thread collection generation; the
/// collector does not reimplement those verdicts.
/// </summary>
/// <remarks>
/// Mutable progress fields remain plain reads. The predicate and accessor calls are side-effect-free
/// queries over that same native state, and every reflected member is pinned by the installed-game
/// contract manifest.
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
    private Func<object, bool>? _visible;
    private Func<object, bool>? _complete;
    private Func<object, bool>? _canDevelop;
    private Func<object, bool>? _withinDevelopRange;
    private Func<object, bool>? _meetsLevelRequirements;
    private Func<object, bool>? _stillHasLeeway;
    private Func<object, bool>? _belowArtificialMaxLevel;
    private Func<object, bool>? _belowMaxInvestmentLevel;
    private Func<object, int>? _purchasedLevels;
    private Func<object, int>? _baseLevel;
    private Func<object, int>? _bonusLevel;
    private Func<object, int>? _totalLevel;
    private Func<object, int>? _artificialMaxLevel;
    private Func<object, bool>? _hiddenLevel;
    private Func<object, int>? _levelVisibilityRange;
    private Func<object, int>? _requiredStagesCached;
    private Func<object, BigDouble>? _requiredTimeCached;
    private Func<object, int>? _baseRequirementLevel;
    private Func<object, int>? _effectiveRequirementLevel;
    private NativeModifierAdjustmentAccess? _requirementAdjustments;
    private string _requirementAdjustmentFailure = string.Empty;
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
        _visible = bind.Call<bool>("IsVisible");
        _complete = bind.Call<bool>("IsComplete");
        _canDevelop = bind.Call<bool>("CanDevelop");
        _withinDevelopRange = bind.Call<bool>("IsWithinDevelopRange");
        _meetsLevelRequirements = bind.Call<bool>("MeetsLevelRequirements");
        _stillHasLeeway = bind.Call<bool>("StillHasLeeway");
        _belowArtificialMaxLevel = bind.Call<bool>("IsBelowArtificialMaxLevel");
        _belowMaxInvestmentLevel = bind.Call<bool>("IsBelowMaxInvestmentLevel");
        _purchasedLevels = bind.Call<int>("GetPurchasedLevels");
        _baseLevel = bind.Call<int>("GetBaseLevel");
        _bonusLevel = bind.Call<int>("GetBonusLevels");
        _totalLevel = bind.Call<int>("GetLevel");
        _artificialMaxLevel = bind.Call<int>("GetArtificialMaxLevel");
        _hiddenLevel = bind.Field<bool>("hiddenLevel");
        _levelVisibilityRange = bind.Field<int>("levelVisibilityRange");
        _requiredStagesCached = bind.Field<int>("requiredStagesCached");
        _requiredTimeCached = bind.Field<BigDouble>("requiredTimeCached");

        // Both are native, read-only evaluators. GetRequirementLevel folds research-type records and
        // this research's passive and active requirementsAdjust sets exactly as the tooltip does.
        _baseRequirementLevel = bind.Call<int>("GetBaseLevel");
        _effectiveRequirementLevel = bind.Call<int>("GetRequirementLevel");
        _requirementAdjustments = NativeModifierAdjustmentAccess.Bind(
            type,
            "requirementsAdjust",
            out _requirementAdjustmentFailure);
        _bonusLevels = bind.ModifierRecord("bonusLevels");
        _baseLevels = bind.ModifierRecord("baseLevels");
        _power = bind.ModifierRecord("power");
        _maxLevelCap = bind.ModifierRecord("maxLevelCap");
        _leewayPoints = bind.ModifierRecord("leewayPoints");
        if (_requirementAdjustmentFailure.Length == 0) return bind.Failure;
        var adjustmentFailure = TypeName + " did not expose " +
            _requirementAdjustmentFailure + " on this build";
        return bind.Failure.Length == 0
            ? adjustmentFailure
            : bind.Failure + "; " + adjustmentFailure;
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
            _visible!(entity),
            _complete!(entity),
            _canDevelop!(entity),
            _withinDevelopRange!(entity),
            _meetsLevelRequirements!(entity),
            _stillHasLeeway!(entity),
            _belowArtificialMaxLevel!(entity),
            _belowMaxInvestmentLevel!(entity),
            _purchasedLevels!(entity),
            _baseLevel!(entity),
            _bonusLevel!(entity),
            _totalLevel!(entity),
            _artificialMaxLevel!(entity),
            _hiddenLevel!(entity),
            _levelVisibilityRange!(entity),
            _requiredStagesCached!(entity),
            _requiredTimeCached!(entity),
            _baseRequirementLevel!(entity),
            _effectiveRequirementLevel!(entity),
            _requirementAdjustments!.Read(entity),
            new RawResearchModifiers(
                _bonusLevels!(entity),
                _baseLevels!(entity),
                _power!(entity),
                _maxLevelCap!(entity),
                _leewayPoints!(entity)));
}

/// <summary>One modifier contributing directly to a research requirement-level adjustment.</summary>
internal readonly struct WorldResearchRequirementAdjustment
{
    internal WorldResearchRequirementAdjustment(
        Guid modifierId,
        Guid sourceId,
        string sourceNativeType,
        int modifierType,
        BigDouble amount,
        int order,
        bool passive)
    {
        ModifierId = modifierId;
        SourceId = sourceId;
        SourceNativeType = sourceNativeType ?? string.Empty;
        ModifierType = modifierType;
        Amount = amount;
        Order = order;
        Passive = passive;
    }

    internal Guid ModifierId { get; }

    internal Guid SourceId { get; }

    internal string SourceNativeType { get; }

    internal int ModifierType { get; }

    internal BigDouble Amount { get; }

    internal int Order { get; }

    internal bool Passive { get; }
}
