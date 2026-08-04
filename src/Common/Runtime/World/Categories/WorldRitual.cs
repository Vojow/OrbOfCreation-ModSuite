using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

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
        WorldDiscoverableDecision discovery = default,
        WorldRitualDecision decision = default)
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
        Decision = decision;
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

    /// <summary>The currently staged player decision; prices exist only for the selected row.</summary>
    internal WorldRitualDecision Decision { get; }
}

internal readonly struct WorldRitualCost
{
    internal WorldRitualCost(Guid resourceId, BigDouble cost)
    {
        ResourceId = resourceId;
        Cost = cost;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Cost { get; }
}

internal readonly struct WorldRitualDecision
{
    private readonly PublicationTable<WorldRitualCost>? _activationCosts;
    private readonly PublicationTable<WorldRitualCost>? _completionCosts;

    internal WorldRitualDecision(
        bool selected,
        int maximumStartingLevel,
        bool usageRequirementsMet,
        bool activationAffordable,
        PublicationTable<WorldRitualCost> activationCosts,
        PublicationTable<WorldRitualCost> completionCosts)
    {
        Selected = selected;
        MaximumStartingLevel = Math.Max(maximumStartingLevel, 0);
        UsageRequirementsMet = usageRequirementsMet;
        ActivationAffordable = activationAffordable;
        _activationCosts = activationCosts ?? throw new ArgumentNullException(nameof(activationCosts));
        _completionCosts = completionCosts ?? throw new ArgumentNullException(nameof(completionCosts));
    }

    internal bool Selected { get; }
    internal int MaximumStartingLevel { get; }
    internal bool UsageRequirementsMet { get; }
    internal bool ActivationAffordable { get; }
    internal PublicationTable<WorldRitualCost> ActivationCosts =>
        _activationCosts ?? PublicationTable<WorldRitualCost>.Empty;
    internal PublicationTable<WorldRitualCost> CompletionCosts =>
        _completionCosts ?? PublicationTable<WorldRitualCost>.Empty;
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
    private readonly Func<string, Type?> _resolveType;
    private WorldRitualDecisionBinding? _decision;

    internal WorldRitualBinder(Func<string, Type?> resolveType)
    {
        _resolveType = resolveType ?? throw new ArgumentNullException(nameof(resolveType));
    }

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
        _decision = new WorldRitualDecisionBinding(type, _resolveType);
        return Join(bind.Failure, _discovery.Failure, _decision.Failure);
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
            _discovery!.Read(entity),
            _decision!.Read(entity));

    private static string Join(params string[] values)
    {
        var result = string.Empty;
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index].Length == 0) continue;
            result = result.Length == 0 ? values[index] : result + "; " + values[index];
        }
        return result;
    }
}

/// <summary>Reads only the selected ritual's level and two screen-visible price lists.</summary>
internal sealed class WorldRitualDecisionBinding
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _managerType;
    private readonly Func<object?>? _manager;
    private readonly Func<object, object?>? _selectedVariable;
    private readonly Func<object, object, bool>? _isSelected;
    private readonly Func<object, int>? _maximumStartingLevel;
    private readonly Func<object, bool>? _usageRequirementsMet;
    private readonly Func<object, object?>? _activationCost;
    private readonly Func<object, object?>? _fillList;
    private readonly Func<object, IList?>? _fillEntries;
    private readonly Func<object, object?>? _fillResource;
    private readonly Func<object, BigDouble>? _fillCapacity;
    private readonly Func<object, bool>? _hasEnough;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, object?>? _costResource;
    private readonly Func<object, BigDouble>? _costValue;
    private readonly Func<object, Guid>? _resourceId;

    internal WorldRitualDecisionBinding(Type ritualType, Func<string, Type?> resolveType)
    {
        _managerType = resolveType("RitualManager");
        var variableType = resolveType("RitualVariable");
        var costType = resolveType("ResourceCostList");
        var tupleType = resolveType("ResourceTuple");
        var resourceType = resolveType("ResourceSO");
        var fillType = resolveType("ResourceFillList");
        var fillEntryType = resolveType("ResourceFillList+ResourceFillEntry");
        if (_managerType is null || variableType is null || costType is null ||
            tupleType is null || resourceType is null || fillType is null || fillEntryType is null)
        {
            Failure = "Ritual decision types were unavailable";
            return;
        }

        _manager = StaticReference(_managerType, "instance", _managerType);
        _selectedVariable = Reference(_managerType, "selectedRitual", variableType);
        _isSelected = CallWithReference<bool>(variableType, "IsItem", ritualType);
        var ritual = new WorldMemberBinding(ritualType, "RitualSO");
        _maximumStartingLevel = ritual.Call<int>("GetMaxSelectedLevel");
        _usageRequirementsMet = ritual.Call<bool>("HasMetUsageRequirements");
        _activationCost = ritual.CallObject("GetActivationCost", costType);
        _fillList = ritual.Reference("resourceFillList", fillType);

        var cost = new WorldMemberBinding(costType, "ResourceCostList");
        _hasEnough = cost.Call<bool>("HasEnough");
        var entriesMethod = costType.GetMethod("GetEntries", Instance, null, Type.EmptyTypes, null);
        var entriesType = entriesMethod?.ReturnType;
        var exactTuple = entriesType is { IsGenericType: true }
            ? entriesType.GetGenericArguments()[0]
            : null;
        _costEntries = cost.CallList("GetEntries", exactTuple);
        var tuple = new WorldMemberBinding(tupleType, "ResourceTuple");
        _costResource = tuple.Reference("resource", resourceType);
        _costValue = tuple.Call<BigDouble>("GetValue");
        _fillEntries = NativeAccessorBinder.CollectionField(fillType, "entries");
        _fillResource = NativeAccessorBinder.CallObject(fillEntryType, "get_resource", resourceType);
        _fillCapacity = NativeAccessorBinder.Call<BigDouble>(fillEntryType, "GetCapacity");
        var resource = new WorldMemberBinding(resourceType, "ResourceSO");
        _resourceId = resource.Call<Guid>("GetGuid");

        Failure = Join(
            _manager is null ? "RitualManager.instance was unavailable" : string.Empty,
            _selectedVariable is null ? "RitualManager.selectedRitual was unavailable" : string.Empty,
            _isSelected is null ? "RitualVariable.IsItem was unavailable" : string.Empty,
            _fillEntries is null || _fillResource is null || _fillCapacity is null
                ? "Ritual completion fill bindings were unavailable"
                : string.Empty,
            ritual.Failure,
            cost.Failure,
            tuple.Failure,
            resource.Failure);
    }

    internal string Failure { get; }

    internal WorldRitualDecision Read(object ritual)
    {
        var manager = _manager!();
        if (manager is null || manager.GetType() != _managerType)
            throw new InvalidOperationException("RitualManager.instance was unavailable");
        var selectedVariable = _selectedVariable!(manager) ??
            throw new InvalidOperationException("RitualManager.selectedRitual was unavailable");
        var selected = _isSelected!(selectedVariable, ritual);
        if (!selected)
            return new WorldRitualDecision(
                false, 0, false, false,
                PublicationTable<WorldRitualCost>.Empty,
                PublicationTable<WorldRitualCost>.Empty);

        var activation = _activationCost!(ritual) ??
            throw new InvalidOperationException("RitualSO.GetActivationCost returned null");
        var completion = _fillList!(ritual) ??
            throw new InvalidOperationException("RitualSO.resourceFillList was unavailable");
        return new WorldRitualDecision(
            true,
            _maximumStartingLevel!(ritual),
            _usageRequirementsMet!(ritual),
            _hasEnough!(activation),
            ReadCosts(activation),
            ReadFillCapacities(completion));
    }

    private PublicationTable<WorldRitualCost> ReadCosts(object cost)
    {
        var entries = _costEntries!(cost) ??
            throw new InvalidOperationException("ResourceCostList.GetEntries returned null");
        var rows = new WorldRitualCost[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index] ??
                throw new InvalidOperationException("Ritual cost row " + index + " was null");
            var resource = _costResource!(entry) ??
                throw new InvalidOperationException("Ritual cost row " + index + " had no resource");
            rows[index] = new WorldRitualCost(
                _resourceId!(resource), _costValue!(entry));
        }
        return PublicationTable<WorldRitualCost>.Create(rows);
    }

    private PublicationTable<WorldRitualCost> ReadFillCapacities(object fill)
    {
        var entries = _fillEntries!(fill) ??
            throw new InvalidOperationException("ResourceFillList.entries was unavailable");
        var rows = new WorldRitualCost[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index] ??
                throw new InvalidOperationException("Ritual completion fill row " + index + " was null");
            var resource = _fillResource!(entry) ??
                throw new InvalidOperationException("Ritual completion fill row " + index + " had no resource");
            rows[index] = new WorldRitualCost(
                _resourceId!(resource), _fillCapacity!(entry));
        }
        return PublicationTable<WorldRitualCost>.Create(rows);
    }

    private static Func<object?>? StaticReference(Type owner, string name, Type exactType)
    {
        var field = owner.GetField(name, Static);
        if (field is null || !field.IsStatic || field.FieldType != exactType) return null;
        try
        {
            return Expression.Lambda<Func<object?>>(
                Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();
        }
        catch (Exception) { return null; }
    }

    private static Func<object, object?>? Reference(Type owner, string name, Type exactType)
    {
        var field = owner.GetField(name, Instance);
        if (field is null || field.IsStatic || field.FieldType != exactType) return null;
        try
        {
            var target = Expression.Parameter(typeof(object), "target");
            return Expression.Lambda<Func<object, object?>>(
                Expression.Convert(
                    Expression.Field(Expression.Convert(target, owner), field),
                    typeof(object)),
                target).Compile();
        }
        catch (Exception) { return null; }
    }

    private static Func<object, object, T>? CallWithReference<T>(
        Type owner,
        string name,
        Type argumentType)
    {
        var method = owner.GetMethod(name, Instance, null, new[] { argumentType }, null);
        if (method is null || method.ReturnType != typeof(T)) return null;
        try
        {
            var target = Expression.Parameter(typeof(object), "target");
            var argument = Expression.Parameter(typeof(object), "argument");
            return Expression.Lambda<Func<object, object, T>>(
                Expression.Call(
                    Expression.Convert(target, owner),
                    method,
                    Expression.Convert(argument, argumentType)),
                target,
                argument).Compile();
        }
        catch (Exception) { return null; }
    }

    private static string Join(params string[] values)
    {
        var result = string.Empty;
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index].Length == 0) continue;
            result = result.Length == 0 ? values[index] : result + "; " + values[index];
        }
        return result;
    }
}
