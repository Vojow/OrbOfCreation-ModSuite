using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoBuyDirtyResourceTests
{
    [Fact]
    public void ResourceSnapshot_ReadsOncePerResourcePerEpochAndPrunesUnusedEntries()
    {
        var reader = new FakeResourceReader();
        var changes = new List<AutoBuyResourceChange>();
        var cache = new AutoBuyResourceSnapshotCache(reader, (_, change) => changes.Add(change));
        var native = new object();
        var definition = new AutoBuyResourceDefinition(
            "mana",
            "Mana",
            native,
            new BigAmount(1.0, 1));

        cache.BeginLazyEpoch();
        Assert.True(cache.TryResolve(definition, out var first));
        Assert.True(cache.TryResolve(definition, out var second));
        Assert.Equal(1, reader.ReadCalls);
        Assert.Equal(first.Epoch, second.Epoch);

        reader.TrueQuantity = new BigAmount(9.0, 2);
        cache.BeginEvaluationEpoch(_ => true);

        Assert.Equal(2, reader.ReadCalls);
        Assert.Contains(changes, change => (change & AutoBuyResourceChange.Quantity) != 0);

        cache.BeginEvaluationEpoch(_ => false);
        Assert.Equal(2, reader.ReadCalls);
        Assert.False(cache.TryResolve(
            new AutoBuyResourceDefinition("missing", "Missing", new object(), new BigAmount(1.0, 0)),
            out _));
    }

    [Fact]
    public void ResourceSnapshotFailure_UsesBoundedRetryBackoff()
    {
        var reader = new FakeResourceReader();
        var cache = new AutoBuyResourceSnapshotCache(reader, (_, _) => { });
        var missing = new AutoBuyResourceDefinition(
            "missing",
            "Missing",
            new object(),
            new BigAmount(1.0, 0));

        cache.BeginLazyEpoch();
        Assert.False(cache.TryResolve(missing, out _));
        Assert.Equal(1, reader.ReadCalls);

        cache.BeginEvaluationEpoch(_ => true);
        Assert.Equal(2, reader.ReadCalls);
        cache.BeginEvaluationEpoch(_ => true);
        Assert.Equal(2, reader.ReadCalls);
    }

    [Fact]
    public void QualityChange_DirtiesAllDependentsButOnlyStructureNominalCosts()
    {
        var index = new AutoBuyCandidateIndex();
        var structure = Candidate("structure", AutoBuyCandidateKind.Structure, "mana", available: true);
        var upgrade = Candidate("upgrade", AutoBuyCandidateKind.Upgrade, "mana", available: true);
        PrimeDependencies(index, structure, upgrade);

        index.InvalidateResource("mana", AutoBuyResourceChange.Quality);

        Assert.True(index.TryGetDirtyReasons(structure.Uuid, out var structureDirty));
        Assert.True(index.TryGetDirtyReasons(upgrade.Uuid, out var upgradeDirty));
        Assert.True((structureDirty & AutoBuyDirtyReason.CostDirty) != 0);
        Assert.True((structureDirty & AutoBuyDirtyReason.ResourceDirty) != 0);
        Assert.False((upgradeDirty & AutoBuyDirtyReason.CostDirty) != 0);
        Assert.True((upgradeDirty & AutoBuyDirtyReason.ResourceDirty) != 0);
    }

    [Fact]
    public void ResourceChange_DirtiesOnlyItsDependents()
    {
        var index = new AutoBuyCandidateIndex();
        var mana = Candidate("mana-user", AutoBuyCandidateKind.Upgrade, "mana", available: true);
        var dust = Candidate("dust-user", AutoBuyCandidateKind.Upgrade, "dust", available: true);
        PrimeDependencies(index, mana, dust);

        index.InvalidateResource("mana", AutoBuyResourceChange.Quantity);

        Assert.True(index.TryGetDirtyReasons(mana.Uuid, out var manaDirty));
        Assert.True(index.TryGetDirtyReasons(dust.Uuid, out var dustDirty));
        Assert.True((manaDirty & AutoBuyDirtyReason.ResourceDirty) != 0);
        Assert.Equal(AutoBuyDirtyReason.None, dustDirty);
    }

    [Fact]
    public void AcceptedPurchase_DirtiesOwnQueueCostAndEverySpentResourceDependent()
    {
        var index = new AutoBuyCandidateIndex();
        var purchased = Candidate("purchased", AutoBuyCandidateKind.Structure, "mana", available: true);
        var dependent = Candidate("dependent", AutoBuyCandidateKind.Upgrade, "mana", available: true);
        PrimeDependencies(index, purchased, dependent);

        index.MarkPurchaseAttempted(purchased);

        Assert.True(index.TryGetDirtyReasons(purchased.Uuid, out var purchasedDirty));
        Assert.True(index.TryGetDirtyReasons(dependent.Uuid, out var dependentDirty));
        Assert.True((purchasedDirty & AutoBuyDirtyReason.LevelDirty) != 0);
        Assert.True((purchasedDirty & AutoBuyDirtyReason.CompletionDirty) != 0);
        Assert.True((purchasedDirty & AutoBuyDirtyReason.CostDirty) != 0);
        Assert.True((dependentDirty & AutoBuyDirtyReason.ResourceDirty) != 0);
    }

    [Fact]
    public void SlowLifecycleSlice_DiscoversLockedCandidateBecomingAvailable()
    {
        var index = new AutoBuyCandidateIndex();
        var candidate = Candidate("later", AutoBuyCandidateKind.Upgrade, "mana", available: false);
        Assert.Empty(index.Reconcile(new[] { candidate }));

        candidate.Available = true;
        var batch = index.PrepareEvaluation(
            new AutoBuyEvaluationRequest(10, true, true),
            lifecycleWorkLimit: 1,
            activeRefreshCount: 0,
            slowRefreshCount: 1);

        Assert.Same(candidate, Assert.Single(batch.ActiveCandidates));
        Assert.Same(candidate, Assert.Single(batch.DirtyCandidates));
    }

    [Fact]
    public void SlowLifecycleSlice_ConservativelyReevaluatesActiveCanPurchaseState()
    {
        var index = new AutoBuyCandidateIndex();
        var candidate = Candidate("requirements", AutoBuyCandidateKind.Upgrade, "mana", available: true);
        PrimeDependencies(index, candidate);
        Assert.True(index.TryGetDirtyReasons(candidate.Uuid, out var clean));
        Assert.Equal(AutoBuyDirtyReason.None, clean);

        var batch = index.PrepareEvaluation(
            new AutoBuyEvaluationRequest(10, true, true),
            lifecycleWorkLimit: 1,
            activeRefreshCount: 1,
            slowRefreshCount: 0);

        Assert.Same(candidate, Assert.Single(batch.DirtyCandidates));
        Assert.True(index.TryGetDirtyReasons(candidate.Uuid, out var refreshed));
        Assert.True((refreshed & AutoBuyDirtyReason.PriorityDirty) != 0);
    }

    [Fact]
    public void NativeQueueChange_DirtiesCachedCandidateCost()
    {
        var index = new AutoBuyCandidateIndex();
        var candidate = Candidate("manual-queue", AutoBuyCandidateKind.Structure, "mana", available: true);
        PrimeDependencies(index, candidate);

        candidate.QueuedLevels = 1;
        index.PrepareEvaluation(
            new AutoBuyEvaluationRequest(10, true, true),
            lifecycleWorkLimit: 1,
            activeRefreshCount: 1,
            slowRefreshCount: 0);

        Assert.True(index.TryGetDirtyReasons(candidate.Uuid, out var dirty));
        Assert.True((dirty & AutoBuyDirtyReason.CostDirty) != 0);
        Assert.True((dirty & AutoBuyDirtyReason.PriorityDirty) != 0);
    }

    [Fact]
    public void InvalidRegistryEntries_RemainQuarantinedDuringSlowRefresh()
    {
        var index = new AutoBuyCandidateIndex();
        var original = Candidate("collision", AutoBuyCandidateKind.Structure, "mana", available: true);
        Assert.Single(index.Reconcile(new[] { original }));
        var wrongType = Candidate("collision", AutoBuyCandidateKind.Upgrade, "mana", available: true);
        Assert.Empty(index.Reconcile(new[] { wrongType }));

        for (var i = 0; i < 4; i++)
        {
            var batch = index.PrepareEvaluation(
                new AutoBuyEvaluationRequest(10, true, true),
                lifecycleWorkLimit: 10,
                activeRefreshCount: 0,
                slowRefreshCount: 10);
            Assert.Empty(batch.ActiveCandidates);
            Assert.True(index.TryGetState("collision", out var state));
            Assert.Equal(AutoBuyCandidateLifecycleState.Invalid, state);
        }

        Assert.Empty(index.Reconcile(Array.Empty<DirtyCandidate>()));
        var missingBatch = index.PrepareEvaluation(
            new AutoBuyEvaluationRequest(10, true, true),
            lifecycleWorkLimit: 10,
            activeRefreshCount: 0,
            slowRefreshCount: 10);
        Assert.Empty(missingBatch.ActiveCandidates);
        Assert.True(index.TryGetState("collision", out var missingState));
        Assert.Equal(AutoBuyCandidateLifecycleState.Invalid, missingState);
    }

    [Fact]
    public void CorrectRegistryIdentity_RecoversQuarantinedTypeCollision()
    {
        var index = new AutoBuyCandidateIndex();
        var original = Candidate("recover", AutoBuyCandidateKind.Structure, "mana", available: true);
        Assert.Single(index.Reconcile(new[] { original }));
        Assert.Empty(index.Reconcile(new[]
        {
            Candidate("recover", AutoBuyCandidateKind.Upgrade, "mana", available: true),
        }));

        var recovered = index.Reconcile(new[] { original });

        Assert.Same(original, Assert.Single(recovered));
        Assert.True(index.TryGetState("recover", out var state));
        Assert.Equal(AutoBuyCandidateLifecycleState.Available, state);
    }

    [Fact]
    public void RegistryCompletionSweep_IsBoundedAcrossCalls()
    {
        var index = new AutoBuyCandidateIndex();
        var candidates = new List<DirtyCandidate>();
        for (var i = 0; i < 20; i++)
        {
            candidates.Add(Candidate($"candidate-{i:00}", AutoBuyCandidateKind.Upgrade, "mana", available: true));
        }

        index.Reconcile(candidates);
        index.BeginRegistryCompletion(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        index.ProcessRegistryCompletion(3);

        Assert.True(index.RegistryCompletionPending);
        Assert.True(index.TryGetState("candidate-00", out var first));
        Assert.Equal(AutoBuyCandidateLifecycleState.Invalid, first);
        Assert.True(index.TryGetState("candidate-19", out var last));
        Assert.NotEqual(AutoBuyCandidateLifecycleState.Invalid, last);

        while (index.RegistryCompletionPending)
        {
            index.ProcessRegistryCompletion(3);
        }

        Assert.True(index.TryGetState("candidate-19", out last));
        Assert.Equal(AutoBuyCandidateLifecycleState.Invalid, last);
    }

    [Fact]
    public void RecreatedRegistryObjects_CoalesceIntoOneLifecycleEpoch()
    {
        var index = new AutoBuyCandidateIndex();
        var originals = new List<DirtyCandidate>();
        for (var i = 0; i < 64; i++)
        {
            originals.Add(Candidate($"replacement-{i:00}", AutoBuyCandidateKind.Upgrade, "mana", available: true));
        }

        index.Reconcile(originals);
        var initialEpoch = index.Epoch;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var replacementDetected = false;
        for (var i = 0; i < originals.Count; i++)
        {
            replacementDetected |= index.ObserveCandidate(
                Candidate(originals[i].Uuid, AutoBuyCandidateKind.Upgrade, "mana", available: true),
                seen);
        }

        Assert.True(replacementDetected);
        Assert.Equal(initialEpoch, index.Epoch);

        index.InvalidateLifecycleIncrementally();
        Assert.Equal(initialEpoch + 1, index.Epoch);
        Assert.True(index.EpochValidationPending);
    }

    [Fact]
    public void IncrementalEngine_ReevaluatesDirtyCandidateAndKeepsCachedDeterministicRanking()
    {
        var first = new EngineCandidate("first", 1.0);
        var second = new EngineCandidate("second", 2.0);
        var catalog = new IncrementalCatalog(first, second);
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.UpgradeAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.Fixed;
        config.MaxPurchasesPerBatch.Value = 1;
        config.LeaveQueueSlots.Value = 0;
        using var engine = new AutoBuyEngine(
            config,
            catalog,
            new ReservePolicy(config),
            new ManualLogSource(),
            _ => 0.0,
            _ => 0.0);

        engine.Tick(config.AutoBuyIntervalSeconds.Value);
        engine.Tick(0.0f);

        Assert.Equal(1, first.PurchaseCalls);
        Assert.Equal(1, second.PurchaseCalls);
        Assert.Equal(2, second.CostReads);
        Assert.Equal(2, catalog.EvaluationCalls);
    }

    [Fact]
    public void IncrementalEngine_LearnsDependenciesBeforeNativeAffordabilityTurnsTrue()
    {
        var candidate = new EngineCandidate("waiting", 10.0) { CanPurchaseValue = false };
        var excluded = new EngineCandidate("excluded", 1.0);
        var catalog = new IncrementalCatalog(candidate, excluded);
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.UpgradeAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.AllowedAutoBuyUuids.Value = "waiting";
        config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.Fixed;
        config.MaxPurchasesPerBatch.Value = 1;
        config.LeaveQueueSlots.Value = 0;
        using var engine = new AutoBuyEngine(
            config,
            catalog,
            new ReservePolicy(config),
            new ManualLogSource(),
            _ => 0.0,
            _ => 0.0);

        engine.Tick(config.AutoBuyIntervalSeconds.Value);
        Assert.Equal(1, candidate.CostReads);
        Assert.Equal(0, candidate.PurchaseCalls);

        candidate.CanPurchaseValue = true;
        engine.Tick(config.AutoBuyIntervalSeconds.Value);

        Assert.Equal(1, candidate.PurchaseCalls);
        Assert.True(candidate.CostReads >= 3);
    }

    [Fact]
    public void IncrementalEngine_RejectsUnresolvedResourceSnapshot()
    {
        var candidate = new EngineCandidate("unresolved", 1.0) { CostsResolved = false };
        var catalog = new IncrementalCatalog(candidate, new EngineCandidate("other", 2.0));
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.UpgradeAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.AllowedAutoBuyUuids.Value = "unresolved";
        config.LeaveQueueSlots.Value = 0;
        using var engine = new AutoBuyEngine(
            config,
            catalog,
            new ReservePolicy(config),
            new ManualLogSource(),
            _ => 0.0,
            _ => 0.0);

        engine.Tick(config.AutoBuyIntervalSeconds.Value);

        Assert.Equal(0, candidate.PurchaseCalls);
        Assert.Equal(1, candidate.CostReads);
    }

    [Fact]
    public void IncrementalEngine_CompletesConfiguredStructureGroupBeforeReranking()
    {
        var first = new EngineCandidate("first", 1.0, AutoBuyCandidateKind.Structure);
        var second = new EngineCandidate("second", 2.0, AutoBuyCandidateKind.Structure);
        var catalog = new IncrementalCatalog(first, second);
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.UpgradeAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.Fixed;
        config.MaxPurchasesPerBatch.Value = 10;
        config.StructureRepeatMode.Value = AutoBuyStructureRepeatMode.Fixed;
        config.FixedStructureLevelsPerCandidate.Value = 3;
        config.LeaveQueueSlots.Value = 0;
        using var engine = new AutoBuyEngine(
            config,
            catalog,
            new ReservePolicy(config),
            new ManualLogSource(),
            _ => 0.0,
            _ => 0.0);

        engine.Tick(config.AutoBuyIntervalSeconds.Value);

        Assert.Equal(3, first.PurchaseCalls);
        Assert.Equal(0, second.PurchaseCalls);

        engine.Tick(0.0f);

        Assert.Equal(3, second.PurchaseCalls);
    }

    [Fact]
    public void NativeCompletion_InvalidatesOtherStructureCostAndUnlockStateAsOneSettlement()
    {
        var index = new AutoBuyCandidateIndex();
        var changedStructure = Candidate("cost-scaling-target", AutoBuyCandidateKind.Structure, "mana", available: true);
        var newlyUnlocked = Candidate("prerequisite-target", AutoBuyCandidateKind.Upgrade, "mana", available: false);
        index.Reconcile(new[] { changedStructure, newlyUnlocked });
        var initial = index.PrepareEvaluation(
            new AutoBuyEvaluationRequest(10, true, true),
            lifecycleWorkLimit: 10,
            activeRefreshCount: 0,
            slowRefreshCount: 0);
        foreach (var candidate in initial.DirtyCandidates)
        {
            index.CompleteCandidateEvaluation(candidate);
        }

        newlyUnlocked.Available = true;
        index.InvalidateCompletionEffects();

        Assert.True(index.SettlementValidationPending);
        var settled = index.PrepareEvaluation(
            new AutoBuyEvaluationRequest(10, true, true),
            lifecycleWorkLimit: 10,
            activeRefreshCount: 0,
            slowRefreshCount: 0);

        Assert.False(index.SettlementValidationPending);
        Assert.Contains(changedStructure, settled.DirtyCandidates);
        Assert.Contains(newlyUnlocked, settled.ActiveCandidates);
        Assert.True(index.TryGetDirtyReasons(changedStructure.Uuid, out var structureDirty));
        Assert.True((structureDirty & AutoBuyDirtyReason.CostDirty) != 0);
        Assert.True((structureDirty & AutoBuyDirtyReason.PriorityDirty) != 0);
    }

    [Fact]
    public void PolicyExcludedCandidate_IsCleanUntilPolicyChanges()
    {
        var index = new AutoBuyCandidateIndex();
        var candidate = Candidate("excluded", AutoBuyCandidateKind.Upgrade, "mana", available: true);
        candidate.CostsResolved = false;
        index.Reconcile(new[] { candidate });
        var first = index.PrepareEvaluation(
            new AutoBuyEvaluationRequest(10, true, true),
            lifecycleWorkLimit: 10,
            activeRefreshCount: 0,
            slowRefreshCount: 0);
        Assert.Same(candidate, Assert.Single(first.DirtyCandidates));

        index.CompleteCandidateEvaluation(candidate, policyExcluded: true);
        var unchanged = index.PrepareEvaluation(
            new AutoBuyEvaluationRequest(10, true, true),
            lifecycleWorkLimit: 10,
            activeRefreshCount: 0,
            slowRefreshCount: 0);
        Assert.Empty(unchanged.DirtyCandidates);

        index.InvalidatePolicy();
        var changed = index.PrepareEvaluation(
            new AutoBuyEvaluationRequest(10, true, true),
            lifecycleWorkLimit: 10,
            activeRefreshCount: 0,
            slowRefreshCount: 0);
        Assert.Same(candidate, Assert.Single(changed.DirtyCandidates));
    }

    [Fact]
    public void FailedPostPurchaseVerification_StillInvalidatesCandidateAndResources()
    {
        var failed = new EngineCandidate("failed", 1.0) { PurchaseSucceeds = false };
        var catalog = new IncrementalCatalog(failed, new EngineCandidate("other", 2.0));
        var config = ActiveConfig("failed");
        using var engine = new AutoBuyEngine(
            config,
            catalog,
            new ReservePolicy(config),
            new ManualLogSource(),
            _ => 0.0,
            _ => 0.0);

        engine.Tick(config.AutoBuyIntervalSeconds.Value);

        Assert.Equal(1, failed.PurchaseCalls);
        Assert.Equal(1, catalog.PurchaseAttemptNotifications);
    }

    [Fact]
    public void FixedStructureGroup_IsClampedToInitialFreeQueueRoom()
    {
        var structure = new EngineCandidate("structure", 1.0, AutoBuyCandidateKind.Structure);
        var catalog = new IncrementalCatalog(structure, new EngineCandidate("other", 2.0))
        {
            RemainingRoom = 2,
        };
        var config = ActiveConfig("structure");
        config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.Fixed;
        config.MaxPurchasesPerBatch.Value = 10;
        config.StructureRepeatMode.Value = AutoBuyStructureRepeatMode.Fixed;
        config.FixedStructureLevelsPerCandidate.Value = 10;
        using var engine = new AutoBuyEngine(
            config,
            catalog,
            new ReservePolicy(config),
            new ManualLogSource(),
            _ => 0.0,
            _ => 0.0);

        engine.Tick(config.AutoBuyIntervalSeconds.Value);

        Assert.Equal(2, structure.PurchaseCalls);
    }

    [Fact]
    public void BulkStructureGroup_IsClampedToInitialFreeQueueRoom()
    {
        var structure = new EngineCandidate("structure", 1.0, AutoBuyCandidateKind.Structure);
        var catalog = new IncrementalCatalog(structure, new EngineCandidate("other", 2.0))
        {
            RemainingRoom = 2,
            BulkLevels = 25,
        };
        var config = ActiveConfig("structure");
        config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.Fixed;
        config.MaxPurchasesPerBatch.Value = 10;
        config.StructureRepeatMode.Value = AutoBuyStructureRepeatMode.BulkDevelopment;
        using var engine = new AutoBuyEngine(
            config,
            catalog,
            new ReservePolicy(config),
            new ManualLogSource(),
            _ => 0.0,
            _ => 0.0);

        engine.Tick(config.AutoBuyIntervalSeconds.Value);

        Assert.Equal(2, structure.PurchaseCalls);
    }

    [Fact]
    public void CpuSlicedFixedStructureGroup_IgnoresItsOwnQueueSignalAndResumesToInitialClamp()
    {
        AssertCpuSlicedSelfSignalingGroupCompletes(
            AutoBuyCandidateKind.Structure,
            config =>
            {
                config.StructureRepeatMode.Value = AutoBuyStructureRepeatMode.Fixed;
                config.FixedStructureLevelsPerCandidate.Value = 25;
            });
    }

    [Fact]
    public void CpuSlicedBulkStructureGroup_IgnoresItsOwnQueueSignalAndResumesToInitialClamp()
    {
        AssertCpuSlicedSelfSignalingGroupCompletes(
            AutoBuyCandidateKind.Structure,
            config => config.StructureRepeatMode.Value = AutoBuyStructureRepeatMode.BulkDevelopment);
    }

    [Fact]
    public void CpuSlicedUpgradeMultiplierGroup_IgnoresItsOwnQueueSignalAndResumesToInitialClamp()
    {
        AssertCpuSlicedSelfSignalingGroupCompletes(
            AutoBuyCandidateKind.Upgrade,
            config => config.RespectActionMultiplier.Value = true);
    }

    [Fact]
    public void ManualQueueSignal_CancelsCpuSlicedRepeatGroupAndForcesFreshEvaluation()
    {
        var structure = new EngineCandidate("structure", 1.0, AutoBuyCandidateKind.Structure)
        {
            RaiseQueueSignalOnPurchase = true,
        };
        var catalog = new IncrementalCatalog(structure, new EngineCandidate("other", 2.0))
        {
            RemainingRoom = 3,
        };
        var config = ActiveConfig("structure");
        config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.Fixed;
        config.MaxPurchasesPerBatch.Value = 10;
        config.StructureRepeatMode.Value = AutoBuyStructureRepeatMode.Fixed;
        config.FixedStructureLevelsPerCandidate.Value = 10;
        using var engine = new AutoBuyEngine(
            config,
            catalog,
            new ReservePolicy(config),
            new ManualLogSource(),
            _ => 0.0,
            _ => double.PositiveInfinity);
        Action<object> handler = engine.NotifyStructureQueueChanged;
        AutoBuyLifecycleSignal.StructureQueueChanged += handler;
        try
        {
            engine.Tick(config.AutoBuyIntervalSeconds.Value);
            Assert.Equal(1, structure.PurchaseCalls);
            Assert.Equal(1, catalog.EvaluationCalls);

            structure.CanPurchaseValue = false;
            AutoBuyLifecycleSignal.RaiseStructureQueueChanged(structure.NativeIdentity);
            engine.Tick(0.0f);

            Assert.Equal(1, structure.PurchaseCalls);
            Assert.Equal(2, catalog.EvaluationCalls);
        }
        finally
        {
            AutoBuyLifecycleSignal.StructureQueueChanged -= handler;
        }
    }

    [Fact]
    public void AutomatedMutationScope_RestoresExternalSignalsAfterException()
    {
        var nativeIdentity = new object();
        var unrelatedIdentity = new object();
        var signals = 0;
        Action<object> handler = _ => signals++;
        AutoBuyLifecycleSignal.StructureQueueChanged += handler;
        try
        {
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using (AutoBuyLifecycleSignal.EnterAutomatedMutation(nativeIdentity))
                {
                    AutoBuyLifecycleSignal.RaiseStructureQueueChanged(nativeIdentity);
                    AutoBuyLifecycleSignal.RaiseStructureQueueChanged(unrelatedIdentity);
                    throw new InvalidOperationException("simulated native failure");
                }
            }));

            AutoBuyLifecycleSignal.RaiseStructureQueueChanged(nativeIdentity);
            Assert.Equal(2, signals);
        }
        finally
        {
            AutoBuyLifecycleSignal.StructureQueueChanged -= handler;
        }
    }

    [Theory]
    [InlineData((int)AutoBuyCandidateKind.Structure)]
    [InlineData((int)AutoBuyCandidateKind.Upgrade)]
    public void QueueSignal_DirtiesOnlyExactNativeCandidate(int kindValue)
    {
        var kind = (AutoBuyCandidateKind)kindValue;
        var target = Candidate("target", kind, "mana", available: true);
        var unaffected = Candidate("unaffected", kind, "dust", available: true);
        var index = new AutoBuyCandidateIndex();
        PrimeDependencies(index, target, unaffected);
        target.QueuedLevels = 1;

        Assert.True(index.InvalidateQueue(target.NativeIdentity, kind));
        var batch = index.PrepareEvaluation(
            new AutoBuyEvaluationRequest(10, true, true),
            lifecycleWorkLimit: 10,
            activeRefreshCount: 0,
            slowRefreshCount: 0);

        Assert.Same(target, Assert.Single(batch.DirtyCandidates));
        Assert.True(index.TryGetDirtyReasons(unaffected.Uuid, out var unaffectedDirty));
        Assert.Equal(AutoBuyDirtyReason.None, unaffectedDirty);
    }

    private static void AssertCpuSlicedSelfSignalingGroupCompletes(
        AutoBuyCandidateKind kind,
        Action<AutomataConfig> configure)
    {
        var candidate = new EngineCandidate("candidate", 1.0, kind)
        {
            RaiseQueueSignalOnPurchase = true,
        };
        var catalog = new IncrementalCatalog(candidate, new EngineCandidate("other", 2.0))
        {
            RemainingRoom = 3,
            BulkLevels = 25,
            ActionMultiplier = 25,
        };
        var config = ActiveConfig("candidate");
        config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.Fixed;
        config.MaxPurchasesPerBatch.Value = 10;
        configure(config);
        using var engine = new AutoBuyEngine(
            config,
            catalog,
            new ReservePolicy(config),
            new ManualLogSource(),
            _ => 0.0,
            _ => double.PositiveInfinity);
        Action<object> handler = kind == AutoBuyCandidateKind.Structure
            ? engine.NotifyStructureQueueChanged
            : engine.NotifyUpgradeQueueChanged;
        if (kind == AutoBuyCandidateKind.Structure)
        {
            AutoBuyLifecycleSignal.StructureQueueChanged += handler;
        }
        else
        {
            AutoBuyLifecycleSignal.UpgradeQueueChanged += handler;
        }

        try
        {
            for (var frame = 1; frame <= 3; frame++)
            {
                engine.Tick(frame == 1 ? config.AutoBuyIntervalSeconds.Value : 0.0f);
                Assert.Equal(frame, candidate.PurchaseCalls);
            }

            Assert.Equal(1, catalog.EvaluationCalls);
        }
        finally
        {
            if (kind == AutoBuyCandidateKind.Structure)
            {
                AutoBuyLifecycleSignal.StructureQueueChanged -= handler;
            }
            else
            {
                AutoBuyLifecycleSignal.UpgradeQueueChanged -= handler;
            }
        }
    }

    [Fact]
    public void ExactCostAdapter_RequiresEveryNativeTupleAndCachesSchema()
    {
        var mana = new ResourceSO { uuid = "mana" };
        var valid = new ResourceCostList();
        valid.costs.Add(new ResourceTuple(mana, new TestBigDouble(2.0, 3)));
        valid.costs.Add(new ResourceTuple(mana, new TestBigDouble(3.0, 3)));
        var decoded = new List<DecodedResourceCost>();
        var before = NativeResourceCostAdapter.CachedSchemaCount;

        Assert.True(NativeResourceCostAdapter.TryRead(valid, decoded, out var tupleCount, out var reason), reason);
        Assert.Equal(2, tupleCount);
        Assert.Equal(2, decoded.Count);
        var afterFirst = NativeResourceCostAdapter.CachedSchemaCount;
        Assert.InRange(afterFirst, before, before + 1);
        Assert.True(NativeResourceCostAdapter.TryRead(valid, decoded, out tupleCount, out reason), reason);
        Assert.Equal(afterFirst, NativeResourceCostAdapter.CachedSchemaCount);

        valid.costs.Add(new ResourceTuple(null!, new TestBigDouble(1.0, 0)));
        Assert.False(NativeResourceCostAdapter.TryRead(valid, decoded, out tupleCount, out _));
        Assert.Equal(3, tupleCount);
        Assert.Empty(decoded);
    }

    [Fact]
    public void EmptyNativeCostList_IsAResolvedFreePurchaseVector()
    {
        var decoded = new List<DecodedResourceCost>();
        Assert.True(NativeResourceCostAdapter.TryRead(
            new ResourceCostList(),
            decoded,
            out var tupleCount,
            out var reason), reason);
        Assert.Equal(0, tupleCount);
        Assert.Empty(decoded);

        var config = AutomataConfig.Bind(new ConfigFile());
        var reserve = new ReservePolicy(config).Evaluate(Array.Empty<ResourceAdmissionCost>());
        Assert.True(reserve.Passed);
        Assert.Equal(0.0, reserve.MaxCostToQuantityRatio);
    }

    [Fact]
    public void CostAdapterFailure_BacksOffBeforeRetryingNativeReflection()
    {
        var native = new StructureSO
        {
            uuid = "broken-cost",
            Cost = new ResourceCostList(),
        };
        native.Cost.costs.Add(new ResourceTuple(null!, new TestBigDouble(1.0, 0)));
        var snapshots = new AutoBuyResourceSnapshotCache(new FakeResourceReader(), (_, _) => { });
        var candidate = new ReflectionAutoBuyCandidate(native, AutoBuyCandidateKind.Structure, snapshots);

        Assert.Empty(candidate.GetCosts());
        Assert.Empty(candidate.GetCosts());
        Assert.Equal(1, native.GetPurchaseCostCalls);

        snapshots.BeginLazyEpoch();
        Assert.Empty(candidate.GetCosts());
        Assert.Equal(2, native.GetPurchaseCostCalls);
    }

    [Fact]
    public void UnchangedRegistryIdentity_ReusesExistingCandidateWrapper()
    {
        var index = new AutoBuyCandidateIndex();
        var candidate = Candidate("stable", AutoBuyCandidateKind.Structure, "mana", available: true);
        index.Reconcile(new[] { candidate });
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.True(index.TryReuseObservedCandidate(
            candidate.Uuid,
            candidate.NativeIdentity,
            AutoBuyCandidateKind.Structure,
            seen,
            out var epochChanged));
        Assert.False(epochChanged);
        Assert.Contains(candidate.Uuid, seen);
        Assert.True(index.TryGetCandidate(candidate.Uuid, out var retained));
        Assert.Same(candidate, retained);
    }

    [Fact]
    public void StructureQueueSettlement_IsBoundedAndBlocksUntilAllCostsRefresh()
    {
        var index = new AutoBuyCandidateIndex();
        var candidates = new List<DirtyCandidate>();
        for (var i = 0; i < 12; i++)
        {
            candidates.Add(Candidate($"structure-{i:00}", AutoBuyCandidateKind.Structure, "mana", available: true));
        }

        index.Reconcile(candidates);
        foreach (var candidate in candidates)
        {
            index.InvalidateQueue(candidate.NativeIdentity, AutoBuyCandidateKind.Structure);
        }
        index.PrepareEvaluation(
            new AutoBuyEvaluationRequest(20, true, true),
            lifecycleWorkLimit: 3,
            activeRefreshCount: 0,
            slowRefreshCount: 0);
        Assert.True(index.SettlementValidationPending);

        while (index.SettlementValidationPending)
        {
            index.PrepareEvaluation(
                new AutoBuyEvaluationRequest(20, true, true),
                lifecycleWorkLimit: 3,
                activeRefreshCount: 0,
                slowRefreshCount: 0);
        }

        Assert.All(candidates, candidate =>
        {
            Assert.True(index.TryGetDirtyReasons(candidate.Uuid, out var dirty));
            Assert.True((dirty & AutoBuyDirtyReason.CostDirty) != 0);
        });
    }

    [Fact]
    public void CompletionSignal_PreventsCachedMutationUntilSettlementFinishes()
    {
        var candidate = new EngineCandidate("candidate", 1.0);
        var catalog = new IncrementalCatalog(candidate, new EngineCandidate("other", 2.0));
        var config = ActiveConfig("candidate");
        using var engine = new AutoBuyEngine(
            config,
            catalog,
            new ReservePolicy(config),
            new ManualLogSource(),
            _ => 0.0,
            _ => 0.0);

        engine.NotifyNativeCompletion();
        engine.Tick(config.AutoBuyIntervalSeconds.Value);
        Assert.Equal(0, candidate.PurchaseCalls);

        catalog.SettlementPending = false;
        engine.Tick(0.0f);
        Assert.Equal(1, candidate.PurchaseCalls);
    }

    [Fact]
    public void LargeCompletionSettlement_WithEngineEvaluation_ReachesZeroAndResumesMutation()
    {
        AssertLargeSettlementResumes(
            AutoBuyCandidateKind.Structure,
            (engine, _) => engine.NotifyNativeCompletion());
    }

    [Fact]
    public void LargeStructureQueueSettlement_WithEngineEvaluation_ReachesZeroAndResumesMutation()
    {
        AssertLargeSettlementResumes(
            AutoBuyCandidateKind.Structure,
            (engine, target) => engine.NotifyStructureQueueChanged(target.NativeIdentity));
    }

    [Fact]
    public void ManualUpgradeQueueOutsideFirstSlice_RefreshesCostAndResumesMutation()
    {
        AssertLargeSettlementResumes(
            AutoBuyCandidateKind.Upgrade,
            (engine, target) =>
            {
                target.QueuedLevels = 1;
                engine.NotifyUpgradeQueueChanged(target.NativeIdentity);
            });
    }

    private static void AssertLargeSettlementResumes(
        AutoBuyCandidateKind kind,
        Action<AutoBuyEngine, DirtyCandidate> signal)
    {
        var candidates = new List<DirtyCandidate>();
        for (var i = 0; i < 65; i++)
        {
            candidates.Add(Candidate($"candidate-{i:00}", kind, "mana", available: true));
        }

        var target = candidates[candidates.Count - 1];
        using var catalog = new IndexedCatalog(candidates);
        var config = ActiveConfig(target.Uuid);
        using var engine = new AutoBuyEngine(
            config,
            catalog,
            new ReservePolicy(config),
            new ManualLogSource(),
            _ => 0.0,
            _ => 0.0);

        signal(engine, target);
        for (var frame = 0; frame < 10 && target.PurchaseCalls == 0; frame++)
        {
            engine.Tick(frame == 0 ? config.AutoBuyIntervalSeconds.Value : 0.0f);
        }

        Assert.False(catalog.Index.SettlementValidationPending);
        Assert.Equal(1, target.PurchaseCalls);
    }

    private static AutomataConfig ActiveConfig(string allowedUuid)
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.UpgradeAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.AllowedAutoBuyUuids.Value = allowedUuid;
        config.LeaveQueueSlots.Value = 0;
        return config;
    }

    private static DirtyCandidate Candidate(
        string uuid,
        AutoBuyCandidateKind kind,
        string resourceId,
        bool available)
    {
        return new DirtyCandidate(uuid, kind, resourceId, available);
    }

    private static void PrimeDependencies(AutoBuyCandidateIndex index, params DirtyCandidate[] candidates)
    {
        index.Reconcile(candidates);
        var batch = index.PrepareEvaluation(
            new AutoBuyEvaluationRequest(100, true, true),
            lifecycleWorkLimit: 100,
            activeRefreshCount: 0,
            slowRefreshCount: 0);
        foreach (var candidate in batch.DirtyCandidates)
        {
            index.CompleteCandidateEvaluation(candidate);
        }
    }

    private sealed class FakeResourceReader : IAutoBuyResourceSnapshotReader
    {
        public int ReadCalls { get; private set; }

        public BigAmount TrueQuantity { get; set; } = new BigAmount(1.0, 3);

        public bool TryRead(
            AutoBuyResourceDefinition definition,
            long epoch,
            out AutoBuyResourceSnapshot snapshot)
        {
            ReadCalls++;
            if (definition.ResourceId == "missing")
            {
                snapshot = default;
                return false;
            }

            snapshot = new AutoBuyResourceSnapshot(
                definition.ResourceId,
                definition.NativeResource,
                new BigAmount(1.0, 3),
                TrueQuantity,
                new BigAmount(1.0, 2),
                new BigAmount(1.0, 2),
                new BigAmount(1.0, 4),
                true,
                epoch);
            return true;
        }
    }

    private sealed class DirtyCandidate :
        IAutoBuyCandidate,
        IAutoBuyLifecycleCandidate,
        IAutoBuyNativeIdentity,
        IAutoBuyDirtyCandidate
    {
        private readonly AutoBuyCandidateSnapshot _snapshot;
        private readonly List<string> _dependencies;

        public DirtyCandidate(string uuid, AutoBuyCandidateKind kind, string resourceId, bool available)
        {
            Uuid = uuid;
            Available = available;
            NativeIdentity = new object();
            _snapshot = new AutoBuyCandidateSnapshot(this, uuid, uuid, kind, kind.ToString());
            _dependencies = new List<string> { resourceId };
        }

        public string Uuid { get; }

        public bool Available { get; set; }

        public int QueuedLevels { get; set; }

        public bool CostsResolved { get; set; } = true;

        public int PurchaseCalls { get; private set; }

        public object NativeIdentity { get; }

        public IReadOnlyList<string> ResourceDependencies => _dependencies;

        public bool HasResolvedCosts => CostsResolved;

        public AutoBuyCandidateSnapshot Snapshot() => _snapshot;

        public bool IsAvailable() => Available;

        public bool CanPurchase(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public IReadOnlyList<ResourceAdmissionCost> GetCosts() => Array.Empty<ResourceAdmissionCost>();

        public bool TryPurchaseOne(out string reason)
        {
            PurchaseCalls++;
            reason = string.Empty;
            return true;
        }

        public bool TryGetLifecycleEvidence(out AutoBuyLifecycleEvidence evidence, out string reason)
        {
            evidence = new AutoBuyLifecycleEvidence(Available, 0, QueuedLevels, false, false, false);
            reason = string.Empty;
            return true;
        }

        public void MarkDirty(AutoBuyDirtyReason reasons)
        {
        }

        public void SetLifecycleEvidence(AutoBuyLifecycleEvidence evidence)
        {
        }
    }

    private sealed class IncrementalCatalog : IAutoBuyCatalog, IAutoBuyIncrementalCatalog
    {
        private readonly EngineCandidate _first;
        private readonly EngineCandidate _second;
        private readonly IAutoBuyCandidate[] _active;

        public IncrementalCatalog(EngineCandidate first, EngineCandidate second)
        {
            _first = first;
            _second = second;
            _active = new IAutoBuyCandidate[] { first, second };
        }

        public int EvaluationCalls { get; private set; }

        public IEnumerable<IAutoBuyCandidate> Discover() => _active;

        public AutoBuyEvaluationBatch BeginEvaluation(AutoBuyEvaluationRequest request)
        {
            EvaluationCalls++;
            IReadOnlyList<IAutoBuyCandidate> dirty = EvaluationCalls == 1
                ? _active
                : new IAutoBuyCandidate[] { _first };
            return new AutoBuyEvaluationBatch(_active, dirty, null, SettlementPending);
        }

        public int PurchaseAttemptNotifications { get; private set; }

        public int RemainingRoom { get; set; } = 10;

        public int BulkLevels { get; set; } = 1;

        public int ActionMultiplier { get; set; } = 1;

        public bool SettlementPending { get; set; }

        public void CompleteCandidateEvaluation(IAutoBuyCandidate candidate, bool policyExcluded)
        {
        }

        public void InvalidatePolicy()
        {
        }

        public void BeginMutationEvaluation()
        {
        }

        public void NotifyPurchaseAttempted(IAutoBuyCandidate candidate)
        {
            PurchaseAttemptNotifications++;
            if (ReferenceEquals(candidate, _first))
            {
                _first.Cost = 100.0;
            }
        }

        public void NotifyStructureQueueChanged(object nativeIdentity)
        {
        }

        public void NotifyUpgradeQueueChanged(object nativeIdentity)
        {
        }

        public void NotifyNativeCompletion()
        {
            SettlementPending = true;
        }

        public void InvalidateLifecycle()
        {
        }

        public bool TryGetRemainingQueueRoom(out int remainingRoom)
        {
            remainingRoom = RemainingRoom;
            return true;
        }

        public bool TryGetBulkDevelopment(out int levels)
        {
            levels = BulkLevels;
            return true;
        }

        public bool TryGetActionMultiplier(out int multiplier)
        {
            multiplier = ActionMultiplier;
            return true;
        }

        public void Dispose()
        {
        }
    }

    private sealed class IndexedCatalog : IAutoBuyCatalog, IAutoBuyIncrementalCatalog
    {
        private readonly IReadOnlyList<IAutoBuyCandidate> _candidates;

        public IndexedCatalog(IReadOnlyList<DirtyCandidate> candidates)
        {
            _candidates = candidates;
            Index.Reconcile(candidates);
            var initial = Index.PrepareEvaluation(
                new AutoBuyEvaluationRequest(1024, true, true),
                lifecycleWorkLimit: 1024,
                activeRefreshCount: 0,
                slowRefreshCount: 0);
            foreach (var candidate in initial.DirtyCandidates)
            {
                Index.CompleteCandidateEvaluation(candidate);
            }
        }

        public AutoBuyCandidateIndex Index { get; } = new AutoBuyCandidateIndex();

        public IEnumerable<IAutoBuyCandidate> Discover() => _candidates;

        public AutoBuyEvaluationBatch BeginEvaluation(AutoBuyEvaluationRequest request)
        {
            var batch = Index.PrepareEvaluation(
                request,
                lifecycleWorkLimit: 32,
                activeRefreshCount: 0,
                slowRefreshCount: 0);
            return new AutoBuyEvaluationBatch(
                batch.ActiveCandidates,
                batch.DirtyCandidates,
                batch.FirstExcludedCandidate,
                Index.SettlementValidationPending);
        }

        public void CompleteCandidateEvaluation(IAutoBuyCandidate candidate, bool policyExcluded)
        {
            Index.CompleteCandidateEvaluation(candidate, policyExcluded);
        }

        public void InvalidatePolicy() => Index.InvalidatePolicy();

        public void BeginMutationEvaluation()
        {
        }

        public void NotifyPurchaseAttempted(IAutoBuyCandidate candidate) => Index.MarkPurchaseAttempted(candidate);

        public void NotifyStructureQueueChanged(object nativeIdentity) =>
            Index.InvalidateQueue(nativeIdentity, AutoBuyCandidateKind.Structure);

        public void NotifyUpgradeQueueChanged(object nativeIdentity) =>
            Index.InvalidateQueue(nativeIdentity, AutoBuyCandidateKind.Upgrade);

        public void NotifyNativeCompletion() => Index.InvalidateCompletionEffects();

        public void InvalidateLifecycle() => Index.BeginLifecycleEpoch();

        public bool TryGetRemainingQueueRoom(out int remainingRoom)
        {
            remainingRoom = 128;
            return true;
        }

        public bool TryGetBulkDevelopment(out int levels)
        {
            levels = 1;
            return true;
        }

        public bool TryGetActionMultiplier(out int multiplier)
        {
            multiplier = 1;
            return true;
        }

        public void Dispose()
        {
        }
    }

    private sealed class EngineCandidate : IAutoBuyCandidate, IAutoBuyNativeIdentity, IAutoBuyDirtyCandidate
    {
        private readonly AutoBuyCandidateSnapshot _snapshot;
        private readonly AutoBuyCandidateKind _kind;
        private double _quantity = 1_000.0;

        public EngineCandidate(
            string uuid,
            double cost,
            AutoBuyCandidateKind kind = AutoBuyCandidateKind.Upgrade)
        {
            Cost = cost;
            _kind = kind;
            NativeIdentity = new object();
            _snapshot = new AutoBuyCandidateSnapshot(
                this,
                uuid,
                uuid,
                kind,
                GetType().Name);
        }

        public double Cost { get; set; }

        public int CostReads { get; private set; }

        public int PurchaseCalls { get; private set; }

        public bool CanPurchaseValue { get; set; } = true;

        public bool CostsResolved { get; set; } = true;

        public bool PurchaseSucceeds { get; set; } = true;

        public bool RaiseQueueSignalOnPurchase { get; set; }

        public object NativeIdentity { get; }

        public IReadOnlyList<string> ResourceDependencies { get; } = new[] { "resource" };

        public bool HasResolvedCosts => CostsResolved;

        public AutoBuyCandidateSnapshot Snapshot() => _snapshot;

        public bool IsAvailable() => true;

        public bool CanPurchase(out string reason)
        {
            reason = CanPurchaseValue ? string.Empty : "native CanPurchase returned false";
            return CanPurchaseValue;
        }

        public IReadOnlyList<ResourceAdmissionCost> GetCosts()
        {
            CostReads++;
            return new[]
            {
                new ResourceAdmissionCost(
                    "resource",
                    "Resource",
                    new BigAmount(Cost, 0),
                    new BigAmount(_quantity, 0)),
            };
        }

        public bool TryPurchaseOne(out string reason)
        {
            PurchaseCalls++;
            _quantity -= Cost;
            if (RaiseQueueSignalOnPurchase)
            {
                if (_kind == AutoBuyCandidateKind.Structure)
                {
                    AutoBuyLifecycleSignal.RaiseStructureQueueChanged(NativeIdentity);
                }
                else
                {
                    AutoBuyLifecycleSignal.RaiseUpgradeQueueChanged(NativeIdentity);
                }
            }

            reason = PurchaseSucceeds ? string.Empty : "post-purchase verification failed";
            return PurchaseSucceeds;
        }

        public void MarkDirty(AutoBuyDirtyReason reasons)
        {
        }

        public void SetLifecycleEvidence(AutoBuyLifecycleEvidence evidence)
        {
        }
    }
}

internal sealed class ResourceCostList
{
    public List<ResourceTuple> costs = new List<ResourceTuple>();
}

internal struct ResourceTuple
{
    public ResourceTuple(ResourceSO resource, TestBigDouble value)
    {
        this.resource = resource;
        _value = value;
    }

    public ResourceSO resource;

    private TestBigDouble _value;

    public TestBigDouble GetValue() => _value;
}

internal sealed class ResourceSO
{
    public string uuid = string.Empty;

    public string GetGuid() => uuid;

    public string GetName() => uuid;
}

internal readonly struct TestBigDouble
{
    public TestBigDouble(double mantissa, long exponent)
    {
        this.mantissa = mantissa;
        this.exponent = exponent;
    }

    public readonly double mantissa;

    public readonly long exponent;
}

internal sealed class StructureSO
{
    public string uuid = string.Empty;

    public ResourceCostList Cost { get; set; } = new ResourceCostList();

    public int GetPurchaseCostCalls { get; private set; }

    public bool IsAvailable() => true;

    public bool CanPurchase() => true;

    public ResourceCostList GetPurchaseCost()
    {
        GetPurchaseCostCalls++;
        return Cost;
    }

    public void Purchase(bool forceOne)
    {
    }

    public int GetPurchaseLevel() => 0;

    public int GetQueuedQuantity() => 0;
}
