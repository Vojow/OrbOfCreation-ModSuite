using OrbModding.Common;

namespace OrbAutomata;

internal readonly struct SpellLevelFeatureStatus
{
    public SpellLevelFeatureStatus(
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
/// What Spell Leveling's health line says, given what the player configured, what the suite is allowed
/// to do, and what the boundary has learned about the game's progression.
/// </summary>
/// <remarks>
/// The order of the terms is the order a player would ask them in: is the feature on, is its parent
/// on, is anything blocking the whole suite, do we hold the action family, has the game unlocked
/// leveling at all, and have we actually run yet. Reporting progression before ownership would tell a
/// player their spells are locked when the truth is that another plugin holds the lease.
/// </remarks>
internal static class SpellLevelFeatureStatusProjector
{
    internal const string ConfigurationDisabledSummary = "Spell leveling is disabled by configuration.";
    internal const string ProgressionLockedSummary = "Spell leveling has not been unlocked.";

    public static SpellLevelFeatureStatus Project(
        bool pluginEnabled,
        bool featureEnabled,
        bool parentEnabled,
        bool emergencyDisabled,
        bool owned,
        bool cycleObserved,
        AutoSpellLevelCapability capability)
    {
        if (!featureEnabled)
        {
            return new SpellLevelFeatureStatus(
                FeatureStatusState.ConfigurationDisabled,
                FeatureStatusReasonCode.ConfigurationDisabled,
                ConfigurationDisabledSummary);
        }

        if (!pluginEnabled)
        {
            return new SpellLevelFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ParentFeatureDisabled,
                "Automata is disabled by configuration.");
        }

        if (!parentEnabled)
        {
            return new SpellLevelFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ParentFeatureDisabled,
                "Auto Buy is disabled by configuration.");
        }

        if (emergencyDisabled)
        {
            return new SpellLevelFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled,
                "Emergency disable blocks spell leveling.");
        }

        if (!owned)
        {
            return new SpellLevelFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Another plugin owns spell-level purchases.");
        }

        // Only meaningful once something has actually looked. Before the first cycle the capability
        // holder still reads Locked because that is what a lifecycle boundary resets it to, and
        // reporting that as progression would show every new game as locked for a moment.
        if (cycleObserved && capability == AutoSpellLevelCapability.Locked)
        {
            return new SpellLevelFeatureStatus(
                FeatureStatusState.Locked,
                FeatureStatusReasonCode.ProgressionLocked,
                ProgressionLockedSummary);
        }

        if (!cycleObserved)
        {
            return new SpellLevelFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.RegistryNotReady,
                "Spell leveling is waiting for its first evaluation.");
        }

        return new SpellLevelFeatureStatus(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            capability == AutoSpellLevelCapability.All
                ? "Spell leveling is active and levels every ready spell at once."
                : "Spell leveling is active.");
    }
}
