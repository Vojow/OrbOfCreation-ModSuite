using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.SpellLeveling.Diagnostics;

/// <summary>
/// What Spell Leveling's health line says. Spell Leveling has no button of its own — Auto Buy's
/// tooltip carries its line — so this projector is the only thing standing between the player and a
/// stale claim about their progression.
/// </summary>
public sealed class SpellLevelFeatureStatusProjectorTests
{
    [Fact]
    public void ItsOwnSwitchOutranksEverythingElse()
    {
        var status = Project(featureEnabled: false, pluginEnabled: false, parentEnabled: false);

        Assert.Equal(FeatureStatusState.ConfigurationDisabled, status.State);
        Assert.Equal(FeatureStatusReasonCode.ConfigurationDisabled, status.Reason);
        Assert.Equal(SpellLevelFeatureStatusProjector.ConfigurationDisabledSummary, status.Summary);
    }

    [Fact]
    public void ADisabledParentIsNamedAsTheParentRatherThanAsThisFeature()
    {
        // Spell Leveling rides Auto Buy's switch, so "off" has two very different explanations and a
        // player who turned Auto Buy off should be told that rather than that spell leveling broke.
        var plugin = Project(pluginEnabled: false);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, plugin.State);
        Assert.Equal(FeatureStatusReasonCode.ParentFeatureDisabled, plugin.Reason);
        Assert.Contains("Automata", plugin.Summary);

        var parent = Project(parentEnabled: false);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, parent.State);
        Assert.Equal(FeatureStatusReasonCode.ParentFeatureDisabled, parent.Reason);
        Assert.Contains("Auto Buy", parent.Summary);
    }

    [Fact]
    public void AnEmergencyStopOutranksOwnershipAndProgression()
    {
        var status = Project(emergencyDisabled: true, owned: false, cycleObserved: true);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.EmergencyDisabled, status.Reason);
    }

    [Fact]
    public void LosingTheActionFamilyIsReportedBeforeProgression()
    {
        // Reporting progression first would tell a player their spells are locked when the truth is
        // that another plugin holds the lease and nothing has been able to look.
        var status = Project(owned: false, cycleObserved: true, capability: AutoSpellLevelCapability.Locked);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, status.Reason);
    }

    [Fact]
    public void BeforeTheFirstCycleTheFeatureIsNotReadyRatherThanLocked()
    {
        // Unknown and locked must not look alike. The capability holder reads Locked until a cycle has
        // run, so reporting progression from it would show every new game as locked for a moment.
        var status = Project(cycleObserved: false, capability: AutoSpellLevelCapability.Locked);

        Assert.Equal(FeatureStatusState.NotReady, status.State);
        Assert.Equal(FeatureStatusReasonCode.RegistryNotReady, status.Reason);
    }

    [Fact]
    public void AfterACycleAnUnlockedProgressionIsReportedAsLocked()
    {
        var status = Project(cycleObserved: true, capability: AutoSpellLevelCapability.Locked);

        Assert.Equal(FeatureStatusState.Locked, status.State);
        Assert.Equal(FeatureStatusReasonCode.ProgressionLocked, status.Reason);
        Assert.Equal(SpellLevelFeatureStatusProjector.ProgressionLockedSummary, status.Summary);
    }

    [Fact]
    public void ARunningFeatureSaysWhetherItLevelsOneSpellOrEveryReadyOne()
    {
        var single = Project(cycleObserved: true, capability: AutoSpellLevelCapability.Single);
        Assert.Equal(FeatureStatusState.Operational, single.State);
        Assert.Equal(FeatureStatusReasonCode.None, single.Reason);
        Assert.DoesNotContain("at once", single.Summary);

        var all = Project(cycleObserved: true, capability: AutoSpellLevelCapability.All);
        Assert.Equal(FeatureStatusState.Operational, all.State);
        Assert.Contains("at once", all.Summary);
    }

    private static SpellLevelFeatureStatus Project(
        bool pluginEnabled = true,
        bool featureEnabled = true,
        bool parentEnabled = true,
        bool emergencyDisabled = false,
        bool owned = true,
        bool cycleObserved = true,
        AutoSpellLevelCapability capability = AutoSpellLevelCapability.Single) =>
        SpellLevelFeatureStatusProjector.Project(
            pluginEnabled, featureEnabled, parentEnabled, emergencyDisabled, owned, cycleObserved, capability);
}
