using System;
using System.Collections.Generic;

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

    public MentorPlan(IReadOnlyList<MentorRecipe> recipients, MentorAmount amount)
    {
        _recipients = recipients;
        _amount = amount;
    }

    public int RemainingCount => Math.Max(0, _recipients.Count - _nextRecipient);

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

internal sealed class MentorSourceAccumulator
{
    private readonly Dictionary<string, MentorAmount> _sources = new(StringComparer.Ordinal);

    public int SourceCount => _sources.Count;
    public bool HasPending => _sources.Count > 0;

    public void Capture(string sourceUuid, MentorAmount amount, bool qualifiesAtEvent)
    {
        if (!qualifiesAtEvent || string.IsNullOrWhiteSpace(sourceUuid) || !amount.IsValidPositive) return;
        if (_sources.TryGetValue(sourceUuid, out var current))
            _sources[sourceUuid] = current.Add(amount);
        else
            _sources.Add(sourceUuid, amount);
    }

    public MentorAmount Drain()
    {
        var total = default(MentorAmount);
        foreach (var amount in _sources.Values) total = total.Add(amount);
        _sources.Clear();
        return total;
    }

    public void Cancel() => _sources.Clear();
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
        IReadOnlyList<MentorRecipe> recipients)
    {
        if (!sourceXp.IsValidPositive || !double.IsFinite(sharePercent) || recipients.Count == 0) return null;
        var fraction = Math.Clamp(sharePercent, 0.0, 100.0) / 100.0;
        var amount = sourceXp.Multiply(mode == MentorEconomyMode.SharedPool ? fraction / recipients.Count : fraction);
        return amount.IsValidPositive ? new MentorPlan(recipients, amount) : null;
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

    public int PendingCount => _pending.Count;
    public void Cancel() { _pending.Clear(); _pendingOrder.Clear(); }
}
