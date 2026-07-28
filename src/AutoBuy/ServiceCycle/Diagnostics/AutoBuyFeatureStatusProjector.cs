using OrbModding.Common;

namespace OrbAutomata;

internal readonly struct AutoBuyFeatureStatus
{
    public AutoBuyFeatureStatus(
        FeatureStatusState state,
        FeatureStatusReasonCode reason,
        string summary)
    {
        State = state;
        Reason = reason;
        Summary = summary;
    }

    public FeatureStatusState State { get; }
    public FeatureStatusReasonCode Reason { get; }
    public string Summary { get; }
}

internal static class AutoBuyFeatureStatusProjector
{
    /// <summary>What the running Auto Buy service reports, independent of saved intent.</summary>
    public static AutoBuyFeatureStatus Project(
        bool emergencyDisabled,
        AutoBuyCandidateKinds owned,
        bool cycleObserved)
    {
        if (emergencyDisabled)
        {
            return new AutoBuyFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled,
                "Emergency disable blocks Auto Buy.");
        }

        if (owned == AutoBuyCandidateKinds.None)
        {
            return new AutoBuyFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Auto Buy purchase action-family ownership is unavailable.");
        }

        if (owned != AutoBuyCandidateKinds.All)
        {
            return new AutoBuyFeatureStatus(
                FeatureStatusState.Degraded,
                FeatureStatusReasonCode.PartialCapabilityUnavailable,
                "One selected Auto Buy purchase kind is owned by another plugin.");
        }

        if (!cycleObserved)
        {
            return new AutoBuyFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.Initializing,
                "Auto Buy is waiting for its first evaluation.");
        }

        return new AutoBuyFeatureStatus(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            string.Empty);
    }
}
