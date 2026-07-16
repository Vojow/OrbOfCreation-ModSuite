using OrbModding.Common;
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
            coordinator.RequestWork(work, 100, SuiteWorkExecutionKind.Cooperative, out var firstLease));
        clock.Advance(0.6);
        firstLease.Complete(3);

        coordinator.BeginFrame(100);
        Assert.Equal(0.6, coordinator.CurrentFrameElapsedMilliseconds, 6);
        Assert.Equal(
            SuiteWorkAdmission.SoftBudgetExhausted,
            coordinator.RequestWork(work, 100, SuiteWorkExecutionKind.Cooperative, out _));

        coordinator.BeginFrame(101);
        Assert.Equal(0.0, coordinator.CurrentFrameElapsedMilliseconds);
        Assert.Equal(
            SuiteWorkAdmission.Granted,
            coordinator.RequestWork(work, 101, SuiteWorkExecutionKind.Cooperative, out var secondLease));
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
            SuiteBudgetClass.HardLimited);
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
                SuiteWorkExecutionKind.NonPreemptibleNative,
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
                SuiteWorkExecutionKind.Cooperative,
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
            coordinator.RequestWork(automata, 8, SuiteWorkExecutionKind.Cooperative, out _));
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
            coordinator.RequestWork(normal, 1, SuiteWorkExecutionKind.Cooperative, out var lease));
        clock.Advance(0.6);
        lease.Complete();

        Assert.Equal(
            SuiteWorkAdmission.SoftBudgetExhausted,
            coordinator.RequestWork(normal, 1, SuiteWorkExecutionKind.Cooperative, out _));
        Assert.Equal(
            SuiteWorkAdmission.Granted,
            coordinator.RequestWork(safety, 1, SuiteWorkExecutionKind.Cooperative, out var safetyLease));
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
            coordinator.RequestWork(work, 1, SuiteWorkExecutionKind.Cooperative, out _));

        work.SetPending(true);
        work.SetEnabled(false);
        Assert.False(work.IsPending);
        Assert.Equal(
            SuiteWorkAdmission.Disabled,
            coordinator.RequestWork(work, 1, SuiteWorkExecutionKind.Cooperative, out _));

        work.Dispose();
        Assert.Equal(
            SuiteWorkAdmission.Unregistered,
            coordinator.RequestWork(work, 1, SuiteWorkExecutionKind.Cooperative, out _));

        Assert.Equal(0.0, coordinator.CurrentFrameElapsedMilliseconds);
        Assert.True(coordinator.TryGetSubsystemSnapshot("Mentor", out var metrics));
        Assert.Equal(0, metrics.AdmittedWorkItems);
        Assert.Equal(0, metrics.CompletedWorkItems);
        Assert.Equal(0, metrics.WorkItemTiming.SampleCount);
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
                SuiteWorkExecutionKind.Cooperative,
                out var lease));
        lease.Complete();
    }

    private sealed class ManualPerformanceClock : IPerformanceClock
    {
        private long _microseconds;

        public long GetTimestamp()
        {
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
