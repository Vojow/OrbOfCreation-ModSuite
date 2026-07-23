namespace OrbAutomata;

internal static class AutoHarvestPairHealthMapper
{
    public static AutoHarvestPairHealth FromDecision(
        AutoHarvestPair pair,
        in AutoHarvestPairDecision decision)
    {
        if (decision.ShouldSubmit) return AutoHarvestPairHealth.Eligible(pair);
        return FromRejection(pair, decision.RejectionReason);
    }

    private static AutoHarvestPairHealth FromRejection(
        AutoHarvestPair pair,
        AutoHarvestRejectionReason reason)
    {
        var kind = reason switch
        {
            AutoHarvestRejectionReason.PlotNotVisible or
            AutoHarvestRejectionReason.ActionUnavailable or
            AutoHarvestRejectionReason.PrerequisitesUnmet => AutoHarvestPairHealthKind.ProgressionLocked,
            AutoHarvestRejectionReason.NotReady or
            AutoHarvestRejectionReason.AlreadyQueuedOrRunning => AutoHarvestPairHealthKind.NativeBusy,
            AutoHarvestRejectionReason.NoActionSlot => AutoHarvestPairHealthKind.QueueBlocked,
            AutoHarvestRejectionReason.UnsupportedPair or
            AutoHarvestRejectionReason.DestructiveAction or
            AutoHarvestRejectionReason.ResourceDrainPresent or
            AutoHarvestRejectionReason.UnsafeCompletionEffects => AutoHarvestPairHealthKind.ContractUnavailable,
            _ => AutoHarvestPairHealthKind.RegistryNotReady,
        };
        return new AutoHarvestPairHealth(pair, selected: true, kind);
    }
}
