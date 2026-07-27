using System;
using System.Linq;
using OrbMentor;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class MentorCoordinatorTests
{
    [Fact]
    public void CooperativeSliceReceivesOnlyRemainingSharedSoftBudget()
    {
        var clock = new ManualClock();
        var coordinator = new SuitePerformanceCoordinator(clock, 0.75, 1.0);
        long frame = 1;
        using var earlier = coordinator.Register("test", "earlier");
        using var mentor = new MentorCoordinatorWork(coordinator, () => frame);
        earlier.SetPending(true);
        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(earlier, frame, out var lease));
        clock.Advance(0.6);
        lease.Complete();
        earlier.SetPending(false);

        mentor.SetState(true, cooperativePending: true, mutationPending: false);
        var observed = -1.0;
        Assert.True(mentor.TryRunCooperative(remaining =>
        {
            observed = remaining;
            return 0;
        }));

        Assert.Equal(0.15, observed, 12);
    }

    [Theory]
    [InlineData(2.0, 0.15, 0.15)]
    [InlineData(0.1, 0.75, 0.1)]
    [InlineData(0.01, 0.75, 0.1)]
    [InlineData(1.0, 0.0, 0.0)]
    [InlineData(1.0, -1.0, 0.0)]
    public void CooperativeBudgetClampsConfiguredAndRemainingWithoutReapplyingFloor(
        double configured,
        double remaining,
        double expected)
    {
        Assert.Equal(
            expected,
            MentorRuntime.EffectiveCooperativeBudgetMilliseconds(configured, remaining),
            12);
    }

    [Fact]
    public void ExhaustedSoftBudgetDoesNotTouchPendingMentorWorkAndNextFrameResumesExactXp()
    {
        var clock = new ManualClock();
        var coordinator = new SuitePerformanceCoordinator(clock, 0.75, 1.0);
        long frame = 1;
        using var earlier = coordinator.Register("test", "earlier");
        using var mentor = new MentorCoordinatorWork(coordinator, () => frame);
        var engine = new MentorEngine();
        engine.Consolidate(new MentorGrant("first", new MentorAmount(7, 4)));
        engine.Consolidate(new MentorGrant("second", new MentorAmount(9, 5)));

        earlier.SetPending(true);
        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(earlier, frame, out var lease));
        clock.Advance(0.75);
        lease.Complete();
        earlier.SetPending(false);
        mentor.SetState(true, cooperativePending: true, mutationPending: false);
        Assert.False(mentor.TryRunCooperative(_ =>
        {
            Assert.Fail("No Mentor work may run after the shared soft budget is exhausted.");
            return 0;
        }));
        Assert.True(engine.TryPeek(out var retained));
        Assert.Equal("first", retained.Uuid);
        Assert.Equal(new MentorAmount(7, 4), retained.Amount);

        frame++;
        mentor.SetState(true, cooperativePending: true, mutationPending: false);
        Assert.True(mentor.TryRunCooperative(_ =>
        {
            Assert.True(engine.TryPeek(out var first));
            Assert.Equal(new MentorAmount(7, 4), first.Amount);
            Assert.True(engine.Complete(first.Uuid));
            return 1;
        }));
        Assert.True(engine.TryPeek(out var stillPending));
        Assert.Equal("second", stillPending.Uuid);
        Assert.Equal(new MentorAmount(9, 5), stillPending.Amount);

        frame++;
        mentor.SetState(true, cooperativePending: true, mutationPending: false);
        Assert.True(mentor.TryRunCooperative(_ =>
        {
            Assert.True(engine.TryPeek(out var second));
            Assert.Equal(new MentorAmount(9, 5), second.Amount);
            Assert.True(engine.Complete(second.Uuid));
            return 1;
        }));
        Assert.False(engine.TryPeek(out _));
    }

    [Fact]
    public void AutomataMutationAndMentorGrantShareOneFrameThenMentorProgresses()
    {
        var coordinator = Coordinator();
        long frame = 41;
        var automataIdentity = SuitePerformanceWorkIdentities.AutoCastMutation;
        using var automata = coordinator.Register(
            automataIdentity.Subsystem,
            automataIdentity.WorkName,
            automataIdentity.BudgetClass,
            automataIdentity.ExecutionKind);
        automata.SetPending(true);
        using var mentor = new MentorCoordinatorWork(coordinator, () => frame);
        mentor.SetState(true, cooperativePending: false, mutationPending: true);
        var engine = new MentorEngine();
        engine.Consolidate(new MentorGrant("recipient", new MentorAmount(9, 3)));

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(automata, frame, out var automataMutation));
        automataMutation.Complete();
        var grants = 0;
        Assert.False(mentor.TryRunMutation(() =>
        {
            Assert.True(engine.TryPeek(out var pendingGrant));
            grants++;
            engine.Complete(pendingGrant.Uuid);
            return 1;
        }));
        Assert.Equal(0, grants);
        Assert.True(engine.TryPeek(out var deniedGrant));
        Assert.Equal(9, deniedGrant.Amount.Mantissa, 12);
        Assert.Equal(3, deniedGrant.Amount.Exponent);
        Assert.Equal(frame, coordinator.CurrentFrameIdentity);

        automata.SetPending(false);
        frame++;
        Assert.True(mentor.TryRunMutation(() =>
        {
            Assert.True(engine.TryPeek(out var pendingGrant));
            grants++;
            engine.Complete(pendingGrant.Uuid);
            return 1;
        }));
        Assert.Equal(1, grants);
        Assert.False(engine.TryPeek(out _));
        Assert.Equal(frame, coordinator.CurrentFrameIdentity);
    }

    [Fact]
    public void CooperativeDenialRetainsExactCapturedXp()
    {
        var coordinator = Coordinator();
        long frame = 7;
        using var blocker = coordinator.Register("test", "earlier read");
        blocker.SetPending(true);
        using var mentor = new MentorCoordinatorWork(coordinator, () => frame);
        var pending = new MentorPendingWork();
        Assert.Equal(MentorCaptureResult.Added, pending.Captures.Capture(
            new MentorCaptureKey(new object(), "source", 5, true, 1),
            new MentorAmount(7, 4)));
        mentor.SetState(true, cooperativePending: true, mutationPending: false);

        var callbacks = 0;
        Assert.False(mentor.TryRunCooperative(() => { callbacks++; return 1; }));
        Assert.Equal(0, callbacks);
        Assert.Equal(1, pending.Captures.EventCount);
        Assert.True(pending.Captures.TryTake(out var captured));
        Assert.Equal(7, captured.Amount.Mantissa, 12);
        Assert.Equal(4, captured.Amount.Exponent);
    }

    [Fact]
    public void LargeEvidenceResolutionResumesAcrossSixteenOperationLeases()
    {
        var coordinator = Coordinator();
        long frame = 100;
        using var mentor = new MentorCoordinatorWork(coordinator, () => frame);
        var recipes = Enumerable.Range(0, 40)
            .Select(index => new MentorRecipe($"recipe-{index:D2}", index == 0 ? 5 : 1, true))
            .ToArray();
        var evidence = MentorRelationshipEvidence.FromSnapshot(
            new MentorRelationshipSnapshot(1, 5, recipes, recipes.Skip(1).ToArray()));
        for (var index = 0; index < 40; index++)
            evidence = evidence.WithChange($"recipe-{index:D2}", index == 0 ? 6 : 2, true, index + 2);
        using var resolver = new MentorRelationshipResolutionWork(evidence);
        var leases = 0;

        while (!resolver.IsComplete)
        {
            mentor.SetState(true, cooperativePending: true, mutationPending: false);
            Assert.True(mentor.TryRunCooperative(() =>
            {
                var operations = 0;
                while (operations < 16 && !resolver.IsComplete)
                {
                    resolver.Step();
                    operations++;
                }
                return operations;
            }));
            leases++;
            frame++;
        }

        Assert.True(leases > 4);
        Assert.NotNull(resolver.Result);
        Assert.Equal(40, resolver.Result!.Discovered.Count);
        Assert.Equal(6, resolver.Result.HighestMastery);
    }

    [Fact]
    public void DisabledWorkIsIdleAndExceptionReleasesCoordinatorLease()
    {
        var coordinator = Coordinator();
        long frame = 3;
        using var mentor = new MentorCoordinatorWork(coordinator, () => frame);
        mentor.SetState(false, cooperativePending: true, mutationPending: true);
        Assert.False(mentor.CooperativePending);
        Assert.False(mentor.MutationPending);
        Assert.False(mentor.TryRunCooperative(() => 1));
        Assert.False(mentor.TryRunMutation(() => 1));

        mentor.SetState(true, cooperativePending: true, mutationPending: false);
        Assert.Throws<InvalidOperationException>(() => mentor.TryRunCooperative(
            () => throw new InvalidOperationException("simulated planning failure")));

        using var recovery = coordinator.Register("test", "recovery");
        recovery.SetPending(true);
        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(recovery, frame, out var lease));
        lease.Complete();
        Assert.True(coordinator.TryGetSubsystemSnapshot("OrbMentor", out var metrics));
        Assert.Equal(1, metrics.FailedWorkItems);
    }

    [Fact]
    public void DuePeriodicRefreshDeniedBySoftBudgetBlocksHardMutationUntilRefreshSettles()
    {
        var coordinator = new SuitePerformanceCoordinator(
            StopwatchPerformanceClock.Instance,
            softBudgetMilliseconds: 0.0,
            hardBudgetMilliseconds: 1000.0);
        long frame = 70;
        using var mentor = new MentorCoordinatorWork(coordinator, () => frame);
        var engine = new MentorEngine();
        engine.Consolidate(new MentorGrant("recipient", new MentorAmount(4, 2)));
        var grants = 0;
        var refreshDue = MentorDomainMutationEligibility.HasCooperativeWork(
            initialized: true,
            needsReconcile: false,
            reconcileActive: false,
            reconcileDue: false,
            relationshipDirty: false,
            refreshActive: false,
            liveRefreshDue: true,
            planningActive: false);

        mentor.SetState(true, cooperativePending: refreshDue, mutationPending: !refreshDue);
        Assert.False(mentor.TryRunCooperative(() => 1));
        Assert.False(mentor.TryRunMutation(() =>
        {
            grants++;
            return 1;
        }));
        Assert.Equal(0, grants);
        Assert.True(engine.TryPeek(out _));

        frame++;
        refreshDue = MentorDomainMutationEligibility.HasCooperativeWork(
            initialized: true,
            needsReconcile: false,
            reconcileActive: false,
            reconcileDue: false,
            relationshipDirty: false,
            refreshActive: false,
            liveRefreshDue: false,
            planningActive: false);
        mentor.SetState(true, cooperativePending: refreshDue, mutationPending: !refreshDue);
        Assert.True(mentor.TryRunMutation(() =>
        {
            Assert.Equal(
                MentorRecipientEligibilityStatus.Eligible,
                MentorRecipientEligibility.Evaluate(discovered: true, mastery: 2, highestMastery: 5));
            Assert.True(engine.TryPeek(out var grant));
            grants++;
            engine.Complete(grant.Uuid);
            return 1;
        }));
        Assert.Equal(1, grants);
        Assert.False(engine.TryPeek(out _));
    }

    [Theory]
    [InlineData(false, 1, 5, (int)MentorRecipientEligibilityStatus.Undiscovered)]
    [InlineData(true, 5, 5, (int)MentorRecipientEligibilityStatus.NotBelowHighestMastery)]
    [InlineData(true, 6, 5, (int)MentorRecipientEligibilityStatus.NotBelowHighestMastery)]
    public void ParkedRecipientSleepsAcrossFramesDoesNotBlockOthersAndGrantsOnceAfterRealRefresh(
        bool discovered,
        int mastery,
        int highestMastery,
        int expectedStatus)
    {
        var coordinator = Coordinator();
        long frame = 90;
        using var mentor = new MentorCoordinatorWork(coordinator, () => frame);
        var engine = new MentorEngine();
        var parked = new MentorParkedGrantLedger();
        var settledGeneration = 1L;
        engine.Consolidate(new MentorGrant("recipient", new MentorAmount(8, 2)));
        engine.Consolidate(new MentorGrant("other-eligible", new MentorAmount(3, 2)));
        var nativeCalls = 0;

        mentor.SetState(true, cooperativePending: false, mutationPending: true);
        Assert.True(mentor.TryRunMutation(() =>
        {
            Assert.True(engine.TryPeek(out var grant));
            Assert.Equal("recipient", grant.Uuid);
            Assert.Equal(
                (MentorRecipientEligibilityStatus)expectedStatus,
                MentorRecipientEligibility.Evaluate(discovered, mastery, highestMastery));
            Assert.Equal(MentorParkResult.Parked, parked.Park(grant, settledGeneration));
            Assert.True(engine.Complete(grant.Uuid));
            return 1;
        }));
        Assert.Equal(0, nativeCalls);
        Assert.Equal(1, parked.Count);
        Assert.False(parked.HasReady(settledGeneration));
        Assert.True(engine.TryPeek(out var next));
        Assert.Equal("other-eligible", next.Uuid);

        frame++;
        mentor.SetState(true, cooperativePending: false, mutationPending: true);
        Assert.True(mentor.TryRunMutation(() =>
        {
            Assert.True(engine.TryPeek(out var grant));
            Assert.Equal("other-eligible", grant.Uuid);
            nativeCalls++;
            Assert.True(engine.Complete(grant.Uuid));
            return 1;
        }));
        Assert.Equal(1, nativeCalls);
        Assert.False(engine.TryPeek(out _));

        for (var idle = 0; idle < 20; idle++)
        {
            frame++;
            Assert.False(parked.HasReady(settledGeneration));
            mentor.SetState(true, cooperativePending: false, mutationPending: false);
            Assert.False(mentor.TryRunMutation(() => { nativeCalls++; return 1; }));
        }
        Assert.Equal(1, nativeCalls);

        // A legitimate periodic authoritative refresh completed but the
        // recipient remained unchanged. Reconsider cooperatively and re-park
        // without spending another mutation admission.
        settledGeneration++;
        frame++;
        mentor.SetState(true, cooperativePending: parked.HasReady(settledGeneration), mutationPending: false);
        Assert.True(mentor.TryRunCooperative(() =>
        {
            Assert.True(parked.TryTakeReady(settledGeneration, out var retained));
            Assert.Equal("recipient", retained.Uuid);
            Assert.Equal(new MentorAmount(8, 2), retained.Amount);
            Assert.Equal(MentorParkResult.Parked, parked.Park(retained, settledGeneration));
            return 1;
        }));
        Assert.False(parked.HasReady(settledGeneration));
        for (var idle = 0; idle < 10; idle++)
        {
            frame++;
            mentor.SetState(true, cooperativePending: false, mutationPending: false);
            Assert.False(mentor.TryRunMutation(() => { nativeCalls++; return 1; }));
        }

        // A later real progression/refresh makes the cached relationship
        // eligible. Cooperative work wakes the exact amount, then final
        // mutation validation grants it once.
        settledGeneration++;
        frame++;
        mentor.SetState(true, cooperativePending: parked.HasReady(settledGeneration), mutationPending: false);
        Assert.True(mentor.TryRunCooperative(() =>
        {
            Assert.True(parked.TryTakeReady(settledGeneration, out var retained));
            Assert.Equal(
                MentorRecipientEligibilityStatus.Eligible,
                MentorRecipientEligibility.Evaluate(discovered: true, mastery: 2, highestMastery: 5));
            engine.Consolidate(retained);
            return 1;
        }));
        mentor.SetState(true, cooperativePending: false, mutationPending: engine.TryPeek(out _));
        Assert.True(mentor.TryRunMutation(() =>
        {
            Assert.True(engine.TryPeek(out var grant));
            Assert.Equal("recipient", grant.Uuid);
            Assert.Equal(new MentorAmount(8, 2), grant.Amount);
            nativeCalls++;
            Assert.True(engine.Complete(grant.Uuid));
            return 1;
        }));
        Assert.Equal(2, nativeCalls);
        Assert.Equal(0, parked.Count);
        Assert.False(engine.TryPeek(out _));

        frame++;
        mentor.SetState(true, cooperativePending: false, mutationPending: false);
        Assert.False(mentor.TryRunMutation(() => { nativeCalls++; return 1; }));
        Assert.Equal(2, nativeCalls);
        Assert.True(coordinator.TryGetSubsystemSnapshot("OrbMentor", out var metrics));
        Assert.Equal(3, metrics.NativeMutationsStarted);
    }

    [Fact]
    public void MutationExceptionPreservesObservedOutcomeAndLegacyOperationCount()
    {
        var coordinator = Coordinator();
        long frame = 1;
        using var mentor = new MentorCoordinatorWork(coordinator, () => frame);
        mentor.SetState(true, cooperativePending: false, mutationPending: true);
        var observed = new NativeMutationCallOutcome(1, 1, 0);

        Assert.Throws<InvalidOperationException>(() => mentor.TryRunMutation(
            () => throw new InvalidOperationException("housekeeping failed after native invocation"),
            () => observed.ToWorkCompletion()));

        Assert.True(coordinator.TryGetSubsystemSnapshot("OrbMentor", out var metrics));
        Assert.Equal(1, metrics.FailedWorkItems);
        Assert.Equal(1, metrics.TotalOperations);
        Assert.Equal(1, metrics.NativeCallsAttempted);
        Assert.Equal(1, metrics.NativeMutationAttempts);
        Assert.Equal(0, metrics.NativeMutationsCommitted);
    }

    private static SuitePerformanceCoordinator Coordinator() =>
        new(StopwatchPerformanceClock.Instance, 1000.0, 1000.0);

    private sealed class ManualClock : IPerformanceClock
    {
        private long _microseconds;
        public long GetTimestamp() => _microseconds;
        public double GetElapsedMilliseconds(long startTimestamp, long endTimestamp) =>
            (endTimestamp - startTimestamp) / 1000.0;
        public void Advance(double milliseconds) =>
            _microseconds += (long)(milliseconds * 1000.0);
    }
}
