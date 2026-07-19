using OrbModding.Common;
using System;
using System.Threading;
using Xunit;

namespace OrbModding.Tests;

public sealed class PerformanceFoundationTests
{
    [Fact]
    public void RollingMetricsRetainOnlyBoundedWindowForSummary()
    {
        var metrics = new RollingPerformanceMetrics(4);

        for (var sample = 1; sample <= 6; sample++)
        {
            metrics.Record(sample, sample);
        }

        var snapshot = metrics.GetSnapshot(0.75);

        Assert.Equal(4, snapshot.Capacity);
        Assert.Equal(4, snapshot.SampleCount);
        Assert.Equal(6, snapshot.TotalSamples);
        Assert.Equal(18, snapshot.Operations);
        Assert.Equal(21, snapshot.TotalOperations);
        Assert.Equal(4.5, snapshot.AverageMilliseconds);
        Assert.Equal(6.0, snapshot.MaximumMilliseconds);
        Assert.Equal(5.0, snapshot.PercentileMilliseconds);
    }

    [Fact]
    public void RollingDistributionFreezesP95AndP99FromTheSameWindow()
    {
        var metrics = new RollingPerformanceMetrics(100);
        for (var sample = 1; sample <= 100; sample++)
        {
            metrics.Record(sample, 1);
        }

        var snapshot = metrics.GetDistributionSnapshot();

        Assert.Equal(50.5, snapshot.AverageMilliseconds);
        Assert.Equal(100.0, snapshot.MaximumMilliseconds);
        Assert.Equal(95.0, snapshot.P95Milliseconds);
        Assert.Equal(99.0, snapshot.P99Milliseconds);
    }

    [Fact]
    public void RegistrationMetricsSeparateSameSubsystemWorkAndTrackWaitReasons()
    {
        var clock = new ManualPerformanceClock();
        var coordinator = new SuitePerformanceCoordinator(
            clock,
            1.0,
            1.0,
            metricsWindow: 8,
            missedRequestFrames: 1,
            starvationThresholdFrames: 2);
        using var burner = coordinator.Register(
            "Automata",
            "Native purchase",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        using var waiting = coordinator.Register(
            "Automata",
            "Candidate refresh",
            SuiteBudgetClass.HardLimited);
        coordinator.BeginFrame(0);
        burner.SetPending(true);
        waiting.SetPending(true);

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(burner, 1, out var first));
        clock.Advance(1.0);
        first.Complete(SuiteWorkCompletion.NativeMutation(1, 1));
        Assert.Equal(SuiteWorkAdmission.HardBudgetExhausted, coordinator.RequestWork(waiting, 1, out _));
        Assert.Equal(SuiteWorkAdmission.HardBudgetExhausted, coordinator.RequestWork(waiting, 1, out _));
        Assert.True(coordinator.TryGetRegistrationSnapshot(waiting, out var sameFrame));
        Assert.Equal(2, sameFrame.DeferredAttempts);
        Assert.Equal(1, sameFrame.DeferredFrames);
        Assert.Equal(1, sameFrame.ConsecutiveDeferredFrames);
        Assert.Equal(1, sameFrame.MaximumConsecutiveDeferredFrames);

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(burner, 2, out var second));
        clock.Advance(1.0);
        second.Complete(SuiteWorkCompletion.NativeMutation(1, 1));
        Assert.Equal(SuiteWorkAdmission.HardBudgetExhausted, coordinator.RequestWork(waiting, 2, out _));

        Assert.True(coordinator.TryGetRegistrationSnapshot(waiting, out var deferred));
        Assert.Equal("Automata", deferred.Subsystem);
        Assert.Equal("Candidate refresh", deferred.WorkName);
        Assert.Equal(3, deferred.DeferredAttempts);
        Assert.Equal(2, deferred.DeferredFrames);
        Assert.Equal(2, deferred.ConsecutiveDeferredFrames);
        Assert.Equal(2, deferred.MaximumConsecutiveDeferredFrames);
        Assert.Equal(3, deferred.DeferralsByReason.HardBudgetExhausted);
        Assert.True(deferred.IsStarved);
        Assert.Equal(1, deferred.StarvationEvents);

        burner.SetPending(false);
        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(waiting, 3, out var admitted));
        admitted.Complete(0);
        Assert.True(coordinator.TryGetRegistrationSnapshot(waiting, out var recovered));
        Assert.Equal(0, recovered.CurrentPendingWaitFrames);
        Assert.Equal(0, recovered.ConsecutiveDeferrals);
        Assert.False(recovered.IsStarved);

        Assert.True(coordinator.TryGetRegistrationSnapshot(burner, out var native));
        Assert.Equal("Native purchase", native.WorkName);
        Assert.Equal(2, native.NativeMutationLeaseAdmissions);
        Assert.Equal(2, native.NativeMutationAttempts);
        Assert.Equal(2, native.NativeMutationsCommitted);

        var all = coordinator.GetRegistrationSnapshots();
        Assert.Equal(2, all.Length);
        Assert.Contains(all, item => item.RegistrationId == burner.RegistrationId && item.WorkName == "Native purchase");
        Assert.Contains(all, item => item.RegistrationId == waiting.RegistrationId && item.WorkName == "Candidate refresh");
    }

    [Fact]
    public void RegistrationMetricsAttributeOverrunAndMutationOutcomesToExactWork()
    {
        var clock = new ManualPerformanceClock();
        var coordinator = new SuitePerformanceCoordinator(clock, 0.5, 1.0, 8);
        using var purchase = coordinator.Register(
            "Automata",
            "Purchase",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        using var release = coordinator.Register(
            "Automata",
            "Release hold",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        purchase.SetPending(true);
        release.SetPending(true);

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(purchase, 1, out var noOp));
        clock.Advance(1.25);
        noOp.Complete(new SuiteWorkCompletion(1));

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(purchase, 2, out var multiple));
        clock.Advance(0.25);
        multiple.Complete(SuiteWorkCompletion.NativeMutation(attempted: 3, committed: 2, operations: 3));

        purchase.SetPending(false);
        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(release, 3, out var failed));
        clock.Advance(0.1);
        failed.Fail(SuiteWorkCompletion.NativeMutation(attempted: 1, committed: 0));

        Assert.True(coordinator.TryGetRegistrationSnapshot(purchase, out var purchaseMetrics));
        Assert.True(coordinator.TryGetRegistrationSnapshot(release, out var releaseMetrics));
        Assert.Equal(1, purchaseMetrics.NativeHardBudgetOverruns);
        Assert.Equal(0, releaseMetrics.NativeHardBudgetOverruns);
        Assert.Equal(2, purchaseMetrics.NativeMutationLeaseAdmissions);
        Assert.Equal(3, purchaseMetrics.NativeMutationAttempts);
        Assert.Equal(2, purchaseMetrics.NativeMutationsCommitted);
        Assert.Equal(1, releaseMetrics.NativeMutationAttempts);
        Assert.Equal(0, releaseMetrics.NativeMutationsCommitted);
        Assert.Equal(1, releaseMetrics.FailedWorkItems);

        Assert.True(coordinator.TryGetSubsystemSnapshot("Automata", out var aggregate));
        Assert.Equal(3, aggregate.NativeMutationLeaseAdmissions);
        Assert.Equal(4, aggregate.NativeMutationAttempts);
        Assert.Equal(2, aggregate.NativeMutationsCommitted);
    }

    [Fact]
    public void ImplicitFailureAndAbandonmentRetainOneLegacyMutationOperation()
    {
        var coordinator = new SuitePerformanceCoordinator(new ManualPerformanceClock(), 10.0, 10.0, 8);
        using var mutation = coordinator.Register(
            "Automata",
            "Mutation cleanup",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        mutation.SetPending(true);

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(mutation, 1, out var disposed));
        disposed.Dispose();
        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(mutation, 2, out var abandoned));
        Assert.True(abandoned.IsGranted);
        coordinator.BeginFrame(3);

        Assert.True(coordinator.TryGetRegistrationSnapshot(mutation, out var snapshot));
        Assert.Equal(2, snapshot.TotalOperations);
        Assert.Equal(2, snapshot.FailedWorkItems);
        Assert.Equal(1, snapshot.AbandonedWorkItems);
    }

    [Fact]
    public void RegistrationSnapshotSurvivesDisableReenableAndDispose()
    {
        var coordinator = new SuitePerformanceCoordinator(new ManualPerformanceClock());
        var work = coordinator.Register("ModConfig", "Repair shell");
        work.SetPending(true);
        work.SetEnabled(false);

        Assert.True(coordinator.TryGetRegistrationSnapshot(work, out var disabled));
        Assert.False(disabled.IsEnabled);
        Assert.False(disabled.IsPending);

        work.SetEnabled(true);
        work.SetPending(true);
        AssertGrantedAndComplete(coordinator, work, 1);
        work.Dispose();

        Assert.True(coordinator.TryGetRegistrationSnapshot(work, out var disposed));
        Assert.True(disposed.IsDisposed);
        Assert.Equal(1, disposed.CompletedWorkItems);
        Assert.Equal("ModConfig", disposed.Subsystem);
        Assert.Equal("Repair shell", disposed.WorkName);
    }

    [Fact]
    public void PendingWaitUsesSuppliedFrameIdentityAndClosesOnAdmissionDisableAndDispose()
    {
        var admittedCoordinator = new SuitePerformanceCoordinator(
            new ManualPerformanceClock(),
            starvationThresholdFrames: 5);
        using var admitted = admittedCoordinator.Register("Automata", "Admission wait");
        admittedCoordinator.BeginFrame(1);
        admitted.SetPending(true);
        Assert.Equal(SuiteWorkAdmission.Granted, admittedCoordinator.RequestWork(admitted, 6, out var lease));
        lease.Complete(1);
        Assert.True(admittedCoordinator.TryGetRegistrationSnapshot(admitted, out var admittedSnapshot));
        Assert.Equal(5, admittedSnapshot.MaximumPendingWaitFrames);
        Assert.Equal(1, admittedSnapshot.StarvationEvents);
        Assert.Equal(0, admittedSnapshot.CurrentPendingWaitFrames);

        var lifecycleCoordinator = new SuitePerformanceCoordinator(
            new ManualPerformanceClock(),
            starvationThresholdFrames: 5);
        var disabled = lifecycleCoordinator.Register("Mentor", "Disable wait");
        lifecycleCoordinator.BeginFrame(10);
        disabled.SetPending(true);
        lifecycleCoordinator.BeginFrame(20);
        disabled.SetEnabled(false);
        Assert.True(lifecycleCoordinator.TryGetRegistrationSnapshot(disabled, out var disabledSnapshot));
        Assert.Equal(10, disabledSnapshot.MaximumPendingWaitFrames);
        Assert.Equal(1, disabledSnapshot.StarvationEvents);
        Assert.Equal(0, disabledSnapshot.CurrentPendingWaitFrames);
        Assert.Equal(0, disabledSnapshot.ConsecutiveDeferrals);

        var disposed = lifecycleCoordinator.Register("ModConfig", "Dispose wait");
        lifecycleCoordinator.BeginFrame(30);
        disposed.SetPending(true);
        lifecycleCoordinator.BeginFrame(37);
        disposed.Dispose();
        Assert.True(lifecycleCoordinator.TryGetRegistrationSnapshot(disposed, out var disposedSnapshot));
        Assert.Equal(7, disposedSnapshot.MaximumPendingWaitFrames);
        Assert.Equal(1, disposedSnapshot.StarvationEvents);
        Assert.Equal(0, disposedSnapshot.CurrentPendingWaitFrames);
        Assert.Equal(0, disposedSnapshot.ConsecutiveDeferrals);
        disabled.Dispose();
    }

    [Fact]
    public void DisposeResetsLiveDeferredRunButRetainsClosedWaitHistory()
    {
        var coordinator = new SuitePerformanceCoordinator(
            new ManualPerformanceClock(),
            softBudgetMilliseconds: 0.0,
            hardBudgetMilliseconds: 1.0,
            starvationThresholdFrames: 10);
        var work = coordinator.Register("Automata", "Deferred disposal");
        coordinator.BeginFrame(1);
        work.SetPending(true);

        Assert.Equal(SuiteWorkAdmission.SoftBudgetExhausted, coordinator.RequestWork(work, 1, out _));
        coordinator.BeginFrame(2);
        work.Dispose();

        Assert.True(coordinator.TryGetRegistrationSnapshot(work, out var snapshot));
        Assert.True(snapshot.IsDisposed);
        Assert.Equal(0, snapshot.CurrentPendingWaitFrames);
        Assert.Equal(0, snapshot.ConsecutiveDeferredFrames);
        Assert.Equal(1, snapshot.MaximumConsecutiveDeferredFrames);
        Assert.Equal(1, snapshot.MaximumPendingWaitFrames);
        Assert.Equal(1, snapshot.DeferredAttempts);
        Assert.Equal(1, snapshot.DeferredFrames);
        Assert.Equal(1, snapshot.DeferralsByReason.SoftBudgetExhausted);
    }

    [Fact]
    public void SparseAndBackwardFrameIdentitiesRemainResetSafe()
    {
        var coordinator = new SuitePerformanceCoordinator(
            new ManualPerformanceClock(),
            softBudgetMilliseconds: 0.0,
            hardBudgetMilliseconds: 1.0,
            starvationThresholdFrames: 100);
        using var work = coordinator.Register("Automata", "Sparse wait");
        coordinator.BeginFrame(1);
        work.SetPending(true);

        Assert.Equal(SuiteWorkAdmission.SoftBudgetExhausted, coordinator.RequestWork(work, 1, out _));
        Assert.Equal(SuiteWorkAdmission.SoftBudgetExhausted, coordinator.RequestWork(work, 121, out _));
        Assert.True(coordinator.TryGetRegistrationSnapshot(work, out var sparse));
        Assert.Equal(120, sparse.CurrentPendingWaitFrames);
        Assert.Equal(120, sparse.MaximumPendingWaitFrames);
        Assert.Equal(2, sparse.DeferredFrames);
        Assert.Equal(1, sparse.ConsecutiveDeferrals);
        Assert.True(sparse.IsStarved);

        Assert.Equal(SuiteWorkAdmission.SoftBudgetExhausted, coordinator.RequestWork(work, 2, out _));
        Assert.True(coordinator.TryGetRegistrationSnapshot(work, out var reset));
        Assert.Equal(0, reset.CurrentPendingWaitFrames);
        Assert.Equal(120, reset.MaximumPendingWaitFrames);
        Assert.Equal(1, reset.ConsecutiveDeferrals);
    }

    [Fact]
    public void RegistrationDeferralRecordingDoesNotAllocateOnHotPath()
    {
        var coordinator = new SuitePerformanceCoordinator(new ManualPerformanceClock());
        using var active = coordinator.Register("Automata", "Active");
        using var denied = coordinator.Register("Automata", "Denied");
        active.SetPending(true);
        denied.SetPending(true);
        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(active, 1, out var lease));

        // Warm the call path before measuring the thread-local allocation counter.
        Assert.Equal(SuiteWorkAdmission.WorkInProgress, coordinator.RequestWork(denied, 1, out _));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var attempt = 0; attempt < 1_000; attempt++)
        {
            coordinator.RequestWork(denied, 1, out _);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        lease.Complete(0);

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void NewFrameResetsBudgetButSameFrameIdentityDoesNot()
    {
        var clock = new ManualPerformanceClock();
        var coordinator = new SuitePerformanceCoordinator(clock, 0.5, 1.0, 8);
        using var work = coordinator.Register("Automata", "Candidate scan");
        work.SetPending(true);

        Assert.Equal(
            SuiteWorkAdmission.Granted,
            coordinator.RequestWork(work, 100, out var firstLease));
        clock.Advance(0.6);
        firstLease.Complete(3);

        coordinator.BeginFrame(100);
        Assert.Equal(0.6, coordinator.CurrentFrameElapsedMilliseconds, 6);
        Assert.Equal(
            SuiteWorkAdmission.SoftBudgetExhausted,
            coordinator.RequestWork(work, 100, out _));

        coordinator.BeginFrame(101);
        Assert.Equal(0.0, coordinator.CurrentFrameElapsedMilliseconds);
        Assert.Equal(
            SuiteWorkAdmission.Granted,
            coordinator.RequestWork(work, 101, out var secondLease));
        secondLease.Complete();

        Assert.True(coordinator.TryGetSubsystemSnapshot("Automata", out var subsystem));
        Assert.Equal(2, subsystem.CompletedWorkItems);
        Assert.Equal(4, subsystem.TotalOperations);
        Assert.Equal(1, subsystem.FrameTiming.SampleCount);
        Assert.Equal(0.6, subsystem.FrameTiming.MaximumMilliseconds, 6);
    }

    [Fact]
    public void NonPreemptibleNativeOverrunStopsFurtherFrameAdmission()
    {
        var clock = new ManualPerformanceClock();
        var coordinator = new SuitePerformanceCoordinator(clock, 0.5, 1.0, 8);
        using var nativeWork = coordinator.Register(
            "Mentor",
            "Grant XP",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNative);
        using var followup = coordinator.Register(
            "Automata",
            "Queue poll",
            SuiteBudgetClass.HardLimited);
        nativeWork.SetPending(true);
        followup.SetPending(true);

        Assert.Equal(
            SuiteWorkAdmission.Granted,
            coordinator.RequestWork(
                nativeWork,
                4,
                out var nativeLease));

        // A synchronous native call cannot be interrupted. Its complete duration
        // is accounted after it returns, even when that crosses the hard limit.
        clock.Advance(1.25);
        nativeLease.Complete();

        Assert.Equal(1.25, coordinator.CurrentFrameElapsedMilliseconds, 6);
        Assert.True(coordinator.IsHardBudgetExceeded);
        Assert.Equal(
            SuiteWorkAdmission.HardBudgetExhausted,
            coordinator.RequestWork(
                followup,
                4,
                out _));

        Assert.True(coordinator.TryGetSubsystemSnapshot("Mentor", out var mentor));
        Assert.Equal(1, mentor.NativeCallsStarted);
        Assert.Equal(1, mentor.NativeHardBudgetOverruns);
    }

    [Fact]
    public void RoundRobinAdmissionPreventsReadyWorkFromStarving()
    {
        var clock = new ManualPerformanceClock();
        var coordinator = new SuitePerformanceCoordinator(clock, 10.0, 10.0, 8);
        using var automata = coordinator.Register("Automata", "Scan");
        using var mentor = coordinator.Register("Mentor", "XP grants");
        using var ui = coordinator.Register("ModConfig", "UI attach");
        automata.SetPending(true);
        mentor.SetPending(true);
        ui.SetPending(true);

        AssertGrantedAndComplete(coordinator, automata, 8);
        Assert.Equal(
            SuiteWorkAdmission.WaitingForTurn,
            coordinator.RequestWork(automata, 8, out _));
        AssertGrantedAndComplete(coordinator, mentor, 8);
        AssertGrantedAndComplete(coordinator, ui, 8);
        AssertGrantedAndComplete(coordinator, automata, 8);

        mentor.SetEnabled(false);
        AssertGrantedAndComplete(coordinator, ui, 8);
        AssertGrantedAndComplete(coordinator, automata, 8);

        Assert.True(coordinator.TryGetSubsystemSnapshot("Automata", out var automataMetrics));
        Assert.True(coordinator.TryGetSubsystemSnapshot("Mentor", out var mentorMetrics));
        Assert.True(coordinator.TryGetSubsystemSnapshot("ModConfig", out var uiMetrics));
        Assert.Equal(3, automataMetrics.CompletedWorkItems);
        Assert.Equal(1, mentorMetrics.CompletedWorkItems);
        Assert.Equal(2, uiMetrics.CompletedWorkItems);
    }

    [Fact]
    public void WeightedAdmissionProvidesBoundedBurstThenYields()
    {
        var clock = new ManualPerformanceClock();
        var coordinator = new SuitePerformanceCoordinator(clock, 10.0, 10.0, 8);
        using var autoBuy = coordinator.RegisterWeighted(
            "Automata",
            "Queue purchase",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation,
            schedulingWeight: 3);
        using var mentor = coordinator.Register(
            "Mentor",
            "Grant XP",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        autoBuy.SetPending(true);
        mentor.SetPending(true);

        for (var frame = 1; frame <= 3; frame++)
        {
            AssertGrantedAndComplete(coordinator, autoBuy, frame);
            Assert.Equal(
                SuiteWorkAdmission.NativeMutationAlreadyAdmitted,
                coordinator.RequestWork(mentor, frame, out _));
        }

        Assert.Equal(
            SuiteWorkAdmission.WaitingForTurn,
            coordinator.RequestWork(autoBuy, 4, out _));
        AssertGrantedAndComplete(coordinator, mentor, 4);
        AssertGrantedAndComplete(coordinator, autoBuy, 5);

        Assert.Equal(3, autoBuy.SchedulingWeight);
        Assert.Equal(1, mentor.SchedulingWeight);
    }

    [Fact]
    public void SchedulingWeightIsBounded()
    {
        var coordinator = new SuitePerformanceCoordinator(new ManualPerformanceClock());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            coordinator.RegisterWeighted(
                "Automata",
                "Invalid",
                SuiteBudgetClass.SoftLimited,
                SuiteWorkExecutionKind.Cooperative,
                schedulingWeight: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            coordinator.RegisterWeighted(
                "Automata",
                "Invalid",
                SuiteBudgetClass.SoftLimited,
                SuiteWorkExecutionKind.Cooperative,
                schedulingWeight: 9));
    }

    [Fact]
    public void HardLimitedWorkCanRunBetweenSoftAndHardLimits()
    {
        var clock = new ManualPerformanceClock();
        var coordinator = new SuitePerformanceCoordinator(clock, 0.5, 1.0, 8);
        using var normal = coordinator.Register("Automata", "Scan");
        using var safety = coordinator.Register(
            "Automata",
            "Queue safety",
            SuiteBudgetClass.HardLimited);
        normal.SetPending(true);
        safety.SetPending(true);

        Assert.Equal(
            SuiteWorkAdmission.Granted,
            coordinator.RequestWork(normal, 1, out var lease));
        clock.Advance(0.6);
        lease.Complete();

        Assert.Equal(
            SuiteWorkAdmission.SoftBudgetExhausted,
            coordinator.RequestWork(normal, 1, out _));
        Assert.Equal(
            SuiteWorkAdmission.Granted,
            coordinator.RequestWork(safety, 1, out var safetyLease));
        clock.Advance(0.2);
        safetyLease.Complete();
        Assert.Equal(0.8, coordinator.CurrentFrameElapsedMilliseconds, 6);
    }

    [Fact]
    public void DisabledUnregisteredAndIdleWorkCannotBeAdmitted()
    {
        var clock = new ManualPerformanceClock();
        var coordinator = new SuitePerformanceCoordinator(clock, 1.0, 2.0, 8);
        var work = coordinator.Register("Mentor", "Recipient rebuild");

        Assert.Equal(
            SuiteWorkAdmission.NoPendingWork,
            coordinator.RequestWork(work, 1, out _));

        work.SetPending(true);
        work.SetEnabled(false);
        Assert.False(work.IsPending);
        Assert.Equal(
            SuiteWorkAdmission.Disabled,
            coordinator.RequestWork(work, 1, out _));

        work.Dispose();
        Assert.Equal(
            SuiteWorkAdmission.Unregistered,
            coordinator.RequestWork(work, 1, out _));

        Assert.Equal(0.0, coordinator.CurrentFrameElapsedMilliseconds);
        Assert.True(coordinator.TryGetSubsystemSnapshot("Mentor", out var metrics));
        Assert.Equal(0, metrics.AdmittedWorkItems);
        Assert.Equal(0, metrics.CompletedWorkItems);
        Assert.Equal(0, metrics.WorkItemTiming.SampleCount);
    }

    [Fact]
    public void InvalidCompletionAndClockFailureReleaseActiveSlot()
    {
        var clock = new ManualPerformanceClock();
        var coordinator = new SuitePerformanceCoordinator(clock, 10.0, 10.0, 8);
        using var first = coordinator.Register("Automata", "First");
        using var second = coordinator.Register("Mentor", "Second");
        first.SetPending(true);
        second.SetPending(true);

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(first, 1, out var invalidLease));
        Assert.Throws<ArgumentOutOfRangeException>(() => invalidLease.Complete(-1));
        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(second, 1, out var secondLease));
        secondLease.Complete();

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(first, 1, out var clockLease));
        clock.ThrowOnNextTimestamp = true;
        Assert.Throws<InvalidOperationException>(() => clockLease.Complete());
        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(second, 1, out var recoveryLease));
        recoveryLease.Complete();

        Assert.True(coordinator.TryGetSubsystemSnapshot("Automata", out var metrics));
        Assert.Equal(2, metrics.FailedWorkItems);
        Assert.Equal(1, metrics.MeasurementFailures);
    }

    [Fact]
    public void CallerFailureAndAbandonedLeaseAreRecoveredAndRecorded()
    {
        var clock = new ManualPerformanceClock();
        var coordinator = new SuitePerformanceCoordinator(clock, 10.0, 10.0, 8);
        using var first = coordinator.Register("Automata", "First");
        using var second = coordinator.Register("Mentor", "Second");
        first.SetPending(true);
        second.SetPending(true);

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(first, 1, out var failedLease));
        try
        {
            clock.Advance(0.2);
            throw new InvalidOperationException("Simulated caller failure.");
        }
        catch (InvalidOperationException)
        {
            failedLease.Fail();
        }

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(second, 1, out var abandonedLease));
        clock.Advance(0.3);

        // Intentionally leak the lease. Starting the next frame records it as
        // abandoned and recovers the coordinator before admitting more work.
        Assert.True(abandonedLease.IsGranted);
        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(first, 2, out var recoveredLease));
        recoveredLease.Complete();

        Assert.True(coordinator.TryGetSubsystemSnapshot("Automata", out var automata));
        Assert.True(coordinator.TryGetSubsystemSnapshot("Mentor", out var mentor));
        Assert.Equal(1, automata.FailedWorkItems);
        Assert.Equal(0, automata.AbandonedWorkItems);
        Assert.Equal(1, mentor.FailedWorkItems);
        Assert.Equal(1, mentor.AbandonedWorkItems);
    }

    [Fact]
    public void StalePendingRegistrationExpiresWithoutLosingRoundRobinFairness()
    {
        var clock = new ManualPerformanceClock();
        var coordinator = new SuitePerformanceCoordinator(
            clock,
            10.0,
            10.0,
            metricsWindow: 8,
            missedRequestFrames: 2);
        using var stale = coordinator.Register("Automata", "Stale scan");
        using var active = coordinator.Register("Mentor", "Active grants");
        stale.SetPending(true);
        active.SetPending(true);

        AssertGrantedAndComplete(coordinator, stale, 1);
        AssertGrantedAndComplete(coordinator, active, 1);

        // Reasserting an already-pending flag must not refresh the watchdog.
        stale.SetPending(true);
        Assert.Equal(SuiteWorkAdmission.WaitingForTurn, coordinator.RequestWork(active, 2, out _));
        stale.SetPending(true);
        AssertGrantedAndComplete(coordinator, active, 3);

        // A previously stale registration can request again and rejoins rotation.
        AssertGrantedAndComplete(coordinator, stale, 4);
        Assert.True(coordinator.TryGetSubsystemSnapshot("Automata", out var metrics));
        Assert.Equal(1, metrics.MissedRequestExpirations);
    }

    [Fact]
    public void OnlyOneNativeMutationCanBeAdmittedAcrossSuitePerFrame()
    {
        var clock = new ManualPerformanceClock();
        var coordinator = new SuitePerformanceCoordinator(clock, 10.0, 10.0, 8);
        using var automataMutation = coordinator.Register(
            "Automata",
            "Purchase",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        using var mentorMutation = coordinator.Register(
            "Mentor",
            "Grant XP",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        using var readWork = coordinator.Register("ModConfig", "Integrity check");
        automataMutation.SetPending(true);
        mentorMutation.SetPending(true);
        readWork.SetPending(true);

        AssertGrantedAndComplete(coordinator, automataMutation, 1);
        Assert.Equal(
            SuiteWorkAdmission.NativeMutationAlreadyAdmitted,
            coordinator.RequestWork(mentorMutation, 1, out _));
        AssertGrantedAndComplete(coordinator, readWork, 1);

        automataMutation.SetPending(false);
        AssertGrantedAndComplete(coordinator, mentorMutation, 2);

        Assert.True(coordinator.TryGetSubsystemSnapshot("Automata", out var automata));
        Assert.True(coordinator.TryGetSubsystemSnapshot("Mentor", out var mentor));
        Assert.Equal(1, automata.NativeMutationsStarted);
        Assert.Equal(1, mentor.NativeMutationsStarted);
    }

    [Fact]
    public void InvalidHandleCannotResetFrameAndCoordinatorEnforcesOwnerThread()
    {
        var clock = new ManualPerformanceClock();
        var coordinator = new SuitePerformanceCoordinator(clock, 1.0, 2.0, 8);
        var foreignCoordinator = new SuitePerformanceCoordinator(clock, 1.0, 2.0, 8);
        using var local = coordinator.Register("Automata", "Local");
        using var foreign = foreignCoordinator.Register("Mentor", "Foreign");
        coordinator.BeginFrame(10);

        Assert.Equal(SuiteWorkAdmission.Unregistered, coordinator.RequestWork(foreign, 999, out _));
        Assert.Equal(10, coordinator.CurrentFrameIdentity);

        var disposed = coordinator.Register("Automata", "Disposed");
        disposed.Dispose();
        Assert.Equal(SuiteWorkAdmission.Unregistered, coordinator.RequestWork(disposed, 998, out _));
        Assert.Equal(10, coordinator.CurrentFrameIdentity);

        Exception? threadFailure = null;
        var thread = new Thread(() =>
        {
            try
            {
                coordinator.BeginFrame(11);
            }
            catch (Exception exception)
            {
                threadFailure = exception;
            }
        });
        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(threadFailure);
        Assert.Equal(10, coordinator.CurrentFrameIdentity);
    }

    private static void AssertGrantedAndComplete(
        SuitePerformanceCoordinator coordinator,
        SuiteWorkRegistration registration,
        long frameIdentity)
    {
        Assert.Equal(
            SuiteWorkAdmission.Granted,
            coordinator.RequestWork(
                registration,
                frameIdentity,
                out var lease));
        lease.Complete();
    }

    private sealed class ManualPerformanceClock : IPerformanceClock
    {
        private long _microseconds;

        public bool ThrowOnNextTimestamp { get; set; }

        public long GetTimestamp()
        {
            if (ThrowOnNextTimestamp)
            {
                ThrowOnNextTimestamp = false;
                throw new InvalidOperationException("Simulated clock failure.");
            }

            return _microseconds;
        }

        public double GetElapsedMilliseconds(long startTimestamp, long endTimestamp)
        {
            return (endTimestamp - startTimestamp) / 1000.0;
        }

        public void Advance(double milliseconds)
        {
            _microseconds += (long)(milliseconds * 1000.0);
        }
    }
}
