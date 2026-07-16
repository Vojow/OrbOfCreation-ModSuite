using System;
using System.Collections.Generic;

namespace OrbAutomata;

internal sealed class AutoBuyCandidateIndex
{
    private readonly Dictionary<string, Entry> _entries =
        new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private readonly List<IAutoBuyCandidate> _active = new List<IAutoBuyCandidate>();
    private long _epoch = 1;

    public long Epoch => _epoch;

    public IReadOnlyList<IAutoBuyCandidate> Reconcile(IEnumerable<IAutoBuyCandidate> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var epochChanged = false;

        foreach (var candidate in candidates)
        {
            AutoBuyCandidateSnapshot snapshot;
            try
            {
                snapshot = candidate.Snapshot();
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(snapshot.Uuid))
            {
                continue;
            }

            seen.Add(snapshot.Uuid);
            if (!_entries.TryGetValue(snapshot.Uuid, out var entry))
            {
                _entries.Add(snapshot.Uuid, new Entry(candidate, snapshot, _epoch));
                continue;
            }

            if (entry.Definition.Kind != snapshot.Kind ||
                !string.Equals(entry.Definition.ReflectedType, snapshot.ReflectedType, StringComparison.Ordinal))
            {
                entry.MarkInvalid("UUID was registered with a different native type", _epoch);
                continue;
            }

            if (!ReferenceEquals(GetNativeIdentity(entry.Candidate), GetNativeIdentity(candidate)))
            {
                entry.Replace(candidate, snapshot, _epoch + 1);
                epochChanged = true;
            }
            else if (entry.State == AutoBuyCandidateLifecycleState.Invalid &&
                     entry.InvalidReason == "UUID was registered with a different native type")
            {
                entry.Restore(_epoch);
            }
        }

        foreach (var pair in _entries)
        {
            if (!seen.Contains(pair.Key))
            {
                pair.Value.MarkInvalid("candidate is no longer present in its native registry", _epoch);
            }
        }

        if (epochChanged)
        {
            BeginLifecycleEpochCore();
        }

        var rollbackDetected = RefreshAll();
        if (rollbackDetected)
        {
            BeginLifecycleEpochCore();
            RefreshAll();
        }

        RebuildActiveSet();
        return _active;
    }

    public void BeginLifecycleEpoch()
    {
        BeginLifecycleEpochCore();
        RefreshAll();
        RebuildActiveSet();
    }

    public bool TryGetState(string uuid, out AutoBuyCandidateLifecycleState state)
    {
        if (_entries.TryGetValue(uuid, out var entry))
        {
            state = entry.State;
            return true;
        }

        state = AutoBuyCandidateLifecycleState.Invalid;
        return false;
    }

    public bool TryGetCandidate(string uuid, out IAutoBuyCandidate candidate)
    {
        if (_entries.TryGetValue(uuid, out var entry))
        {
            candidate = entry.Candidate;
            return true;
        }

        candidate = null!;
        return false;
    }

    public void Clear()
    {
        _entries.Clear();
        _active.Clear();
        _epoch++;
    }

    private bool RefreshAll()
    {
        var rollbackDetected = false;
        foreach (var entry in _entries.Values)
        {
            if (entry.State == AutoBuyCandidateLifecycleState.Invalid &&
                entry.InvalidReason == "UUID was registered with a different native type")
            {
                continue;
            }

            rollbackDetected |= entry.Refresh(_epoch);
        }

        return rollbackDetected;
    }

    private void BeginLifecycleEpochCore()
    {
        _epoch++;
        foreach (var entry in _entries.Values)
        {
            entry.BeginEpoch(_epoch);
        }
    }

    private void RebuildActiveSet()
    {
        _active.Clear();
        foreach (var entry in _entries.Values)
        {
            if (entry.IsEligibleForHotSet)
            {
                _active.Add(entry.Candidate);
            }
        }

        _active.Sort((left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.Snapshot().Uuid, right.Snapshot().Uuid));
    }

    private static object GetNativeIdentity(IAutoBuyCandidate candidate)
    {
        return candidate is IAutoBuyNativeIdentity identity ? identity.NativeIdentity : candidate;
    }

    private sealed class Entry
    {
        private int? _lastCurrentLevel;

        public Entry(IAutoBuyCandidate candidate, AutoBuyCandidateSnapshot snapshot, long epoch)
        {
            Candidate = candidate;
            Definition = new Definition(snapshot.Uuid, snapshot.Kind, snapshot.ReflectedType);
            State = AutoBuyCandidateLifecycleState.Registered;
            Epoch = epoch;
            InvalidReason = string.Empty;
        }

        public IAutoBuyCandidate Candidate { get; private set; }

        public Definition Definition { get; }

        public AutoBuyCandidateLifecycleState State { get; private set; }

        public long Epoch { get; private set; }

        public string InvalidReason { get; private set; }

        public bool IsEligibleForHotSet { get; private set; }

        public void Replace(IAutoBuyCandidate candidate, AutoBuyCandidateSnapshot snapshot, long epoch)
        {
            Candidate = candidate;
            State = AutoBuyCandidateLifecycleState.Registered;
            Epoch = epoch;
            InvalidReason = string.Empty;
            IsEligibleForHotSet = false;
            _lastCurrentLevel = null;
        }

        public void BeginEpoch(long epoch)
        {
            Epoch = epoch;
            State = AutoBuyCandidateLifecycleState.Registered;
            InvalidReason = string.Empty;
            IsEligibleForHotSet = false;
            _lastCurrentLevel = null;
        }

        public void Restore(long epoch)
        {
            State = AutoBuyCandidateLifecycleState.Registered;
            Epoch = epoch;
            InvalidReason = string.Empty;
            IsEligibleForHotSet = false;
        }

        public void MarkInvalid(string reason, long epoch)
        {
            State = AutoBuyCandidateLifecycleState.Invalid;
            Epoch = epoch;
            InvalidReason = reason;
            IsEligibleForHotSet = false;
        }

        public bool Refresh(long epoch)
        {
            if (Candidate is not IAutoBuyLifecycleCandidate lifecycle)
            {
                MarkInvalid("native lifecycle evidence unavailable", epoch);
                return false;
            }

            if (!lifecycle.TryGetLifecycleEvidence(out var evidence, out var reason))
            {
                MarkInvalid(string.IsNullOrWhiteSpace(reason) ? "native lifecycle evidence unavailable" : reason, epoch);
                return false;
            }

            if (evidence.CurrentLevel < 0 || evidence.QueuedLevels < 0 ||
                (evidence.IsMaxLevel && !evidence.HasFiniteLevels) ||
                (evidence.IsMaxLevel && evidence.QueuedLevels > 0) ||
                (evidence.IsMaxLevel && !evidence.IsMaxQueuedLevel))
            {
                MarkInvalid("native lifecycle evidence is contradictory", epoch);
                return false;
            }

            var rollbackDetected = _lastCurrentLevel.HasValue && evidence.CurrentLevel < _lastCurrentLevel.Value;
            _lastCurrentLevel = evidence.CurrentLevel;
            Epoch = epoch;
            InvalidReason = string.Empty;
            IsEligibleForHotSet = false;

            if (evidence.HasFiniteLevels && evidence.IsMaxLevel && evidence.QueuedLevels == 0)
            {
                State = AutoBuyCandidateLifecycleState.Completed;
            }
            else if (evidence.HasFiniteLevels && evidence.IsMaxQueuedLevel && evidence.QueuedLevels > 0)
            {
                State = AutoBuyCandidateLifecycleState.TerminalQueued;
            }
            else if (evidence.QueuedLevels > 0)
            {
                State = AutoBuyCandidateLifecycleState.Queued;
                IsEligibleForHotSet = evidence.IsAvailable;
            }
            else
            {
                State = evidence.IsAvailable
                    ? AutoBuyCandidateLifecycleState.Available
                    : AutoBuyCandidateLifecycleState.Locked;
                IsEligibleForHotSet = evidence.IsAvailable;
            }

            return rollbackDetected;
        }
    }

    private sealed class Definition
    {
        public Definition(string uuid, AutoBuyCandidateKind kind, string reflectedType)
        {
            Uuid = uuid;
            Kind = kind;
            ReflectedType = reflectedType;
        }

        public string Uuid { get; }

        public AutoBuyCandidateKind Kind { get; }

        public string ReflectedType { get; }
    }
}
