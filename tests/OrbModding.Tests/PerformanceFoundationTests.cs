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
