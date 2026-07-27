using System;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoBuyFeatureStatusProjectorTests
{
    [Fact]
    public void ConfiguredAndOwnedFeatureReportsOperationalOnceItHasEvaluated()
    {
        var result = Project(cycleObserved: true);

        Assert.Equal(FeatureStatusState.Operational, result.State);
        Assert.Equal(FeatureStatusReasonCode.None, result.Reason);
        Assert.Equal(string.Empty, result.Summary);
    }

    [Fact]
    public void ConfiguredFeatureWaitsUntilItHasEvaluated()
    {
        var result = Project(cycleObserved: false);

        Assert.Equal(FeatureStatusState.NotReady, result.State);
        Assert.Equal(FeatureStatusReasonCode.Initializing, result.Reason);
    }

    [Fact]
    public void DisabledFeatureSaysDisabledByConfiguration()
    {
        var result = Project(featureEnabled: false);

        Assert.Equal(FeatureStatusState.ConfigurationDisabled, result.State);
        Assert.Equal(FeatureStatusReasonCode.ConfigurationDisabled, result.Reason);
        Assert.Contains("disabled", result.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal responder turns the mode off and explains why. The configuration publication that
    /// follows must not replace that explanation with the generic disabled-by-configuration line.
    /// </summary>
    [Fact]
    public void StandDownSummarySurvivesTheConfigurationItCaused()
    {
        const string summary = "Auto Buy planned a purchase the game would not take.";

        var result = Project(featureEnabled: false, standDownSummary: summary);

        Assert.Equal(FeatureStatusState.ConfigurationDisabled, result.State);
        Assert.Equal(FeatureStatusReasonCode.InvariantViolation, result.Reason);
        Assert.Equal(summary, result.Summary);
    }

    [Fact]
    public void SuiteMasterSwitchBlocksWithoutClaimingTheFeatureIsSwitchedOff()
    {
        var result = Project(pluginEnabled: false);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, result.State);
        Assert.Equal(FeatureStatusReasonCode.ParentFeatureDisabled, result.Reason);
    }

    [Fact]
    public void EmergencyDisableBlocksAnEnabledFeature()
    {
        var result = Project(emergencyDisabled: true);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, result.State);
        Assert.Equal(FeatureStatusReasonCode.EmergencyDisabled, result.Reason);
    }

    /// <summary>
    /// Mode Active with nothing selected is on and buying nothing, which is not the same as off: a
    /// configuration-disabled reading here would put the toggle button back to OFF while the setting
    /// reads Active — the contradiction this projection exists to remove.
    /// </summary>
    [Fact]
    public void ActiveModeWithNothingSelectedStaysConfiguredOn()
    {
        var result = Project(selected: AutoBuyCandidateKinds.None);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, result.State);
        Assert.Equal(FeatureStatusReasonCode.ConfigurationDisabled, result.Reason);
    }

    [Fact]
    public void LosingEveryOwnedPurchaseKindReportsAnActionFamilyConflict()
    {
        var result = Project(owned: AutoBuyCandidateKinds.None);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, result.State);
        Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, result.Reason);
    }

    [Fact]
    public void LosingOneOfTwoSelectedPurchaseKindsReportsDegraded()
    {
        var result = Project(
            selected: AutoBuyCandidateKinds.All,
            owned: AutoBuyCandidateKinds.Structures);

        Assert.Equal(FeatureStatusState.Degraded, result.State);
        Assert.Equal(FeatureStatusReasonCode.PartialCapabilityUnavailable, result.Reason);
    }

    [Fact]
    public void AnUnselectedPurchaseKindIsNotMissingCapability()
    {
        var result = Project(
            selected: AutoBuyCandidateKinds.Structures,
            owned: AutoBuyCandidateKinds.All,
            cycleObserved: true);

        Assert.Equal(FeatureStatusState.Operational, result.State);
    }

    private static AutoBuyFeatureStatus Project(
        bool pluginEnabled = true,
        bool featureEnabled = true,
        bool emergencyDisabled = false,
        AutoBuyCandidateKinds selected = AutoBuyCandidateKinds.All,
        AutoBuyCandidateKinds owned = AutoBuyCandidateKinds.All,
        bool cycleObserved = true,
        string? standDownSummary = null) =>
        AutoBuyFeatureStatusProjector.Project(
            pluginEnabled,
            featureEnabled,
            emergencyDisabled,
            selected,
            owned,
            cycleObserved,
            standDownSummary);
}
