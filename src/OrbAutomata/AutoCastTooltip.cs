using System.Collections.Generic;
using UnityEngine;

namespace OrbAutomata;

internal sealed class AutoCastTooltip : ITooltipable
{
    private readonly AutomataConfig _config;
    private readonly AutoCastToggleControl _control;

    public AutoCastTooltip(AutomataConfig config, AutoCastToggleControl control) { _config = config; _control = control; }
    public string GetName() => "Auto Cast";
    public string GetDisplayType() => _control.State switch
    {
        AutoCastToggleVisualState.On => "ON",
        AutoCastToggleVisualState.Blocked => "BLOCKED",
        _ => "OFF",
    };
    public Sprite GetIcon() => null!;
    public Color GetColor() => _control.State switch
    {
        AutoCastToggleVisualState.On => new Color(.4f, 1, .55f),
        AutoCastToggleVisualState.Blocked => new Color(1, .35f, .3f),
        _ => new Color(.7f, .7f, .7f),
    };
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => "Automatically casts eligible equipped spells through the native spell manager.";
    public List<TooltipNode> GetTooltipNodes()
    {
        var nodes = new List<TooltipNode>
        {
            new TooltipNode($"State: {GetDisplayType()}", GetColor()),
            new TooltipNode($"Minimum resource fullness: {_config.AutoCastStartResourcePercent.Value:0.##}%"),
            new TooltipNode($"Evaluation interval: {_config.AutoCastIntervalSeconds.Value:0.##} seconds"),
            new TooltipNode($"Pause after manual cast: {_config.AutoCastManualPauseSeconds.Value:0.##} seconds"),
            new TooltipNode("Click or press Left Alt + X to toggle."),
        };
        if (_control.State == AutoCastToggleVisualState.Blocked)
            nodes.Add(new TooltipNode("Blocked: Automata Emergency Disable is active.", new Color(1, .35f, .3f)));
        return nodes;
    }
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
