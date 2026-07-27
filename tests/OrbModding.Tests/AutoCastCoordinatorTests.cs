using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoCastCoordinatorTests
{
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
            Assert.True(coordinator.TryGetSubsystemSnapshot("OrbAutomata.AutoCast", out var failed));
            Assert.Equal(1, failed.FailedWorkItems);
            Assert.Equal(
                1,
                Assert.Single(
                    coordinator.GetRegistrationSnapshots(),
                    item => item.Subsystem == "OrbAutomata.AutoCast" &&
                            item.ExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNativeMutation)
                    .TotalOperations);
            Assert.Equal(3, failed.NativeCallsAttempted);
            Assert.Equal(3, failed.NativeMutationAttempts);
            Assert.Equal(2, failed.NativeMutationsCommitted);

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
        Assert.Equal(
            2,
            Assert.Single(
                coordinator.GetRegistrationSnapshots(),
                item => item.Subsystem == "OrbAutomata.AutoCast" &&
                        item.ExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNativeMutation)
                .TotalOperations);
        blocker.Dispose();
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
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
    [Trait("Category", "HeadlessE2E")]
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

    private static SuitePerformanceCoordinator Coordinator() =>
        new(StopwatchPerformanceClock.Instance, 1000.0, 1000.0);

    private static BepInExAutomataConfiguration Config() =>
        BepInExAutomataConfiguration.Bind(new ConfigFile());

    private static AutoCastEngine CastEngine(
        BepInExAutomataConfiguration config,
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

    private sealed class CastCatalog : IAutoCastCatalog
    {
        private IAutoCastCandidate[] _candidates;

        public CastCatalog(params IAutoCastCandidate[] candidates) => _candidates = candidates;

        public IReadOnlyList<IAutoCastCandidate> DiscoverActiveLoadout() => _candidates;

        public void Replace(params IAutoCastCandidate[] candidates) => _candidates = candidates;

        public bool IsNativeCastBusy() => false;
        public bool IsTargeting() => false;
        public void Dispose() { }
    }

    private sealed class CastCandidate : IAutoCastCandidate
    {
        private readonly object _nativeIdentity = new();

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
                    throw new InvalidOperationException("simulated fire failure");
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
