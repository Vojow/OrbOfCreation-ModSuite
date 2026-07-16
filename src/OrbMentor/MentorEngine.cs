using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace OrbMentor;

public enum MentorEconomyMode { SharedPool, PerRecipient }

internal sealed class MentorRecipe
{
    public MentorRecipe(string uuid, int masteryLevel, bool isDiscovered) { Uuid = uuid; MasteryLevel = masteryLevel; IsDiscovered = isDiscovered; }
    public string Uuid { get; }
    public int MasteryLevel { get; }
    public bool IsDiscovered { get; }
}

internal sealed class MentorGrant
{
    public MentorGrant(string uuid, MentorAmount amount) { Uuid = uuid; Amount = amount; }
    public string Uuid { get; }
    public MentorAmount Amount { get; }
}

internal sealed class MentorPlan
{
    private readonly IReadOnlyList<MentorRecipe> _recipients;
    private readonly MentorAmount _amount;
    private int _nextRecipient;

    public MentorPlan(IReadOnlyList<MentorRecipe> recipients, MentorAmount amount, int sourceEventCount = 1)
    {
        _recipients = recipients;
        _amount = amount;
        SourceEventCount = Math.Max(0, sourceEventCount);
    }

    public int RemainingCount => Math.Max(0, _recipients.Count - _nextRecipient);
    public int SourceEventCount { get; }

    public bool TryTake(out MentorGrant grant)
    {
        if (_nextRecipient >= _recipients.Count)
        {
            grant = null!;
            return false;
        }

        grant = new MentorGrant(_recipients[_nextRecipient++].Uuid, _amount);
        return true;
    }
}

internal readonly struct MentorQualifiedBatch
{
    public MentorQualifiedBatch(MentorAmount amount, int eventCount, int sourceCount, MentorRelationshipSnapshot? relationship = null)
    {
        Amount = amount;
        EventCount = eventCount;
        SourceCount = sourceCount;
        Relationship = relationship;
    }

    public MentorAmount Amount { get; }
    public int EventCount { get; }
    public int SourceCount { get; }
    public MentorRelationshipSnapshot? Relationship { get; }
}

internal sealed class MentorRelationshipSnapshot
{
    private readonly HashSet<string> _discoveredIds;
    private readonly HashSet<string> _recipientIds;

    public MentorRelationshipSnapshot(
        long epoch,
        int highestMastery,
        IReadOnlyList<MentorRecipe> discovered,
        IReadOnlyList<MentorRecipe> recipients)
        : this(epoch, highestMastery, discovered, recipients, null, null)
    {
    }

    private MentorRelationshipSnapshot(
        long epoch,
        int highestMastery,
        IReadOnlyList<MentorRecipe> discovered,
        IReadOnlyList<MentorRecipe> recipients,
        HashSet<string>? discoveredIds,
        HashSet<string>? recipientIds)
    {
        Epoch = epoch;
        HighestMastery = highestMastery;
        Discovered = discovered;
        Recipients = recipients;
        _discoveredIds = discoveredIds ?? BuildIds(discovered);
        _recipientIds = recipientIds ?? BuildIds(recipients);
    }

    public long Epoch { get; }
    public int HighestMastery { get; }
    public IReadOnlyList<MentorRecipe> Discovered { get; }
    public IReadOnlyList<MentorRecipe> Recipients { get; }

    internal static MentorRelationshipSnapshot CreatePreindexed(
        long epoch,
        int highestMastery,
        IReadOnlyList<MentorRecipe> discovered,
        IReadOnlyList<MentorRecipe> recipients,
        HashSet<string> discoveredIds,
        HashSet<string> recipientIds) =>
        new(epoch, highestMastery, discovered, recipients, discoveredIds, recipientIds);

    public MentorQualificationStatus Qualify(MentorCaptureKey captured) =>
        !captured.Discovered || captured.MasteryLevel < HighestMastery
            ? MentorQualificationStatus.SourceIneligible
            : SelectBaseRecipients(captured).Count > RecipientAdjustment(captured)
                ? MentorQualificationStatus.Qualified
                : MentorQualificationStatus.NoRecipients;

    public IReadOnlyList<MentorRecipe> SelectRecipients(MentorCaptureKey captured)
    {
        var source = SelectBaseRecipients(captured);
        var adjustment = RecipientAdjustment(captured);
        if (adjustment == 0) return source;
        var filtered = new List<MentorRecipe>(Math.Max(0, source.Count - 1));
        foreach (var recipe in source)
            if (!string.Equals(recipe.Uuid, captured.Uuid, StringComparison.Ordinal)) filtered.Add(recipe);
        return filtered;
    }

    public MentorRelationshipSnapshot ForCapture(MentorCaptureKey captured)
    {
        var recipients = SelectRecipients(captured);
        return ReferenceEquals(recipients, Recipients)
            ? this
            : new MentorRelationshipSnapshot(Epoch, captured.MasteryLevel, Discovered, recipients);
    }

    private IReadOnlyList<MentorRecipe> SelectBaseRecipients(MentorCaptureKey captured) =>
        captured.MasteryLevel > HighestMastery ? Discovered : Recipients;

    private int RecipientAdjustment(MentorCaptureKey captured)
    {
        return (captured.MasteryLevel > HighestMastery ? _discoveredIds : _recipientIds).Contains(captured.Uuid) ? 1 : 0;
    }

    private static HashSet<string> BuildIds(IReadOnlyList<MentorRecipe> recipes)
    {
        var result = new HashSet<string>(recipes.Count, StringComparer.Ordinal);
        foreach (var recipe in recipes) result.Add(recipe.Uuid);
        return result;
    }
}

internal sealed class MentorRelationshipEvidence
{
    private MentorRelationshipSnapshot? _resolved;
    private MentorRelationshipEvidence(
        MentorRelationshipSnapshot basis,
        MentorRelationshipEvidence? parent,
        string? changedUuid,
        int changedMastery,
        bool changedDiscovered,
        long epoch)
    {
        Basis = basis;
        Parent = parent;
        ChangedUuid = changedUuid;
        ChangedMastery = changedMastery;
        ChangedDiscovered = changedDiscovered;
        Epoch = epoch;
    }

    public MentorRelationshipSnapshot Basis { get; }
    public MentorRelationshipEvidence? Parent { get; }
    public string? ChangedUuid { get; }
    public int ChangedMastery { get; }
    public bool ChangedDiscovered { get; }
    public long Epoch { get; }
    public MentorRelationshipSnapshot? Resolved => _resolved;

    public static MentorRelationshipEvidence FromSnapshot(MentorRelationshipSnapshot snapshot) =>
        new(snapshot, null, null, 0, false, snapshot.Epoch);

    public MentorRelationshipEvidence WithChange(string uuid, int mastery, bool discovered, long epoch) =>
        new(Basis, this, uuid, mastery, discovered, epoch);

    public MentorRelationshipSnapshot Publish(MentorRelationshipSnapshot snapshot) =>
        _resolved ??= snapshot;
}

internal sealed class MentorRelationshipResolutionWork : IDisposable
{
    private enum Phase { GatherChanges, ReadBasis, ReadChanges, Order, BuildRecipients, Complete }

    private readonly MentorRelationshipEvidence _evidence;
    private readonly Dictionary<string, MentorRecipe> _changes = new(StringComparer.Ordinal);
    private readonly MentorIncrementalOrder<MentorRecipe> _order = new();
    private MentorRelationshipEvidence? _cursor;
    private IEnumerator<KeyValuePair<string, MentorRecipe>>? _changeEnumerator;
    private int _basisIndex;
    private int _recipientIndex;
    private Phase _phase;
    private readonly List<MentorRecipe> _discovered;
    private readonly List<MentorRecipe> _recipients;
    private readonly HashSet<string> _discoveredIds;
    private readonly HashSet<string> _recipientIds;
    private int _highestMastery = int.MinValue;

    public MentorRelationshipResolutionWork(MentorRelationshipEvidence evidence)
    {
        _evidence = evidence;
        _cursor = evidence;
        _discovered = new List<MentorRecipe>(evidence.Basis.Discovered.Count);
        _recipients = new List<MentorRecipe>(evidence.Basis.Discovered.Count);
        _discoveredIds = new HashSet<string>(evidence.Basis.Discovered.Count, StringComparer.Ordinal);
        _recipientIds = new HashSet<string>(evidence.Basis.Discovered.Count, StringComparer.Ordinal);
        if (evidence.Resolved is not null)
        {
            Result = evidence.Resolved;
            _phase = Phase.Complete;
        }
    }

    public MentorRelationshipSnapshot? Result { get; private set; }
    public bool IsComplete => _phase == Phase.Complete;

    public void Step()
    {
        switch (_phase)
        {
            case Phase.GatherChanges:
                if (_cursor is not null)
                {
                    if (_cursor.ChangedUuid is not null && !_changes.ContainsKey(_cursor.ChangedUuid))
                        _changes.Add(_cursor.ChangedUuid, new MentorRecipe(
                            _cursor.ChangedUuid, _cursor.ChangedMastery, _cursor.ChangedDiscovered));
                    _cursor = _cursor.Parent;
                    return;
                }
                _phase = Phase.ReadBasis;
                return;
            case Phase.ReadBasis:
                if (_basisIndex < _evidence.Basis.Discovered.Count)
                {
                    var basis = _evidence.Basis.Discovered[_basisIndex++];
                    if (_changes.Remove(basis.Uuid, out var changed)) basis = changed;
                    if (basis.IsDiscovered) _order.TryAdd(basis.Uuid, basis);
                    return;
                }
                _changeEnumerator = _changes.GetEnumerator();
                _phase = Phase.ReadChanges;
                return;
            case Phase.ReadChanges:
                if (_changeEnumerator!.MoveNext())
                {
                    var changed = _changeEnumerator.Current.Value;
                    if (changed.IsDiscovered) _order.TryAdd(changed.Uuid, changed);
                    return;
                }
                _changeEnumerator.Dispose();
                _changeEnumerator = null;
                _phase = Phase.Order;
                return;
            case Phase.Order:
                if (_order.TryTakeNext(out var orderedRecipe))
                {
                    _discovered.Add(orderedRecipe);
                    _discoveredIds.Add(orderedRecipe.Uuid);
                    if (orderedRecipe.MasteryLevel > _highestMastery) _highestMastery = orderedRecipe.MasteryLevel;
                    return;
                }
                _phase = Phase.BuildRecipients;
                return;
            case Phase.BuildRecipients:
                if (_recipientIndex < _discovered.Count)
                {
                    var recipientRecipe = _discovered[_recipientIndex++];
                    if (recipientRecipe.MasteryLevel < _highestMastery)
                    {
                        _recipients.Add(recipientRecipe);
                        _recipientIds.Add(recipientRecipe.Uuid);
                    }
                    return;
                }
                Result = _evidence.Publish(MentorRelationshipSnapshot.CreatePreindexed(
                    _evidence.Epoch, _highestMastery, _discovered, _recipients,
                    _discoveredIds, _recipientIds));
                _phase = Phase.Complete;
                return;
        }
    }

    public void Dispose()
    {
        _changeEnumerator?.Dispose();
        _order.Dispose();
    }
}

internal sealed class MentorSourceAccumulator
{
    private sealed class SnapshotAmount
    {
        public readonly HashSet<string> Sources = new(StringComparer.Ordinal);
        public MentorAmount Total;
        public int EventCount;
    }

    private static readonly MentorRelationshipSnapshot LegacyRelationship =
        new(0, int.MinValue, Array.Empty<MentorRecipe>(), Array.Empty<MentorRecipe>());
    private readonly Dictionary<MentorRelationshipSnapshot, SnapshotAmount> _snapshots;
    private readonly Queue<MentorRelationshipSnapshot> _order;

    public MentorSourceAccumulator(int capacity = 256)
    {
        _snapshots = new Dictionary<MentorRelationshipSnapshot, SnapshotAmount>(Math.Max(1, Math.Min(capacity, 16)));
        _order = new Queue<MentorRelationshipSnapshot>(Math.Max(1, Math.Min(capacity, 16)));
    }

    public int SourceCount { get; private set; }
    public bool HasPending => _order.Count > 0;
    public int EventCount { get; private set; }

    public void Capture(string sourceUuid, MentorAmount amount, bool qualifiesAtEvent, int eventCount = 1)
    {
        if (!qualifiesAtEvent || string.IsNullOrWhiteSpace(sourceUuid) || !amount.IsValidPositive || eventCount <= 0) return;
        Capture(LegacyRelationship, sourceUuid, amount, eventCount);
    }

    public void Capture(MentorRelationshipSnapshot relationship, string sourceUuid, MentorAmount amount, int eventCount = 1)
    {
        if (relationship is null || string.IsNullOrWhiteSpace(sourceUuid) || !amount.IsValidPositive || eventCount <= 0) return;
        if (!_snapshots.TryGetValue(relationship, out var current))
        {
            current = new SnapshotAmount();
            _snapshots.Add(relationship, current);
            _order.Enqueue(relationship);
        }
        if (current.Sources.Add(sourceUuid)) SourceCount++;
        current.Total = current.Total.Add(amount);
        current.EventCount += eventCount;
        EventCount += eventCount;
    }

    public MentorQualifiedBatch Drain()
    {
        if (_order.Count == 0) return default;
        var relationship = _order.Dequeue();
        var current = _snapshots[relationship];
        _snapshots.Remove(relationship);
        EventCount -= current.EventCount;
        SourceCount -= current.Sources.Count;
        return new MentorQualifiedBatch(current.Total, current.EventCount, current.Sources.Count,
            ReferenceEquals(relationship, LegacyRelationship) ? null : relationship);
    }

    public void Cancel() { _snapshots.Clear(); _order.Clear(); SourceCount = 0; EventCount = 0; }
}

internal readonly struct MentorCaptureKey : IEquatable<MentorCaptureKey>
{
    public MentorCaptureKey(
        object source,
        string uuid,
        int masteryLevel,
        bool discovered,
        long progressionEpoch = 0,
        MentorRelationshipSnapshot? relationship = null,
        MentorRelationshipEvidence? evidence = null)
    {
        Source = source;
        Uuid = uuid;
        MasteryLevel = masteryLevel;
        Discovered = discovered;
        ProgressionEpoch = progressionEpoch;
        Relationship = relationship;
        Evidence = evidence;
    }

    public object Source { get; }
    public string Uuid { get; }
    public int MasteryLevel { get; }
    public bool Discovered { get; }
    public long ProgressionEpoch { get; }
    public MentorRelationshipSnapshot? Relationship { get; }
    public MentorRelationshipEvidence? Evidence { get; }
    public bool Equals(MentorCaptureKey other) =>
        ReferenceEquals(Source, other.Source) &&
        MasteryLevel == other.MasteryLevel &&
        Discovered == other.Discovered &&
        ProgressionEpoch == other.ProgressionEpoch &&
        ReferenceEquals(Relationship, other.Relationship) &&
        ReferenceEquals(Evidence, other.Evidence) &&
        string.Equals(Uuid, other.Uuid, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is MentorCaptureKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        RuntimeHelpers.GetHashCode(Source), Uuid, MasteryLevel, Discovered, ProgressionEpoch,
        Relationship is null ? 0 : RuntimeHelpers.GetHashCode(Relationship),
        Evidence is null ? 0 : RuntimeHelpers.GetHashCode(Evidence));
}

internal sealed class MentorCapturedEvent
{
    public MentorCapturedEvent(MentorCaptureKey key, MentorAmount amount)
    {
        Key = key;
        Amount = amount;
        EventCount = 1;
    }

    public MentorCaptureKey Key { get; }
    public MentorAmount Amount { get; set; }
    public int EventCount { get; set; }
}

internal enum MentorCaptureResult { Added, Coalesced, Overflow, Invalid }

internal sealed class MentorCaptureQueue
{
    private readonly int _capacity;
    private readonly Dictionary<MentorCaptureKey, MentorCapturedEvent> _pending;
    private readonly Queue<MentorCaptureKey> _order;

    public MentorCaptureQueue(int capacity = 256)
    {
        _capacity = Math.Max(1, capacity);
        _pending = new Dictionary<MentorCaptureKey, MentorCapturedEvent>(_capacity);
        _order = new Queue<MentorCaptureKey>(_capacity);
    }
    public int Count => _pending.Count;
    public int EventCount { get; private set; }

    public MentorCaptureResult Capture(MentorCaptureKey key, MentorAmount amount)
    {
        if (key.Source is null || string.IsNullOrWhiteSpace(key.Uuid) || !amount.IsValidPositive)
            return MentorCaptureResult.Invalid;
        if (_pending.TryGetValue(key, out var current))
        {
            current.Amount = current.Amount.Add(amount);
            current.EventCount++;
            EventCount++;
            return MentorCaptureResult.Coalesced;
        }
        if (_pending.Count >= _capacity) return MentorCaptureResult.Overflow;
        _pending.Add(key, new MentorCapturedEvent(key, amount));
        _order.Enqueue(key);
        EventCount++;
        return MentorCaptureResult.Added;
    }

    public bool TryTake(out MentorCapturedEvent captured)
    {
        while (_order.Count > 0)
        {
            var key = _order.Dequeue();
            if (!_pending.Remove(key, out captured!)) continue;
            EventCount -= captured.EventCount;
            return true;
        }
        captured = null!;
        return false;
    }

    public void Cancel() { _pending.Clear(); _order.Clear(); EventCount = 0; }
}

internal class MentorPendingWork
{
    public readonly MentorEngine Engine = new();
    public readonly MentorCaptureQueue Captures = new();
    public readonly MentorSourceAccumulator Sources = new();
    public MentorPlan? ActivePlan;
    public MentorCapturedEvent? ResolvingCapture;
    public MentorRelationshipResolutionWork? RelationshipResolution;

    public bool HasGrantBarrier => Captures.Count > 0 || Sources.HasPending || ActivePlan is not null ||
        ResolvingCapture is not null || RelationshipResolution is not null;

    public void CancelPending()
    {
        Captures.Cancel();
        Sources.Cancel();
        ActivePlan = null;
        RelationshipResolution?.Dispose();
        RelationshipResolution = null;
        ResolvingCapture = null;
        Engine.Cancel();
    }
}

internal static class MentorIdentityTransition
{
    public static bool CancelPendingOnChange(bool identityChanged, MentorPendingWork pending)
    {
        if (!identityChanged) return false;
        pending.CancelPending();
        return true;
    }
}

internal sealed class MentorWorkGeneration
{
    private long _requested = 1;
    public long Current => _requested;
    public long Request() => ++_requested;
    public bool IsCurrent(long consumed) => consumed == _requested;
}

internal sealed class MentorIncrementalOrder<T> : IDisposable
{
    private readonly SortedDictionary<string, T> _sorted = new(StringComparer.Ordinal);
    private IEnumerator<KeyValuePair<string, T>>? _enumerator;

    public int Count => _sorted.Count;
    public bool TryAdd(string key, T value)
    {
        if (_enumerator is not null || _sorted.ContainsKey(key)) return false;
        _sorted.Add(key, value);
        return true;
    }

    public bool TryTakeNext(out T value)
    {
        _enumerator ??= _sorted.GetEnumerator();
        if (_enumerator.MoveNext())
        {
            value = _enumerator.Current.Value;
            return true;
        }
        value = default!;
        return false;
    }

    public void Dispose() => _enumerator?.Dispose();
}

internal enum MentorDropReason
{
    CaptureUnavailable,
    CaptureOverflow,
    SourceIdentityChanged,
    CatalogIdentityChanged,
    SourceIneligible,
    NoRecipients,
    ZeroShare,
    RecipientIdentityChanged,
    LifecycleReset,
    Disabled,
    ContractFailure,
}

internal sealed class MentorDiagnostics
{
    private readonly Dictionary<MentorDropReason, int> _drops = new();
    public int CapturedEvents { get; private set; }
    public int CoalescedEvents { get; private set; }
    public int QualifiedEvents { get; private set; }
    public int NativeGrants { get; private set; }
    public int DeferredGrants { get; private set; }
    public int DroppedEvents { get; private set; }
    public int DroppedGrants { get; private set; }

    public void RecordCapture(bool coalesced) { CapturedEvents++; if (coalesced) CoalescedEvents++; }
    public void RecordQualified(int count) => QualifiedEvents += Math.Max(0, count);
    public void RecordGrant() => NativeGrants++;
    public void RecordDeferredGrant() => DeferredGrants++;
    public void RecordDrop(MentorDropReason reason, int count, bool grant)
    {
        count = Math.Max(0, count);
        if (grant) DroppedGrants += count; else DroppedEvents += count;
        _drops[reason] = DropCount(reason) + count;
    }
    public int DropCount(MentorDropReason reason) => _drops.TryGetValue(reason, out var count) ? count : 0;
}

internal sealed class MentorFailureState
{
    public string? PermanentReason { get; private set; }
    public string? TransientReason { get; private set; }
    public bool IsBlocked => PermanentReason is not null || TransientReason is not null;
    public string? Reason => PermanentReason ?? TransientReason;
    public void BlockPermanent(string reason) => PermanentReason ??= reason;
    public void BlockTransient(string reason) => TransientReason = reason;
    public void ResetLifecycle() => TransientReason = null;
}

internal sealed class MentorFailureRegistry
{
    private readonly MentorFailureState _global = new();
    private readonly MentorFailureState[] _domains =
    {
        new(), new(), new(),
    };

    public MentorFailureState Global => _global;
    public MentorFailureState For(MentorDomain domain) => _domains[(int)domain];
    public bool IsDomainBlocked(MentorDomain domain) =>
        domain == MentorDomain.Spells ? _global.IsBlocked : For(domain).IsBlocked;
    public void ResetLifecycle()
    {
        _global.ResetLifecycle();
        foreach (var domain in _domains) domain.ResetLifecycle();
    }
}

internal sealed class MentorLifecycleSignal
{
    private bool _pending;
    public bool IsPending => _pending;
    public void Request() => _pending = true;
    public bool TryConsume()
    {
        if (!_pending) return false;
        _pending = false;
        return true;
    }
}

internal enum MentorIdentityStatus { Valid, Destroyed, WrongType, UuidMismatch, RegistryMismatch }

internal enum MentorQualificationStatus { Qualified, SourceIneligible, NoRecipients }

internal static class MentorRelationshipQualification
{
    public static MentorQualificationStatus Evaluate(
        MentorCaptureKey captured,
        long relationshipEpoch,
        int highestMastery,
        int recipientCount)
    {
        if (captured.Relationship is not null) return captured.Relationship.Qualify(captured);
        if (!captured.Discovered || captured.MasteryLevel != highestMastery) return MentorQualificationStatus.SourceIneligible;
        return recipientCount > 0 ? MentorQualificationStatus.Qualified : MentorQualificationStatus.NoRecipients;
    }
}

internal static class MentorProgressionObservation
{
    public static void AfterNativeGrant(ref bool relationshipDirty) => relationshipDirty = true;

    public static bool AdvanceRefreshEpoch(
        ref long progressionEpoch,
        long relationshipEpoch,
        bool liveStateChanged)
    {
        if (!liveStateChanged || progressionEpoch != relationshipEpoch) return false;
        progressionEpoch++;
        return true;
    }

    public static bool Apply(
        ref long progressionEpoch,
        ref bool relationshipDirty,
        ref int cachedMastery,
        ref bool cachedDiscovered,
        int observedMastery,
        bool observedDiscovered,
        bool epochAlreadyAdvanced = false)
    {
        if (cachedMastery == observedMastery && cachedDiscovered == observedDiscovered) return false;
        cachedMastery = observedMastery;
        cachedDiscovered = observedDiscovered;
        if (!epochAlreadyAdvanced) progressionEpoch++;
        relationshipDirty = true;
        return true;
    }
}

internal static class MentorIdentityValidation
{
    public static MentorIdentityStatus Validate(
        Type expectedType,
        string expectedUuid,
        object candidate,
        object? registryCurrent,
        string? observedUuid,
        bool destroyed)
    {
        if (destroyed) return MentorIdentityStatus.Destroyed;
        if (!expectedType.IsInstanceOfType(candidate)) return MentorIdentityStatus.WrongType;
        if (!string.Equals(expectedUuid, observedUuid, StringComparison.Ordinal)) return MentorIdentityStatus.UuidMismatch;
        return ReferenceEquals(candidate, registryCurrent) ? MentorIdentityStatus.Valid : MentorIdentityStatus.RegistryMismatch;
    }
}

internal sealed class MentorEngine
{
    private readonly Dictionary<string, MentorAmount> _pending = new(StringComparer.Ordinal);
    private readonly Queue<string> _pendingOrder = new();

    public IReadOnlyList<MentorRecipe> EligibleRecipients(
        string sourceUuid,
        IReadOnlyCollection<MentorRecipe> recipes)
    {
        var highest = int.MinValue;
        MentorRecipe? source = null;
        foreach (var recipe in recipes)
        {
            if (!recipe.IsDiscovered || string.IsNullOrWhiteSpace(recipe.Uuid)) continue;
            if (recipe.MasteryLevel > highest) highest = recipe.MasteryLevel;
            if (string.Equals(recipe.Uuid, sourceUuid, StringComparison.Ordinal)) source = recipe;
        }

        if (source is null || source.MasteryLevel != highest) return Array.Empty<MentorRecipe>();
        var recipients = new List<MentorRecipe>();
        foreach (var recipe in recipes)
        {
            if (recipe.IsDiscovered &&
                recipe.MasteryLevel < highest &&
                !string.Equals(recipe.Uuid, sourceUuid, StringComparison.Ordinal))
            {
                recipients.Add(recipe);
            }
        }

        recipients.Sort((left, right) => StringComparer.Ordinal.Compare(left.Uuid, right.Uuid));
        return recipients;
    }

    public IReadOnlyList<MentorGrant> Plan(
        MentorAmount sourceXp,
        double sharePercent,
        MentorEconomyMode mode,
        IReadOnlyList<MentorRecipe> recipients)
    {
        var plan = CreatePlan(sourceXp, sharePercent, mode, recipients);
        if (plan is null) return Array.Empty<MentorGrant>();
        var grants = new List<MentorGrant>(recipients.Count);
        while (plan.TryTake(out var grant)) grants.Add(grant);
        return grants;
    }

    public MentorPlan? CreatePlan(
        MentorAmount sourceXp,
        double sharePercent,
        MentorEconomyMode mode,
        IReadOnlyList<MentorRecipe> recipients,
        int sourceEventCount = 1)
    {
        if (!sourceXp.IsValidPositive || !double.IsFinite(sharePercent) || recipients.Count == 0) return null;
        var fraction = Math.Clamp(sharePercent, 0.0, 100.0) / 100.0;
        var amount = sourceXp.Multiply(mode == MentorEconomyMode.SharedPool ? fraction / recipients.Count : fraction);
        return amount.IsValidPositive ? new MentorPlan(recipients, amount, sourceEventCount) : null;
    }

    public void Consolidate(IEnumerable<MentorGrant> grants)
    {
        foreach (var grant in grants) Consolidate(grant);
    }

    public void Consolidate(MentorGrant grant)
    {
        if (!grant.Amount.IsValidPositive) return;
        if (_pending.TryGetValue(grant.Uuid, out var current))
        {
            _pending[grant.Uuid] = current.Add(grant.Amount);
            return;
        }

        _pending[grant.Uuid] = grant.Amount;
        _pendingOrder.Enqueue(grant.Uuid);
    }

    public IReadOnlyList<MentorGrant> Take(int operationBudget)
    {
        var result = new List<MentorGrant>();
        while (result.Count < Math.Max(0, operationBudget) && _pendingOrder.Count > 0)
        {
            var uuid = _pendingOrder.Dequeue();
            if (!_pending.Remove(uuid, out var amount)) continue;
            result.Add(new MentorGrant(uuid, amount));
        }
        return result;
    }

    public bool TryTake(out MentorGrant grant)
    {
        while (_pendingOrder.Count > 0)
        {
            var uuid = _pendingOrder.Dequeue();
            if (!_pending.Remove(uuid, out var amount)) continue;
            grant = new MentorGrant(uuid, amount);
            return true;
        }

        grant = null!;
        return false;
    }

    public bool TryPeek(out MentorGrant grant)
    {
        while (_pendingOrder.Count > 0)
        {
            var uuid = _pendingOrder.Peek();
            if (_pending.TryGetValue(uuid, out var amount))
            {
                grant = new MentorGrant(uuid, amount);
                return true;
            }
            _pendingOrder.Dequeue();
        }
        grant = null!;
        return false;
    }

    public bool TryPeek(out string uuid, out MentorAmount amount)
    {
        while (_pendingOrder.Count > 0)
        {
            uuid = _pendingOrder.Peek();
            if (_pending.TryGetValue(uuid, out amount)) return true;
            _pendingOrder.Dequeue();
        }
        uuid = string.Empty;
        amount = default;
        return false;
    }

    public bool Complete(string uuid)
    {
        if (_pendingOrder.Count == 0 || !string.Equals(_pendingOrder.Peek(), uuid, StringComparison.Ordinal)) return false;
        _pendingOrder.Dequeue();
        return _pending.Remove(uuid);
    }

    public int PendingCount => _pending.Count;
    public void Cancel() { _pending.Clear(); _pendingOrder.Clear(); }
}
