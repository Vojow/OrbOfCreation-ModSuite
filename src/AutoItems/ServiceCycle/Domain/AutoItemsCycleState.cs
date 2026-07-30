using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal enum AutoItemsDecisionKind
{
    Disabled = 0,
    Idle = 1,
    Relic = 2,
    Scroll = 3,
}

internal readonly struct AutoItemsDecisionMetrics
{
    internal AutoItemsDecisionMetrics(
        int captured,
        int rejectedProfiles,
        int eligibleRelics,
        int eligibleScrolls,
        int plannedActions,
        AutoItemsDecisionKind kind)
    {
        Captured = captured;
        RejectedProfiles = rejectedProfiles;
        EligibleRelics = eligibleRelics;
        EligibleScrolls = eligibleScrolls;
        PlannedActions = plannedActions;
        Kind = kind;
    }

    internal int Captured { get; }
    internal int RejectedProfiles { get; }
    internal int EligibleRelics { get; }
    internal int EligibleScrolls { get; }
    internal int PlannedActions { get; }
    internal AutoItemsDecisionKind Kind { get; }
}

internal struct AutoItemsCycleState
{
    private AutoItemsCycleState(LifecycleGeneration lifecycle)
    {
        Lifecycle = lifecycle;
        Decision = default;
    }

    internal LifecycleGeneration Lifecycle { get; }
    internal AutoItemsDecisionMetrics Decision { get; private set; }

    internal static AutoItemsCycleState Create(LifecycleGeneration lifecycle) => new(lifecycle);
    internal void RecordDecision(in AutoItemsDecisionMetrics decision) => Decision = decision;
}
