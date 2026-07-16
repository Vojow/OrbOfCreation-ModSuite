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
    public MentorQualifiedBatch(MentorAmount amount, int eventCount, int sourceCount, MentorRecipientSnapshot? relationship = null)
    {
        Amount = amount;
        EventCount = eventCount;
        SourceCount = sourceCount;
        Relationship = relationship;
    }

    public MentorAmount Amount { get; }
    public int EventCount { get; }
    public int SourceCount { get; }
    public MentorRecipientSnapshot? Relationship { get; }
}

internal sealed class MentorRecipientSnapshot
{
    public MentorRecipientSnapshot(long epoch, int highestMastery, IReadOnlyList<MentorRecipe> recipients, int derivationSteps = 1)
    {
        Epoch = epoch;
        HighestMastery = highestMastery;
        Recipients = recipients;
        DerivationSteps = derivationSteps;
    }

    public long Epoch { get; }
    public int HighestMastery { get; }
    public IReadOnlyList<MentorRecipe> Recipients { get; }
    public int DerivationSteps { get; }
}

internal sealed class MentorRecipientSelection : IReadOnlyList<MentorRecipe>
{
    private readonly IReadOnlyList<MentorRecipe> _source;
    private readonly int _excludedIndex;

    public MentorRecipientSelection(IReadOnlyList<MentorRecipe> source, int excludedIndex)
    {
        _source = source;
        _excludedIndex = excludedIndex;
    }

    public int Count => _source.Count - (_excludedIndex >= 0 ? 1 : 0);
    public MentorRecipe this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _source[index >= _excludedIndex && _excludedIndex >= 0 ? index + 1 : index];
        }
    }
    public IEnumerator<MentorRecipe> GetEnumerator()
    {
        for (var index = 0; index < _source.Count; index++)
            if (index != _excludedIndex) yield return _source[index];
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class MentorRelationshipSnapshot
{
    private readonly Dictionary<string, int> _discoveredIndices;
    private readonly Dictionary<string, int> _recipientIndices;
    private readonly MentorRecipientSnapshot _defaultRecipients;

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
        Dictionary<string, int>? discoveredIndices,
        Dictionary<string, int>? recipientIndices)
    {
        Epoch = epoch;
        HighestMastery = highestMastery;
        Discovered = discovered;
        Recipients = recipients;
        _discoveredIndices = discoveredIndices ?? BuildIndices(discovered);
        _recipientIndices = recipientIndices ?? BuildIndices(recipients);
        _defaultRecipients = new MentorRecipientSnapshot(epoch, highestMastery, recipients);
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
        Dictionary<string, int> discoveredIndices,
        Dictionary<string, int> recipientIndices) =>
        new(epoch, highestMastery, discovered, recipients, discoveredIndices, recipientIndices);

    public MentorQualificationStatus Qualify(MentorCaptureKey captured) =>
        !captured.Discovered || captured.MasteryLevel < HighestMastery
            ? MentorQualificationStatus.SourceIneligible
            : SelectBaseRecipients(captured).Count > RecipientAdjustment(captured)
                ? MentorQualificationStatus.Qualified
                : MentorQualificationStatus.NoRecipients;

    public MentorRecipientSnapshot ForCapture(MentorCaptureKey captured)
    {
        var source = SelectBaseRecipients(captured);
        var indices = captured.MasteryLevel > HighestMastery ? _discoveredIndices : _recipientIndices;
        var excludedIndex = indices.TryGetValue(captured.Uuid, out var index) ? index : -1;
        if (ReferenceEquals(source, Recipients) && excludedIndex < 0) return _defaultRecipients;
        return new MentorRecipientSnapshot(
            Epoch,
            Math.Max(HighestMastery, captured.MasteryLevel),
            new MentorRecipientSelection(source, excludedIndex));
    }

    private IReadOnlyList<MentorRecipe> SelectBaseRecipients(MentorCaptureKey captured) =>
        captured.MasteryLevel > HighestMastery ? Discovered : Recipients;

    private int RecipientAdjustment(MentorCaptureKey captured)
    {
        return (captured.MasteryLevel > HighestMastery ? _discoveredIndices : _recipientIndices).ContainsKey(captured.Uuid) ? 1 : 0;
    }

    private static Dictionary<string, int> BuildIndices(IReadOnlyList<MentorRecipe> recipes)
    {
        var result = new Dictionary<string, int>(recipes.Count, StringComparer.Ordinal);
        for (var index = 0; index < recipes.Count; index++) result.Add(recipes[index].Uuid, index);
        return result;
    }
}

internal sealed class MentorRelationshipEvidence
{
    private readonly MentorRelationshipEvidenceBuffer? _buffer;
    private int _captureReferences;
    private MentorRelationshipSnapshot? _resolved;
    private MentorRelationshipEvidence(
        MentorRelationshipSnapshot basis,
        MentorRelationshipEvidence? parent,
        string? changedUuid,
        int changedMastery,
        bool changedDiscovered,
        long epoch,
        MentorRelationshipEvidenceBuffer? buffer = null)
    {
        Basis = basis;
        Parent = parent;
        ChangedUuid = changedUuid;
        ChangedMastery = changedMastery;
        ChangedDiscovered = changedDiscovered;
        Epoch = epoch;
        _buffer = buffer;
    }

    public MentorRelationshipSnapshot Basis { get; }
    public MentorRelationshipEvidence? Parent { get; }
    public string? ChangedUuid { get; }
    public int ChangedMastery { get; }
    public bool ChangedDiscovered { get; }
    public long Epoch { get; }
    public MentorRelationshipSnapshot? Resolved => _resolved;
    internal int CaptureReferences => _captureReferences;

    public static MentorRelationshipEvidence FromSnapshot(MentorRelationshipSnapshot snapshot) =>
        new(snapshot, null, null, 0, false, snapshot.Epoch);

    public MentorRelationshipEvidence WithChange(string uuid, int mastery, bool discovered, long epoch) =>
        new(Basis, this, uuid, mastery, discovered, epoch, _buffer);

    internal static MentorRelationshipEvidence CreateBasis(
        MentorRelationshipSnapshot snapshot,
        MentorRelationshipEvidenceBuffer buffer) =>
        new(snapshot, null, null, 0, false, snapshot.Epoch, buffer);

    internal MentorRelationshipEvidence Append(string uuid, int mastery, bool discovered, long epoch) =>
        new(Basis, this, uuid, mastery, discovered, epoch, _buffer);

    internal MentorRelationshipEvidence ReplaceLatest(string uuid, int mastery, bool discovered, long epoch) =>
        new(Basis, Parent, uuid, mastery, discovered, epoch, _buffer);

    internal bool BelongsTo(MentorRelationshipEvidenceBuffer buffer) => ReferenceEquals(_buffer, buffer);
    internal void RetainCapture()
    {
        _captureReferences++;
        _buffer?.RetainCapture();
    }

    internal void ReleaseCapture()
    {
        if (_captureReferences <= 0) return;
        _captureReferences--;
        _buffer?.ReleaseCapture();
    }

    public MentorRelationshipSnapshot Publish(MentorRelationshipSnapshot snapshot) =>
        _resolved ??= snapshot;
}

internal enum MentorEvidenceAppendResult { Added, Coalesced, Overflow, MissingBasis }

internal sealed class MentorRelationshipEvidenceBuffer
{
    public const int DefaultCapacity = 64;
    private readonly int _capacity;
    private int _captureReferences;

    public MentorRelationshipEvidenceBuffer(int capacity = DefaultCapacity) =>
        _capacity = Math.Max(2, capacity);

    public MentorRelationshipEvidence? Head { get; private set; }
    public int VersionCount { get; private set; }
    public int Capacity => _capacity;
    public int CaptureReferences => _captureReferences;

    public void Rebase(MentorRelationshipSnapshot snapshot)
    {
        if (_captureReferences != 0)
            throw new InvalidOperationException("Cannot replace evidence while captures retain immutable heads.");
        Head = MentorRelationshipEvidence.CreateBasis(snapshot, this);
        VersionCount = 1;
    }

    public void Invalidate()
    {
        if (_captureReferences != 0)
            throw new InvalidOperationException("Cannot invalidate evidence while captures retain immutable heads.");
        Head = null;
        VersionCount = 0;
    }

    public MentorEvidenceAppendResult Append(string uuid, int mastery, bool discovered, long epoch)
    {
        if (Head is null) return MentorEvidenceAppendResult.MissingBasis;
        if (string.Equals(Head.ChangedUuid, uuid, StringComparison.Ordinal) && Head.CaptureReferences == 0)
        {
            Head = Head.ReplaceLatest(uuid, mastery, discovered, epoch);
            return MentorEvidenceAppendResult.Coalesced;
        }
        if (VersionCount >= _capacity) return MentorEvidenceAppendResult.Overflow;
        Head = Head.Append(uuid, mastery, discovered, epoch);
        VersionCount++;
        return MentorEvidenceAppendResult.Added;
    }

    internal void RetainCapture() => _captureReferences++;
    internal void ReleaseCapture()
    {
        if (_captureReferences > 0) _captureReferences--;
    }
}

internal sealed class MentorRelationshipRequirement
{
    public MentorRelationshipRequirement(long requestGeneration) => RequestGeneration = requestGeneration;
    public long RequestGeneration { get; }
    public MentorRelationshipSnapshot? Resolved { get; private set; }
    public bool IsUncertain { get; private set; }
    public void Resolve(MentorRelationshipSnapshot relationship)
    {
        if (!IsUncertain) Resolved ??= relationship;
    }
    public void MarkUncertain()
    {
        if (Resolved is null) IsUncertain = true;
    }
}

internal static class MentorRefreshCaptureOrdering
{
    public static void ObserveDelta(
        ref MentorRelationshipRequirement? current,
        long requestGeneration)
    {
        if (current?.RequestGeneration == requestGeneration) current.MarkUncertain();
        current = null;
    }

    public static void Commit(
        MentorRelationshipRequirement? current,
        long requestGeneration,
        MentorRelationshipSnapshot relationship)
    {
        if (current?.RequestGeneration == requestGeneration) current.Resolve(relationship);
    }
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
    private readonly Dictionary<string, int> _discoveredIndices;
    private readonly Dictionary<string, int> _recipientIndices;
    private int _highestMastery = int.MinValue;

    public MentorRelationshipResolutionWork(MentorRelationshipEvidence evidence)
    {
        _evidence = evidence;
        _cursor = evidence;
        _discovered = new List<MentorRecipe>(evidence.Basis.Discovered.Count);
        _recipients = new List<MentorRecipe>(evidence.Basis.Discovered.Count);
        _discoveredIndices = new Dictionary<string, int>(evidence.Basis.Discovered.Count, StringComparer.Ordinal);
        _recipientIndices = new Dictionary<string, int>(evidence.Basis.Discovered.Count, StringComparer.Ordinal);
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
                    _discoveredIndices.Add(orderedRecipe.Uuid, _discovered.Count - 1);
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
                        _recipientIndices.Add(recipientRecipe.Uuid, _recipients.Count - 1);
                    }
                    return;
                }
                Result = _evidence.Publish(MentorRelationshipSnapshot.CreatePreindexed(
                    _evidence.Epoch, _highestMastery, _discovered, _recipients,
                    _discoveredIndices, _recipientIndices));
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

    private static readonly MentorRecipientSnapshot LegacyRelationship =
        new(0, int.MinValue, Array.Empty<MentorRecipe>());
    private readonly Dictionary<MentorRecipientSnapshot, SnapshotAmount> _snapshots;
    private readonly Queue<MentorRecipientSnapshot> _order;

    public MentorSourceAccumulator(int capacity = 256)
    {
        _snapshots = new Dictionary<MentorRecipientSnapshot, SnapshotAmount>(Math.Max(1, Math.Min(capacity, 16)));
        _order = new Queue<MentorRecipientSnapshot>(Math.Max(1, Math.Min(capacity, 16)));
    }

    public int SourceCount { get; private set; }
    public bool HasPending => _order.Count > 0;
    public int EventCount { get; private set; }

    public void Capture(string sourceUuid, MentorAmount amount, bool qualifiesAtEvent, int eventCount = 1)
    {
        if (!qualifiesAtEvent || string.IsNullOrWhiteSpace(sourceUuid) || !amount.IsValidPositive || eventCount <= 0) return;
        Capture(LegacyRelationship, sourceUuid, amount, eventCount);
    }

    public void Capture(MentorRecipientSnapshot relationship, string sourceUuid, MentorAmount amount, int eventCount = 1)
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
        MentorRelationshipEvidence? evidence = null,
        MentorRelationshipRequirement? requirement = null)
    {
        Source = source;
        Uuid = uuid;
        MasteryLevel = masteryLevel;
        Discovered = discovered;
        ProgressionEpoch = progressionEpoch;
        Relationship = relationship;
        Evidence = evidence;
        Requirement = requirement;
    }

    public object Source { get; }
    public string Uuid { get; }
    public int MasteryLevel { get; }
    public bool Discovered { get; }
    public long ProgressionEpoch { get; }
    public MentorRelationshipSnapshot? Relationship { get; }
    public MentorRelationshipEvidence? Evidence { get; }
    public MentorRelationshipRequirement? Requirement { get; }
    public bool Equals(MentorCaptureKey other) =>
        ReferenceEquals(Source, other.Source) &&
        MasteryLevel == other.MasteryLevel &&
        Discovered == other.Discovered &&
        ProgressionEpoch == other.ProgressionEpoch &&
        ReferenceEquals(Relationship, other.Relationship) &&
        ReferenceEquals(Evidence, other.Evidence) &&
        ReferenceEquals(Requirement, other.Requirement) &&
        string.Equals(Uuid, other.Uuid, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is MentorCaptureKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        RuntimeHelpers.GetHashCode(Source), Uuid, MasteryLevel, Discovered, ProgressionEpoch,
        Relationship is null ? 0 : RuntimeHelpers.GetHashCode(Relationship),
        Evidence is null ? 0 : RuntimeHelpers.GetHashCode(Evidence),
        Requirement is null ? 0 : RuntimeHelpers.GetHashCode(Requirement));
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

    public MentorCapturedEvent WithoutRouting()
    {
        var result = new MentorCapturedEvent(
            new MentorCaptureKey(
                Key.Source, Key.Uuid, Key.MasteryLevel, Key.Discovered, Key.ProgressionEpoch),
            Amount)
        {
            EventCount = EventCount,
        };
        return result;
    }

    public MentorCapturedEvent WithRelationship(MentorRelationshipSnapshot relationship)
    {
        var result = new MentorCapturedEvent(
            new MentorCaptureKey(
                Key.Source, Key.Uuid, Key.MasteryLevel, Key.Discovered,
                Key.ProgressionEpoch, relationship),
            Amount)
        {
            EventCount = EventCount,
        };
        return result;
    }

    public void RetainEvidence() => Key.Evidence?.RetainCapture();
    public void ReleaseEvidence() => Key.Evidence?.ReleaseCapture();
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
        var captured = new MentorCapturedEvent(key, amount);
        captured.RetainEvidence();
        _pending.Add(key, captured);
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

    public int TransferEvidence(MentorRelationshipEvidenceBuffer buffer, MentorUnroutableLedger destination)
    {
        var overflowEvents = 0;
        var queued = _order.Count;
        for (var index = 0; index < queued; index++)
        {
            var key = _order.Dequeue();
            if (!_pending.TryGetValue(key, out var captured)) continue;
            if (captured.Key.Evidence is null || !captured.Key.Evidence.BelongsTo(buffer))
            {
                _order.Enqueue(key);
                continue;
            }
            _pending.Remove(key);
            EventCount -= captured.EventCount;
            if (captured.Key.Evidence.Resolved is not null)
            {
                if (!RequeueResolved(captured, captured.Key.Evidence.Resolved))
                    overflowEvents += captured.EventCount;
            }
            else if (!destination.Retain(captured)) overflowEvents += captured.EventCount;
            captured.ReleaseEvidence();
        }
        return overflowEvents;
    }

    public bool RequeueResolved(MentorCapturedEvent captured, MentorRelationshipSnapshot relationship)
    {
        var rebound = captured.WithRelationship(relationship);
        if (_pending.TryGetValue(rebound.Key, out var current))
        {
            current.Amount = current.Amount.Add(rebound.Amount);
            current.EventCount += rebound.EventCount;
            EventCount += rebound.EventCount;
            return true;
        }
        if (_pending.Count >= _capacity) return false;
        _pending.Add(rebound.Key, rebound);
        _order.Enqueue(rebound.Key);
        EventCount += rebound.EventCount;
        return true;
    }

    public void Cancel()
    {
        foreach (var captured in _pending.Values) captured.ReleaseEvidence();
        _pending.Clear();
        _order.Clear();
        EventCount = 0;
    }
}

internal sealed class MentorUnroutableLedger
{
    private readonly int _capacity;
    private readonly Dictionary<MentorCaptureKey, MentorCapturedEvent> _pending;

    public MentorUnroutableLedger(int capacity = 256)
    {
        _capacity = Math.Max(1, capacity);
        _pending = new Dictionary<MentorCaptureKey, MentorCapturedEvent>(_capacity);
    }

    public int Count => _pending.Count;
    public int EventCount { get; private set; }
    public MentorAmount TotalAmount { get; private set; }

    public bool Retain(MentorCapturedEvent captured)
    {
        captured = captured.WithoutRouting();
        if (_pending.TryGetValue(captured.Key, out var current))
        {
            current.Amount = current.Amount.Add(captured.Amount);
            current.EventCount += captured.EventCount;
            EventCount += captured.EventCount;
            TotalAmount = TotalAmount.Add(captured.Amount);
            return true;
        }
        if (_pending.Count >= _capacity) return false;
        _pending.Add(captured.Key, captured);
        EventCount += captured.EventCount;
        TotalAmount = TotalAmount.Add(captured.Amount);
        return true;
    }

    public void Cancel()
    {
        _pending.Clear();
        EventCount = 0;
        TotalAmount = default;
    }
}

internal class MentorPendingWork
{
    public readonly MentorEngine Engine = new();
    public readonly MentorCaptureQueue Captures = new();
    public readonly MentorSourceAccumulator Sources = new();
    public readonly MentorUnroutableLedger Unroutable = new();
    public MentorPlan? ActivePlan;
    public MentorCapturedEvent? ResolvingCapture;
    public MentorRelationshipResolutionWork? RelationshipResolution;

    public bool HasGrantBarrier => Captures.Count > 0 || Sources.HasPending || ActivePlan is not null ||
        ResolvingCapture is not null || RelationshipResolution is not null;

    public void CancelPending()
    {
        Captures.Cancel();
        Sources.Cancel();
        Unroutable.Cancel();
        ActivePlan = null;
        ResolvingCapture?.ReleaseEvidence();
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

internal static class MentorRefreshPassContinuity
{
    public static bool ShouldStartNewPass(bool hasActivePass) => !hasActivePass;
    public static bool RequiresFollowUp(MentorWorkGeneration requests, long passGeneration) =>
        !requests.IsCurrent(passGeneration);
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
