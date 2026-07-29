using System.Collections.Generic;
using BepInEx.Configuration;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests;

public sealed class EmergencyStopControlTests
{
    [Fact]
    public void StopIsImmediateAndResumeRequiresPreviewConfirmation()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var events = new List<string>();
        var publications = new List<(SuiteRuntimeConfiguration Configuration, ConfigGeneration Generation)>();
        var store = new AutomataConfigurationStore(
            config,
            (configuration, generation) =>
            {
                events.Add("published");
                publications.Add((configuration, generation));
            });
        var control = new EmergencyStopControl(
            store,
            () => new[] { "Auto Buy", "Spell Leveling" },
            _ => events.Add("changed"));

        control.Activate();

        Assert.True(store.Current.Safety.EmergencyDisable);
        Assert.Equal("STOPPED", control.Label);
        Assert.Equal(new[] { "changed", "published" }, events);
        Assert.Single(publications);
        Assert.True(publications[0].Configuration.Safety.EmergencyDisable);
        Assert.Equal(new ConfigGeneration(2), publications[0].Generation);

        control.Activate();

        Assert.True(store.Current.Safety.EmergencyDisable);
        Assert.True(control.ResumeArmed);
        Assert.Equal("RESUME?", control.Label);
        Assert.Equal("Will resume: Auto Buy, Spell Leveling", control.ResumePreview);
        Assert.Equal(2, events.Count);
        Assert.Single(publications);

        control.Activate();

        Assert.False(store.Current.Safety.EmergencyDisable);
        Assert.False(control.ResumeArmed);
        Assert.Equal(
            new[] { "changed", "published", "changed", "published" },
            events);
        Assert.Equal(2, publications.Count);
        Assert.False(publications[1].Configuration.Safety.EmergencyDisable);
        Assert.Equal(new ConfigGeneration(3), publications[1].Generation);
    }

    [Fact]
    public void EmergencyProjectionOverridesHealthyNotReadyAndFaultedRuntimeStates()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var registry = new FeatureStatusRegistry();
        var initialGeneration = new ConfigGeneration(1);
        using var statuses = new AutomataFeatureStatuses(
            config.Current,
            3,
            registry,
            initialGeneration);
        var store = new AutomataConfigurationStore(
            config,
            statuses.ObserveConfiguration);
        var control = new EmergencyStopControl(
            store,
            () => new[] { "Auto Buy", "Auto Cast" },
            _ => { });

        statuses.AutoCast.ObserveOperational();
        control.Activate();
        AssertStopped(statuses.AutoCast.Current);

        statuses.ObserveLifecycleNotReady(
            store.Current,
            4,
            statuses.AutoBuy.ConfigurationGeneration);
        AssertStopped(statuses.AutoCast.Current);

        statuses.ObserveServiceCycleUnavailable(
            store.Current,
            statuses.AutoBuy.ConfigurationGeneration);
        AssertStopped(statuses.AutoCast.Current);
    }

    [Fact]
    public void CompatibilityQuarantineCannotBeResumedBeforeAcknowledgement()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.EmergencyDisable.Value = true;
        var store = new AutomataConfigurationStore(config, (_, _) => { });
        var control = new EmergencyStopControl(
            store,
            () => new[] { "Auto Buy" },
            _ => { },
            canResume: () => false);

        control.Activate();
        control.Activate();

        Assert.True(store.Current.Safety.EmergencyDisable);
        Assert.False(control.ResumeArmed);
        Assert.Equal("STOPPED", control.Label);
        Assert.Contains("Mods > General", control.ResumePreview);
    }

    private static void AssertStopped(FeatureStatusSnapshot status)
    {
        Assert.True(status.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.EmergencyDisabled, status.Reason.Code);
    }
}
