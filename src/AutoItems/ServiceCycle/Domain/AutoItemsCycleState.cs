using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal enum AutoItemsDecisionKind
{
    Disabled = 0,
    Idle = 1,
    NativeBusy = 2,
    WaitingForToxicityRecovery = 3,
    Relic = 4,
    Scroll = 5,
    TemporaryItem = 6,
    AwaitingTemporaryActivation = 7,
    TemporaryEffectActive = 8,
    TemporaryItemQuarantined = 9,
}

internal readonly struct AutoItemsDecisionMetrics
{
    internal AutoItemsDecisionMetrics(
        int captured,
        int rejectedProfiles,
        int temporaryItems,
        int eligibleRelics,
        int eligibleScrolls,
        int plannedActions,
        AutoItemsDecisionKind kind)
    {
        Captured = captured;
        RejectedProfiles = rejectedProfiles;
        TemporaryItems = temporaryItems;
        EligibleRelics = eligibleRelics;
        EligibleScrolls = eligibleScrolls;
        PlannedActions = plannedActions;
        Kind = kind;
    }

    internal int Captured { get; }
    internal int RejectedProfiles { get; }
    internal int TemporaryItems { get; }
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
        RecoveryWaitActive = false;
    }

    internal LifecycleGeneration Lifecycle { get; }
    internal AutoItemsDecisionMetrics Decision { get; private set; }
    internal bool RecoveryWaitActive { get; private set; }

    internal static AutoItemsCycleState Create(LifecycleGeneration lifecycle) => new(lifecycle);
    internal void RecordDecision(in AutoItemsDecisionMetrics decision) => Decision = decision;
    internal void BeginRecoveryWait() => RecoveryWaitActive = true;
    internal void EndRecoveryWait() => RecoveryWaitActive = false;
}
