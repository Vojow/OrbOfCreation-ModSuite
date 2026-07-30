using OrbModding.Common;

namespace OrbAutomata;

internal readonly struct AutoConceptFeatureStatus
{
    internal AutoConceptFeatureStatus(
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

internal static class AutoConceptFeatureStatusProjector
{
    public static AutoConceptFeatureStatus Project(
        bool emergencyDisabled,
        bool owned,
        bool cycleObserved,
        AutoConceptIdleReason idleReason = AutoConceptIdleReason.None)
    {
        if (emergencyDisabled)
            return new AutoConceptFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled,
                "Automata Emergency Disable is active.");
        if (!owned)
            return new AutoConceptFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Another automation owner holds the native concept-assignment action family.");
        if (!cycleObserved)
            return new AutoConceptFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.GameplayNotReady,
                "Auto Concept is waiting for its first evaluation.");
        if (idleReason == AutoConceptIdleReason.WaitingForTraining)
            return new AutoConceptFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.NativeBusy,
                "Auto Concept is waiting for the active assignment to settle and complete its training period.");
        if (idleReason == AutoConceptIdleReason.NoUnlockedAssignableReplacement)
            return new AutoConceptFeatureStatus(
                FeatureStatusState.Locked,
                FeatureStatusReasonCode.ProgressionLocked,
                "No other unlocked concept can be assigned.");
        if (idleReason == AutoConceptIdleReason.WaitingForCandidateRetry)
            return new AutoConceptFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.NativeBusy,
                "Unlocked replacements were refused by live slot or resource safety checks; Auto Concept will reconsider them after the next world or configuration publication.");
        return new AutoConceptFeatureStatus(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            "Auto Concept is active.");
    }
}
