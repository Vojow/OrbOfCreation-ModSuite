namespace OrbMentor;

/// <summary>The immutable Mentor settings pinned with a service cycle.</summary>
internal sealed record MentorConfiguration
{
    internal MentorOperationMode Mode { get; init; }
    internal MentorEconomyMode EconomyMode { get; init; }
    internal MentorSpellSourcePolicy SpellSourcePolicy { get; init; }
    internal double SpellSharePercent { get; init; }
    internal bool ArtifactsEnabled { get; init; }
    internal double ArtifactSharePercent { get; init; }
    internal bool AlchemyEnabled { get; init; }
    internal double AlchemySharePercent { get; init; }

    internal static MentorConfiguration Read(MentorConfig source) => new()
    {
        Mode = source.Enabled.Value ? source.Mode.Value : MentorOperationMode.Disabled,
        EconomyMode = source.EconomyMode.Value,
        SpellSourcePolicy = source.SpellSourcePolicy.Value,
        SpellSharePercent = source.SharePercent.Value,
        ArtifactsEnabled = source.ArtifactsEnabled.Value,
        ArtifactSharePercent = source.ArtifactSharePercent.Value,
        AlchemyEnabled = source.AlchemyEnabled.Value,
        AlchemySharePercent = source.AlchemySharePercent.Value,
    };
}
