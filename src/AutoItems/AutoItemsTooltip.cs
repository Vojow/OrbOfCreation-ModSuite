using System.Collections.Generic;
using System.Linq;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

internal sealed class AutoItemsTooltip : ITooltipable
{
    private readonly AutoItemsToggleControl _control;

    internal AutoItemsTooltip(AutoItemsToggleControl control) => _control = control;

    public string GetName() => "Auto Items";
    public string GetDisplayType() => _control.IsOn ? "ON" : "OFF";
    public Sprite GetIcon() => null!;
    public Color GetColor() => _control.IsOn
        ? new Color(0.4f, 1.0f, 0.55f)
        : new Color(0.7f, 0.7f, 0.7f);
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() =>
        "Uses audited native item families under the configured toxicity and targeting policy.";

    public List<TooltipNode> GetTooltipNodes()
    {
        var config = _control.Config.AutoItems;
        var nodes = new List<TooltipNode>();
        TooltipNodeLayout.AddFeatureStatus(nodes, _control.Status, GetColor(), lineWidth: 54);
        nodes.Add(new TooltipNode($"Scrolls: {(config.UseScrolls ? "ON" : "OFF")}"));
        nodes.Add(new TooltipNode($"Relics: {(config.UseRelics ? "ON" : "OFF")}"));
        var temporaryFamilies = new[]
            {
                config.UseFruits ? "Fruit" : null,
                config.UsePotions ? "Potion" : null,
                config.UseThreads ? "Thread" : null,
            }
            .Where(name => name is not null)
            .ToArray();
        nodes.Add(new TooltipNode(
            "Temporary families: " +
            (temporaryFamilies.Length == 0
                ? "none"
                : string.Join(", ", temporaryFamilies))));
        nodes.Add(new TooltipNode("Click to toggle Auto Items."));
        return nodes;
    }

    public List<TooltipNode> GetAltTooltipNodes() => new();
}
