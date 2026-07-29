using OrbModConfig;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace OrbModding.Tests;

public sealed class ModConfigPanelLayoutTests
{
    [Theory]
    [InlineData(0f, 86f)]
    [InlineData(20f, 86f)]
    [InlineData(80f, 129f)]
    [InlineData(float.NaN, 86f)]
    [InlineData(float.PositiveInfinity, 86f)]
    public void RowsExpandFromRenderedDescriptionHeight(float preferredHeight, float expectedHeight)
    {
        Assert.Equal(expectedHeight, ModConfigPanel.CalculateSettingRowHeight(preferredHeight));
    }

    [Theory]
    [InlineData(-10f, 500f, 200f, 0f)]
    [InlineData(125f, 500f, 200f, 125f)]
    [InlineData(400f, 500f, 200f, 300f)]
    [InlineData(50f, 100f, 200f, 0f)]
    [InlineData(float.NaN, 500f, 200f, 0f)]
    public void SamePageAbsoluteScrollOffsetIsClamped(float requested, float content, float viewport, float expected)
    {
        Assert.Equal(expected, ModConfigPanel.ClampScrollOffset(requested, content, viewport));
    }

    [Fact]
    public void AbsoluteOffsetConvertsToUnityVerticalNormalizedPosition()
    {
        Assert.Equal(1f, ModConfigPanel.CalculateVerticalNormalizedPosition(0f, 500f, 200f));
        Assert.Equal(0.5f, ModConfigPanel.CalculateVerticalNormalizedPosition(150f, 500f, 200f), 3);
        Assert.Equal(0f, ModConfigPanel.CalculateVerticalNormalizedPosition(300f, 500f, 200f));
        Assert.Equal(1f, ModConfigPanel.CalculateVerticalNormalizedPosition(50f, 100f, 200f));
    }

    [Fact]
    public void DescriptionWidthTracksUsableContentWidthWithStartupFallback()
    {
        Assert.Equal(600f, ModConfigPanel.CalculateDescriptionWidth(0f));
        Assert.Equal(600f, ModConfigPanel.CalculateDescriptionWidth(319f));
        Assert.Equal(1024f * 0.98f * 0.532f, ModConfigPanel.CalculateDescriptionWidth(1024f), 3);
    }

    [Fact]
    public void ResponsiveRemeasurementIgnoresSubpixelNoiseButDetectsResizeAndUiScaleChanges()
    {
        Assert.True(ModConfigPanel.DescriptionWidthChanged(0f, 600f));
        Assert.False(ModConfigPanel.DescriptionWidthChanged(600f, 600.5f));
        Assert.True(ModConfigPanel.DescriptionWidthChanged(600f, 600.51f));
        Assert.False(ModConfigPanel.DescriptionWidthChanged(600f, float.NaN));
    }

    [Fact]
    public void SavedAndRuntimeMessagesRemainDistinctAndExact()
    {
        Assert.Equal("Saved setting; runtime effect is reported separately.", ModConfigPanel.SavedRuntimeMessage);
        Assert.Equal("Configuration saved.", ModConfigPanel.ConfigurationSavedMessage);
    }

    [Fact]
    public void ClonedModsNavigationHasNoUnityTransitionPixelWriter()
    {
        var root = new GameObject("Mods");
        var frame = root.AddComponent<Image>();
        var button = root.AddComponent<Button>();
        button.targetGraphic = frame;

        ModConfigNativeNavigationInstaller.ClaimVisualOwnership(button);

        Assert.Null(button.targetGraphic);
    }
}
