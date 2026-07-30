using Xunit;

namespace OrbModding.GameContractTests;

public sealed class TooltipFeasibilityProbeTests
{
    [GameAssemblyFact]
    public void TooltipExplorerNativeShape_IsStructurallyReachable()
    {
        var assembly = GameAssemblyPaths.Require().ManagedDirectory + "/Assembly-CSharp.dll";
        using var metadata = new GameAssemblyMetadata(assembly);
        var tooltipItem = metadata.GetField("HoverTooltip", "tooltipItem");
        Assert.Equal("public", tooltipItem.Visibility);
        Assert.Equal("ITooltipable", tooltipItem.FieldType);

        var nested = metadata.GetField("HoverTooltip", "subTooltips");
        Assert.Equal("private", nested.Visibility);
        Assert.Equal("System.Collections.Generic.List`1<ITooltipable>", nested.FieldType);

        Assert.Single(metadata.GetMethods("HoverTooltip", "OpenTooltip"));
        Assert.Single(metadata.GetMethods("ITooltipable", "GetName"));
        Assert.Single(metadata.GetMethods("ITooltipable", "GetDisplayType"));
        Assert.Single(metadata.GetMethods("ITooltipable", "GetDescription"));
        Assert.Single(metadata.GetMethods("ITooltipable", "HasAltTooltips"));

        var viewText = metadata.GetField("UIViewRadioButton", "viewText");
        Assert.Equal("TMPro.TextMeshProUGUI", viewText.FieldType);
    }
}
