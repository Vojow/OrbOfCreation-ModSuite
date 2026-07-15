using System.Collections.Generic;
using UnityEngine;

namespace OrbMentor;

internal sealed class MentorTooltip : ITooltipable
{
    private readonly MentorConfig _config;
    private readonly MentorRuntime _runtime;

    public MentorTooltip(MentorConfig config, MentorRuntime runtime) { _config = config; _runtime = runtime; }
    public string GetName() => "Orb Mentor";
    public string GetDisplayType() => _runtime.IsBlocked ? "BLOCKED" : _config.Active ? "ON" : "OFF";
    public Sprite GetIcon() => null!;
    public Color GetColor() => _runtime.IsBlocked ? new Color(1, .3f, .25f) : _config.Active ? new Color(.4f, 1, .55f) : new Color(.7f, .7f, .7f);
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => "Highest-mastery spells share final native mastery XP with discovered lower-mastery spells.";
    public List<TooltipNode> GetTooltipNodes()
    {
        var nodes = new List<TooltipNode>
        {
            new TooltipNode($"State: {GetDisplayType()}", GetColor()),
            new TooltipNode($"Economy: {_config.EconomyMode.Value}"),
            new TooltipNode($"Share: {_config.SharePercent.Value:0.##}%"),
            new TooltipNode(_runtime.StatusText()),
            new TooltipNode("Click or press Alt+M to toggle."),
        };
        if (_runtime.IsBlocked) nodes.Add(new TooltipNode($"Blocked: {_runtime.BlockedReason}", new Color(1, .3f, .25f)));
        return nodes;
    }
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
