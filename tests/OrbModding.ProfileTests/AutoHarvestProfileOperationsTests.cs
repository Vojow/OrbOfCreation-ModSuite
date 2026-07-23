using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;
using static OrbModding.ProfileTests.AutoHarvestProfileTestSupport;

namespace OrbModding.ProfileTests;

public sealed class AutoHarvestProfileOperationsTests
{
    [Fact]
    public void NestedStageFaultsTheProfileWithoutEscapingIntoGameplay()
    {
        var recorder = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new IncrementingProfileRawClock(),
            new ProvenIncrementingProfileAllocationCounter());
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(recorder);
        var operations = new AutoHarvestProfileOperations(probe);
        var context = CaptureContext(serviceOrdinal: 0, frameIdentity: 1);
        var outer = operations.Begin(1001, context, ServiceCycleProfileTemperature.Warm);
        try
        {
            var nested = operations.Begin(1002, context, ServiceCycleProfileTemperature.Warm);
            Assert.False(nested.IsActive);
            nested.Abandon();
            outer.Complete();
        }
        finally
        {
            outer.Abandon();
        }

        Assert.Equal(ServiceCycleProfileProbeFault.StageOverlapRejected, probe.Fault);
        Assert.Equal(ServiceCycleProfileMeasurementFault.None, recorder.Fault);
        Assert.Same(recorder, probe.Detach());
        Assert.True(recorder.Seal());
        Assert.Equal(0, recorder.GroupCount);
    }

    [Fact]
    public void CounterExhaustionFaultsWithoutPublishingInventedOperations()
    {
        var recorder = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new IncrementingProfileRawClock(),
            new ProvenIncrementingProfileAllocationCounter());
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(recorder);
        var operations = new AutoHarvestProfileOperations(probe);
        var stage = operations.Begin(
            1001,
            CaptureContext(serviceOrdinal: 0, frameIdentity: 1),
            ServiceCycleProfileTemperature.Warm);
        try
        {
            operations.AddSelectedPairs(uint.MaxValue);
            operations.AddSelectedPairs(1);
            stage.Complete();
        }
        finally
        {
            stage.Abandon();
        }

        Assert.Equal(ServiceCycleProfileProbeFault.OperationCounterExhausted, probe.Fault);
        Assert.Equal(ServiceCycleProfileMeasurementFault.None, recorder.Fault);
        Assert.Same(recorder, probe.Detach());
        Assert.True(recorder.Seal());
        Assert.Equal(0, recorder.GroupCount);
    }

    [Fact]
    public void ActionStageUsesThePumpCoordinatesCarriedByTheActionContext()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutoHarvestProfileOperations(probe);
        var stage = operations.Begin(
            AutoHarvestServiceCycleProfileStageCodes.ActionFactRevalidation,
            ActionContext(serviceOrdinal: 3, frameIdentity: 42),
            ServiceCycleProfileTemperature.Warm);

        stage.Complete();

        var completed = Assert.Single(measurement.Completed);
        Assert.Equal(AutoHarvestServiceCycleProfileStageCodes.ActionFactRevalidation, completed.Context.StageCode);
        Assert.Equal(3, completed.Context.ServiceOrdinal);
        Assert.Equal(42UL, completed.Context.Frame);
        Assert.Equal(5UL, completed.Context.Cycle);
    }
}
