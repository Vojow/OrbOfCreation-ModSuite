using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbMentor;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class ActionFamilyIntegrationTests
{
    [Fact]
    public void ExactAutobuyOrbGuidBlocksOnlyOverlappingAutomataFamilies()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyStructures.Value = true;
        config.AutoBuyUpgrades.Value = true;
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        config.AutoConceptMode.Value = AutoConceptOperationMode.Active;
        config.AutoLevelSpells.Value = true;
        config.AutoHarvestMode.Value = AutoHarvestOperationMode.Active;
        using var ownership = new AutomataActionFamilyOwnership(registry);

        ownership.RefreshLoadedPluginInventory(
            1,
            guid => guid == AutomataActionFamilyOwnership.KnownAutoBuyPluginGuid);
        ownership.Refresh(config.Current, lifecycleReady: true);

        Assert.False(ownership.OwnsAutoBuy(AutoBuyCandidateKind.Structure));
        Assert.False(ownership.OwnsAutoBuy(AutoBuyCandidateKind.Upgrade));
        Assert.True(ownership.OwnsCast);
        Assert.True(ownership.OwnsConcept);
        Assert.True(ownership.OwnsSpellLevel);
        Assert.True(ownership.OwnsHarvest);
    }

    [Fact]
    public void SimilarUnknownGuidDoesNotClaimKnownExternalFamilies()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyStructures.Value = true;
        config.AutoBuyUpgrades.Value = true;
        using var ownership = new AutomataActionFamilyOwnership(registry);

        ownership.RefreshLoadedPluginInventory(1, guid => guid == "IngoH.OrbOfCreation.AutoBuyOrb.Fork");
        ownership.Refresh(config.Current, lifecycleReady: true);

        Assert.True(ownership.OwnsAutoBuy(AutoBuyCandidateKind.Structure));
        Assert.True(ownership.OwnsAutoBuy(AutoBuyCandidateKind.Upgrade));
    }

    [Fact]
    public void PersistentKnownConflictRetriesClaimsOnlyAtBoundedIntervals()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyStructures.Value = true;
        config.AutoBuyUpgrades.Value = true;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.RefreshLoadedPluginInventory(
            1,
            guid => guid == AutomataActionFamilyOwnership.KnownAutoBuyPluginGuid);
        ownership.Refresh(config.Current, lifecycleReady: true, frame: 0);
        var initialAttempts = ownership.ClaimAttempts;

        for (var frame = 1; frame < 60; frame++)
            ownership.Refresh(config.Current, lifecycleReady: true, frame);

        Assert.Equal(initialAttempts, ownership.ClaimAttempts);
        ownership.Refresh(config.Current, lifecycleReady: true, frame: 60);
        Assert.Equal(initialAttempts + 2, ownership.ClaimAttempts);
    }

    [Fact]
    public void AutomataConfigurationDisableReleasesClaims()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.Refresh(config.Current, lifecycleReady: true);
        Assert.True(ownership.OwnsCast);

        config.Enabled.Value = false;
        ownership.Refresh(config.Current, lifecycleReady: true);

        using var replacement = Claim(
            registry,
            new FeatureStatusKey("tests", "replacement-cast"),
            AutomationActionFamily.SpellCast);
        Assert.True(replacement.IsHeld);
    }

    [Fact]
    public void HarvestClaimIsRevokedBeforeAnotherNativeTransactionCanStart()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoHarvestMode.Value = AutoHarvestOperationMode.Active;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.Refresh(config.Current, lifecycleReady: true);

        Assert.True(ownership.OwnsHarvest);
        Assert.True(ownership.TryCaptureHarvestMutationPermit());

        using var external = registry.RegisterKnownExternal(
            new ActionFamilyOwner(
                new FeatureStatusKey("tests.external", "Harvest"),
                "External Harvest"),
            new[] { AutomationActionFamily.HarvestAction });

        Assert.False(ownership.OwnsHarvest);
        Assert.False(ownership.TryCaptureHarvestMutationPermit());
    }

    [Fact]
    public void MentorDomainClaimsAreIndependentAndReleaseOnLifecycleTeardown()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = MentorConfig.Bind(new ConfigFile());
        config.Mode.Value = MentorOperationMode.Active;
        config.ArtifactsEnabled.Value = true;
        config.AlchemyEnabled.Value = true;
        using var ownership = new MentorActionFamilyOwnership(registry);
        ownership.Refresh(config, lifecycleReady: true, frame: 1);

        using var artifactExternal = registry.RegisterKnownExternal(
            new ActionFamilyOwner(new FeatureStatusKey("external", "artifacts"), "External artifacts"),
            new[] { AutomationActionFamily.ArtifactMasteryExperienceGrant });
        ownership.Refresh(config, lifecycleReady: true, frame: 2);

        Assert.True(ownership.IsHeld(MentorDomain.Spells));
        Assert.False(ownership.IsHeld(MentorDomain.Artifacts));
        Assert.True(ownership.IsHeld(MentorDomain.Alchemy));

        ownership.ReleaseLifecycleClaims();
        using var spellReplacement = Claim(
            registry,
            new FeatureStatusKey("tests", "replacement-spells"),
            AutomationActionFamily.SpellMasteryExperienceGrant);
        using var alchemyReplacement = Claim(
            registry,
            new FeatureStatusKey("tests", "replacement-alchemy"),
            AutomationActionFamily.AlchemyMasteryExperienceGrant);
        Assert.True(spellReplacement.IsHeld);
        Assert.True(alchemyReplacement.IsHeld);
    }

    [Fact]
    public void AutoCastFinalGateRejectsOwnershipLostDuringAdmission()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var owned = true;
        var candidate = new OwnershipChangingCastCandidate(() => owned = false);
        using var engine = new AutoCastEngine(
            config,
            new SingleCastCatalog(candidate),
            new ReservePolicy(config),
            new ResourceFullnessPolicy(),
            new ManualLogSource(),
            isGameplayScene: () => true,
            ownsActionFamily: () => owned);

        engine.Tick(config.AutoCastIntervalSeconds.Value);

        Assert.Equal(0, candidate.FireCalls);
    }

    [Fact]
    public void MentorRootDegradesWhenLockedDomainPrecedesConflictAndHealthySibling()
    {
        var domains = new[]
        {
            new FeatureStatusSnapshot(
                MentorFeatureStatus.Key(MentorDomain.Spells), "Mentor spells", true,
                FeatureStatusState.Locked,
                new FeatureStatusReason(FeatureStatusReasonCode.ProgressionLocked, "spells locked"), 1),
            new FeatureStatusSnapshot(
                MentorFeatureStatus.Key(MentorDomain.Artifacts), "Mentor artifacts", true,
                FeatureStatusState.TemporarilyBlocked,
                new FeatureStatusReason(FeatureStatusReasonCode.ActionFamilyConflict, "artifact conflict"), 1),
            new FeatureStatusSnapshot(
                MentorFeatureStatus.Key(MentorDomain.Alchemy), "Mentor alchemy", true,
                FeatureStatusState.Operational, lifecycleGeneration: 1),
        };

        var root = MentorFeatureStatus.ProjectRoot(
            configured: true,
            emergencyDisabled: false,
            globalFailure: MentorFeatureFailureKind.None,
            globalFailureReason: null,
            globalFailureCause: AutomationDecisionCode.None,
            domains: domains,
            lifecycleGeneration: 1);

        Assert.Equal(FeatureStatusState.Degraded, root.State);
        Assert.Equal(FeatureStatusReasonCode.PartialCapabilityUnavailable, root.Reason.Code);
    }

    private static ActionFamilyLeaseSet Claim(
        ActionFamilyOwnershipRegistry registry,
        FeatureStatusKey key,
        AutomationActionFamily family)
    {
        Assert.True(registry.TryClaimSet(
            new ActionFamilyOwner(key, key.FeatureId),
            new[] { family },
            out var lease,
            out _));
        return lease!;
    }

    private sealed class SingleCastCatalog : IAutoCastCatalog
    {
        private readonly IAutoCastCandidate _candidate;
        public SingleCastCatalog(IAutoCastCandidate candidate) => _candidate = candidate;
        public IReadOnlyList<IAutoCastCandidate> DiscoverActiveLoadout() => new[] { _candidate };
        public bool IsNativeCastBusy() => false;
        public bool IsTargeting() => false;
        public void Dispose() { }
    }

    private sealed class OwnershipChangingCastCandidate : IAutoCastCandidate
    {
        private readonly Action _loseOwnership;
        private readonly object _native = new();
        public OwnershipChangingCastCandidate(Action loseOwnership) => _loseOwnership = loseOwnership;
        public int FireCalls { get; private set; }
        public int SlotIndex => 0;
        public string DisplayName => "Spell";
        public AutoCastSpellKind Kind => AutoCastSpellKind.Instant;
        public bool IsEmpty => false;
        public bool IsCharged => false;
        public bool IsCasting => false;
        public bool IsReadyingCast => false;
        public bool CanCast(out string reason) { reason = string.Empty; return true; }
        public bool TryGetImmediateCosts(out IReadOnlyList<ResourceAdmissionCost> costs)
        {
            _loseOwnership();
            costs = Array.Empty<ResourceAdmissionCost>();
            return true;
        }
        public bool TryGetDrainCosts(out IReadOnlyList<ResourceAdmissionCost> costs) { costs = Array.Empty<ResourceAdmissionCost>(); return true; }
        public bool HasValidTargets(out string reason) { reason = string.Empty; return true; }
        public bool TryFireAndResolveTargets(out string reason) { FireCalls++; reason = string.Empty; return true; }
        public bool TryGetIdentity(out AutoCastCandidateIdentity identity, out string reason)
        {
            identity = new AutoCastCandidateIdentity("spell", _native, _native.GetType(), 0);
            reason = string.Empty;
            return true;
        }
        public bool TrySetChargeHold(bool isHolding, out string reason) { reason = string.Empty; return true; }
    }
}
