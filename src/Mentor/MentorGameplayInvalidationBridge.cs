using System;
using OrbModding.Common;

namespace OrbMentor;

internal sealed class MentorGameplayInvalidationBridge
{
    private readonly GameplayInvalidationBus _bus;

    public MentorGameplayInvalidationBridge(GameplayInvalidationBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    public void Pump(long frame) =>
        _bus.Pump(frame, GameplayInvalidationBus.DefaultMaxOperationsPerFrame);

    public void PublishProgression(MentorDomain domain, long frame, string? entityId)
    {
        var stableEntityId = Guid.TryParseExact(entityId, "D", out _)
            ? entityId
            : null;
        _bus.Publish(
            GameplayInvalidationKind.Progression,
            frame,
            Domain(domain),
            stableEntityId,
            stableEntityId is null ? null : ExpectedTypeName(domain),
            PluginIds.SuiteGuid);
    }

    public void PublishSpellLoadout(long frame) =>
        _bus.Publish(
            GameplayInvalidationKind.Inventory,
            frame,
            GameplayInvalidationDomains.MentorSpellLoadout,
            source: PluginIds.SuiteGuid);

    private static string Domain(MentorDomain domain) => domain switch
    {
        MentorDomain.Artifacts => GameplayInvalidationDomains.MentorArtifacts,
        MentorDomain.Alchemy => GameplayInvalidationDomains.MentorAlchemy,
        _ => GameplayInvalidationDomains.MentorSpells,
    };

    private static string ExpectedTypeName(MentorDomain domain) => domain switch
    {
        MentorDomain.Artifacts => "EquipmentSO",
        MentorDomain.Alchemy => "AlchemyRecipeSO",
        _ => "SpellRecipeSO",
    };
}
