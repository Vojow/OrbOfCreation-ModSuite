using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One ritual as published. The rolled effects themselves (<c>ritualInstances</c>) and the spoils on
/// offer (<c>currentSpoils</c>) are variable-size and stay behind; how many effects are running does
/// not, because that is the game's own definition of a ritual being active.
/// </summary>
internal readonly struct WorldRitual : IWorldEntity
{
    internal WorldRitual(
        Guid ritualId,
        bool discovered,
        bool inBattle,
        int activeInstances,
        int reachedLevel,
        int lastReachedLevel,
        int selectedLevel,
        int wavesCompleted,
        int discoveryRarityLevel,
        int critLevel,
        int echoLevel,
        int chainLevel,
        int durationRewardBlocks,
        BigDouble battleTotalWeight,
        in RawRitualModifiers modifiers,
        bool hideEndScreenResults,
        bool isDiscoverRequired,
        bool forceLevel,
        int forceLevelValue,
        int baseWaves,
        int maxWaves,
        double baseWeight,
        int minimumEffectLevel,
        WorldDiscoverableDecision discovery = default)
    {
        RitualId = ritualId;
        Discovered = discovered;
        InBattle = inBattle;
        ActiveInstances = activeInstances;
        ReachedLevel = reachedLevel;
        LastReachedLevel = lastReachedLevel;
        SelectedLevel = selectedLevel;
        WavesCompleted = wavesCompleted;
        DiscoveryRarityLevel = discoveryRarityLevel;
        CritLevel = critLevel;
        EchoLevel = echoLevel;
        ChainLevel = chainLevel;
        DurationRewardBlocks = durationRewardBlocks;
        BattleTotalWeight = battleTotalWeight;
        Modifiers = modifiers;
        HideEndScreenResults = hideEndScreenResults;
        IsDiscoverRequired = isDiscoverRequired;
        ForceLevel = forceLevel;
        ForceLevelValue = forceLevelValue;
        BaseWaves = baseWaves;
        MaxWaves = maxWaves;
        BaseWeight = baseWeight;
        MinimumEffectLevel = minimumEffectLevel;
        Discovery = discovery;
    }

    internal Guid RitualId { get; }

    public Guid EntityId => RitualId;

    /// <summary>
    /// Whether the ritual is unlocked. This is the whole of it: the game's <c>IsAvailable()</c> and
    /// <c>IsVisible()</c> both forward to <c>IsDiscovered()</c>, which returns this field.
    /// </summary>
    internal bool Discovered { get; }

    /// <summary>Whether a run is in progress right now.</summary>
    internal bool InBattle { get; }

    /// <summary>
    /// How many rolled effects are currently running. The game's <c>HasActiveInstances()</c> and
    /// <c>IsDurationActive()</c> are both <c>ritualInstances.Count &gt; 0</c>, so this is what "the
    /// ritual is active" means for a completed run whose reward is still ticking — a different
    /// question from <see cref="InBattle"/>, and the one a re-run decision turns on.
    /// </summary>
    internal int ActiveInstances { get; }

    /// <summary>The best level ever reached, the level reached in the last run, and the level chosen.</summary>
    internal int ReachedLevel { get; }

    internal int LastReachedLevel { get; }

    internal int SelectedLevel { get; }

    /// <summary>Waves cleared in the run in progress.</summary>
    internal int WavesCompleted { get; }

    internal int DiscoveryRarityLevel { get; }

    /// <summary>The tiers the run in progress has rolled up to.</summary>
    internal int CritLevel { get; }

    internal int EchoLevel { get; }

    internal int ChainLevel { get; }

    /// <summary>
    /// How many duration rewards the ritual defines. <c>IsDurationRitual()</c> is
    /// <c>durationRewardBlocks.Count &gt; 0</c>, so this is what says whether
    /// <see cref="ActiveInstances"/> is a question worth asking about this ritual at all.
    /// </summary>
    internal int DurationRewardBlocks { get; }

    /// <summary>
    /// The total enemy weight of the run in progress — the denominator the game divides destroyed
    /// weight by to advance completion.
    /// </summary>
    internal BigDouble BattleTotalWeight { get; }

    internal RawRitualModifiers Modifiers { get; }

    /// <summary>
    /// The rest of the ritual's own numbers: the wave and level bounds a run is fought within, and
    /// whether the level is forced rather than chosen.
    /// </summary>
    internal bool HideEndScreenResults { get; }

    internal bool IsDiscoverRequired { get; }

    internal bool ForceLevel { get; }

    internal int ForceLevelValue { get; }

    internal int BaseWaves { get; }

    internal int MaxWaves { get; }

    internal double BaseWeight { get; }

    internal int MinimumEffectLevel { get; }

    internal WorldDiscoverableDecision Discovery { get; }
}

/// <summary>A ritual's cached modifier records — what a run at the chosen level is worth.</summary>
internal readonly struct RawRitualModifiers
{
    internal RawRitualModifiers(
        BigDouble power,
        BigDouble speed,
        BigDouble special,
        BigDouble durationMod,
        BigDouble echoRating,
        BigDouble echoPower,
        BigDouble critRating,
        BigDouble critPower,
        BigDouble critDurationMod,
        BigDouble chainLengthBonus,
        BigDouble chainPower,
        BigDouble completionCostMod,
        BigDouble completionRateMod)
    {
        Power = power;
        Speed = speed;
        Special = special;
        DurationMod = durationMod;
        EchoRating = echoRating;
        EchoPower = echoPower;
        CritRating = critRating;
        CritPower = critPower;
        CritDurationMod = critDurationMod;
        ChainLengthBonus = chainLengthBonus;
        ChainPower = chainPower;
        CompletionCostMod = completionCostMod;
        CompletionRateMod = completionRateMod;
    }

    internal BigDouble Power { get; }

    internal BigDouble Speed { get; }

    internal BigDouble Special { get; }

    /// <summary>How long a rolled effect lasts, before and after a crit.</summary>
    internal BigDouble DurationMod { get; }

    /// <summary>The chance of an echo and what one is worth, then the same for crits.</summary>
    internal BigDouble EchoRating { get; }

    internal BigDouble EchoPower { get; }

    internal BigDouble CritRating { get; }

    internal BigDouble CritPower { get; }

    internal BigDouble CritDurationMod { get; }

    /// <summary>How many rituals may chain, and what chaining is worth.</summary>
    internal BigDouble ChainLengthBonus { get; }

    internal BigDouble ChainPower { get; }

    /// <summary>What finishing a run costs, and how fast the completion fills.</summary>
    internal BigDouble CompletionCostMod { get; }

    internal BigDouble CompletionRateMod { get; }
}

internal sealed class WorldRitualBinder : WorldPlainBinder<WorldRitual>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _discovered;
    private Func<object, bool>? _inBattle;
    private Func<object, int>? _activeInstances;
    private Func<object, int>? _reachedLevel;
    private Func<object, int>? _lastReachedLevel;
    private Func<object, int>? _selectedLevel;
    private Func<object, int>? _wavesCompleted;
    private Func<object, int>? _discRarityLevel;
    private Func<object, int>? _critLevel;
    private Func<object, int>? _echoLevel;
    private Func<object, int>? _chainLevel;
    private Func<object, int>? _durationRewardBlocks;
    private Func<object, BigDouble>? _battleTotalWeight;
    private Func<object, BigDouble>? _power;
    private Func<object, BigDouble>? _speed;
    private Func<object, BigDouble>? _special;
    private Func<object, BigDouble>? _durationMod;
    private Func<object, BigDouble>? _echoRating;
    private Func<object, BigDouble>? _echoPower;
    private Func<object, BigDouble>? _critRating;
    private Func<object, BigDouble>? _critPower;
    private Func<object, BigDouble>? _critDurationMod;
    private Func<object, BigDouble>? _chainLengthBonus;
    private Func<object, BigDouble>? _chainPower;
    private Func<object, BigDouble>? _completionCostMod;
    private Func<object, BigDouble>? _completionRateMod;
    private Func<object, bool>? _hideEndScreenResults;
    private Func<object, bool>? _isDiscoverRequired;
    private Func<object, bool>? _forceLevel;
    private Func<object, int>? _forceLevelValue;
    private Func<object, int>? _baseWaves;
    private Func<object, int>? _maxWaves;
    private Func<object, double>? _baseWeight;
    private Func<object, int>? _minimumEffectLevel;
    private WorldDiscoverableBinding? _discovery;

    internal override string Category => "rituals";

    internal override string TypeName => "RitualSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _discovered = bind.Field<bool>("discovered");
        _inBattle = bind.Field<bool>("inBattle");

        // The elements are variable-size and stay behind; the count is what HasActiveInstances()
        // returns, and is the game's own test for the ritual being active.
        _activeInstances = bind.CollectionCount("ritualInstances");
        _reachedLevel = bind.Field<int>("reachedLevel");
        _lastReachedLevel = bind.Field<int>("lastReachedLevel");
        _selectedLevel = bind.Field<int>("selectedLevel");
        _wavesCompleted = bind.Field<int>("wavesCompleted");
        _discRarityLevel = bind.Field<int>("discRarityLevel");
        _critLevel = bind.Field<int>("critLevel");
        _echoLevel = bind.Field<int>("echoLevel");
        _chainLevel = bind.Field<int>("chainLevel");
        _durationRewardBlocks = bind.CollectionCount("durationRewardBlocks");
        _battleTotalWeight = bind.Field<BigDouble>("battleTotalWeight");
        _power = bind.ModifierRecord("power");
        _speed = bind.ModifierRecord("speed");
        _special = bind.ModifierRecord("special");
        _durationMod = bind.ModifierRecord("durationMod");
        _echoRating = bind.ModifierRecord("echoRating");
        _echoPower = bind.ModifierRecord("echoPower");
        _critRating = bind.ModifierRecord("critRating");
        _critPower = bind.ModifierRecord("critPower");
        _critDurationMod = bind.ModifierRecord("critDurationMod");
        _chainLengthBonus = bind.ModifierRecord("chainLengthBonus");
        _chainPower = bind.ModifierRecord("chainPower");
        _completionCostMod = bind.ModifierRecord("completionCostMod");
        _completionRateMod = bind.ModifierRecord("completionRateMod");
        _hideEndScreenResults = bind.Field<bool>("hideEndScreenResults");
        _isDiscoverRequired = bind.Field<bool>("isDiscoverRequired");
        _forceLevel = bind.Field<bool>("forceLevel");
        _forceLevelValue = bind.Field<int>("forceLevelValue");
        _baseWaves = bind.Field<int>("baseWaves");
        _maxWaves = bind.Field<int>("maxWaves");
        _baseWeight = bind.Field<double>("baseWeight");
        _minimumEffectLevel = bind.Field<int>("minimumEffectLevel");
        _discovery = new WorldDiscoverableBinding(type, TypeName);
        return Join(bind.Failure, _discovery.Failure);
    }

    internal override WorldRitual Read(object entity) =>
        new(
            _id!(entity),
            _discovered!(entity),
            _inBattle!(entity),
            _activeInstances!(entity),
            _reachedLevel!(entity),
            _lastReachedLevel!(entity),
            _selectedLevel!(entity),
            _wavesCompleted!(entity),
            _discRarityLevel!(entity),
            _critLevel!(entity),
            _echoLevel!(entity),
            _chainLevel!(entity),
            _durationRewardBlocks!(entity),
            _battleTotalWeight!(entity),
            new RawRitualModifiers(
                _power!(entity),
                _speed!(entity),
                _special!(entity),
                _durationMod!(entity),
                _echoRating!(entity),
                _echoPower!(entity),
                _critRating!(entity),
                _critPower!(entity),
                _critDurationMod!(entity),
                _chainLengthBonus!(entity),
                _chainPower!(entity),
                _completionCostMod!(entity),
                _completionRateMod!(entity)),
            _hideEndScreenResults!(entity),
            _isDiscoverRequired!(entity),
            _forceLevel!(entity),
            _forceLevelValue!(entity),
            _baseWaves!(entity),
            _maxWaves!(entity),
            _baseWeight!(entity),
            _minimumEffectLevel!(entity),
            _discovery!.Read(entity));

    private static string Join(string left, string right) =>
        left.Length == 0 ? right : right.Length == 0 ? left : left + "; " + right;
}
