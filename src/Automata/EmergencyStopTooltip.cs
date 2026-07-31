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
        : "Immediately clears the stop. Configured automation resumes through the ordinary fresh-world safety gate.";
    public List<TooltipNode> GetTooltipNodes() => new()
    {
        new TooltipNode(_control.IsStopped
            ? "One click resumes every automation service that is configured on."
            : "One click stops every automation service and discards prepared work."),
        new TooltipNode("The emergency state changes immediately; no confirmation step is used."),
    };
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
