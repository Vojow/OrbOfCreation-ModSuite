using System.Collections.Generic;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

internal sealed class AutoHarvestTooltip : ITooltipable
{
    private readonly AutoHarvestToggleControl _control;

    internal AutoHarvestTooltip(AutoHarvestToggleControl control) => _control = control;

    public string GetName() => "Auto Harvest";
    public string GetDisplayType() => _control.IsOn ? "ON" : "OFF";
    public Sprite GetIcon() => null!;
    public Color GetColor() => _control.IsOn
        ? new Color(0.4f, 1.0f, 0.55f)
        : new Color(0.7f, 0.7f, 0.7f);
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() =>
        "Queues audited native fruit-tree and treasure-tree collection actions.";

    public List<TooltipNode> GetTooltipNodes()
    {
        var nodes = new List<TooltipNode>();
        TooltipNodeLayout.AddFeatureStatus(nodes, _control.Status, GetColor(), lineWidth: 54);
        nodes.Add(new TooltipNode(
            $"Fruit trees: {(_control.Config.AutoHarvest.CollectFruitTrees ? "ON" : "OFF")}"));
        nodes.Add(new TooltipNode(
            $"Treasure trees: {(_control.Config.AutoHarvest.CollectTreasureTrees ? "ON" : "OFF")}"));
        nodes.Add(new TooltipNode("Click to toggle Auto Harvest."));
        return nodes;
    }

    public List<TooltipNode> GetAltTooltipNodes() => new();
}
