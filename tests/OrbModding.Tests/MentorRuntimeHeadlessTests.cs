using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbMentor;
using OrbModding.Common;
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
        RegisterUnlockedView(MentorDomainUnlockGate.MasteriesEnabledUuid);
        RegisterUnlockedView(MentorDomainUnlockGate.SpellbookUuid);
        RegisterUnlockedView(MentorDomainUnlockGate.ArtifactWorkshopUuid);
        RegisterUnlockedView(MentorDomainUnlockGate.AlchemyScreenUuid);
        RegisterConceptRecipes();
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
        Assert.Equal(1, grant.Mantissa, 12);
        Assert.Equal(1, grant.Exponent);
        Assert.Empty(source.GrantedMasteryExperience);
        Assert.Equal(1, runtime.Diagnostics.CapturedEvents);
        Assert.Equal(1, runtime.Diagnostics.QualifiedEvents);

        Drive(runtime, 200);
        Assert.Single(recipient.GrantedMasteryExperience);
        Assert.Equal(1, runtime.Diagnostics.NativeGrants);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void NoOpSpellGrantBlocksDomainUntilLifecycleRecovery()
    {
        var source = RegisterSpell(mastery: 5);
        var recipient = RegisterSpell(mastery: 2);
        recipient.SuppressMasteryGain = true;
        SpellManager.instance!.activeSpells.Add(new Spell(source));
        var statusRegistry = new FeatureStatusRegistry();
        using var runtime = CreateRuntime(sharePercent: 10, featureStatusRegistry: statusRegistry);

        Drive(runtime, 200);
        runtime.Observe(source, new BigDouble(100, 0));
        DriveUntil(runtime, () => runtime.IsBlocked);

        Assert.Equal(1, recipient.MasteryGrantCalls);
        Assert.Empty(recipient.GrantedMasteryExperience);
        Assert.Equal(0, runtime.Diagnostics.NativeGrants);
        Assert.Equal(FeatureStatusState.Faulted,
            Status(statusRegistry, MentorFeatureStatus.RootFeatureId).State);
        Assert.Equal(
            FeatureStatusReasonCode.PostconditionFailed,
            Status(statusRegistry, MentorFeatureStatus.SpellsFeatureId).Reason.Code);
        Drive(runtime, 100);
        Assert.Equal(1, recipient.MasteryGrantCalls);

        recipient.SuppressMasteryGain = false;
        runtime.RequestLifecycleReset();
        Drive(runtime, 200);
        runtime.Observe(source, new BigDouble(100, 0));
        DriveUntil(runtime, () => runtime.Diagnostics.NativeGrants == 1);

        Assert.Equal(2, recipient.MasteryGrantCalls);
        Assert.Single(recipient.GrantedMasteryExperience);
        DriveUntil(runtime, () =>
            Status(statusRegistry, MentorFeatureStatus.RootFeatureId).State == FeatureStatusState.Operational);
        Assert.Equal(FeatureStatusState.Operational,
            Status(statusRegistry, MentorFeatureStatus.RootFeatureId).State);
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
        Drive(runtime, 200);

        var grant = Assert.Single(recipient.GetExperienceElement().Grants);
        Assert.Equal(1, grant.Mantissa, 12);
        Assert.Equal(1, grant.Exponent);
        Assert.Equal(grant.Mantissa, recipient.masteryXp.Mantissa, 12);
        Assert.Empty(source.GetExperienceElement().Grants);
        Assert.Equal(1, runtime.Diagnostics.QualifiedEvents);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ArtifactGrantVerification_AcceptsMultipleNativeLevelRollovers()
    {
        var source = RegisterEquipment(mastery: 100);
        var recipient = RegisterEquipment(mastery: 80);
        recipient.SetMasteryState(
            level: 80,
            experience: new BigDouble(2, 1),
            experiencePerLevel: new BigDouble(1, 2));
        using var runtime = CreateRuntime(sharePercent: 10, artifacts: true);

        Drive(runtime, 300);
        runtime.BeginArtifactTick(source);
        runtime.ObserveExperienceContainer(source.GetExperienceElement(), new BigDouble(3, 3));
        runtime.EndArtifactTick(nativeSucceeded: true);
        DriveUntil(runtime, () => runtime.Diagnostics.NativeGrants == 1);

        Assert.Equal(83, recipient.masteryLevel);
        Assert.Equal(83, recipient.GetExperienceElement().GetLevel());
        var residual = recipient.GetExperienceElement().GetExperience();
        Assert.Equal(20, residual.Mantissa * Math.Pow(10, residual.Exponent), 9);
        Assert.Equal(20, recipient.masteryXp.Mantissa * Math.Pow(10, recipient.masteryXp.Exponent), 9);
        Assert.False(runtime.IsBlocked);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ArtifactGrantVerification_StillBlocksANativeNoOp()
    {
        var source = RegisterEquipment(mastery: 6);
        var recipient = RegisterEquipment(mastery: 2);
        recipient.GetExperienceElement().SuppressGain = true;
        using var runtime = CreateRuntime(sharePercent: 10, artifacts: true);

        Drive(runtime, 300);
        runtime.BeginArtifactTick(source);
        runtime.ObserveExperienceContainer(source.GetExperienceElement(), new BigDouble(100, 0));
        runtime.EndArtifactTick(nativeSucceeded: true);
        DriveUntil(runtime, () =>
            runtime.CurrentMentor(MentorDomain.Artifacts).StartsWith("Blocked:", StringComparison.Ordinal));

        Assert.Equal(2, recipient.masteryLevel);
        Assert.Equal(0, runtime.Diagnostics.NativeGrants);
        Assert.Single(recipient.GetExperienceElement().Grants);
        Assert.Equal(
            FeatureStatusReasonCode.PostconditionFailed,
            runtime.DomainFeatureStatus(MentorDomain.Artifacts).Reason.Code);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void BlockedOptionalDomain_DoesNotStarveHealthySpellGrant()
    {
        var source = RegisterSpell(mastery: 5);
        var recipient = RegisterSpell(mastery: 1);
        SpellManager.instance!.activeSpells.Add(new Spell(source));
        using var runtime = CreateRuntime(sharePercent: 10, artifacts: true);

        Drive(runtime, 200);
        runtime.QuarantineDomain(MentorDomain.Artifacts, "fixture artifact contract failure");
        runtime.Observe(source, new BigDouble(100, 0));
        DriveUntil(runtime, () => runtime.Diagnostics.NativeGrants == 1);

        Assert.Single(recipient.GrantedMasteryExperience);
        Assert.StartsWith("Blocked:", runtime.CurrentMentor(MentorDomain.Artifacts));
        Assert.NotEqual("Blocked", runtime.CurrentMentor(MentorDomain.Spells));
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
        Assert.Equal(1, grant.Mantissa, 12);
        Assert.Equal(1, grant.Exponent);
        Assert.Empty(source.GrantedMasteryExperience);
        Assert.Equal(1, runtime.Diagnostics.CapturedEvents);
        Assert.Equal(1, runtime.Diagnostics.NativeGrants);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void SpellOwnershipConflictLeavesArtifactDomainOperational()
    {
        var spellSource = RegisterSpell(mastery: 5);
        var spellRecipient = RegisterSpell(mastery: 1);
        SpellManager.instance!.activeSpells.Add(new Spell(spellSource));
        var artifactSource = RegisterEquipment(mastery: 6);
        var artifactRecipient = RegisterEquipment(mastery: 2);
        var statuses = new FeatureStatusRegistry();
        using var runtime = CreateRuntime(
            sharePercent: 10,
            artifacts: true,
            featureStatusRegistry: statuses,
            ownsActionFamily: domain => domain != MentorDomain.Spells);

        Drive(runtime, 300);
        runtime.Observe(spellSource, new BigDouble(100, 0));
        runtime.BeginArtifactTick(artifactSource);
        runtime.ObserveExperienceContainer(artifactSource.GetExperienceElement(), new BigDouble(100, 0));
        runtime.EndArtifactTick(nativeSucceeded: true);
        DriveUntil(runtime, () => runtime.Diagnostics.NativeGrants == 1);

        DriveUntil(runtime, () =>
            Status(statuses, MentorFeatureStatus.ArtifactsFeatureId).State == FeatureStatusState.Operational);

        Assert.Empty(spellRecipient.GrantedMasteryExperience);
        Assert.Single(artifactRecipient.GetExperienceElement().Grants);
        Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict,
            Status(statuses, MentorFeatureStatus.SpellsFeatureId).Reason.Code);
        Assert.Equal(FeatureStatusState.Operational,
            Status(statuses, MentorFeatureStatus.ArtifactsFeatureId).State);
        Assert.Equal(FeatureStatusState.Degraded,
            Status(statuses, MentorFeatureStatus.RootFeatureId).State);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void OwnershipLossCancelsCapturedXpAndReacquisitionAcceptsOnlyFreshEvents()
    {
        var source = RegisterSpell(mastery: 5);
        var recipient = RegisterSpell(mastery: 1);
        SpellManager.instance!.activeSpells.Add(new Spell(source));
        var owned = true;
        using var runtime = CreateRuntime(10, ownsActionFamily: _ => owned);

        Drive(runtime, 200);
        runtime.Observe(source, new BigDouble(100, 0));
        owned = false;
        Drive(runtime, 20);

        Assert.Empty(recipient.GrantedMasteryExperience);
        Assert.Equal(1, runtime.Diagnostics.DropCount(MentorDropReason.ActionFamilyConflict));

        owned = true;
        Drive(runtime, 200);
        runtime.Observe(source, new BigDouble(100, 0));
        DriveUntil(runtime, () => runtime.Diagnostics.NativeGrants == 1);

        Assert.Single(recipient.GrantedMasteryExperience);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ArtifactPermitCompletesTransactionWhenOwnershipIsRevokedByNativeHook()
    {
        var source = RegisterEquipment(mastery: 6);
        var recipient = RegisterEquipment(mastery: 2);
        var registry = new ActionFamilyOwnershipRegistry();
        Assert.True(registry.TryClaimSet(
            new ActionFamilyOwner(new FeatureStatusKey("tests", "mentor-artifact"), "Mentor artifact"),
            new[] { AutomationActionFamily.ArtifactMasteryExperienceGrant },
            out var lease,
            out _));
        using var ownedLease = lease!;
        IDisposable? external = null;
        recipient.GetExperienceElement().AfterGainExperience = () =>
            external ??= registry.RegisterKnownExternal(
                new ActionFamilyOwner(new FeatureStatusKey("external", "artifact"), "External artifact"),
                new[] { AutomationActionFamily.ArtifactMasteryExperienceGrant });
        using var runtime = CreateRuntime(
            sharePercent: 10,
            artifacts: true,
            ownsActionFamily: domain => domain != MentorDomain.Artifacts || ownedLease.IsHeld,
            captureActionFamilyMutation: domain =>
                domain != MentorDomain.Artifacts || ownedLease.TryCaptureMutationPermit());

        try
        {
            Drive(runtime, 300);
            runtime.BeginArtifactTick(source);
            runtime.ObserveExperienceContainer(source.GetExperienceElement(), new BigDouble(100, 0));
            runtime.EndArtifactTick(nativeSucceeded: true);
            DriveUntil(runtime, () => runtime.Diagnostics.NativeGrants == 1);

            Assert.False(ownedLease.IsHeld);
            Assert.Single(recipient.GetExperienceElement().Grants);
            Assert.Equal(1, recipient.masteryXp.Mantissa, 12);
            Assert.Equal(1, recipient.masteryXp.Exponent);
        }
        finally
        {
            external?.Dispose();
        }
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
        bool alchemy = false,
        FeatureStatusRegistry? featureStatusRegistry = null,
        Func<MentorDomain, bool>? ownsActionFamily = null,
        Func<MentorDomain, bool>? captureActionFamilyMutation = null)
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
        return new MentorRuntime(
            config,
            new ManualLogSource(),
            featureStatusRegistry: featureStatusRegistry,
            ownsActionFamily: ownsActionFamily,
            captureActionFamilyMutation: captureActionFamilyMutation);
    }

    private static FeatureStatusSnapshot Status(FeatureStatusRegistry registry, string featureId)
    {
        Assert.True(registry.TryGet(new FeatureStatusKey(PluginIds.SuiteGuid, featureId), out var status));
        return status;
    }

    private static void RegisterUnlockedView(string uuid)
    {
        var view = new ViewSO
        {
            uuid = new Guid(uuid),
            available = true,
        };
        IdScriptableObject.RuntimeLookup.Add(view.uuid, view);
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
            isCreated = true,
        };
        equipment.SetMasteryState(mastery, default, new BigDouble(1, 100));
        EquipmentSO.All.Add(equipment);
        IdScriptableObject.RuntimeLookup.Add(equipment.GetGuid(), equipment);
        return equipment;
    }

    private static AlchemyRecipeSO RegisterAlchemy(int mastery)
    {
        var recipe = new AlchemyRecipeSO(
            Guid.NewGuid().ToString(),
            "Alchemy",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.BrewingTypeUuid.ToString()) })
        {
            masteryLevel = mastery,
            discovered = true,
        };
        AlchemyRecipeSO.All.Add(recipe);
        IdScriptableObject.RuntimeLookup.Add(recipe.GetGuid(), recipe);
        return recipe;
    }

    private static void RegisterConceptRecipes()
    {
        var concept = new AlchemyRecipeSO(
            Guid.NewGuid().ToString(),
            "Fixture Concept",
            new[]
            {
                new AlchemyTypeSO(
                    AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString()),
            });
        var registry = new AlchemyRecipeListVariable();
        registry.SetGuid(AlchemyGameplayDomainClassifier.ConceptRecipesUuid);
        registry.value.Add(concept);
        IdScriptableObject.RuntimeLookup[AlchemyGameplayDomainClassifier.ConceptRecipesUuid] = registry;
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
