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
    public string GetDescription() => "Highest-mastery spells, equipped artifacts, and active alchemy recipes can mentor lower-mastery discoveries in their own domain.";
    public List<TooltipNode> GetTooltipNodes()
    {
        var nodes = new List<TooltipNode>
        {
            new TooltipNode($"State: {GetDisplayType()}", GetColor()),
            new TooltipNode($"Economy: {_config.EconomyMode.Value}"),
            new TooltipNode($"Spells {_config.SharePercent.Value:0.##}% | Mentor: {_runtime.CurrentMentor(MentorDomain.Spells)}"),
            new TooltipNode($"Artifacts {(_config.ArtifactsEnabled.Value ? $"{_config.ArtifactSharePercent.Value:0.##}%" : "OFF")} | Mentor: {_runtime.CurrentMentor(MentorDomain.Artifacts)}"),
            new TooltipNode($"Alchemy {(_config.AlchemyEnabled.Value ? $"{_config.AlchemySharePercent.Value:0.##}%" : "OFF")} | Mentor: {_runtime.CurrentMentor(MentorDomain.Alchemy)}"),
            new TooltipNode("Click or press Alt+M to toggle."),
        };
        if (_runtime.IsBlocked) nodes.Add(new TooltipNode($"Blocked: {_runtime.BlockedReason}", new Color(1, .3f, .25f)));
        return nodes;
    }
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
