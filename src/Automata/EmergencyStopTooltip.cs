using System.Collections.Generic;
using UnityEngine;

namespace OrbAutomata;

internal sealed class EmergencyStopTooltip : ITooltipable
{
    private readonly EmergencyStopControl _control;

    public EmergencyStopTooltip(EmergencyStopControl control) => _control = control;
    public string GetName() => "Suite emergency stop";
    public string GetDisplayType() =>
        ConfiguredIntentIconButtonVisual.FromEmergencyStop(_control).TooltipLabel;
    public Sprite GetIcon() => null!;
    public Color GetColor() =>
        ConfiguredIntentIconButtonVisual.FromEmergencyStop(_control).Color;
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => !_control.IsStopped
        ? "Immediately discards prepared work and prevents every suite automation from starting new actions."
        : _control.ResumeArmed
            ? "Click again to clear the stop. " + _control.ResumePreview
            : "Click once to review what will resume, then click again to clear the stop.";
    public List<TooltipNode> GetTooltipNodes() => new()
    {
        new TooltipNode(_control.IsStopped ? _control.ResumePreview : "One click stops every automation service."),
        new TooltipNode(_control.IsStopped
            ? _control.ResumeArmed ? "Click again to resume." : "Click to arm resume."
            : "No confirmation is required to stop."),
    };
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
