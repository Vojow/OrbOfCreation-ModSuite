using System.Collections.Generic;
using System;
using OrbModding.Common;
using UnityEngine;

namespace OrbMentor;

internal sealed class MentorTooltip : ITooltipable
{
    private readonly MentorConfig _config;
    private readonly Func<FeatureStatusSnapshot> _readStatus;

    public MentorTooltip(MentorConfig config, Func<FeatureStatusSnapshot> readStatus)
    {
        _config = config;
        _readStatus = readStatus;
    }
    public string GetName() => "Orb Mentor";
    public string GetDisplayType() => MentorToggleButton.StatusLabel(
        FeatureStatusPresenter.Present(_readStatus()).ConfiguredState);
    public Sprite GetIcon() => null!;
    public Color GetColor() => MentorToggleButton.StatusColor(
        FeatureStatusPresenter.Present(_readStatus()).ConfiguredState);
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => "Equipped spells (or highest discovered spells), equipped artifacts, and active alchemy recipes can mentor lower-mastery discoveries in their own domain.";
    public List<TooltipNode> GetTooltipNodes()
    {
        var nodes = new List<TooltipNode>();
        TooltipNodeLayout.AddFeatureStatus(nodes, _readStatus(), GetColor(), lineWidth: 72);
        nodes.AddRange(new[]
        {
            new TooltipNode($"Economy: {_config.EconomyMode.Value}"),
            new TooltipNode($"Spell sources: {_config.SpellSourcePolicy.Value}"),
            new TooltipNode($"Spells {_config.SharePercent.Value:0.##}%"),
            new TooltipNode($"Artifacts {(_config.ArtifactsEnabled.Value ? $"{_config.ArtifactSharePercent.Value:0.##}%" : "OFF")}"),
            new TooltipNode($"Alchemy {(_config.AlchemyEnabled.Value ? $"{_config.AlchemySharePercent.Value:0.##}%" : "OFF")}"),
            new TooltipNode("Click or press Alt+M to toggle."),
        });
        return nodes;
    }
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
