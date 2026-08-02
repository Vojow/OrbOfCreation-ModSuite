using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One challenge as published: its level, the state it is in, and the shape of its reward.</summary>
internal readonly struct WorldChallenge : IWorldEntity
{
    internal WorldChallenge(
        Guid challengeId,
        int level,
        int state,
        bool seen,
        bool rewardQueued,
        int maxLevel,
        int weight,
        double difficulty,
        double baseReward,
        bool availableToRun = false,
        bool completedOnce = false,
        bool maximumLevelReached = false,
        BigDouble nextDifficulty = default,
        BigDouble nextReward = default)
    {
        ChallengeId = challengeId;
        Level = level;
        State = state;
        Seen = seen;
        RewardQueued = rewardQueued;
        MaxLevel = maxLevel;
        Weight = weight;
        Difficulty = difficulty;
        BaseReward = baseReward;
        AvailableToRun = availableToRun;
        CompletedOnce = completedOnce;
        MaximumLevelReached = maximumLevelReached;
        NextDifficulty = nextDifficulty;
        NextReward = nextReward;
    }

    internal Guid ChallengeId { get; }

    public Guid EntityId => ChallengeId;

    internal int Level { get; }

    internal int State { get; }

    internal bool Seen { get; }

    internal bool RewardQueued { get; }

    internal int MaxLevel { get; }

    internal int Weight { get; }

    internal double Difficulty { get; }

    internal double BaseReward { get; }

    internal bool AvailableToRun { get; }

    internal bool CompletedOnce { get; }

    internal bool MaximumLevelReached { get; }

    internal BigDouble NextDifficulty { get; }

    internal BigDouble NextReward { get; }
}

internal sealed class WorldChallengeBinder : WorldPlainBinder<WorldChallenge>
{
    private Func<object, Guid>? _id;
    private Func<object, int>? _level;
    private Func<object, int>? _state;
    private Func<object, bool>? _seen;
    private Func<object, bool>? _rewardQueued;
    private Func<object, int>? _maxLevel;
    private Func<object, int>? _weight;
    private Func<object, double>? _difficulty;
    private Func<object, double>? _baseReward;
    private Func<object, bool>? _availableToRun;
    private Func<object, bool>? _completedOnce;
    private Func<object, bool>? _maximumLevelReached;
    private Func<object, BigDouble>? _nextDifficulty;
    private Func<object, BigDouble>? _nextReward;

    internal override string Category => "challenges";

    internal override string TypeName => "ChallengeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _level = bind.Field<int>("level");
        _state = bind.EnumField("state");
        _seen = bind.Field<bool>("hasBeenSeen");
        _rewardQueued = bind.Field<bool>("rewardQueued");
        _maxLevel = bind.Field<int>("maxLevel");
        _weight = bind.Field<int>("weight");
        _difficulty = bind.Field<double>("difficulty");
        _baseReward = bind.Field<double>("baseReward");
        _availableToRun = bind.Call<bool>("IsAvailableToRun");
        _completedOnce = bind.Call<bool>("IsCompletedOnce");
        _maximumLevelReached = bind.Call<bool>("IsMaxLevel");
        _nextDifficulty = bind.Call<BigDouble>("GetDifficulty");
        _nextReward = bind.Call<BigDouble>("GetNextInstanceBaseReward");
        return bind.Failure;
    }

    internal override WorldChallenge Read(object entity) =>
        new(
            _id!(entity),
            _level!(entity),
            _state!(entity),
            _seen!(entity),
            _rewardQueued!(entity),
            _maxLevel!(entity),
            _weight!(entity),
            _difficulty!(entity),
            _baseReward!(entity),
            _availableToRun!(entity),
            _completedOnce!(entity),
            _maximumLevelReached!(entity),
            _nextDifficulty!(entity),
            _nextReward!(entity));
}
