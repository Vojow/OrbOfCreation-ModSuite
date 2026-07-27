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
    internal const string ConfigurationDisabledSummary = "Auto Buy is disabled by configuration.";

    /// <summary>
    /// What the feature's health line says, given what the player configured and what the running
    /// cycle reports.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The in-game toggle button, its tooltip, and the Mod Config health row all read the feature
    /// status registry rather than the configuration, so every condition that can hold Auto Buy back
    /// has to arrive here or the player is told something that is not true. Before this existed the
    /// registry was written at load and at lifecycle boundaries only, and a mode changed during
    /// gameplay moved nothing on screen at all.
    /// </para>
    /// <para>
    /// <paramref name="standDownSummary"/> is what keeps a refusal stand-down legible. The responder
    /// turns the mode off and says why; the configuration publication that follows one frame later
    /// projects the same disabled feature and would otherwise replace that account with the generic
    /// disabled-by-configuration line, so the richer reason is carried through instead of overwritten.
    /// </para>
    /// </remarks>
    public static AutoBuyFeatureStatus Project(
        bool pluginEnabled,
        bool featureEnabled,
        bool emergencyDisabled,
        AutoBuyCandidateKinds selected,
        AutoBuyCandidateKinds owned,
        bool cycleObserved,
        string? standDownSummary = null)
    {
        if (!featureEnabled)
        {
            var stoodDown = !string.IsNullOrEmpty(standDownSummary);
            return new AutoBuyFeatureStatus(
                FeatureStatusState.ConfigurationDisabled,
                stoodDown
                    ? FeatureStatusReasonCode.InvariantViolation
                    : FeatureStatusReasonCode.ConfigurationDisabled,
                stoodDown ? standDownSummary! : ConfigurationDisabledSummary);
        }

        if (!pluginEnabled)
        {
            return new AutoBuyFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ParentFeatureDisabled,
                "Automata is disabled by configuration.");
        }

        if (emergencyDisabled)
        {
            return new AutoBuyFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled,
                "Emergency disable blocks Auto Buy.");
        }

        // Mode Active with neither purchase kind selected is switched on and buying nothing. It is not
        // the same as switched off: saying disabled here would put the button back to OFF while the
        // setting reads Active, which is the exact contradiction this projection exists to remove.
        if (selected == AutoBuyCandidateKinds.None)
        {
            return new AutoBuyFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ConfigurationDisabled,
                "Auto Buy has neither structures nor upgrades selected to buy.");
        }

        var usable = owned & selected;
        if (usable == AutoBuyCandidateKinds.None)
        {
            return new AutoBuyFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Auto Buy purchase action-family ownership is unavailable.");
        }

        if (usable != selected)
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
