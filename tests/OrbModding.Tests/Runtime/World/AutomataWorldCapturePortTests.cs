using System.Collections.Generic;
using OrbAutomata;
using Xunit;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The capture port's only behaviour of its own: saying what a pass managed, without saying it four
/// times a second.
/// </summary>
public sealed class AutomataWorldCapturePortTests
{
    [Fact]
    public void AHealthyPassIsAnnouncedOnceEvenAsTheWorldGrows()
    {
        var seeded = SeedScribeRelations();
        var announced = new List<string>();
        var port = new AutomataWorldCapturePort(
            new GameWorldCollector(),
            () => 1,
            () => 1,
            r => announced.Add(r.Describe()));
        var frame = new GameWorldCycleFrame();

        port.Collect(frame);
        global::ResourceSO.All.Add(new global::ResourceSO { uuid = System.Guid.NewGuid().ToString() });
        port.Collect(frame);

        try
        {
            var line = Assert.Single(announced);
            Assert.StartsWith("World collection complete", line);
        }
        finally
        {
            global::ResourceSO.All.Clear();
            foreach (var identity in seeded)
                global::IdScriptableObject.RuntimeLookup.Remove(identity);
        }
    }

    /// <summary>
    /// The reason this exists: without it a build that renamed one member reaches the operator as a
    /// count of unavailable categories and no member name anywhere.
    /// </summary>
    [Fact]
    public void AShortfallIsAnnouncedWithItsCategoryAndReason()
    {
        var announced = new List<string>();
        var port = new AutomataWorldCapturePort(
            new GameWorldCollector(_ => null),
            () => 1,
            () => 1,
            r => announced.Add(r.Describe()));

        port.Collect(new GameWorldCycleFrame());
        port.Collect(new GameWorldCycleFrame());

        var line = Assert.Single(announced);
        Assert.StartsWith("World collection incomplete", line);
        Assert.Contains("resources", line);
    }

    private static IReadOnlyList<System.Guid> SeedScribeRelations()
    {
        var identities = new List<System.Guid>();
        void Register(System.Guid identity, global::IdScriptableObject value)
        {
            value.SetGuid(identity);
            global::IdScriptableObject.RuntimeLookup[identity] = value;
            identities.Add(identity);
        }

        Register(KnownEntities.ScribeCraftingRecipes.Uuid, new global::CraftingRecipeListVariable());
        Register(KnownEntities.ActiveScribeInstances.Uuid, new global::CraftingInstanceListVariable());
        Register(
            KnownEntities.AutoScribeInstances.Uuid,
            new global::CraftingInstanceListVariable { isAutoList = true });
        Register(
            KnownEntities.ScribeCrafting.Uuid,
            new global::CraftingRecipeTypeSO { maxStartingLevel = 1, isLevelType = true });

        foreach (var (scrollId, enchantmentId) in new[]
                 {
                     (KnownEntities.ScrollAdvancement.Uuid, KnownEntities.EnchantAdvancement.Uuid),
                     (KnownEntities.ScrollDevelopment.Uuid, KnownEntities.EnchantDevelopment.Uuid),
                     (KnownEntities.ScrollEcho.Uuid, KnownEntities.EnchantEcho.Uuid),
                     (KnownEntities.ScrollExcellence.Uuid, KnownEntities.EnchantExcellence.Uuid),
                     (KnownEntities.ScrollInvestment.Uuid, KnownEntities.EnchantInvestment.Uuid),
                     (KnownEntities.ScrollLearning.Uuid, KnownEntities.EnchantLearning.Uuid),
                     (KnownEntities.ScrollPower.Uuid, KnownEntities.EnchantPower.Uuid),
                     (KnownEntities.ScrollSpeed.Uuid, KnownEntities.EnchantSpeed.Uuid),
                 })
        {
            var enchantment = new global::EnchantmentSO();
            Register(enchantmentId, enchantment);
            var targetBlock = new global::InstantEffectBlock();
            targetBlock.effectScripts.Add(new global::RequestTargetEffectScript
            {
                targetOptions = new global::Targeting.TargetSelectOptions
                {
                    Targeting = new global::Targeting.TargetStructure(),
                },
            });
            targetBlock.effectScripts.Add(new global::EnchantmentSO.EnchantItemScript
            {
                enchantment = enchantment,
            });
            var scroll = new global::ConsumableSO();
            scroll.onUseEffects.Add(targetBlock);
            Register(scrollId, scroll);
        }
        return identities;
    }
}
