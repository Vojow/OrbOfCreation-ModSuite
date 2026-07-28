using BepInEx.Configuration;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using Xunit;

namespace OrbModding.Tests;

/// <summary>
/// Auto Cast's configuration surface and its toggle button, which are all that is left in this file:
/// the runtime it used to test moved onto the ServiceCycle and is covered by the evaluator and
/// action-adapter tests beside it.
/// </summary>
public sealed class AutoCastTests
{
    [Fact]
    public void FreshConfigUsesZeroResourceThreshold()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());

        Assert.Equal(0.0f, config.AutoCastStartResourcePercent.Value);
    }

    [Fact]
    public void FreshConfigFullChargesChargedSpellsByDefault()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());

        Assert.True(config.AutoCastFullCharge.Value);
    }

    [Fact]
    public void ExistingResourceThresholdIsPreserved()
    {
        var file = new ConfigFile();
        file.Bind("AutoCast", "StartResourcePercent", 80.0f, "existing").Value = 37.0f;

        var config = BepInExAutomataConfiguration.Bind(file);

        Assert.Equal(37.0f, config.AutoCastStartResourcePercent.Value);
    }

    [Theory]
    [InlineData(0, "AC OFF")]
    [InlineData(1, "AC ON")]
    public void CompactToggleUsesConsistentAutoCastLabels(int state, string expected)
    {
        Assert.Equal(expected, AutoCastToggleButton.FormatLabel((AutoCastToggleVisualState)state));
    }

    [Fact]
    public void EmergencyStopDisplaysDesiredAndRuntimeAxes()
    {
        Assert.Equal(
            "AC ON / STOPPED",
            AutoCastToggleButton.FormatLabel(AutoCastToggleVisualState.On, stopped: true));
    }

    [Fact]
    public void ToggleSwitchesBetweenDisabledAndActive()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var toggle = new AutoCastToggleControl(config);

        Assert.Equal(AutoCastToggleVisualState.Off, toggle.State);
        toggle.Toggle();
        Assert.Equal(AutoCastOperationMode.Active, config.AutoCastMode.Value);
        Assert.Equal(AutoCastToggleVisualState.On, toggle.State);

        toggle.Toggle();
        Assert.Equal(AutoCastToggleVisualState.Off, toggle.State);
        toggle.Toggle();
        Assert.Equal(AutoCastToggleVisualState.On, toggle.State);
    }

    [Fact]
    public void EmergencyDisableKeepsConfiguredIntentVisuallyOn()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var toggle = new AutoCastToggleControl(config);

        Assert.Equal(AutoCastToggleVisualState.On, toggle.State);
        config.EmergencyDisable.Value = true;
        Assert.Equal(AutoCastToggleVisualState.On, toggle.State);
    }
}
