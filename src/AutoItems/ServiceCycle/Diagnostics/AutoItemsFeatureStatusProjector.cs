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
        string ownershipReason,
        bool bindingsAvailable,
        string bindingFailure,
        bool cycleObserved,
        AutoItemsDecisionKind decisionKind,
        AutoItemsActionHealth health)
    {
        if (emergencyDisabled)
            return Blocked(
                FeatureStatusReasonCode.EmergencyDisabled,
                "Automata Emergency Disable is active.");
        if (!owned)
            return Blocked(
                FeatureStatusReasonCode.ActionFamilyConflict,
                string.IsNullOrWhiteSpace(ownershipReason)
                    ? "Auto Items does not own ConsumableUse and NativeMultiBuyOverride."
                    : ownershipReason);
        if (!bindingsAvailable)
            return new AutoItemsFeatureStatus(
                FeatureStatusState.ContractUnavailable,
                FeatureStatusReasonCode.ContractUnavailable,
                string.IsNullOrWhiteSpace(bindingFailure)
                    ? "The lifecycle-scoped Auto Items native binding set is unavailable."
                    : bindingFailure);
        if (health.HasFailure)
            return FromActionFailure(health.Preflight, health.Reason);
        if (!cycleObserved)
            return new AutoItemsFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.GameplayNotReady,
                "Auto Items is waiting for its first world publication.");

        return new AutoItemsFeatureStatus(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            decisionKind switch
            {
                AutoItemsDecisionKind.Relic => "Auto Items planned a Relic from the latest world.",
                AutoItemsDecisionKind.Scroll => "Auto Items planned a Scroll from the latest world.",
                _ => "Auto Items is active and waiting for an eligible Scroll or Relic.",
            });
    }

    private static AutoItemsFeatureStatus FromActionFailure(
        AutoItemsPreflight preflight,
        string reason)
    {
        var summary = string.IsNullOrWhiteSpace(reason)
            ? $"Auto Items failed at {preflight}."
            : reason;
        return preflight switch
        {
            AutoItemsPreflight.ContractUnavailable =>
                new AutoItemsFeatureStatus(
                    FeatureStatusState.ContractUnavailable,
                    FeatureStatusReasonCode.ContractUnavailable,
                    summary),
            AutoItemsPreflight.Quarantined =>
                new AutoItemsFeatureStatus(
                    FeatureStatusState.Faulted,
                    FeatureStatusReasonCode.MutationQuarantined,
                    summary),
            AutoItemsPreflight.ItemUnavailable =>
                new AutoItemsFeatureStatus(
                    FeatureStatusState.NotReady,
                    FeatureStatusReasonCode.RegistryNotReady,
                    summary),
            AutoItemsPreflight.FamilyChanged =>
                new AutoItemsFeatureStatus(
                    FeatureStatusState.ContractUnavailable,
                    FeatureStatusReasonCode.IdentityMismatch,
                    summary),
            AutoItemsPreflight.MultiBuyUnavailable =>
                new AutoItemsFeatureStatus(
                    FeatureStatusState.Faulted,
                    FeatureStatusReasonCode.ContractUnavailable,
                    summary),
            AutoItemsPreflight.NativeBusy or AutoItemsPreflight.CanFireRefused =>
                Blocked(FeatureStatusReasonCode.NativeBusy, summary),
            AutoItemsPreflight.MutationPermitUnavailable =>
                Blocked(FeatureStatusReasonCode.ActionFamilyConflict, summary),
            _ => Blocked(FeatureStatusReasonCode.TemporarySafetyBlock, summary),
        };
    }

    private static AutoItemsFeatureStatus Blocked(
        FeatureStatusReasonCode reason,
        string summary) =>
        new(FeatureStatusState.TemporarilyBlocked, reason, summary);
}
