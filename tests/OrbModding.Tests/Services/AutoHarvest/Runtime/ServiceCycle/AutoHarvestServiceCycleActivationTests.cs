using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoHarvestServiceCycleActivationTests
{
    [Fact]
    public void WaitsForReadinessThenForwardsTheFirstTickAndLifecycle()
    {
        var ready = false;
        var configuration = 1;
        var capturedConfiguration = 0;
        var attempts = 0;
        var runtime = new RecordingRuntime();
        var snapshot = Configuration();
        var activation = new AutoHarvestServiceCycleActivation(
            () => ready,
            () =>
            {
                attempts++;
                capturedConfiguration = configuration;
                return runtime;
            });

        activation.Tick(0.125f);
        activation.PublishSavedConfiguration(snapshot);
        activation.InvalidateLifecycle();
        activation.CancelPreparedWork();

        Assert.Equal(0, attempts);
        Assert.Empty(runtime.TickDurations);
        Assert.Equal(0, runtime.Publications);
        Assert.Equal(0, runtime.Invalidations);
        Assert.Equal(0, runtime.Cancellations);

        configuration = 2;
        ready = true;
        activation.Tick(0.25f);
        activation.Tick(0.5f);
        activation.PublishSavedConfiguration(snapshot);
        activation.InvalidateLifecycle();
        activation.CancelPreparedWork();

        Assert.Equal(1, attempts);
        Assert.Equal(2, capturedConfiguration);
        Assert.Equal(new[] { 0.25f, 0.5f }, runtime.TickDurations);
        Assert.Equal(1, runtime.Publications);
        Assert.Equal(1, runtime.Invalidations);
        Assert.Equal(1, runtime.Cancellations);

        activation.Dispose();
        activation.Tick(1f);
        activation.PublishSavedConfiguration(snapshot);

        Assert.Equal(1, runtime.Disposals);
        Assert.Equal(2, runtime.TickDurations.Count);
        Assert.Equal(1, runtime.Publications);
    }

    [Fact]
    public void FailedActivationIsAttemptedOnceAndDoesNotBlockFollowingServices()
    {
        var attempts = 0;
        var sibling = new RecordingRuntime();
        using var registry = new AutomataServiceRegistry();
        registry.Register(new AutoHarvestServiceCycleActivation(
            () => true,
            () =>
            {
                attempts++;
                return null;
            }));
        registry.Register(sibling);

        registry.Tick(0.25f);
        registry.Tick(0.5f);

        Assert.Equal(1, attempts);
        Assert.Equal(new[] { 0.25f, 0.5f }, sibling.TickDurations);
    }

    [Fact]
    public void DisposalBeforeReadinessPreventsActivation()
    {
        var attempts = 0;
        var ready = false;
        var activation = new AutoHarvestServiceCycleActivation(
            () => ready,
            () =>
            {
                attempts++;
                return new RecordingRuntime();
            });

        activation.Dispose();
        ready = true;
        activation.Tick(0.25f);

        Assert.Equal(0, attempts);
    }

    private sealed class RecordingRuntime : IAutoHarvestServiceCycleRuntime
    {
        public List<float> TickDurations { get; } = new();
        public int Publications { get; private set; }
        public int Invalidations { get; private set; }
        public int Cancellations { get; private set; }
        public int Disposals { get; private set; }

        public void Tick(float unscaledDeltaTime) => TickDurations.Add(unscaledDeltaTime);
        public void PublishSavedConfiguration(AutomataConfiguration configuration) =>
            Publications++;
        public void InvalidateLifecycle() => Invalidations++;
        public void CancelPreparedWork() => Cancellations++;
        public void Dispose() => Disposals++;
    }

    private static AutomataConfiguration Configuration() => AutoHarvestConfigurationFactory.Create(
        false,
        false,
        false,
        true,
        true,
        new MonotonicDuration(1));
}
