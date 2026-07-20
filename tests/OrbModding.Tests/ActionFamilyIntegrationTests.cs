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
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyStructures.Value = true;
        config.AutoBuyUpgrades.Value = true;
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        config.AutoConceptMode.Value = AutoConceptOperationMode.Active;
        config.AutoLevelSpells.Value = true;
        using var ownership = new AutomataActionFamilyOwnership(registry);

        ownership.RefreshLoadedPluginInventory(
            1,
            guid => guid == AutomataActionFamilyOwnership.KnownAutoBuyPluginGuid);
        ownership.Refresh(config, lifecycleReady: true);

        Assert.False(ownership.OwnsAutoBuy(AutoBuyCandidateKind.Structure));
        Assert.False(ownership.OwnsAutoBuy(AutoBuyCandidateKind.Upgrade));
        Assert.True(ownership.OwnsCast);
        Assert.True(ownership.OwnsConcept);
        Assert.True(ownership.OwnsSpellLevel);
    }

    [Fact]
    public void SimilarUnknownGuidDoesNotClaimKnownExternalFamilies()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyStructures.Value = true;
        config.AutoBuyUpgrades.Value = true;
        using var ownership = new AutomataActionFamilyOwnership(registry);

        ownership.RefreshLoadedPluginInventory(1, guid => guid == "IngoH.OrbOfCreation.AutoBuyOrb.Fork");
        ownership.Refresh(config, lifecycleReady: true);

        Assert.True(ownership.OwnsAutoBuy(AutoBuyCandidateKind.Structure));
        Assert.True(ownership.OwnsAutoBuy(AutoBuyCandidateKind.Upgrade));
    }

    [Fact]
    public void PersistentKnownConflictRetriesClaimsOnlyAtBoundedIntervals()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyStructures.Value = true;
        config.AutoBuyUpgrades.Value = true;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.RefreshLoadedPluginInventory(
            1,
            guid => guid == AutomataActionFamilyOwnership.KnownAutoBuyPluginGuid);
        ownership.Refresh(config, lifecycleReady: true, frame: 0);
        var initialAttempts = ownership.ClaimAttempts;

        for (var frame = 1; frame < 60; frame++)
            ownership.Refresh(config, lifecycleReady: true, frame);

        Assert.Equal(initialAttempts, ownership.ClaimAttempts);
        ownership.Refresh(config, lifecycleReady: true, frame: 60);
        Assert.Equal(initialAttempts + 2, ownership.ClaimAttempts);
    }

    [Fact]
    public void AutomataConfigurationDisableReleasesClaims()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.Refresh(config, lifecycleReady: true);
        Assert.True(ownership.OwnsCast);

        config.Enabled.Value = false;
        ownership.Refresh(config, lifecycleReady: true);

        using var replacement = Claim(
            registry,
            new FeatureStatusKey("tests", "replacement-cast"),
            AutomationActionFamily.SpellCast);
        Assert.True(replacement.IsHeld);
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
    public void AutoBuyFinalGateRejectsOwnershipLostDuringAdmission()
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyStructures.Value = true;
        config.AutoBuyUpgrades.Value = false;
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0;
        var owned = true;
        var candidate = new OwnershipChangingBuyCandidate(() => owned = false);
        using var engine = new AutoBuyEngine(
            config,
            new SingleBuyCatalog(candidate),
            new ReservePolicy(config),
            new ManualLogSource(),
            ownsActionFamily: _ => owned);

        engine.Tick(config.AutoBuyIntervalSeconds.Value);

        Assert.Equal(0, candidate.PurchaseCalls);
    }

    [Fact]
    public void AutoCastFinalGateRejectsOwnershipLostDuringAdmission()
    {
        var config = AutomataConfig.Bind(new ConfigFile());
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
    public void PartialAutoBuyOwnershipImmediatelyReportsDegradedWhileIdle()
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyStructures.Value = true;
        config.AutoBuyUpgrades.Value = true;
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config, 1, registry);
        using var engine = new AutoBuyEngine(
            config,
            new EmptyBuyCatalog(),
            new ReservePolicy(config),
            new ManualLogSource(),
            featureStatus: statuses.AutoBuy,
            ownsActionFamily: kind => kind == AutoBuyCandidateKind.Upgrade);

        engine.Tick(config.AutoBuyIntervalSeconds.Value);

        Assert.Equal(FeatureStatusState.Degraded, statuses.AutoBuy.Current.State);
        Assert.Equal(FeatureStatusReasonCode.PartialCapabilityUnavailable, statuses.AutoBuy.Current.Reason.Code);
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

    private sealed class SingleBuyCatalog : IAutoBuyCatalog
    {
        private readonly IAutoBuyCandidate _candidate;
        public SingleBuyCatalog(IAutoBuyCandidate candidate) => _candidate = candidate;
        public IEnumerable<IAutoBuyCandidate> Discover() { yield return _candidate; }
        public bool TryCaptureQueueCapacity(int usage, int reservation, out QueueCapacitySnapshot snapshot) =>
            QueueCapacitySnapshot.TryCreate(10, 10, usage, reservation, out snapshot, out _);
        public bool TryGetBulkDevelopment(out int levels) { levels = 1; return true; }
        public bool TryGetActionMultiplier(out int multiplier) { multiplier = 1; return true; }
        public void Dispose() { }
    }

    private sealed class EmptyBuyCatalog : IAutoBuyCatalog
    {
        public IEnumerable<IAutoBuyCandidate> Discover() => Array.Empty<IAutoBuyCandidate>();
        public bool TryCaptureQueueCapacity(int usage, int reservation, out QueueCapacitySnapshot snapshot) =>
            QueueCapacitySnapshot.TryCreate(10, 10, usage, reservation, out snapshot, out _);
        public bool TryGetBulkDevelopment(out int levels) { levels = 1; return true; }
        public bool TryGetActionMultiplier(out int multiplier) { multiplier = 1; return true; }
        public void Dispose() { }
    }

    private sealed class OwnershipChangingBuyCandidate : IAutoBuyCandidate
    {
        private readonly Action _loseOwnership;
        private readonly AutoBuyCandidateSnapshot _snapshot;
        public OwnershipChangingBuyCandidate(Action loseOwnership)
        {
            _loseOwnership = loseOwnership;
            _snapshot = new AutoBuyCandidateSnapshot(this, "structure", "Structure", AutoBuyCandidateKind.Structure, "StructureSO");
        }
        public int PurchaseCalls { get; private set; }
        public AutoBuyCandidateSnapshot Snapshot() => _snapshot;
        public bool IsAvailable() => true;
        public bool CanPurchase(out string reason) { reason = string.Empty; return true; }
        public IReadOnlyList<ResourceAdmissionCost> GetCosts()
        {
            _loseOwnership();
            return new[] { new ResourceAdmissionCost("resource", "Resource", new BigAmount(1, 0), new BigAmount(100, 0)) };
        }
        public bool TryPurchaseOne(out string reason) { PurchaseCalls++; reason = string.Empty; return true; }
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
