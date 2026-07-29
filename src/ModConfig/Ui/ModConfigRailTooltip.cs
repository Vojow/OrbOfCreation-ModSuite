using System.Collections.Generic;
using UnityEngine;

namespace OrbModConfig;

internal sealed class ModConfigRailTooltip : ITooltipable
{
    private readonly string _label;
    private readonly Sprite _icon;

    public ModConfigRailTooltip(string label, Sprite icon)
    {
        _label = label;
        _icon = icon;
    }

    public string GetName() => _label;
    public string GetDisplayType() => "MODSUITE";
    public Sprite GetIcon() => _icon;
    public Color GetColor() => Color.white;
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => $"Open the {_label} page.";
    public List<TooltipNode> GetTooltipNodes() =>
        _label.StartsWith("Runtime", System.StringComparison.Ordinal)
            ? new() { new TooltipNode("Health updates live and diagnostic actions run immediately.") }
            : _label is "Auto Buy" or "Auto Cast" or "Auto Concept" or "Auto Harvest" or "Mentor"
                ? new() { new TooltipNode("On/off is immediate; policy edits remain staged until Apply.") }
                : new() { new TooltipNode("Settings changes remain staged until Apply.") };
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
