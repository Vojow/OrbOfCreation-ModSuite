using OrbModding.Common;

namespace OrbAutomata;

internal readonly struct AutoItemsFeatureStatus
{
    internal AutoItemsFeatureStatus(
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

internal static class AutoItemsFeatureStatusProjector
{
    internal static AutoItemsFeatureStatus Project(
        bool emergencyDisabled,
        bool owned,
        bool cycleObserved)
    {
        if (emergencyDisabled)
            return new AutoItemsFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled,
                "Automata Emergency Disable is active.");
        if (!owned)
            return new AutoItemsFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Another automation owner holds the complete consumable-use transaction.");
        if (!cycleObserved)
            return new AutoItemsFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.GameplayNotReady,
                "Auto Items is waiting for its first evaluation.");
        return new AutoItemsFeatureStatus(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            "Auto Items is active; Relics have first priority when native headroom permits, Scrolls use native random targeting, and temporary items require exact opt-in.");
    }
}
