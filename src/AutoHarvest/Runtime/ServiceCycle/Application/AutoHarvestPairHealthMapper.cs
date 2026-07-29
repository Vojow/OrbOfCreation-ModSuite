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
            // Three separate native refusals, and the player's next move differs for each: reach the
            // plot, wait for it to bear again, or wait for the game to evaluate a prerequisite. They
            // were one health kind and therefore one sentence, which was accurate for at most one of
            // them at a time — and for the third, for none of them, since the latch it reads cannot
            // distinguish an unmet prerequisite from one nobody has looked at.
            AutoHarvestRejectionReason.PlotNotVisible => AutoHarvestPairHealthKind.PlotNotVisible,
            AutoHarvestRejectionReason.ActionUnavailable => AutoHarvestPairHealthKind.ActionNotOffered,
            AutoHarvestRejectionReason.PrerequisitesNotConfirmed =>
                AutoHarvestPairHealthKind.PrerequisitesNotConfirmed,
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
