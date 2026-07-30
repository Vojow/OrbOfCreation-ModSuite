using System.Collections.Generic;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

internal sealed class AutoScribeTooltip : ITooltipable
{
    private readonly AutoScribeToggleControl _control;

    internal AutoScribeTooltip(AutoScribeToggleControl control) => _control = control;

    public string GetName() => "Auto Scribe";
    public string GetDisplayType() => _control.IsOn ? "ON" : "OFF";
    public Sprite GetIcon() => null!;
    public Color GetColor() => _control.IsOn
        ? new Color(0.4f, 1.0f, 0.55f)
        : new Color(0.7f, 0.7f, 0.7f);
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() =>
        "Prepares the strongest useful audited Scrolls for native-valid structure targets.";

    public List<TooltipNode> GetTooltipNodes()
    {
        var roles = _control.Config.AutoScribe.Roles;
        var nodes = new List<TooltipNode>();
        TooltipNodeLayout.AddFeatureStatus(nodes, _control.Status, GetColor(), lineWidth: 54);
        nodes.Add(new TooltipNode(
            string.IsNullOrWhiteSpace(roles)
                ? "Roles: all audited"
                : "Roles: configured selection"));
        nodes.Add(new TooltipNode("Requires healthy Auto Items Scroll consumption."));
        nodes.Add(new TooltipNode("Click to toggle Auto Scribe."));
        return nodes;
    }

    public List<TooltipNode> GetAltTooltipNodes() => new();
}
