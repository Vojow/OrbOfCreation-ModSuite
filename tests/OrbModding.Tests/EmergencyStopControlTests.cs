using BepInEx.Configuration;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class EmergencyStopControlTests
{
    [Fact]
    public void StopIsImmediateAndResumeRequiresPreviewConfirmation()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var changes = 0;
        var control = new EmergencyStopControl(
            config,
            () => new[] { "Auto Buy", "Spell Leveling" },
            _ => changes++);

        control.Activate();

        Assert.True(config.Current.Safety.EmergencyDisable);
        Assert.Equal("STOPPED", control.Label);
        Assert.Equal(1, changes);

        control.Activate();

        Assert.True(config.Current.Safety.EmergencyDisable);
        Assert.True(control.ResumeArmed);
        Assert.Equal("RESUME?", control.Label);
        Assert.Equal("Will resume: Auto Buy, Spell Leveling", control.ResumePreview);
        Assert.Equal(1, changes);

        control.Activate();

        Assert.False(config.Current.Safety.EmergencyDisable);
        Assert.False(control.ResumeArmed);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void EmergencyProjectionOverridesHealthyNotReadyAndFaultedRuntimeStates()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 3, registry);
        var control = new EmergencyStopControl(
            config,
            () => new[] { "Auto Buy", "Auto Cast" },
            _ => statuses.ObserveConfiguration(config.Current));

        statuses.AutoCast.ObserveOperational();
        control.Activate();
        AssertStopped(statuses.AutoCast.Current);

        statuses.ObserveLifecycleNotReady(config.Current, 4);
        AssertStopped(statuses.AutoCast.Current);

        statuses.ObserveServiceCycleUnavailable(config.Current);
        AssertStopped(statuses.AutoCast.Current);
    }

    private static void AssertStopped(FeatureStatusSnapshot status)
    {
        Assert.True(status.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.EmergencyDisabled, status.Reason.Code);
    }
}
