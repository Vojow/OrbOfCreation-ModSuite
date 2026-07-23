using OrbModding.Common;

namespace OrbAutomata;

internal readonly struct AutoHarvestFeatureStatus
{
    public AutoHarvestFeatureStatus(
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

internal static class AutoHarvestFeatureStatusProjector
{
    public static AutoHarvestFeatureStatus Project(
        in AutoHarvestPairHealth fruit,
        in AutoHarvestPairHealth treasure)
    {
        var selectedCount = CountSelected(fruit) + CountSelected(treasure);
        if (selectedCount == 0)
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.ConfigurationDisabled,
                FeatureStatusReasonCode.ConfigurationDisabled,
                "Auto Harvest is disabled by configuration.");
        }

        if (TryProjectFeatureScoped(fruit, treasure, out var featureScoped))
            return featureScoped;

        var hasUnavailablePair = IsPairUnavailable(fruit) || IsPairUnavailable(treasure);
        var hasCapableSibling = IsCapable(fruit) || IsCapable(treasure);
        if (hasUnavailablePair && hasCapableSibling)
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.Degraded,
                FeatureStatusReasonCode.PartialCapabilityUnavailable,
                "One selected harvest pair is unavailable while another remains usable.");
        }

        if (IsEligible(fruit) || IsEligible(treasure))
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.Operational,
                FeatureStatusReasonCode.None,
                string.Empty);
        }

        if (IsNativeBusy(fruit) || IsNativeBusy(treasure))
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.NativeBusy,
                "An unlocked selected harvest action is waiting for native readiness.");
        }

        if (IsQueueBlocked(fruit) || IsQueueBlocked(treasure))
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.QueueFull,
                "The native plot-action list has no free action entry.");
        }

        if (IsPairFaulted(fruit) || IsPairFaulted(treasure))
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.Faulted,
                FeatureStatusReasonCode.PostconditionFailed,
                "A selected harvest pair is blocked after an unverifiable native mutation.");
        }

        if (IsPairContractUnavailable(fruit) || IsPairContractUnavailable(treasure))
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.ContractUnavailable,
                FeatureStatusReasonCode.ContractUnavailable,
                "A selected harvest pair failed its audited native contract.");
        }

        if (IsRegistryNotReady(fruit) || IsRegistryNotReady(treasure) ||
            IsNotObserved(fruit) || IsNotObserved(treasure))
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.RegistryNotReady,
                "Auto Harvest is waiting for authoritative native evidence.");
        }

        return new AutoHarvestFeatureStatus(
            FeatureStatusState.Locked,
            FeatureStatusReasonCode.ProgressionLocked,
            "The selected harvest content is not currently unlocked and available.");
    }

    private static bool TryProjectFeatureScoped(
        in AutoHarvestPairHealth fruit,
        in AutoHarvestPairHealth treasure,
        out AutoHarvestFeatureStatus health)
    {
        var faulted = IsFeatureScoped(fruit, AutoHarvestPairHealthKind.Faulted) ||
                      IsFeatureScoped(treasure, AutoHarvestPairHealthKind.Faulted);
        if (faulted)
        {
            health = new AutoHarvestFeatureStatus(
                FeatureStatusState.Faulted,
                FeatureStatusReasonCode.RuntimeFailure,
                "The Auto Harvest runtime is unavailable after a native failure.");
            return true;
        }

        var contract = IsFeatureScoped(fruit, AutoHarvestPairHealthKind.ContractUnavailable) ||
                       IsFeatureScoped(treasure, AutoHarvestPairHealthKind.ContractUnavailable);
        if (contract)
        {
            health = new AutoHarvestFeatureStatus(
                FeatureStatusState.ContractUnavailable,
                FeatureStatusReasonCode.ContractUnavailable,
                "The native Auto Harvest contract is unavailable.");
            return true;
        }

        var registry = IsFeatureScoped(fruit, AutoHarvestPairHealthKind.RegistryNotReady) ||
                       IsFeatureScoped(treasure, AutoHarvestPairHealthKind.RegistryNotReady);
        if (registry)
        {
            health = new AutoHarvestFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.RegistryNotReady,
                "Auto Harvest is waiting for its audited native registry entries.");
            return true;
        }

        health = default;
        return false;
    }

    private static int CountSelected(in AutoHarvestPairHealth value) => value.Selected ? 1 : 0;
    private static bool IsFeatureScoped(in AutoHarvestPairHealth value, AutoHarvestPairHealthKind kind) =>
        value.Selected && value.FeatureScoped && value.Kind == kind;
    private static bool IsPairUnavailable(in AutoHarvestPairHealth value) =>
        value.Selected && !value.FeatureScoped &&
        (value.Kind == AutoHarvestPairHealthKind.ContractUnavailable ||
         value.Kind == AutoHarvestPairHealthKind.Faulted ||
         value.Kind == AutoHarvestPairHealthKind.RegistryNotReady);
    private static bool IsCapable(in AutoHarvestPairHealth value) =>
        value.Selected &&
        (value.Kind == AutoHarvestPairHealthKind.Eligible ||
         value.Kind == AutoHarvestPairHealthKind.NativeBusy ||
         value.Kind == AutoHarvestPairHealthKind.QueueBlocked);
    private static bool IsEligible(in AutoHarvestPairHealth value) =>
        value.Selected && value.Kind == AutoHarvestPairHealthKind.Eligible;
    private static bool IsNativeBusy(in AutoHarvestPairHealth value) =>
        value.Selected && value.Kind == AutoHarvestPairHealthKind.NativeBusy;
    private static bool IsQueueBlocked(in AutoHarvestPairHealth value) =>
        value.Selected && value.Kind == AutoHarvestPairHealthKind.QueueBlocked;
    private static bool IsPairFaulted(in AutoHarvestPairHealth value) =>
        value.Selected && !value.FeatureScoped && value.Kind == AutoHarvestPairHealthKind.Faulted;
    private static bool IsPairContractUnavailable(in AutoHarvestPairHealth value) =>
        value.Selected && !value.FeatureScoped && value.Kind == AutoHarvestPairHealthKind.ContractUnavailable;
    private static bool IsRegistryNotReady(in AutoHarvestPairHealth value) =>
        value.Selected && value.Kind == AutoHarvestPairHealthKind.RegistryNotReady;
    private static bool IsNotObserved(in AutoHarvestPairHealth value) =>
        value.Selected && value.Kind == AutoHarvestPairHealthKind.NotObserved;
}
