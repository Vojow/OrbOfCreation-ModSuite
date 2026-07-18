using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataCoordinatorTests
{
    [Fact]
    public void AutoBuyAndAutoCastShareFrameIdentityAndOnlyOneMutatesPerFrame()
    {
        var coordinator = Coordinator();
        long frame = 41;
        var buyConfig = Config();
        var castConfig = Config();
        castConfig.AutoCastMode.Value = AutoCastOperationMode.Active;
        var buyCandidate = new BuyCandidate("upgrade", AutoBuyCandidateKind.Upgrade);
        var spell = new CastCandidate();
        using var buy = BuyEngine(buyConfig, new BuyCatalog(4, buyCandidate), coordinator, () => frame);
        using var cast = CastEngine(castConfig, new CastCatalog(spell), coordinator, () => frame);

        buy.Tick(1.0f);
        cast.Tick(1.0f);

        Assert.Equal(41, coordinator.CurrentFrameIdentity);
        Assert.Equal(1, buyCandidate.PurchaseCalls + spell.FireCalls);

        frame++;
        buy.Tick(0.0f);
        cast.Tick(0.0f);

        Assert.Equal(42, coordinator.CurrentFrameIdentity);
        Assert.Equal(2, buyCandidate.PurchaseCalls + spell.FireCalls);
        Assert.Equal(1, buyCandidate.PurchaseCalls);
        Assert.Equal(1, spell.FireCalls);
    }

    [Fact]
    public void ReadAdmissionDeferralRemainsPendingAndResumesWithoutLoss()
    {
        var coordinator = Coordinator();
        var blocker = coordinator.Register("test", "read blocker");
        blocker.SetPending(true);
        long frame = 10;
        var candidate = new BuyCandidate("deferred", AutoBuyCandidateKind.Upgrade);
        var catalog = new BuyCatalog(4, candidate);
        using var engine = BuyEngine(Config(), catalog, coordinator, () => frame);

        engine.Tick(1.0f);
        Assert.Equal(0, catalog.DiscoverCalls);
        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(blocker, frame, out var lease));
        lease.Complete();
        blocker.SetPending(false);

        frame++;
        engine.Tick(0.0f);

        Assert.Equal(1, catalog.DiscoverCalls);
        Assert.Equal(1, candidate.PurchaseCalls);
        blocker.Dispose();
    }

    [Fact]
    public void DisabledEnginesRemainIdleAndClearCoordinatorWork()
    {
        var coordinator = Coordinator();
        long frame = 1;
        var buyConfig = Config();
        buyConfig.AutoBuyMode.Value = AutoBuyOperationMode.Disabled;
        var castConfig = Config();
        castConfig.AutoCastMode.Value = AutoCastOperationMode.Disabled;
        var buyCatalog = new BuyCatalog(4, new BuyCandidate("idle", AutoBuyCandidateKind.Upgrade));
        var castCatalog = new CastCatalog(new CastCandidate());
        using var buy = BuyEngine(buyConfig, buyCatalog, coordinator, () => frame);
        using var cast = CastEngine(castConfig, castCatalog, coordinator, () => frame);

        for (; frame <= 4; frame++)
        {
            buy.Tick(1.0f);
            cast.Tick(1.0f);
        }

        Assert.Equal(0, buyCatalog.DiscoverCalls);
        Assert.Equal(0, castCatalog.DiscoverCalls);
        Assert.True(coordinator.TryGetSubsystemSnapshot("OrbAutomata.AutoBuy", out var buySnapshot));
        Assert.True(coordinator.TryGetSubsystemSnapshot("OrbAutomata.AutoCast", out var castSnapshot));
        Assert.Equal(0, buySnapshot.AdmittedWorkItems);
        Assert.Equal(0, castSnapshot.AdmittedWorkItems);
    }

    [Theory]
    [InlineData(1, false, 0)]
    [InlineData(2, false, 0)]
    [InlineData(0, true, 1)]
    public void RepeatGroupKeepsInitialQueueClampAcrossMutationFrames(
        int repeatMode,
        bool respectActionMultiplier,
        int candidateKind)
    {
        var coordinator = Coordinator();
        long frame = 1;
        var config = Config();
        config.RepeatWhileAffordable.Value = false;
        config.StructureRepeatMode.Value = (AutoBuyStructureRepeatMode)repeatMode;
        config.FixedStructureLevelsPerCandidate.Value = 20;
        config.RespectActionMultiplier.Value = respectActionMultiplier;
        var candidate = new BuyCandidate("repeat", (AutoBuyCandidateKind)candidateKind);
        var catalog = new BuyCatalog(4, candidate)
        {
            BulkDevelopment = 20,
            ActionMultiplier = 20,
        };
        using var engine = BuyEngine(config, catalog, coordinator, () => frame);

        engine.Tick(1.0f);
        Assert.Equal(1, candidate.PurchaseCalls);
        frame++;
        engine.Tick(0.0f);
        Assert.Equal(2, candidate.PurchaseCalls);
        frame++;
        engine.Tick(0.0f);

        Assert.Equal(3, candidate.PurchaseCalls);
        Assert.Equal(1, catalog.DiscoverCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AffordableRepeatGroupFeedsEveryUsableSlotWithoutRescanning(int candidateKind)
    {
        var coordinator = Coordinator();
        long frame = 1;
        var config = Config();
        config.RepeatWhileAffordable.Value = true;
        config.RespectActionMultiplier.Value = false;
        config.LeaveQueueSlots.Value = 1;
        var candidate = new BuyCandidate("affordable-repeat", (AutoBuyCandidateKind)candidateKind);
        var catalog = new BuyCatalog(6, candidate)
        {
            BulkDevelopment = 2,
        };
        using var engine = BuyEngine(config, catalog, coordinator, () => frame);

        for (var expectedPurchases = 1; expectedPurchases <= 5; expectedPurchases++)
        {
            engine.Tick(expectedPurchases == 1 ? 1.0f : 0.0f);
            Assert.Equal(expectedPurchases, candidate.PurchaseCalls);
            frame++;
        }

        Assert.Equal(1, catalog.DiscoverCalls);
    }

    [Fact]
    public void LoneAffordableCandidateStillFillsTwoHundredSlots()
    {
        var coordinator = Coordinator();
        long frame = 1;
        var config = Config();
        config.RepeatWhileAffordable.Value = true;
        config.RespectActionMultiplier.Value = false;
        config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.FillAvailableQueue;
        config.LeaveQueueSlots.Value = 1;
        var selected = new BuyCandidate("only-candidate", AutoBuyCandidateKind.Structure);
        var catalog = new BuyCatalog(201, selected);
        selected.OnPurchase = () => catalog.RemainingRoom--;
        using var engine = BuyEngine(config, catalog, coordinator, () => frame);

        for (var expectedPurchases = 1; expectedPurchases <= 200; expectedPurchases++)
        {
            engine.Tick(expectedPurchases == 1 ? 1.0f : 0.0f);
            Assert.Equal(expectedPurchases, selected.PurchaseCalls);
            frame++;
        }

        Assert.Equal(200, selected.PurchaseCalls);
        Assert.Equal(1, catalog.DiscoverCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AffordableRepeatPassVisitsEveryRankedCandidateBeforeRepeating(int candidateKind)
    {
        var coordinator = Coordinator();
        long frame = 1;
        var config = Config();
        config.RepeatWhileAffordable.Value = true;
        config.RespectActionMultiplier.Value = false;
        config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.FillAvailableQueue;
        config.LeaveQueueSlots.Value = 1;
        var first = new BuyCandidate("a-first", (AutoBuyCandidateKind)candidateKind);
        var second = new BuyCandidate("b-second", (AutoBuyCandidateKind)candidateKind);
        var third = new BuyCandidate("c-third", (AutoBuyCandidateKind)candidateKind);
        var catalog = new BuyCatalog(201, first, second, third);
        first.OnPurchase = () => catalog.RemainingRoom--;
        second.OnPurchase = () => catalog.RemainingRoom--;
        third.OnPurchase = () => catalog.RemainingRoom--;
        using var engine = BuyEngine(config, catalog, coordinator, () => frame);

        engine.Tick(1.0f);
        Assert.Equal((1, 0, 0), (first.PurchaseCalls, second.PurchaseCalls, third.PurchaseCalls));

        frame++;
        engine.Tick(0.0f);
        Assert.Equal((1, 1, 0), (first.PurchaseCalls, second.PurchaseCalls, third.PurchaseCalls));

        frame++;
        engine.Tick(0.0f);
        Assert.Equal((1, 1, 1), (first.PurchaseCalls, second.PurchaseCalls, third.PurchaseCalls));

        frame++;
        engine.Tick(0.0f);

        Assert.Equal((2, 1, 1), (first.PurchaseCalls, second.PurchaseCalls, third.PurchaseCalls));
        Assert.Equal(2, catalog.DiscoverCalls);
    }

    [Fact]
    public void MutationExceptionReleasesLeaseAndAutomatedFireScope()
    {
        var coordinator = Coordinator();
        long frame = 7;
        var config = Config();
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        config.AutoCastFullCharge.Value = true;
        var spell = new CastCandidate { IsCharged = true, ThrowOnFire = true };
        using var engine = CastEngine(config, new CastCatalog(spell), coordinator, () => frame);
        var manualSignals = 0;
        void ObserveManualFire() => manualSignals++;
        AutoCastManualSignal.ManualSpellFired += ObserveManualFire;
        try
        {
            Assert.Throws<InvalidOperationException>(() => engine.Tick(1.0f));
            Assert.Equal(1, spell.HoldCalls);
            Assert.Equal(1, spell.ReleaseCalls);
            AutoCastManualSignal.NotifySpellFire();
            Assert.Equal(1, manualSignals);

            var probe = coordinator.Register(
                "test",
                "mutation probe",
                SuiteBudgetClass.HardLimited,
                SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
            probe.SetPending(true);
            frame++;
            Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(probe, frame, out var lease));
            lease.Complete();
            probe.Dispose();
        }
        finally
        {
            AutoCastManualSignal.ManualSpellFired -= ObserveManualFire;
        }
    }

    [Fact]
    public void ChargedCastAndNaturalReleaseEachUseOneMutationFrame()
    {
        var coordinator = Coordinator();
        long frame = 80;
        var config = Config();
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        config.AutoCastFullCharge.Value = true;
        var spell = new CastCandidate { IsCharged = true, IsReadyingCast = true };
        using var engine = CastEngine(config, new CastCatalog(spell), coordinator, () => frame);

        engine.Tick(1.0f);

        Assert.Equal(1, spell.HoldCalls);
        Assert.Equal(1, spell.FireCalls);
        Assert.Equal(0, spell.ReleaseCalls);

        spell.IsReadyingCast = false;
        frame++;
        var blocker = coordinator.Register(
            "test",
            "release blocker",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        blocker.SetPending(true);
        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(blocker, frame, out var blockedLease));
        blockedLease.Complete();
        blocker.SetPending(false);

        engine.Tick(0.0f);
        Assert.Equal(0, spell.ReleaseCalls);

        frame++;
        engine.Tick(0.0f);

        Assert.Equal(1, spell.ReleaseCalls);
        Assert.Equal(1, spell.FireCalls);
        Assert.True(coordinator.TryGetSubsystemSnapshot("OrbAutomata.AutoCast", out var snapshot));
        Assert.Equal(2, snapshot.NativeMutationsStarted);
        blocker.Dispose();
    }

    [Fact]
    public void ChargedCastBlocksAutoBuyMutationInSameFrame()
    {
        var coordinator = Coordinator();
        long frame = 90;
        var castConfig = Config();
        castConfig.AutoCastMode.Value = AutoCastOperationMode.Active;
        castConfig.AutoCastFullCharge.Value = true;
        var spell = new CastCandidate { IsCharged = true, IsReadyingCast = true };
        var buyCandidate = new BuyCandidate("upgrade", AutoBuyCandidateKind.Upgrade);
        using var cast = CastEngine(castConfig, new CastCatalog(spell), coordinator, () => frame);
        using var buy = BuyEngine(Config(), new BuyCatalog(4, buyCandidate), coordinator, () => frame);

        cast.Tick(1.0f);
        buy.Tick(1.0f);

        Assert.Equal(1, spell.FireCalls);
        Assert.Equal(0, buyCandidate.PurchaseCalls);

        frame++;
        cast.Tick(0.0f);
        buy.Tick(0.0f);

        Assert.Equal(1, buyCandidate.PurchaseCalls);
    }

    [Fact]
    public void DeferredCastCancelsWhenCurrentSlotIdentityChanges()
    {
        var coordinator = Coordinator();
        long frame = 20;
        var blocker = coordinator.Register(
            "test",
            "mutation blocker",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        blocker.SetPending(true);
        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(blocker, frame, out var blockerLease));
        blockerLease.Complete();
        blocker.SetPending(false);

        var config = Config();
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var original = new CastCandidate();
        var replacement = new CastCandidate();
        var catalog = new CastCatalog(original);
        using var engine = CastEngine(config, catalog, coordinator, () => frame);

        engine.Tick(1.0f);
        Assert.Equal(0, original.FireCalls);
        catalog.Replace(replacement);

        frame++;
        engine.Tick(0.0f);
        Assert.Equal(0, original.FireCalls);
        Assert.Equal(0, replacement.FireCalls);

        frame++;
        engine.Tick(0.0f);
        Assert.Equal(0, original.FireCalls);
        Assert.Equal(1, replacement.FireCalls);
        blocker.Dispose();
    }

    [Fact]
    public void LifecycleInvalidationDiscardsDeferredCastAndReplansCurrentSlot()
    {
        var coordinator = Coordinator();
        long frame = 30;
        var blocker = coordinator.Register(
            "test",
            "mutation blocker",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        blocker.SetPending(true);
        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(blocker, frame, out var blockerLease));
        blockerLease.Complete();
        blocker.SetPending(false);

        var config = Config();
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var stale = new CastCandidate();
        var current = new CastCandidate();
        var catalog = new CastCatalog(stale);
        using var engine = CastEngine(config, catalog, coordinator, () => frame);

        engine.Tick(1.0f);
        engine.InvalidateLifecycle();
        catalog.Replace(current);

        frame++;
        engine.Tick(0.0f);
        Assert.Equal(0, stale.FireCalls);
        Assert.Equal(1, current.FireCalls);
        blocker.Dispose();
    }

    [Fact]
    public void QueueWaitPollUsesReadLeaseAndLeavesMutationAdmissionAvailable()
    {
        var coordinator = Coordinator();
        long frame = 50;
        var candidate = new BuyCandidate("queue-wait", AutoBuyCandidateKind.Upgrade);
        var catalog = new BuyCatalog(1, candidate)
        {
            QueueRooms = new Queue<int>(new[] { 4, 1, 1 }),
        };
        using var engine = BuyEngine(Config(), catalog, coordinator, () => frame);

        engine.Tick(1.0f);
        Assert.Equal(0, candidate.PurchaseCalls);

        frame++;
        engine.Tick(0.1f);
        Assert.Equal(0, candidate.PurchaseCalls);

        var probe = coordinator.Register(
            "test",
            "mutation probe",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        probe.SetPending(true);
        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(probe, frame, out var lease));
        lease.Complete();
        probe.Dispose();
    }

    [Fact]
    public void MultiBuyQuarantineDropsRankedUpgradesAndAllowsLowerRankedStructure()
    {
        NativeMultiBuyScope.ResetQuarantineForTests();
        GlobalVariables.MultiBuy = new IntVariable { Value = 7 };
        try
        {
            var coordinator = Coordinator();
            long frame = 60;
            var log = new ManualLogSource();
            var upgrade = new QuarantiningUpgradeCandidate("a-upgrade");
            var structure = new BuyCandidate("z-structure", AutoBuyCandidateKind.Structure);
            using var engine = BuyEngine(
                Config(),
                new BuyCatalog(4, upgrade, structure),
                coordinator,
                () => frame,
                log,
                _ => 0.0);

            engine.Tick(1.0f);

            Assert.True(
                NativeMultiBuyScope.IsMutationQuarantined,
                $"UpgradeCalls={upgrade.PurchaseCalls}; StructureCalls={structure.PurchaseCalls}; MultiBuySetCalls={GlobalVariables.MultiBuy.SetCalls}");
            Assert.Equal(1, upgrade.PurchaseCalls);
            Assert.Equal(0, structure.PurchaseCalls);
            var setterCallsAfterQuarantine = GlobalVariables.MultiBuy.SetCalls;
            Assert.Equal(2, setterCallsAfterQuarantine);

            // Lifecycle invalidation must not clear the process quarantine.
            engine.InvalidateLifecycle();
            frame++;
            engine.Tick(0.0f);

            Assert.True(NativeMultiBuyScope.IsMutationQuarantined);
            Assert.Equal(1, upgrade.PurchaseCalls);
            Assert.Equal(setterCallsAfterQuarantine, GlobalVariables.MultiBuy.SetCalls);
            Assert.Equal(1, structure.PurchaseCalls);
            Assert.True(coordinator.TryGetSubsystemSnapshot("OrbAutomata.AutoBuy", out var snapshot));
            Assert.Equal(2, snapshot.NativeMutationsStarted);
            Assert.Single(
                log.Entries,
                entry => entry?.ToString()?.Contains(
                    "removed automated Upgrades from admission and ranking",
                    StringComparison.Ordinal) == true);
        }
        finally
        {
            NativeMultiBuyScope.ResetQuarantineForTests();
            GlobalVariables.MultiBuy = new IntVariable();
        }
    }

    [Fact]
    public void MultiBuyQuarantinePurgesLargeCacheOnceThenUsesConstantTimeTickCheck()
    {
        NativeMultiBuyScope.ResetQuarantineForTests();
        GlobalVariables.MultiBuy = new IntVariable { Value = 7 };
        try
        {
            const int upgradeCount = 256;
            var coordinator = Coordinator();
            long frame = 100;
            var log = new ManualLogSource();
            var quarantineUpgrade = new QuarantiningUpgradeCandidate("a-quarantine");
            var replayedUpgrade = new BuyCandidate("u-000", AutoBuyCandidateKind.Upgrade);
            var structure = new OneShotStructureCandidate("z-structure");
            var candidates = new List<IAutoBuyCandidate>(upgradeCount + 1)
            {
                quarantineUpgrade,
                replayedUpgrade,
            };
            for (var i = 1; i < upgradeCount - 1; i++)
            {
                candidates.Add(new BuyCandidate($"u-{i:000}", AutoBuyCandidateKind.Upgrade));
            }
            candidates.Add(structure);

            var catalog = new ReplayIncrementalCatalog(
                candidates.ToArray(),
                new IAutoBuyCandidate[] { replayedUpgrade, structure });
            var config = Config();
            config.RepeatWhileAffordable.Value = false;
            using var engine = BuyEngine(
                config,
                catalog,
                coordinator,
                () => frame,
                log,
                _ => 0.0);

            engine.Tick(1.0f);

            Assert.Equal(1, engine.UpgradeQuarantinePurgePasses);
            Assert.Equal(upgradeCount + 1, engine.UpgradeQuarantineCacheEntriesInspected);
            Assert.Equal(upgradeCount, engine.UpgradeQuarantineDecisionsRemoved);

            frame++;
            engine.Tick(0.0f);
            Assert.Equal(1, structure.PurchaseCalls);

            // Settle the purchased Structure, then exercise the steady-state
            // quarantine check across many active scans. The catalog
            // deliberately replays an Upgrade as dirty to verify it cannot
            // re-enter the cached ranking.
            frame++;
            engine.Tick(0.0f);
            for (var i = 0; i < 64; i++)
            {
                frame++;
                engine.Tick(1.0f);
            }

            Assert.Equal(1, engine.UpgradeQuarantinePurgePasses);
            Assert.Equal(upgradeCount + 1, engine.UpgradeQuarantineCacheEntriesInspected);
            Assert.Equal(upgradeCount, engine.UpgradeQuarantineDecisionsRemoved);
            Assert.Equal(1, quarantineUpgrade.PurchaseCalls);
            Assert.Equal(0, replayedUpgrade.PurchaseCalls);
            Assert.Equal(2, GlobalVariables.MultiBuy.SetCalls);
            Assert.True(catalog.EvaluationCalls >= 66);
            Assert.True(coordinator.TryGetSubsystemSnapshot("OrbAutomata.AutoBuy", out var snapshot));
            Assert.Equal(2, snapshot.NativeMutationsStarted);
            Assert.Single(
                log.Entries,
                entry => entry?.ToString()?.Contains(
                    "removed automated Upgrades from admission and ranking",
                    StringComparison.Ordinal) == true);
        }
        finally
        {
            NativeMultiBuyScope.ResetQuarantineForTests();
            GlobalVariables.MultiBuy = new IntVariable();
        }
    }

    private static SuitePerformanceCoordinator Coordinator() =>
        new(StopwatchPerformanceClock.Instance, 1000.0, 1000.0);

    private static AutomataConfig Config()
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.UpgradeAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        return config;
    }

    private static AutoBuyEngine BuyEngine(
        AutomataConfig config,
        IAutoBuyCatalog catalog,
        SuitePerformanceCoordinator coordinator,
        Func<long> frameIdentity,
        ManualLogSource? log = null,
        Func<Stopwatch, double>? readElapsedMilliseconds = null) =>
        new(
            config,
            catalog,
            new ReservePolicy(config),
            log ?? new ManualLogSource(),
            readElapsedMilliseconds: readElapsedMilliseconds,
            coordinator: coordinator,
            readFrameIdentity: frameIdentity);

    private static AutoCastEngine CastEngine(
        AutomataConfig config,
        IAutoCastCatalog catalog,
        SuitePerformanceCoordinator coordinator,
        Func<long> frameIdentity) =>
        new(
            config,
            catalog,
            new ReservePolicy(config),
            new ResourceFullnessPolicy(),
            new ManualLogSource(),
            () => true,
            coordinator,
            frameIdentity);

    private sealed class BuyCatalog : IAutoBuyCatalog
    {
        private readonly IAutoBuyCandidate[] _candidates;

        public BuyCatalog(int queueRoom, params IAutoBuyCandidate[] candidates)
        {
            RemainingRoom = queueRoom;
            _candidates = candidates;
        }

        public int RemainingRoom { get; set; }

        public int BulkDevelopment { get; set; } = 1;

        public int ActionMultiplier { get; set; } = 1;

        public Queue<int>? QueueRooms { get; set; }

        public int DiscoverCalls { get; private set; }

        public IEnumerable<IAutoBuyCandidate> Discover()
        {
            DiscoverCalls++;
            return _candidates;
        }

        public bool TryGetRemainingQueueRoom(out int remainingRoom)
        {
            remainingRoom = QueueRooms is { Count: > 0 } ? QueueRooms.Dequeue() : RemainingRoom;
            return true;
        }

        public bool TryGetBulkDevelopment(out int levels)
        {
            levels = BulkDevelopment;
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

    private sealed class BuyCandidate : IAutoBuyCandidate
    {
        private readonly AutoBuyCandidateSnapshot _snapshot;

        public BuyCandidate(string uuid, AutoBuyCandidateKind kind)
        {
            _snapshot = new AutoBuyCandidateSnapshot(this, uuid, uuid, kind, GetType().Name);
        }

        public int PurchaseCalls { get; private set; }

        public Action? OnPurchase { get; set; }

        public AutoBuyCandidateSnapshot Snapshot() => _snapshot;

        public bool IsAvailable() => true;

        public bool CanPurchase(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public IReadOnlyList<ResourceAdmissionCost> GetCosts() => Array.Empty<ResourceAdmissionCost>();

        public bool TryPurchaseOne(out string reason)
        {
            PurchaseCalls++;
            OnPurchase?.Invoke();
            reason = string.Empty;
            return true;
        }
    }

    private sealed class QuarantiningUpgradeCandidate : IAutoBuyCandidate
    {
        private readonly AutoBuyCandidateSnapshot _snapshot;

        public QuarantiningUpgradeCandidate(string uuid)
        {
            _snapshot = new AutoBuyCandidateSnapshot(
                this,
                uuid,
                uuid,
                AutoBuyCandidateKind.Upgrade,
                GetType().Name);
        }

        public int PurchaseCalls { get; private set; }

        public AutoBuyCandidateSnapshot Snapshot() => _snapshot;

        public bool IsAvailable() => true;

        public bool CanPurchase(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public IReadOnlyList<ResourceAdmissionCost> GetCosts() => Array.Empty<ResourceAdmissionCost>();

        public bool TryPurchaseOne(out string reason)
        {
            PurchaseCalls++;
            if (!NativeMultiBuyScope.TryEnterOne(out var scope, out reason))
            {
                return false;
            }

            GlobalVariables.MultiBuy.ThrowBeforeWriteFor = 7;
            scope.Dispose();
            reason = "forced unverified restoration";
            return false;
        }
    }

    private sealed class OneShotStructureCandidate : IAutoBuyCandidate
    {
        private readonly AutoBuyCandidateSnapshot _snapshot;

        public OneShotStructureCandidate(string uuid)
        {
            _snapshot = new AutoBuyCandidateSnapshot(
                this,
                uuid,
                uuid,
                AutoBuyCandidateKind.Structure,
                GetType().Name);
        }

        public int PurchaseCalls { get; private set; }

        public AutoBuyCandidateSnapshot Snapshot() => _snapshot;

        public bool IsAvailable() => PurchaseCalls == 0;

        public bool CanPurchase(out string reason)
        {
            reason = string.Empty;
            return PurchaseCalls == 0;
        }

        public IReadOnlyList<ResourceAdmissionCost> GetCosts() => Array.Empty<ResourceAdmissionCost>();

        public bool TryPurchaseOne(out string reason)
        {
            PurchaseCalls++;
            reason = string.Empty;
            return true;
        }
    }

    private sealed class ReplayIncrementalCatalog : IAutoBuyCatalog, IAutoBuyIncrementalCatalog
    {
        private readonly IAutoBuyCandidate[] _active;
        private readonly IAutoBuyCandidate[] _steadyDirty;

        public ReplayIncrementalCatalog(
            IAutoBuyCandidate[] active,
            IAutoBuyCandidate[] steadyDirty)
        {
            _active = active;
            _steadyDirty = steadyDirty;
        }

        public int EvaluationCalls { get; private set; }

        public IEnumerable<IAutoBuyCandidate> Discover() => _active;

        public AutoBuyEvaluationBatch BeginEvaluation(AutoBuyEvaluationRequest request)
        {
            EvaluationCalls++;
            return new AutoBuyEvaluationBatch(
                _active,
                EvaluationCalls == 1 ? _active : _steadyDirty,
                null,
                false);
        }

        public void CompleteCandidateEvaluation(
            IAutoBuyCandidate candidate,
            bool suppressResourceTracking,
            bool policyExcluded)
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
        }

        public void CompleteMutationGroup()
        {
        }

        public void NotifyStructureQueueChanged(object nativeIdentity)
        {
        }

        public void NotifyUpgradeQueueChanged(object nativeIdentity)
        {
        }

        public void NotifyNativeCompletion()
        {
        }

        public void InvalidateLifecycle()
        {
        }

        public bool TryGetRemainingQueueRoom(out int remainingRoom)
        {
            remainingRoom = 4;
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

    private sealed class CastCatalog : IAutoCastCatalog
    {
        private IAutoCastCandidate[] _candidates;

        public CastCatalog(params IAutoCastCandidate[] candidates)
        {
            _candidates = candidates;
        }

        public int DiscoverCalls { get; private set; }

        public IReadOnlyList<IAutoCastCandidate> DiscoverActiveLoadout()
        {
            DiscoverCalls++;
            return _candidates;
        }

        public void Replace(params IAutoCastCandidate[] candidates)
        {
            _candidates = candidates;
        }

        public bool IsNativeCastBusy() => false;

        public bool IsTargeting() => false;

        public void Dispose()
        {
        }
    }

    private sealed class CastCandidate : IAutoCastCandidate
    {
        private readonly object _nativeIdentity = new object();

        public int SlotIndex => 0;

        public string DisplayName => "spell";

        public AutoCastSpellKind Kind => AutoCastSpellKind.Instant;

        public bool IsEmpty => false;

        public bool IsCharged { get; set; }

        public bool IsCasting => false;

        public bool IsReadyingCast { get; set; }

        public bool ThrowOnFire { get; set; }

        public int FireCalls { get; private set; }

        public int HoldCalls { get; private set; }

        public int ReleaseCalls { get; private set; }

        public bool CanCast(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public bool TryGetImmediateCosts(out IReadOnlyList<ResourceAdmissionCost> costs)
        {
            costs = Array.Empty<ResourceAdmissionCost>();
            return true;
        }

        public bool TryGetDrainCosts(out IReadOnlyList<ResourceAdmissionCost> costs)
        {
            costs = Array.Empty<ResourceAdmissionCost>();
            return true;
        }

        public bool HasValidTargets(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public bool TryFireAndResolveTargets(out string reason)
        {
            using (AutoCastManualSignal.EnterAutomatedFire())
            {
                AutoCastManualSignal.NotifySpellFire();
                if (ThrowOnFire)
                {
                    throw new InvalidOperationException("simulated fire failure");
                }

                FireCalls++;
            }

            reason = string.Empty;
            return true;
        }

        public bool TryGetIdentity(out AutoCastCandidateIdentity identity, out string reason)
        {
            identity = new AutoCastCandidateIdentity(DisplayName, _nativeIdentity, GetType(), SlotIndex);
            reason = string.Empty;
            return true;
        }

        public bool TrySetChargeHold(bool isHolding, out string reason)
        {
            if (isHolding) HoldCalls++;
            else ReleaseCalls++;
            reason = string.Empty;
            return true;
        }
    }
}
