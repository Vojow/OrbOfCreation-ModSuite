using System;
using OrbAutomata;
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;
using static OrbModding.ProfileTests.AutoHarvestProfileTestSupport;

namespace OrbModding.ProfileTests;

public sealed class AutoHarvestCaptureProfileTests
{
    [Fact]
    public void CaptureRoutesFiveColdStagesWithExpectedPortEvidence()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutoHarvestProfileOperations(probe);
        var bindings = new BindingPort(operations);
        var reader = new CaptureStatePort(operations);
        var adapter = CreateAdapter(bindings, reader, operations);
        var context = CaptureContext(serviceOrdinal: 3, frameIdentity: 41);

        var disposition = adapter.Capture(
            Configuration(),
            new LifecycleGeneration(1),
            context,
            out var frame);

        Assert.Equal(AutoHarvestCycleCaptureDisposition.Captured, disposition);
        Assert.True(frame.OwnsActionFamily);
        Assert.Equal(ServiceCycleProfileTemperature.Warm, bindings.CurrentTemperature);
        Assert.Empty(measurement.Abandoned);
        Assert.Collection(
            measurement.Completed,
            item => AssertStage(item, 1001, stableIdReads: 2),
            item => AssertStage(item, 1002, fieldReads: 1, methodCalls: 1, listEntries: 1),
            item => AssertStage(item, 1003, methodCalls: 1, stableIdReads: 2),
            item => AssertStage(item, 1004, methodCalls: 1, stableIdReads: 2),
            item => AssertStage(item, 1005, selectedPairs: 2, readyPairs: 2));
        Assert.Same(measurement, probe.Detach());
    }

    [Fact]
    public void ExpectedNativeFailureAbandonsOnlyTheInterruptedStage()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutoHarvestProfileOperations(probe);
        var bindings = new BindingPort(operations);
        var reader = new CaptureStatePort(
            operations,
            new InvalidOperationException("native contract drift"));
        var adapter = CreateAdapter(bindings, reader, operations);
        var context = CaptureContext(serviceOrdinal: 0, frameIdentity: 1);

        var disposition = adapter.Capture(
            Configuration(),
            new LifecycleGeneration(1),
            context,
            out _);

        Assert.Equal(AutoHarvestCycleCaptureDisposition.Captured, disposition);
        Assert.Equal(new[] { 1001, 1005 }, StageCodes(measurement.Completed));
        Assert.Equal(new[] { 1002 }, StageCodes(measurement.Abandoned));
        Assert.Same(measurement, probe.Detach());
    }

    [Fact]
    public void PartialSiblingBindingKeepsUsefulCaptureButAbandonsBindingSample()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutoHarvestProfileOperations(probe);
        var bindings = new BindingPort(operations, treasureAvailable: false);
        var reader = new CaptureStatePort(operations);
        var adapter = CreateAdapter(bindings, reader, operations);

        var disposition = adapter.Capture(
            Configuration(),
            new LifecycleGeneration(1),
            CaptureContext(serviceOrdinal: 0, frameIdentity: 1),
            out var frame);

        Assert.Equal(AutoHarvestCycleCaptureDisposition.Captured, disposition);
        Assert.Equal(AutoHarvestPairCaptureKind.Captured, frame.Fruit.Kind);
        Assert.Equal(AutoHarvestPairCaptureKind.Unavailable, frame.Treasure.Kind);
        Assert.Equal(new[] { 1002, 1003, 1005 }, StageCodes(measurement.Completed));
        Assert.Equal(new[] { 1001 }, StageCodes(measurement.Abandoned));
        Assert.Equal(ServiceCycleProfileTemperature.ColdProcess, bindings.CurrentTemperature);
        Assert.Same(measurement, probe.Detach());
    }

    [Fact]
    public void MissingProfileCoordinatesStopObservationWithoutChangingCapture()
    {
        var probe = new ServiceCycleProfileProbe();
        var operations = new AutoHarvestProfileOperations(probe);
        var bindings = new BindingPort(operations);
        var reader = new CaptureStatePort(operations);
        var adapter = CreateAdapter(bindings, reader, operations);

        var disposition = adapter.Capture(
            Configuration(),
            new LifecycleGeneration(1),
            CaptureContextWithoutProfileCoordinates(),
            out var frame);

        Assert.Equal(AutoHarvestCycleCaptureDisposition.Captured, disposition);
        Assert.Equal(AutoHarvestPairCaptureKind.Captured, frame.Fruit.Kind);
        Assert.Equal(AutoHarvestPairCaptureKind.Captured, frame.Treasure.Kind);
        Assert.Equal(ServiceCycleProfileProbeFault.ContextRejected, probe.Fault);
    }

    [Fact]
    public void DriftRacingWarmPreflightAbandonsOnlyBindingWithoutRelabelingCapture()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutoHarvestProfileOperations(probe);
        var bindings = new BindingPort(operations, driftDuringNextResolve: true);
        Assert.True(bindings.TryComplete(ServiceCycleProfileTemperature.ColdProcess));
        var reader = new CaptureStatePort(operations);
        var adapter = CreateAdapter(bindings, reader, operations);

        var disposition = adapter.Capture(
            Configuration(),
            new LifecycleGeneration(1),
            CaptureContext(serviceOrdinal: 0, frameIdentity: 1),
            out _);

        Assert.Equal(AutoHarvestCycleCaptureDisposition.Captured, disposition);
        Assert.Equal(new[] { 1002, 1003, 1004, 1005 }, StageCodes(measurement.Completed));
        Assert.All(measurement.Completed, item =>
            Assert.Equal(ServiceCycleProfileTemperature.Warm, item.Context.Temperature));
        Assert.Equal(new[] { 1001 }, StageCodes(measurement.Abandoned));
        Assert.Equal(
            ServiceCycleProfileTemperature.LifecycleRebind,
            bindings.CurrentTemperature);
        Assert.Same(measurement, probe.Detach());
    }

    private static void AssertStage(
        in CapturedMeasurement item,
        int stageCode,
        uint fieldReads = 0,
        uint methodCalls = 0,
        uint stableIdReads = 0,
        uint listEntries = 0,
        uint selectedPairs = 0,
        uint readyPairs = 0)
    {
        Assert.Equal(stageCode, item.Context.StageCode);
        Assert.Equal(3, item.Context.ServiceOrdinal);
        Assert.Equal((ulong)1, item.Context.Lifecycle);
        Assert.Equal((ulong)5, item.Context.Cycle);
        Assert.Equal((ulong)41, item.Context.Frame);
        Assert.Equal(ServiceCycleProfileTemperature.ColdProcess, item.Context.Temperature);
        Assert.Equal(fieldReads, item.Operations.ReflectedFieldReads);
        Assert.Equal(methodCalls, item.Operations.ReflectedMethodCalls);
        Assert.Equal(stableIdReads, item.Operations.StableIdReads);
        Assert.Equal(listEntries, item.Operations.ListEntries);
        Assert.Equal(selectedPairs, item.Operations.SelectedPairs);
        Assert.Equal(readyPairs, item.Operations.ReadyPairs);
        Assert.Equal((uint)0, item.Operations.InvocationArgumentArrays);
        Assert.Equal((uint)0, item.Operations.RecordCopies);
    }
}
