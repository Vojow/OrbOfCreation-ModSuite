using OrbModding.Common;

namespace OrbAutomata;

internal readonly struct AutoCastFeatureStatus
{
    public AutoCastFeatureStatus(
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

/// <summary>
/// What Auto Cast's health line says, given what the player configured, what the suite is allowed to
/// do, and whether the service has run yet.
/// </summary>
/// <remarks>
/// <para>
/// The order of the terms is the legacy engine's, unchanged, because the order is what makes the line
/// answer the question a player is actually asking. Configuration comes first, so a feature the player
/// switched off says exactly that instead of blaming whatever happens to also be true. The emergency
/// stop comes before ownership, because a suite that has stood down entirely is a bigger fact than
/// which plugin holds a lease. Manual pause comes last of the blocking terms, because it is the only
/// one that resolves on its own.
/// </para>
/// <para>
/// The plugin-disabled term is new to the projector but not to the behaviour: the legacy engine was
/// never ticked at all with the plugin off, so its line simply went stale. A projector that reports
/// only what it was handed has no such hiding place, and saying "Automata is disabled" is what the
/// old silence meant.
/// </para>
/// </remarks>
internal static class AutoCastFeatureStatusProjector
{
    internal const string ConfigurationDisabledSummary = "Auto Cast is disabled by configuration.";
    internal const string EmergencyDisabledSummary = "Automata Emergency Disable is active.";
    internal const string ManualPauseSummary = "Auto Cast is paused after manual spell input.";

    public static AutoCastFeatureStatus Project(
        bool pluginEnabled,
        bool featureEnabled,
        bool emergencyDisabled,
        bool owned,
        bool manualPaused,
        bool cycleObserved)
    {
        if (!featureEnabled)
        {
            return new AutoCastFeatureStatus(
                FeatureStatusState.ConfigurationDisabled,
                FeatureStatusReasonCode.ConfigurationDisabled,
                ConfigurationDisabledSummary);
        }

        if (!pluginEnabled)
        {
            return new AutoCastFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ParentFeatureDisabled,
                "Automata is disabled by configuration.");
        }

        if (emergencyDisabled)
        {
            return new AutoCastFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled,
                EmergencyDisabledSummary);
        }

        if (!owned)
        {
            return new AutoCastFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Another plugin owns spell casting.");
        }

        if (manualPaused)
        {
            return new AutoCastFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ManualPause,
                ManualPauseSummary);
        }

        // The legacy engine's gameplay-scene term, reached the way the runtime states it: a service
        // that has not completed a cycle has nothing to report about a world it has not seen.
        if (!cycleObserved)
        {
            return new AutoCastFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.GameplayNotReady,
                "Auto Cast is waiting for its first evaluation.");
        }

        return new AutoCastFeatureStatus(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            "Auto Cast is active.");
    }
}
