using System;
using System.Collections.Generic;

namespace OrbAutomata;

internal enum AutoConceptOperationMode
{
    Disabled,
    Active,
}

internal enum AutoConceptSlotManagementMode
{
    RotateAll,
    PreserveManual,
}

internal readonly struct ConceptProgress
{
    public ConceptProgress(string uuid, int masteryLevel, double masteryProgress, bool eligible)
    {
        Uuid = uuid;
        MasteryLevel = masteryLevel;
        MasteryProgress = double.IsFinite(masteryProgress)
            ? Math.Clamp(masteryProgress, 0.0, 1.0)
            : 1.0;
        Eligible = eligible;
    }

    public string Uuid { get; }
    public int MasteryLevel { get; }
    public double MasteryProgress { get; }
    public bool Eligible { get; }
}

internal static class AutoConceptBalancer
{
    public static IReadOnlyList<ConceptProgress> Rank(IReadOnlyList<ConceptProgress> candidates)
    {
        var result = new List<ConceptProgress>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (candidate.Eligible && !string.IsNullOrWhiteSpace(candidate.Uuid)) result.Add(candidate);
        }
        result.Sort(Compare);
        return result;
    }

    public static bool HasStrictlyLowerMastery(ConceptProgress candidate, ConceptProgress active)
    {
        var level = candidate.MasteryLevel.CompareTo(active.MasteryLevel);
        return level < 0 || level == 0 && candidate.MasteryProgress < active.MasteryProgress;
    }

    private static int Compare(ConceptProgress left, ConceptProgress right)
    {
        var level = left.MasteryLevel.CompareTo(right.MasteryLevel);
        if (level != 0) return level;
        var progress = left.MasteryProgress.CompareTo(right.MasteryProgress);
        return progress != 0 ? progress : StringComparer.Ordinal.Compare(left.Uuid, right.Uuid);
    }
}

internal sealed class ConceptOwnershipLedger
{
    private readonly Dictionary<string, ConceptOwnership> _entries = new(StringComparer.Ordinal);

    public IEnumerable<KeyValuePair<string, ConceptOwnership>> Entries => _entries;

    public ConceptOwnership ObserveBaseline(string uuid, int quantity)
    {
        var ownership = new ConceptOwnership(Math.Max(0, quantity), 0);
        _entries[uuid] = ownership;
        return ownership;
    }

    public ConceptOwnership GetOrObserve(string uuid, int quantity)
    {
        if (_entries.TryGetValue(uuid, out var ownership)) return ownership;
        return ObserveBaseline(uuid, quantity);
    }

    public bool TryGet(string uuid, out ConceptOwnership ownership) => _entries.TryGetValue(uuid, out ownership);

    public void RecordAutomatedDelta(string uuid, int currentQuantity, int delta)
    {
        var ownership = GetOrObserve(uuid, currentQuantity - Math.Max(0, delta));
        _entries[uuid] = new ConceptOwnership(
            ownership.ManualBaseline,
            Math.Max(0, ownership.AutomatedDelta + delta));
    }

    public bool RebaselineIfUnexpected(string uuid, int actualQuantity)
    {
        if (!_entries.TryGetValue(uuid, out var ownership))
        {
            ObserveBaseline(uuid, actualQuantity);
            return false;
        }
        if (actualQuantity == ownership.ExpectedQuantity) return false;
        ObserveBaseline(uuid, actualQuantity);
        return true;
    }

    public void Clear() => _entries.Clear();
}

internal readonly struct ConceptOwnership
{
    public ConceptOwnership(int manualBaseline, int automatedDelta)
    {
        ManualBaseline = Math.Max(0, manualBaseline);
        AutomatedDelta = Math.Max(0, automatedDelta);
    }

    public int ManualBaseline { get; }
    public int AutomatedDelta { get; }
    public int ExpectedQuantity => checked(ManualBaseline + AutomatedDelta);
}
