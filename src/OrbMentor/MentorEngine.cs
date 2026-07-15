using System;
using System.Collections.Generic;
using System.Linq;

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

internal sealed class MentorEngine
{
    private readonly Dictionary<string, MentorAmount> _pending = new(StringComparer.Ordinal);
    private readonly Queue<string> _pendingOrder = new();

    public IReadOnlyList<MentorRecipe> EligibleRecipients(
        string sourceUuid,
        IReadOnlyCollection<MentorRecipe> recipes)
    {
        var discovered = recipes.Where(r => r.IsDiscovered && !string.IsNullOrWhiteSpace(r.Uuid)).ToArray();
        if (discovered.Length == 0) return Array.Empty<MentorRecipe>();
        var highest = discovered.Max(r => r.MasteryLevel);
        var source = discovered.FirstOrDefault(r => string.Equals(r.Uuid, sourceUuid, StringComparison.Ordinal));
        if (source is null || source.MasteryLevel != highest) return Array.Empty<MentorRecipe>();
        return discovered
            .Where(r => !string.Equals(r.Uuid, sourceUuid, StringComparison.Ordinal) && r.MasteryLevel < highest)
            .OrderBy(r => r.Uuid, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<MentorGrant> Plan(
        MentorAmount sourceXp,
        double sharePercent,
        MentorEconomyMode mode,
        IReadOnlyList<MentorRecipe> recipients)
    {
        if (!sourceXp.IsValidPositive || !double.IsFinite(sharePercent) || recipients.Count == 0)
            return Array.Empty<MentorGrant>();
        var fraction = Math.Clamp(sharePercent, 0.0, 100.0) / 100.0;
        var amount = sourceXp.Multiply(mode == MentorEconomyMode.SharedPool ? fraction / recipients.Count : fraction);
        if (!amount.IsValidPositive) return Array.Empty<MentorGrant>();
        return recipients.OrderBy(r => r.Uuid, StringComparer.Ordinal)
            .Select(r => new MentorGrant(r.Uuid, amount)).ToArray();
    }

    public void Consolidate(IEnumerable<MentorGrant> grants)
    {
        foreach (var grant in grants.Where(g => g.Amount.IsValidPositive))
        {
            if (_pending.TryGetValue(grant.Uuid, out var current))
            {
                _pending[grant.Uuid] = current.Add(grant.Amount);
                continue;
            }

            _pending[grant.Uuid] = grant.Amount;
            _pendingOrder.Enqueue(grant.Uuid);
        }
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

    public int PendingCount => _pending.Count;
    public void Cancel() { _pending.Clear(); _pendingOrder.Clear(); }
}
