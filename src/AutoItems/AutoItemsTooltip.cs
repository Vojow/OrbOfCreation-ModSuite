using System.Collections.Generic;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

internal sealed class AutoItemsTooltip : ITooltipable
{
    private readonly AutoItemsToggleControl _control;

    internal AutoItemsTooltip(AutoItemsToggleControl control) => _control = control;

    public string GetName() => "Auto Items";
    public string GetDisplayType() =>
        ConfiguredIntentIconButtonVisual.TooltipLabelFor(_control.Status);
    public Sprite GetIcon() => null!;
    public Color GetColor() =>
        ConfiguredIntentIconButtonVisual.FromFeatureStatus(_control.Status).Color;
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() =>
        "Uses Scrolls, Relics, and approved temporary items through one feature-wide mode.";

    public List<TooltipNode> GetTooltipNodes()
    {
        var config = _control.Config.AutoItems;
        var nodes = new List<TooltipNode>();
        TooltipNodeLayout.AddFeatureStatus(nodes, _control.Status, GetColor(), lineWidth: 58);
        nodes.Add(new TooltipNode($"Scrolls: {(config.UseScrolls ? "ON" : "OFF")}"));
        nodes.Add(new TooltipNode($"Relics: {(config.UseRelics ? "ON" : "OFF")}"));
        nodes.Add(new TooltipNode(
            "Temporary items: this same mode; exact approval stays in the Mods allowlist."));
        nodes.Add(new TooltipNode("Click to toggle all Auto Items work."));
        return nodes;
    }

    public List<TooltipNode> GetAltTooltipNodes() => new();
}
