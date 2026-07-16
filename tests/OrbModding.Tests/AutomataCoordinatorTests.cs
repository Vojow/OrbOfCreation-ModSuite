using System;
using System.Collections.Generic;
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

    [Fact]
    public void MutationExceptionReleasesLeaseAndAutomatedFireScope()
    {
        var coordinator = Coordinator();
        long frame = 7;
        var config = Config();
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var spell = new CastCandidate { ThrowOnFire = true };
        using var engine = CastEngine(config, new CastCatalog(spell), coordinator, () => frame);
        var manualSignals = 0;
        void ObserveManualFire() => manualSignals++;
        AutoCastManualSignal.ManualSpellFired += ObserveManualFire;
        try
        {
            Assert.Throws<InvalidOperationException>(() => engine.Tick(1.0f));
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
                log);

            engine.Tick(1.0f);

            Assert.True(NativeMultiBuyScope.IsMutationQuarantined);
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
        ManualLogSource? log = null) =>
        new(
            config,
            catalog,
            new ReservePolicy(config),
            log ?? new ManualLogSource(),
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
        private readonly int _queueRoom;
        private readonly IAutoBuyCandidate[] _candidates;

        public BuyCatalog(int queueRoom, params IAutoBuyCandidate[] candidates)
        {
            _queueRoom = queueRoom;
            _candidates = candidates;
        }

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
            remainingRoom = QueueRooms is { Count: > 0 } ? QueueRooms.Dequeue() : _queueRoom;
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

        public bool IsCharged => false;

        public bool IsCasting => false;

        public bool ThrowOnFire { get; set; }

        public int FireCalls { get; private set; }

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
    }
}
