using System.Collections.Generic;
using UnityEngine;

namespace OrbAutomata;

internal sealed class EmergencyStopTooltip : ITooltipable
{
    private readonly EmergencyStopControl _control;

    public EmergencyStopTooltip(EmergencyStopControl control) => _control = control;
    public string GetName() => "Suite emergency stop";
    public string GetDisplayType() => _control.IsStopped ? "STOPPED" : "READY";
    public Sprite GetIcon() => null!;
    public Color GetColor() => _control.IsStopped
        ? new Color(1.0f, 0.45f, 0.25f)
        : new Color(1.0f, 0.75f, 0.35f);
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
