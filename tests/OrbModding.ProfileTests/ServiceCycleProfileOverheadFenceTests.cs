using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The full trace emits from inside the frame it is recording. Without a fence the frame's own span
/// would report the cost of recording it, which is the red herring the north star's full-trace
/// mandate forbids.
/// </summary>
public sealed class ServiceCycleProfileOverheadFenceTests
{
    [Fact]
    public void AnEnclosingSpanDoesNotReportTheTraceEmissionNestedInsideIt()
    {
        var recorder = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new ScriptedProfileRawClock(1_000, new long[] { 100, 102, 200, 210, 240, 300 }),
            new ProvenIncrementingProfileAllocationCounter());
        var counters = default(ServiceCycleProfileOperationCounters);
        var pump = Context(ServiceCycleProfileSpan.OverallPump);
        var emission = Context(ServiceCycleProfileSpan.SemanticPumpSummary);

        Assert.True(recorder.TryBegin(in pump, out var pumpToken));
        Assert.True(recorder.TryBegin(in emission, out var emissionToken));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Accepted,
            recorder.Complete(in emissionToken, in counters));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Accepted,
            recorder.Complete(in pumpToken, in counters));
        Assert.True(recorder.Seal());

        Assert.Equal((ulong)30, TotalElapsed(recorder, ServiceCycleProfileSpan.SemanticPumpSummary));
        Assert.Equal((ulong)70, TotalElapsed(recorder, ServiceCycleProfileSpan.OverallPump));
    }

    [Fact]
    public void AnEnclosingSpanStillReportsTheGameWorkNestedInsideIt()
    {
        var recorder = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new ScriptedProfileRawClock(1_000, new long[] { 100, 102, 200, 210, 240, 300 }),
            new ProvenIncrementingProfileAllocationCounter());
        var counters = default(ServiceCycleProfileOperationCounters);
        var pump = Context(ServiceCycleProfileSpan.OverallPump);
        var work = Context(ServiceCycleProfileSpan.AutoBuyActionQueueRoomRead);

        Assert.True(recorder.TryBegin(in pump, out var pumpToken));
        Assert.True(recorder.TryBegin(in work, out var workToken));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Accepted,
            recorder.Complete(in workToken, in counters));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Accepted,
            recorder.Complete(in pumpToken, in counters));
        Assert.True(recorder.Seal());

        Assert.Equal((ulong)30, TotalElapsed(recorder, ServiceCycleProfileSpan.AutoBuyActionQueueRoomRead));
        Assert.Equal((ulong)100, TotalElapsed(recorder, ServiceCycleProfileSpan.OverallPump));
    }

    [Fact]
    public void TheFenceReachesEverySpanTheEmissionIsNestedInside()
    {
        var recorder = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new ScriptedProfileRawClock(
                1_000,
                new long[] { 100, 102, 200, 210, 220, 250, 280, 300 }),
            new ProvenIncrementingProfileAllocationCounter());
        var counters = default(ServiceCycleProfileOperationCounters);
        var pump = Context(ServiceCycleProfileSpan.OverallPump);
        var phase = Context(ServiceCycleProfileSpan.StartCycles);
        var emission = Context(ServiceCycleProfileSpan.SemanticStart);

        Assert.True(recorder.TryBegin(in pump, out var pumpToken));
        Assert.True(recorder.TryBegin(in phase, out var phaseToken));
        Assert.True(recorder.TryBegin(in emission, out var emissionToken));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Accepted,
            recorder.Complete(in emissionToken, in counters));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Accepted,
            recorder.Complete(in phaseToken, in counters));
        Assert.Equal(
            ServiceCycleProfileMeasurementResult.Accepted,
            recorder.Complete(in pumpToken, in counters));
        Assert.True(recorder.Seal());

        Assert.Equal((ulong)30, TotalElapsed(recorder, ServiceCycleProfileSpan.SemanticStart));
        Assert.Equal((ulong)40, TotalElapsed(recorder, ServiceCycleProfileSpan.StartCycles));
        Assert.Equal((ulong)70, TotalElapsed(recorder, ServiceCycleProfileSpan.OverallPump));
    }

    private static ServiceCycleProfileContext Context(ServiceCycleProfileSpan span) =>
        ServiceCycleProfileAggregatorTests.Context((int)span, lifecycle: 7);

    private static ulong TotalElapsed(
        ServiceCycleProfileMeasurementRecorder recorder,
        ServiceCycleProfileSpan span)
    {
        for (var ordinal = 0; ordinal < recorder.GroupCount; ordinal++)
        {
            var aggregate = recorder.GetAggregate(ordinal);
            if (aggregate.StageCode == (int)span) return aggregate.TotalElapsedRawTicks;
        }
        Assert.Fail("No profile aggregate was recorded for " + span + ".");
        return 0;
    }
}
