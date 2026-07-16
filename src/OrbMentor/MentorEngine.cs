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
    public MentorQualifiedBatch(MentorAmount amount, int eventCount, int sourceCount)
    {
        Amount = amount;
        EventCount = eventCount;
        SourceCount = sourceCount;
    }

    public MentorAmount Amount { get; }
    public int EventCount { get; }
    public int SourceCount { get; }
}

internal sealed class MentorSourceAccumulator
{
    private sealed class SourceAmount
    {
        public MentorAmount Amount;
        public int EventCount;
    }

    private readonly Dictionary<string, SourceAmount> _sources = new(StringComparer.Ordinal);

    public int SourceCount => _sources.Count;
    public bool HasPending => _sources.Count > 0;
    public int EventCount { get; private set; }

    public void Capture(string sourceUuid, MentorAmount amount, bool qualifiesAtEvent, int eventCount = 1)
    {
        if (!qualifiesAtEvent || string.IsNullOrWhiteSpace(sourceUuid) || !amount.IsValidPositive || eventCount <= 0) return;
        if (_sources.TryGetValue(sourceUuid, out var current))
        {
            current.Amount = current.Amount.Add(amount);
            current.EventCount += eventCount;
        }
        else
        {
            _sources.Add(sourceUuid, new SourceAmount { Amount = amount, EventCount = eventCount });
        }
        EventCount += eventCount;
    }

    public MentorQualifiedBatch Drain()
    {
        var total = default(MentorAmount);
        foreach (var source in _sources.Values) total = total.Add(source.Amount);
        var result = new MentorQualifiedBatch(total, EventCount, _sources.Count);
        _sources.Clear();
        EventCount = 0;
        return result;
    }

    public void Cancel() { _sources.Clear(); EventCount = 0; }
}

internal readonly struct MentorCaptureKey : IEquatable<MentorCaptureKey>
{
    public MentorCaptureKey(object source, string uuid, int masteryLevel, bool discovered, long progressionEpoch = 0)
    {
        Source = source;
        Uuid = uuid;
        MasteryLevel = masteryLevel;
        Discovered = discovered;
        ProgressionEpoch = progressionEpoch;
    }

    public object Source { get; }
    public string Uuid { get; }
    public int MasteryLevel { get; }
    public bool Discovered { get; }
    public long ProgressionEpoch { get; }
    public bool Equals(MentorCaptureKey other) =>
        ReferenceEquals(Source, other.Source) &&
        MasteryLevel == other.MasteryLevel &&
        Discovered == other.Discovered &&
        ProgressionEpoch == other.ProgressionEpoch &&
        string.Equals(Uuid, other.Uuid, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is MentorCaptureKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Source), Uuid, MasteryLevel, Discovered, ProgressionEpoch);
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
    private readonly Dictionary<MentorCaptureKey, MentorCapturedEvent> _pending = new();
    private readonly Queue<MentorCaptureKey> _order = new();

    public MentorCaptureQueue(int capacity = 256) => _capacity = Math.Max(1, capacity);
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

internal enum MentorDropReason
{
    CaptureUnavailable,
    CaptureOverflow,
    SourceIdentityChanged,
    StaleRelationship,
    SourceIneligible,
    NoRecipients,
    ZeroShare,
    RecipientIdentityChanged,
    RecipientIneligible,
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

internal enum MentorQualificationStatus { Qualified, StaleRelationship, SourceIneligible, NoRecipients }

internal static class MentorRelationshipQualification
{
    public static MentorQualificationStatus Evaluate(
        MentorCaptureKey captured,
        long relationshipEpoch,
        int highestMastery,
        int recipientCount)
    {
        if (captured.ProgressionEpoch != relationshipEpoch) return MentorQualificationStatus.StaleRelationship;
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

    public bool Complete(string uuid)
    {
        if (_pendingOrder.Count == 0 || !string.Equals(_pendingOrder.Peek(), uuid, StringComparison.Ordinal)) return false;
        _pendingOrder.Dequeue();
        return _pending.Remove(uuid);
    }

    public int PendingCount => _pending.Count;
    public void Cancel() { _pending.Clear(); _pendingOrder.Clear(); }
}
