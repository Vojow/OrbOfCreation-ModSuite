using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoHarvestControllerTests
{
    [Fact]
    public void DisabledByDefault_PerformsNoNativeReadsOrMutations()
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        var runtime = new FakeRuntime();
        long frame = 1;
        using var controller = CreateController(config, runtime, () => frame);

        controller.Tick(10.0f);

        Assert.Empty(runtime.ReadPairs);
        Assert.Empty(runtime.SubmittedPairs);
    }

    [Fact]
    public void ActiveMode_SubmitsOneAtATimeInRoundRobinOrder()
    {
        var config = ActiveConfig();
        var runtime = new FakeRuntime();
        long frame = 1;
        using var controller = CreateController(config, runtime, () => frame);

        controller.Tick(0.1f);
        frame++;
        controller.Tick(1.0f);

        Assert.Equal(new[] { AutoHarvestPair.FruitTree, AutoHarvestPair.TreasureTree }, runtime.SubmittedPairs);
    }

    [Fact]
    public void AmbiguousSubmission_BlocksOnlyThatPairUntilLifecycleRecovery()
    {
        var config = ActiveConfig();
        config.AutoHarvestTreasureTrees.Value = false;
        var runtime = new FakeRuntime { NextSubmission = new(false, true, "ambiguous") };
        long frame = 1;
        using var controller = CreateController(config, runtime, () => frame);

        controller.Tick(0.1f);
        frame++;
        controller.Tick(1.0f);
        Assert.Single(runtime.SubmittedPairs);

        runtime.NextSubmission = new(true, true, string.Empty);
        controller.InvalidateLifecycle();
        frame++;
        controller.Tick(0.1f);

        Assert.Equal(2, runtime.SubmittedPairs.Count);
        Assert.Equal(1, runtime.Invalidations);
    }

    [Fact]
    public void EmergencyDisable_DiscardsPendingWorkAndStopsFurtherReads()
    {
        var config = ActiveConfig();
        var runtime = new FakeRuntime();
        long frame = 1;
        using var controller = CreateController(config, runtime, () => frame);
        controller.Tick(0.1f);
        var reads = runtime.ReadPairs.Count;

        config.EmergencyDisable.Value = true;
        frame++;
        controller.Tick(10.0f);

        Assert.Equal(reads, runtime.ReadPairs.Count);
        Assert.Single(runtime.SubmittedPairs);
    }

    private static AutoHarvestController CreateController(
        AutomataConfig config,
        FakeRuntime runtime,
        Func<long> readFrame) =>
        new(
            config,
            runtime,
            new ManualLogSource(),
            new SuitePerformanceCoordinator(new ZeroClock(), 10.0, 10.0, 16),
            readFrame,
            () => 7);

    private static AutomataConfig ActiveConfig()
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AutoHarvestMode.Value = AutoHarvestOperationMode.Active;
        config.AutoHarvestFruitTrees.Value = true;
        config.AutoHarvestTreasureTrees.Value = true;
        config.AutoHarvestEvaluationIntervalSeconds.Value = 1.0f;
        return config;
    }

    private sealed class FakeRuntime : IAutoHarvestRuntime
    {
        public List<AutoHarvestPair> ReadPairs { get; } = new();
        public List<AutoHarvestPair> SubmittedPairs { get; } = new();
        public AutoHarvestSubmissionResult NextSubmission { get; set; } = new(true, true, string.Empty);
        public int Invalidations { get; private set; }

        public bool TryReadCandidate(
            AutoHarvestPair pair,
            bool selected,
            out NativeAutoHarvestCandidate? candidate,
            out AutoHarvestCandidateSnapshot snapshot,
            out string reason)
        {
            ReadPairs.Add(pair);
            var plot = new object();
            var action = new object();
            candidate = new NativeAutoHarvestCandidate(pair, 7, plot, action, new object());
            snapshot = new AutoHarvestCandidateSnapshot(
                pair == AutoHarvestPair.FruitTree ? AutoHarvestKnownIds.FruitTreePlot : AutoHarvestKnownIds.TreasureTreePlot,
                pair == AutoHarvestPair.FruitTree ? AutoHarvestKnownIds.FruitTreeCollect : AutoHarvestKnownIds.TreasureTreeCollect,
                7,
                selected,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestActionSafetyState.NativePhaseCyclePreserving,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified);
            reason = string.Empty;
            return true;
        }

        public AutoHarvestSubmissionResult TrySubmit(NativeAutoHarvestCandidate candidate)
        {
            SubmittedPairs.Add(candidate.Pair);
            return NextSubmission;
        }

        public void InvalidateLifecycle() => Invalidations++;
        public void Dispose() { }
    }

    private sealed class ZeroClock : IPerformanceClock
    {
        public long GetTimestamp() => 0;
        public double GetElapsedMilliseconds(long startTimestamp, long endTimestamp) => 0;
    }
}
