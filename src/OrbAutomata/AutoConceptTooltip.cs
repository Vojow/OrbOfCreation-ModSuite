using System.Collections.Generic;
using OrbModding.Common;
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
        AutoCastToggleVisualState.Waiting => "WAITING",
        _ => "OFF",
    };

    public Sprite GetIcon() => null!;

    public Color GetColor() => _control.State switch
    {
        AutoCastToggleVisualState.On => new Color(0.4f, 1.0f, 0.55f),
        AutoCastToggleVisualState.Blocked => new Color(1.0f, 0.35f, 0.3f),
        AutoCastToggleVisualState.Waiting => new Color(1.0f, 0.75f, 0.35f),
        _ => new Color(0.7f, 0.7f, 0.7f),
    };

    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => "Balances discovered Scholar Concept mastery through the native Active Concepts list.";

    public List<TooltipNode> GetTooltipNodes()
    {
        var nodes = new List<TooltipNode>
        {
            new(FeatureStatusPresenter.Format(_control.Status), GetColor()),
            new($"Slot management: {Config.AutoConceptSlotManagement.Value}"),
            new($"Training period: {Config.AutoConceptTrainingPeriodSeconds.Value} seconds"),
            new($"Rate reserve: {Config.AutoConceptRateReservePercent.Value:0.##}%"),
            new($"Minimum resource fullness: {Config.AutoConceptMinimumResourcePercent.Value:0.##}%"),
            new("Click to toggle Auto Concept."),
        };
        return nodes;
    }

    public List<TooltipNode> GetAltTooltipNodes() => new();
}
