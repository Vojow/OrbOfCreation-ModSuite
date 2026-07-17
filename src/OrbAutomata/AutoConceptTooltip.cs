using System.Collections.Generic;
using UnityEngine;

namespace OrbAutomata;

internal sealed class AutoConceptTooltip : ITooltipable
{
    private readonly AutoConceptToggleControl _control;
    private AutomataConfig Config => _control.Config;

    public AutoConceptTooltip(AutoConceptToggleControl control)
    {
        _control = control;
    }

    public string GetName() => "Auto Concept";

    public string GetDisplayType() => _control.State switch
    {
        AutoCastToggleVisualState.On => "ON",
        AutoCastToggleVisualState.Blocked => "BLOCKED",
        _ => "OFF",
    };

    public Sprite GetIcon() => null!;

    public Color GetColor() => _control.State switch
    {
        AutoCastToggleVisualState.On => new Color(0.4f, 1.0f, 0.55f),
        AutoCastToggleVisualState.Blocked => new Color(1.0f, 0.35f, 0.3f),
        _ => new Color(0.7f, 0.7f, 0.7f),
    };

    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => "Balances discovered Scholar Concept mastery through the native Active Concepts list.";

    public List<TooltipNode> GetTooltipNodes()
    {
        var nodes = new List<TooltipNode>
        {
            new($"State: {GetDisplayType()}", GetColor()),
            new($"Slot management: {Config.AutoConceptSlotManagement.Value}"),
            new($"Training period: {Config.AutoConceptTrainingPeriodSeconds.Value} seconds"),
            new($"Rate reserve: {Config.AutoConceptRateReservePercent.Value:0.##}%"),
            new($"Minimum resource fullness: {Config.AutoConceptMinimumResourcePercent.Value:0.##}%"),
            new("Click to toggle Auto Concept."),
        };
        if (_control.State == AutoCastToggleVisualState.Blocked)
            nodes.Add(new TooltipNode("Blocked: Automata Emergency Disable is active.", new Color(1.0f, 0.35f, 0.3f)));
        return nodes;
    }

    public List<TooltipNode> GetAltTooltipNodes() => new();
}
