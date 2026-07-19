using System.Collections.Generic;
using OrbModding.Common;
using UnityEngine;

namespace OrbMentor;

internal sealed class MentorTooltip : ITooltipable
{
    private readonly MentorConfig _config;
    private readonly MentorRuntime _runtime;

    public MentorTooltip(MentorConfig config, MentorRuntime runtime) { _config = config; _runtime = runtime; }
    public string GetName() => "Orb Mentor";
    public string GetDisplayType() => MentorToggleButton.StatusLabel(_runtime.RootFeatureStatus.State);
    public Sprite GetIcon() => null!;
    public Color GetColor() => MentorToggleButton.StatusColor(_runtime.RootFeatureStatus.State);
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => "Equipped spells (or highest discovered spells), equipped artifacts, and active alchemy recipes can mentor lower-mastery discoveries in their own domain.";
    public List<TooltipNode> GetTooltipNodes()
    {
        var nodes = new List<TooltipNode>
        {
            new TooltipNode(FeatureStatusPresenter.Format(_runtime.RootFeatureStatus), GetColor()),
            new TooltipNode("Spells — " + FeatureStatusPresenter.Format(_runtime.DomainFeatureStatus(MentorDomain.Spells))),
            new TooltipNode("Artifacts — " + FeatureStatusPresenter.Format(_runtime.DomainFeatureStatus(MentorDomain.Artifacts))),
            new TooltipNode("Alchemy — " + FeatureStatusPresenter.Format(_runtime.DomainFeatureStatus(MentorDomain.Alchemy))),
            new TooltipNode($"Economy: {_config.EconomyMode.Value}"),
            new TooltipNode($"Spell sources: {_config.SpellSourcePolicy.Value}"),
            new TooltipNode($"Spells {_config.SharePercent.Value:0.##}% | Mentor: {_runtime.CurrentMentor(MentorDomain.Spells)}"),
            new TooltipNode($"Artifacts {(_config.ArtifactsEnabled.Value ? $"{_config.ArtifactSharePercent.Value:0.##}%" : "OFF")} | Mentor: {_runtime.CurrentMentor(MentorDomain.Artifacts)}"),
            new TooltipNode($"Alchemy {(_config.AlchemyEnabled.Value ? $"{_config.AlchemySharePercent.Value:0.##}%" : "OFF")} | Mentor: {_runtime.CurrentMentor(MentorDomain.Alchemy)}"),
            new TooltipNode("Click or press Alt+M to toggle."),
        };
        return nodes;
    }
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
