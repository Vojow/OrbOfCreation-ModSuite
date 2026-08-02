using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct WorldChallengeReference
{
    internal WorldChallengeReference(int position, Guid challengeId, bool selectionRestricted = false)
    {
        Position = position;
        ChallengeId = challengeId;
        SelectionRestricted = selectionRestricted;
    }

    internal int Position { get; }
    internal Guid ChallengeId { get; }
    internal bool SelectionRestricted { get; }
}

/// <summary>The complete player-facing challenge decision state captured in one Unity frame.</summary>
internal readonly struct WorldChallengeContext
{
    private readonly PublicationTable<WorldChallengeReference>? _selected;
    private readonly PublicationTable<WorldChallengeReference>? _timeOffers;
    private readonly PublicationTable<WorldChallengeReference>? _prestigeOffers;

    internal WorldChallengeContext(bool available, string unavailableReason,
        bool worldCycleComplete, bool challengesFetched, int rerollsLeft, int rerollsMaximum,
        int selectionMaximum, PublicationTable<WorldChallengeReference> selected,
        PublicationTable<WorldChallengeReference> timeOffers,
        PublicationTable<WorldChallengeReference> prestigeOffers)
    {
        Available = available;
        UnavailableReason = unavailableReason ?? string.Empty;
        WorldCycleComplete = worldCycleComplete;
        ChallengesFetched = challengesFetched;
        RerollsLeft = rerollsLeft;
        RerollsMaximum = rerollsMaximum;
        SelectionMaximum = selectionMaximum;
        _selected = selected;
        _timeOffers = timeOffers;
        _prestigeOffers = prestigeOffers;
    }

    internal bool Available { get; }
    internal string UnavailableReason { get; }
    internal bool WorldCycleComplete { get; }
    internal bool ChallengesFetched { get; }
    internal int RerollsLeft { get; }
    internal int RerollsMaximum { get; }
    internal int SelectionMaximum { get; }
    internal PublicationTable<WorldChallengeReference> Selected =>
        _selected ?? PublicationTable<WorldChallengeReference>.Empty;
    internal PublicationTable<WorldChallengeReference> TimeOffers =>
        _timeOffers ?? PublicationTable<WorldChallengeReference>.Empty;
    internal PublicationTable<WorldChallengeReference> PrestigeOffers =>
        _prestigeOffers ?? PublicationTable<WorldChallengeReference>.Empty;
}

internal sealed class WorldChallengeContextBuffer
{
    private WorldChallengeReference[] _selected = new WorldChallengeReference[8];
    private WorldChallengeReference[] _time = new WorldChallengeReference[8];
    private WorldChallengeReference[] _prestige = new WorldChallengeReference[8];
    private int _selectedCount;
    private int _timeCount;
    private int _prestigeCount;

    internal bool Available { get; private set; }
    internal string UnavailableReason { get; private set; } = string.Empty;
    internal bool WorldCycleComplete { get; private set; }
    internal bool ChallengesFetched { get; private set; }
    internal int RerollsLeft { get; private set; }
    internal int RerollsMaximum { get; private set; }
    internal int SelectionMaximum { get; private set; }

    internal void Reset()
    {
        _selectedCount = _timeCount = _prestigeCount = 0;
        Available = false;
        UnavailableReason = string.Empty;
        WorldCycleComplete = false;
        ChallengesFetched = false;
        RerollsLeft = RerollsMaximum = SelectionMaximum = 0;
    }

    internal void SetHeader(bool complete, bool fetched, int left, int maximum, int selectionMaximum)
    {
        Available = true;
        WorldCycleComplete = complete;
        ChallengesFetched = fetched;
        RerollsLeft = Math.Max(left, 0);
        RerollsMaximum = Math.Max(maximum, 0);
        SelectionMaximum = Math.Max(selectionMaximum, 0);
    }

    internal void SetUnavailable(string reason)
    {
        Available = false;
        UnavailableReason = reason ?? string.Empty;
    }

    internal void AppendSelected(Guid id, bool restricted = false) =>
        Append(ref _selected, ref _selectedCount, id, restricted);
    internal void AppendTime(Guid id, bool restricted = false) =>
        Append(ref _time, ref _timeCount, id, restricted);
    internal void AppendPrestige(Guid id, bool restricted = false) =>
        Append(ref _prestige, ref _prestigeCount, id, restricted);

    internal WorldChallengeContext Build() => new(Available, UnavailableReason,
        WorldCycleComplete, ChallengesFetched, RerollsLeft, RerollsMaximum, SelectionMaximum,
        PublicationTable<WorldChallengeReference>.Create(_selected, _selectedCount),
        PublicationTable<WorldChallengeReference>.Create(_time, _timeCount),
        PublicationTable<WorldChallengeReference>.Create(_prestige, _prestigeCount));

    private static void Append(ref WorldChallengeReference[] rows, ref int count, Guid id, bool restricted)
    {
        if (count >= rows.Length) Array.Resize(ref rows, rows.Length * 2);
        rows[count] = new WorldChallengeReference(count, id, restricted);
        count++;
    }
}

/// <summary>One read-only main-thread capture of challenge selections, offers, and rerolls.</summary>
internal sealed class WorldChallengeContextReader : IWorldCategoryReader
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Type? _challengeType;
    private readonly Type? _challengeManagerType;
    private readonly Type? _resetManagerType;
    private readonly Func<object?>? _challengeManager;
    private readonly Func<object?>? _resetManager;
    private readonly Func<object, object?>? _preferred;
    private readonly Func<object, object?>? _timeOffers;
    private readonly Func<object, object?>? _prestigeOffers;
    private readonly Func<object, object?>? _rerollsLeft;
    private readonly Func<object, object?>? _rerollsMaximum;
    private readonly Func<object, object?>? _worldCycleComplete;
    private readonly Func<object, object?>? _challengesFetched;
    private readonly Func<object, IList?>? _values;
    private readonly Func<object, int>? _maximum;
    private readonly Func<object, object, bool>? _restricted;
    private readonly Func<object, int>? _asInt;
    private readonly Func<object, bool>? _getBool;
    private readonly Func<object, Guid>? _id;
    private readonly string _unavailable;

    internal WorldChallengeContextReader(Func<string, Type?> resolveType)
    {
        _challengeType = resolveType("ChallengeSO");
        _challengeManagerType = resolveType("ChallengeManager");
        _resetManagerType = resolveType("PersistentResetManager");
        var listType = resolveType("ChallengeListVariable");
        var intType = resolveType("IntVariable");
        var boolType = resolveType("BoolVariable");
        _challengeManager = StaticReference(_challengeManagerType, "instance", _challengeManagerType);
        _resetManager = StaticReference(_resetManagerType, "instance", _resetManagerType);
        _preferred = NativeAccessorBinder.Reference(_challengeManagerType, "preferredChallenges", listType);
        _timeOffers = NativeAccessorBinder.Reference(_challengeManagerType, "activeChallenges", listType);
        _prestigeOffers = NativeAccessorBinder.Reference(_resetManagerType, "activeChallenges", listType);
        _rerollsLeft = NativeAccessorBinder.Reference(_resetManagerType, "challengeRerollsLeft", intType);
        _rerollsMaximum = NativeAccessorBinder.Reference(_resetManagerType, "challengeRerollsMax", intType);
        _worldCycleComplete = NativeAccessorBinder.Reference(_resetManagerType, "hasCompleteWorldCycle", boolType);
        _challengesFetched = NativeAccessorBinder.Reference(_resetManagerType, "hasFetchedChallenges", boolType);
        _values = NativeAccessorBinder.CollectionField(listType, "value");
        _maximum = NativeAccessorBinder.Call<int>(listType, "GetMax");
        _restricted = NativeAccessorBinder.CallWithObjectArgument<bool>(
            listType, "IsChallengeRestricted", _challengeType);
        _asInt = NativeAccessorBinder.Call<int>(intType, "AsInt");
        _getBool = NativeAccessorBinder.Call<bool>(boolType, "GetValue");
        _id = NativeAccessorBinder.Call<Guid>(_challengeType, "GetGuid");
        _unavailable = _challengeType is null || _challengeManagerType is null ||
            _resetManagerType is null || listType is null || intType is null || boolType is null ||
            _challengeManager is null || _resetManager is null || _preferred is null ||
            _timeOffers is null || _prestigeOffers is null || _rerollsLeft is null ||
            _rerollsMaximum is null || _worldCycleComplete is null || _challengesFetched is null ||
            _values is null || _maximum is null || _restricted is null || _asInt is null ||
            _getBool is null || _id is null
                ? "the complete challenge decision binding set was unavailable"
                : string.Empty;
    }

    public string Category => "challenge decisions";
    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        var buffer = frame.ChallengeContext;
        buffer.Reset();
        if (!IsAvailable)
        {
            buffer.SetUnavailable(_unavailable);
            return WorldCategoryReport.Missing(Category, _unavailable);
        }
        try
        {
            var challengeManager = _challengeManager!();
            var resetManager = _resetManager!();
            if (challengeManager is null || challengeManager.GetType() != _challengeManagerType ||
                resetManager is null || resetManager.GetType() != _resetManagerType)
            {
                buffer.SetUnavailable("challenge managers were unavailable");
                return WorldCategoryReport.Missing(Category, "challenge managers were unavailable");
            }
            var preferred = _preferred!(challengeManager);
            var time = _timeOffers!(challengeManager);
            var prestige = _prestigeOffers!(resetManager);
            var left = _rerollsLeft!(resetManager);
            var maximum = _rerollsMaximum!(resetManager);
            var complete = _worldCycleComplete!(resetManager);
            var fetched = _challengesFetched!(resetManager);
            if (preferred is null || time is null || prestige is null || left is null ||
                maximum is null || complete is null || fetched is null)
                throw new InvalidOperationException("a challenge decision member was null");
            buffer.SetHeader(_getBool!(complete), _getBool!(fetched), _asInt!(left),
                _asInt!(maximum), _maximum!(preferred));
            Append(preferred, preferred, buffer.AppendSelected);
            Append(time, preferred, buffer.AppendTime);
            Append(prestige, preferred, buffer.AppendPrestige);
            return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected, 1, 0, string.Empty);
        }
        catch (Exception exception)
        {
            var reason = "reading challenge decisions threw: " + exception.GetBaseException().Message;
            buffer.SetUnavailable(reason);
            return WorldCategoryReport.Missing(Category, reason);
        }
    }

    private void Append(object list, object preferred, Action<Guid, bool> append)
    {
        var values = _values!(list);
        for (var index = 0; index < (values?.Count ?? 0); index++)
        {
            var value = values![index];
            if (value is null || value.GetType() != _challengeType) continue;
            append(_id!(value), _restricted!(preferred, value));
        }
    }

    private static Func<object?>? StaticReference(Type? owner, string name, Type? exactType)
    {
        if (owner is null || exactType is null) return null;
        var field = owner.GetField(name, Static);
        if (field is null || field.FieldType != exactType) return null;
        try
        {
            return Expression.Lambda<Func<object?>>(Expression.Convert(
                Expression.Field(null, field), typeof(object))).Compile();
        }
        catch (Exception) { return null; }
    }
}
