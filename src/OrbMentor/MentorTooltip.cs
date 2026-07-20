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
    public string GetDisplayType() => MentorToggleButton.StatusLabel(
        FeatureStatusPresenter.Present(_runtime.RootFeatureStatus).ConfiguredState);
    public Sprite GetIcon() => null!;
    public Color GetColor() => MentorToggleButton.StatusColor(
        FeatureStatusPresenter.Present(_runtime.RootFeatureStatus).ConfiguredState);
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => "Equipped spells (or highest discovered spells), equipped artifacts, and active alchemy recipes can mentor lower-mastery discoveries in their own domain.";
    public List<TooltipNode> GetTooltipNodes()
    {
        var nodes = new List<TooltipNode>();
        TooltipNodeLayout.AddFeatureStatus(nodes, _runtime.RootFeatureStatus, GetColor(), lineWidth: 72);
        TooltipNodeLayout.AddCompactFeatureStatus(
            nodes,
            "Spells",
            _runtime.DomainFeatureStatus(MentorDomain.Spells),
            lineWidth: 72);
        TooltipNodeLayout.AddCompactFeatureStatus(
            nodes,
            "Artifacts",
            _runtime.DomainFeatureStatus(MentorDomain.Artifacts),
            lineWidth: 72);
        TooltipNodeLayout.AddCompactFeatureStatus(
            nodes,
            "Alchemy",
            _runtime.DomainFeatureStatus(MentorDomain.Alchemy),
            lineWidth: 72);
        nodes.AddRange(new[]
        {
            new TooltipNode($"Economy: {_config.EconomyMode.Value}"),
            new TooltipNode($"Spell sources: {_config.SpellSourcePolicy.Value}"),
            new TooltipNode($"Spells {_config.SharePercent.Value:0.##}% | Mentor: {_runtime.CurrentMentor(MentorDomain.Spells)}"),
            new TooltipNode($"Artifacts {(_config.ArtifactsEnabled.Value ? $"{_config.ArtifactSharePercent.Value:0.##}%" : "OFF")} | Mentor: {_runtime.CurrentMentor(MentorDomain.Artifacts)}"),
            new TooltipNode($"Alchemy {(_config.AlchemyEnabled.Value ? $"{_config.AlchemySharePercent.Value:0.##}%" : "OFF")} | Mentor: {_runtime.CurrentMentor(MentorDomain.Alchemy)}"),
            new TooltipNode("Click or press Alt+M to toggle."),
        });
        return nodes;
    }
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
