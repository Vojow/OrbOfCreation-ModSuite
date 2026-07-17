using System.Collections.Generic;
using UnityEngine;

namespace OrbAutomata;

internal sealed class AutoBuyTooltip : ITooltipable
{
    private readonly AutoBuyToggleControl _control;
    private AutomataConfig Config => _control.Config;
    public AutoBuyTooltip(AutoBuyToggleControl control) { _control = control; }
    public string GetName() => "Automata Auto Buy";
    public string GetDisplayType() => _control.State == AutoCastToggleVisualState.On ? "ON" : _control.State == AutoCastToggleVisualState.Blocked ? "BLOCKED" : "OFF";
    public Sprite GetIcon() => null!;
    public Color GetColor() => _control.State == AutoCastToggleVisualState.On ? new Color(.4f, 1, .55f) : _control.State == AutoCastToggleVisualState.Blocked ? new Color(1, .35f, .3f) : new Color(.7f, .7f, .7f);
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => "Purchases eligible structures, upgrades, and spell levels through native game actions.";
    public List<TooltipNode> GetTooltipNodes()
    {
        var nodes = new List<TooltipNode>
        {
            new TooltipNode($"State: {GetDisplayType()}", GetColor()),
            new TooltipNode($"Structures: {(Config.AutoBuyStructures.Value ? "ON" : "OFF")} ({Config.AutoBuyAffordability.Value})"),
            new TooltipNode($"Upgrades: {(Config.AutoBuyUpgrades.Value ? "ON" : "OFF")} ({Config.UpgradeAffordability.Value})"),
            new TooltipNode(!Config.AutoLevelSpells.Value
                ? "Spell leveling: Disabled"
                : !Config.CanStartAutoBuyActively
                    ? "Spell leveling: Paused with Auto Buy"
                    : $"Spell leveling: {_control.SpellLevelCapability}"),
            new TooltipNode($"Queue slots reserved: {Config.LeaveQueueSlots.Value}"),
            new TooltipNode($"Batch sizing: {Config.AutoBuyBatchSizing.Value}"),
            new TooltipNode($"Repeat policy: {(Config.RespectActionMultiplier.Value ? "Action multiplier" : Config.RepeatWhileAffordable.Value ? "While affordable" : Config.StructureRepeatMode.Value.ToString())}"),
            new TooltipNode("Click to toggle Auto Buy and its enabled spell leveling."),
        };
        if (_control.State == AutoCastToggleVisualState.Blocked)
            nodes.Add(new TooltipNode("Blocked: Automata Emergency Disable is active.", new Color(1, .35f, .3f)));
        return nodes;
    }
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
