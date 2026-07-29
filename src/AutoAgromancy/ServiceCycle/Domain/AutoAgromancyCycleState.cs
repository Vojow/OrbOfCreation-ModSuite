using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal enum AutoAgromancyDecisionKind
{
    Disabled = 0,
    CaptureUnavailable = 1,
    Idle = 2,
    DirectIncrease = 3,
    TriggerSweep = 4,
    Unsustainable = 5,
    InvalidFacts = 6,
    AlreadyBalanced = 7,
}

internal readonly struct AutoAgromancyDecisionMetrics
{
    internal AutoAgromancyDecisionMetrics(
        int activePairs,
        int sweepCursor,
        int plannedActions,
        AutoAgromancyDecisionKind kind,
        AutoAgromancyPlanDisposition planDisposition)
    {
        ActivePairs = activePairs;
        SweepCursor = sweepCursor;
        PlannedActions = plannedActions;
        Kind = kind;
        PlanDisposition = planDisposition;
    }

    public int ActivePairs { get; }
    public int SweepCursor { get; }
    public int PlannedActions { get; }
    public AutoAgromancyDecisionKind Kind { get; }
    public AutoAgromancyPlanDisposition PlanDisposition { get; }
}

internal sealed class AutoAgromancyObservedLevelStore
{
    private Guid[] _actions = new Guid[16];
    private Guid[] _elements = new Guid[16];
    private int[] _levels = new int[16];
    private int _count;

    internal bool IsInitialized { get; private set; }

    internal void Initialize(PublicationTable<WorldHarvestAction> table)
    {
        _count = 0;
        var rows = table.AsSpan();
        Ensure(rows.Length);
        for (var index = 0; index < rows.Length; index++)
            Append(rows[index].ActionId, rows[index].ElementId, rows[index].CurrentLevel);
        IsInitialized = true;
    }

    internal bool TryTakeIncrease(
        PublicationTable<WorldHarvestAction> table,
        out Guid actionId,
        out Guid elementId,
        out int previousLevel)
    {
        actionId = Guid.Empty;
        elementId = Guid.Empty;
        previousLevel = 0;
        if (!IsInitialized)
        {
            Initialize(table);
            return false;
        }

        var rows = table.AsSpan();
        for (var index = 0; index < rows.Length; index++)
        {
            ref readonly var row = ref rows[index];
            var stored = Find(row.ActionId, row.ElementId);
            if (stored < 0)
            {
                Ensure(_count + 1);
                Append(row.ActionId, row.ElementId, row.CurrentLevel);
                continue;
            }

            if (row.CurrentLevel > _levels[stored] && actionId == Guid.Empty)
            {
                actionId = row.ActionId;
                elementId = row.ElementId;
                previousLevel = _levels[stored];
            }
            else if (row.CurrentLevel <= _levels[stored])
            {
                // A removal is authoritative and only refreshes the baseline.
                _levels[stored] = row.CurrentLevel;
            }
        }

        RemoveMissing(rows);
        return actionId != Guid.Empty;
    }

    internal void Accept(Guid actionId, Guid elementId, int level)
    {
        var stored = Find(actionId, elementId);
        if (stored >= 0) _levels[stored] = level;
    }

    private void RemoveMissing(ReadOnlySpan<WorldHarvestAction> rows)
    {
        for (var index = _count - 1; index >= 0; index--)
        {
            var present = false;
            for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                if (rows[rowIndex].ActionId == _actions[index] &&
                    rows[rowIndex].ElementId == _elements[index])
                {
                    present = true;
                    break;
                }
            }
            if (present) continue;
            _count--;
            _actions[index] = _actions[_count];
            _elements[index] = _elements[_count];
            _levels[index] = _levels[_count];
        }
    }

    private int Find(Guid actionId, Guid elementId)
    {
        for (var index = 0; index < _count; index++)
            if (_actions[index] == actionId && _elements[index] == elementId) return index;
        return -1;
    }

    private void Append(Guid actionId, Guid elementId, int level)
    {
        _actions[_count] = actionId;
        _elements[_count] = elementId;
        _levels[_count] = level;
        _count++;
    }

    private void Ensure(int count)
    {
        if (count <= _actions.Length) return;
        var capacity = _actions.Length;
        while (capacity < count) capacity *= 2;
        Array.Resize(ref _actions, capacity);
        Array.Resize(ref _elements, capacity);
        Array.Resize(ref _levels, capacity);
    }
}

internal struct AutoAgromancyCycleState
{
    private AutoAgromancyCycleState(LifecycleGeneration lifecycle)
    {
        Lifecycle = lifecycle;
        ObservedLevels = new AutoAgromancyObservedLevelStore();
        PlotActionEpoch = 0;
        HarvestSubmissionEpoch = 0;
        SweepPending = false;
        SweepCursor = 0;
        SweepPairCount = 0;
        SweepPairIdentityFingerprint = 0;
        PendingActionId = Guid.Empty;
        PendingElementId = Guid.Empty;
        PendingObservedLevel = 0;
        PendingTargetLevel = 0;
        PendingWasSweep = false;
        Decision = default;
    }

    public LifecycleGeneration Lifecycle { get; private set; }
    internal AutoAgromancyObservedLevelStore ObservedLevels;
    internal long PlotActionEpoch;
    internal long HarvestSubmissionEpoch;
    internal bool SweepPending;
    internal int SweepCursor;
    internal int SweepPairCount;
    internal ulong SweepPairIdentityFingerprint;
    internal Guid PendingActionId;
    internal Guid PendingElementId;
    internal int PendingObservedLevel;
    internal int PendingTargetLevel;
    internal bool PendingWasSweep;
    public AutoAgromancyDecisionMetrics Decision { get; private set; }

    public static AutoAgromancyCycleState Create(LifecycleGeneration lifecycle) => new(lifecycle);

    internal void RecordPending(in AutoAgromancyCycleAction action, bool wasSweep)
    {
        PendingActionId = action.ActionId;
        PendingElementId = action.ElementId;
        PendingObservedLevel = action.ObservedLevel;
        PendingTargetLevel = action.TargetLevel;
        PendingWasSweep = wasSweep;
    }

    internal void ClearPending()
    {
        PendingActionId = Guid.Empty;
        PendingElementId = Guid.Empty;
        PendingObservedLevel = 0;
        PendingTargetLevel = 0;
        PendingWasSweep = false;
    }

    internal void RecordDecision(in AutoAgromancyDecisionMetrics decision) => Decision = decision;
}
