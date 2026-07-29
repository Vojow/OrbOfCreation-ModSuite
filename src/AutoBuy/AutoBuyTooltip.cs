using System.Collections.Generic;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

internal sealed class AutoBuyTooltip : ITooltipable
{
    private readonly AutoBuyToggleControl _control;
    private SuiteRuntimeConfiguration Config => _control.Config;
    public AutoBuyTooltip(AutoBuyToggleControl control) { _control = control; }
    public string GetName() => "Automata Auto Buy";
    public string GetDisplayType() => _control.State == AutoCastToggleVisualState.On ? "ON" : "OFF";
    public Sprite GetIcon() => null!;
    public Color GetColor() => _control.State == AutoCastToggleVisualState.On ? new Color(.4f, 1, .55f) : new Color(.7f, .7f, .7f);
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => "Purchases eligible structures, upgrades, and spell levels through native game actions.";
    public List<TooltipNode> GetTooltipNodes()
    {
        var nodes = new List<TooltipNode>();
        TooltipNodeLayout.AddFeatureStatus(nodes, _control.Status, GetColor(), lineWidth: 68);
        nodes.AddRange(new[]
        {
            new TooltipNode($"Structures: {(Config.AutoBuy.IncludeStructures ? "ON" : "OFF")} ({Config.AutoBuy.StructureAffordability})"),
            new TooltipNode($"Upgrades: {(Config.AutoBuy.IncludeUpgrades ? "ON" : "OFF")} ({Config.AutoBuy.UpgradeAffordability})"),
        });
        TooltipNodeLayout.AddCompactFeatureStatus(
            nodes,
            "Spell leveling",
            _control.SpellLevelStatus,
            lineWidth: 68);
        nodes.AddRange(new[]
        {
            new TooltipNode($"Queue slots reserved: {Config.AutoBuy.LeaveQueueSlots}"),
            new TooltipNode($"Batch sizing: {Config.AutoBuy.BatchSizing}"),
            new TooltipNode($"Purchase grouping: {Config.AutoBuy.PurchaseGrouping}"),
            new TooltipNode("Click to toggle Auto Buy and its enabled spell leveling."),
        });
        return nodes;
    }
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
