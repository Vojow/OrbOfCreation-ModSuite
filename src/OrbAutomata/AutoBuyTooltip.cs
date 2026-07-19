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
    public string GetDisplayType() => _control.State == AutoCastToggleVisualState.On ? "ON" : _control.State == AutoCastToggleVisualState.Blocked ? "BLOCKED" : _control.State == AutoCastToggleVisualState.Waiting ? "WAITING" : "OFF";
    public Sprite GetIcon() => null!;
    public Color GetColor() => _control.State == AutoCastToggleVisualState.On ? new Color(.4f, 1, .55f) : _control.State == AutoCastToggleVisualState.Blocked ? new Color(1, .35f, .3f) : _control.State == AutoCastToggleVisualState.Waiting ? new Color(1, .75f, .35f) : new Color(.7f, .7f, .7f);
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => "Purchases eligible structures, upgrades, and spell levels through native game actions.";
    public List<TooltipNode> GetTooltipNodes()
    {
        var nodes = new List<TooltipNode>
        {
            new TooltipNode(FeatureStatusPresenter.Format(_control.Status), GetColor()),
            new TooltipNode($"Structures: {(Config.AutoBuyStructures.Value ? "ON" : "OFF")} ({Config.AutoBuyAffordability.Value})"),
            new TooltipNode($"Upgrades: {(Config.AutoBuyUpgrades.Value ? "ON" : "OFF")} ({Config.UpgradeAffordability.Value})"),
            new TooltipNode($"Spell leveling: {FeatureStatusPresenter.Format(_control.SpellLevelStatus)}"),
            new TooltipNode($"Queue slots reserved: {Config.LeaveQueueSlots.Value}"),
            new TooltipNode($"Batch sizing: {Config.AutoBuyBatchSizing.Value}"),
            new TooltipNode($"Repeat policy: {(Config.RespectActionMultiplier.Value ? "Action multiplier" : Config.RepeatWhileAffordable.Value ? "While affordable" : Config.StructureRepeatMode.Value.ToString())}"),
            new TooltipNode("Click to toggle Auto Buy and its enabled spell leveling."),
        };
        var latestDecision = _control.LatestDecision;
        if (latestDecision.HasValue)
        {
            var decision = latestDecision.Value;
            nodes.Add(new TooltipNode(
                $"Decision: {AutomationDecisionPresenter.Format(decision)}",
                decision.Disposition == AutomationDecisionDisposition.Accepted
                    ? new Color(.4f, 1, .55f)
                    : new Color(1, .75f, .35f)));
        }
        return nodes;
    }
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
