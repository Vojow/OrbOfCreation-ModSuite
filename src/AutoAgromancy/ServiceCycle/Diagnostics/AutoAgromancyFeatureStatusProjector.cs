using OrbModding.Common;

namespace OrbAutomata;

internal readonly struct AutoAgromancyFeatureStatus
{
    internal AutoAgromancyFeatureStatus(
        FeatureStatusState state,
        FeatureStatusReasonCode reason,
        string summary)
    {
        State = state;
        Reason = reason;
        Summary = summary;
    }

    internal FeatureStatusState State { get; }
    internal FeatureStatusReasonCode Reason { get; }
    internal string Summary { get; }
}

internal static class AutoAgromancyFeatureStatusProjector
{
    internal static AutoAgromancyFeatureStatus Project(
        bool emergencyDisabled,
        bool owned,
        bool projectionObserved,
        AutoAgromancyDecisionKind decision,
        int plannedActions,
        bool faulted)
    {
        if (emergencyDisabled)
            return new AutoAgromancyFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled,
                "Automata Emergency Disable is active.");
        if (!owned)
            return new AutoAgromancyFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Another automation owner holds the Druidry level-adjustment action family.");
        if (faulted)
            return new AutoAgromancyFeatureStatus(
                FeatureStatusState.Faulted,
                FeatureStatusReasonCode.PostconditionFailed,
                "Auto Agromancy stopped after a native or service-cycle failure.");
        if (!projectionObserved)
            return new AutoAgromancyFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.GameplayNotReady,
                "Auto Agromancy is waiting for its first evaluation.");
        if (decision is AutoAgromancyDecisionKind.CaptureUnavailable or
            AutoAgromancyDecisionKind.InvalidFacts)
            return new AutoAgromancyFeatureStatus(
                FeatureStatusState.ContractUnavailable,
                FeatureStatusReasonCode.EvidenceUnavailable,
                "Auto Agromancy cannot obtain complete authoritative harvest facts.");
        if (plannedActions > 0 ||
            decision is AutoAgromancyDecisionKind.DirectIncrease or
                AutoAgromancyDecisionKind.TriggerSweep)
            return new AutoAgromancyFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.TargetingInProgress,
                "Auto Agromancy is applying a verified level target.");
        return new AutoAgromancyFeatureStatus(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            "Auto Agromancy is active.");
    }
}
