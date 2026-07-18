using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbMentor;
using Xunit;

namespace OrbModding.Tests;

public sealed class MentorRuntimeHeadlessTests : IDisposable
{
    public MentorRuntimeHeadlessTests()
    {
        SpellRecipeSO.All.Clear();
        EquipmentSO.All.Clear();
        AlchemyRecipeSO.All.Clear();
        IdScriptableObject.RuntimeLookup.Clear();
        SpellManager.instance = new SpellManager();
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void EquippedSpellCapture_ReachesTheNativeRecipientOnceWithoutRecursion()
    {
        var source = RegisterSpell(mastery: 5);
        var recipient = RegisterSpell(mastery: 2);
        SpellManager.instance!.activeSpells.Add(new Spell(source));
        using var runtime = CreateRuntime(sharePercent: 10);

        Drive(runtime, 200);
        runtime.Observe(source, new BigDouble(100, 0));
        DriveUntil(runtime, () => runtime.Diagnostics.NativeGrants == 1);

        var grant = Assert.Single(recipient.GrantedMasteryExperience);
        Assert.Equal(1, grant.mantissa, 12);
        Assert.Equal(1, grant.exponent);
        Assert.Empty(source.GrantedMasteryExperience);
        Assert.Equal(1, runtime.Diagnostics.CapturedEvents);
        Assert.Equal(1, runtime.Diagnostics.QualifiedEvents);

        Drive(runtime, 200);
        Assert.Single(recipient.GrantedMasteryExperience);
        Assert.Equal(1, runtime.Diagnostics.NativeGrants);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void UnequippedSpell_IsRejectedAtCaptureTime()
    {
        var equipped = RegisterSpell(mastery: 5);
        var unequipped = RegisterSpell(mastery: 5);
        RegisterSpell(mastery: 1);
        SpellManager.instance!.activeSpells.Add(new Spell(equipped));
        using var runtime = CreateRuntime(sharePercent: 10);

        Drive(runtime, 200);
        runtime.Observe(unequipped, new BigDouble(100, 0));
        Drive(runtime, 50);

        Assert.Equal(0, runtime.Diagnostics.CapturedEvents);
        Assert.Equal(1, runtime.Diagnostics.DropCount(MentorDropReason.SourceIneligible));
        Assert.Equal(0, runtime.Diagnostics.NativeGrants);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void LifecycleReset_CancelsCapturedXpBeforeItCanMutateNativeState()
    {
        var source = RegisterSpell(mastery: 5);
        var recipient = RegisterSpell(mastery: 1);
        SpellManager.instance!.activeSpells.Add(new Spell(source));
        using var runtime = CreateRuntime(sharePercent: 10);

        Drive(runtime, 200);
        runtime.Observe(source, new BigDouble(100, 0));
        Assert.Equal(1, runtime.Diagnostics.CapturedEvents);

        runtime.RequestLifecycleReset();
        Drive(runtime, 200);

        Assert.Empty(recipient.GrantedMasteryExperience);
        Assert.Equal(0, runtime.Diagnostics.NativeGrants);
        Assert.Equal(1, runtime.Diagnostics.DropCount(MentorDropReason.LifecycleReset));
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ArtifactCapture_UsesTheNativeExperienceContainerGrantPath()
    {
        var source = RegisterEquipment(mastery: 6);
        var recipient = RegisterEquipment(mastery: 2);
        using var runtime = CreateRuntime(sharePercent: 10, artifacts: true);

        Drive(runtime, 300);
        runtime.BeginArtifactTick(source);
        runtime.ObserveExperienceContainer(source.GetExperienceElement(), new BigDouble(100, 0));
        runtime.EndArtifactTick(nativeSucceeded: true);
        DriveUntil(runtime, () => runtime.Diagnostics.NativeGrants == 1);

        var grant = Assert.Single(recipient.GetExperienceElement().Grants);
        Assert.Equal(1, grant.mantissa, 12);
        Assert.Equal(1, grant.exponent);
        Assert.Equal(grant.mantissa, recipient.masteryXp.mantissa, 12);
        Assert.Empty(source.GetExperienceElement().Grants);
        Assert.Equal(1, runtime.Diagnostics.QualifiedEvents);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void AlchemyCapture_UsesNativeMasteryGrantWithoutCrossDomainDuplication()
    {
        var source = RegisterAlchemy(mastery: 5);
        var recipient = RegisterAlchemy(mastery: 1);
        using var runtime = CreateRuntime(sharePercent: 10, alchemy: true);

        Drive(runtime, 300);
        runtime.ObserveAlchemy(source, new BigDouble(100, 0));
        DriveUntil(runtime, () => runtime.Diagnostics.NativeGrants == 1);

        var grant = Assert.Single(recipient.GrantedMasteryExperience);
        Assert.Equal(1, grant.mantissa, 12);
        Assert.Equal(1, grant.exponent);
        Assert.Empty(source.GrantedMasteryExperience);
        Assert.Equal(1, runtime.Diagnostics.CapturedEvents);
        Assert.Equal(1, runtime.Diagnostics.NativeGrants);
    }

    public void Dispose()
    {
        SpellRecipeSO.All.Clear();
        EquipmentSO.All.Clear();
        AlchemyRecipeSO.All.Clear();
        IdScriptableObject.RuntimeLookup.Clear();
        SpellManager.instance = null;
    }

    private static MentorRuntime CreateRuntime(
        double sharePercent,
        bool artifacts = false,
        bool alchemy = false)
    {
        var config = MentorConfig.Bind(new ConfigFile());
        config.Mode.Value = MentorOperationMode.Active;
        config.SpellSourcePolicy.Value = MentorSpellSourcePolicy.EquippedSpells;
        config.SharePercent.Value = sharePercent;
        config.ArtifactsEnabled.Value = artifacts;
        config.ArtifactSharePercent.Value = sharePercent;
        config.AlchemyEnabled.Value = alchemy;
        config.AlchemySharePercent.Value = sharePercent;
        config.OperationsPerFrame.Value = 8;
        config.CpuBudgetMilliseconds.Value = 1;
        return new MentorRuntime(config, new ManualLogSource());
    }

    private static SpellRecipeSO RegisterSpell(int mastery)
    {
        var spell = new SpellRecipeSO
        {
            uuid = Guid.NewGuid().ToString(),
            masteryLevel = mastery,
            discovered = true,
        };
        SpellRecipeSO.All.Add(spell);
        IdScriptableObject.RuntimeLookup.Add(spell.GetGuid(), spell);
        return spell;
    }

    private static EquipmentSO RegisterEquipment(int mastery)
    {
        var equipment = new EquipmentSO
        {
            uuid = Guid.NewGuid().ToString(),
            masteryLevel = mastery,
            isCreated = true,
        };
        EquipmentSO.All.Add(equipment);
        IdScriptableObject.RuntimeLookup.Add(equipment.GetGuid(), equipment);
        return equipment;
    }

    private static AlchemyRecipeSO RegisterAlchemy(int mastery)
    {
        var recipe = new AlchemyRecipeSO(
            Guid.NewGuid().ToString(),
            "Alchemy",
            new[] { new AlchemyTypeSO("alchemy") })
        {
            masteryLevel = mastery,
            discovered = true,
        };
        AlchemyRecipeSO.All.Add(recipe);
        IdScriptableObject.RuntimeLookup.Add(recipe.GetGuid(), recipe);
        return recipe;
    }

    private static void Drive(MentorRuntime runtime, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++) runtime.LateTick();
    }

    private static void DriveUntil(MentorRuntime runtime, Func<bool> completed)
    {
        for (var tick = 0; tick < 500 && !completed(); tick++) runtime.LateTick();
        Assert.True(completed(), "Mentor runtime did not settle within the bounded headless fixture.");
    }
}
