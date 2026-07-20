using System.Collections.Generic;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

internal sealed class AutoBuyTooltip : ITooltipable
{
    private readonly AutoBuyToggleControl _control;
    private AutomataConfig Config => _control.Config;
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
            new TooltipNode($"Structures: {(Config.AutoBuyStructures.Value ? "ON" : "OFF")} ({Config.AutoBuyAffordability.Value})"),
            new TooltipNode($"Upgrades: {(Config.AutoBuyUpgrades.Value ? "ON" : "OFF")} ({Config.UpgradeAffordability.Value})"),
        });
        TooltipNodeLayout.AddCompactFeatureStatus(
            nodes,
            "Spell leveling",
            _control.SpellLevelStatus,
            lineWidth: 68);
        nodes.AddRange(new[]
        {
            new TooltipNode($"Queue slots reserved: {Config.LeaveQueueSlots.Value}"),
            new TooltipNode($"Batch sizing: {Config.AutoBuyBatchSizing.Value}"),
            new TooltipNode($"Repeat policy: {(Config.RespectActionMultiplier.Value ? "Action multiplier" : Config.RepeatWhileAffordable.Value ? "While affordable" : Config.StructureRepeatMode.Value.ToString())}"),
            new TooltipNode("Click to toggle Auto Buy and its enabled spell leveling."),
        });
        var latestDecision = _control.LatestDecision;
        if (latestDecision.HasValue)
        {
            var decision = latestDecision.Value;
            var color = decision.Disposition == AutomationDecisionDisposition.Accepted
                ? new Color(.4f, 1, .55f)
                : new Color(1, .75f, .35f);
            TooltipNodeLayout.AddLines(
                nodes,
                AutomationDecisionPresenter.FormatExpandedLines(decision, maximumResourceGroups: 2),
                color,
                firstLinePrefix: "Decision: ");
        }
        return nodes;
    }
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
