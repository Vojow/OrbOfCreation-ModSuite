using System;
using System.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

public sealed class ServiceActionOutcomeWindowProjectionTests
{
    [Fact]
    public void CountsExactActionOutcomesAndKeepsTheLastBoundaryReason()
    {
        using var registry = Registry("outcomes.service");
        using var registration = registry.Register(new SyntheticServiceDefinition("outcomes.service"));
        var projection = ServiceActionOutcomeWindowProjection.Create(registry, 4);

        var completed = BatchReceipt.Completed(
            Identity("outcomes.service", 1),
            new BatchId(1),
            actionCount: 3,
            committedCount: 2,
            new ServiceNativeCallTotals(2, 2, 2),
            new MonotonicTimestamp(20),
            preNativeSkippedCount: 1);
        var rejected = BatchReceipt.Terminated(
            Identity("outcomes.service", 2),
            new BatchId(2),
            actionCount: 4,
            committedCount: 1,
            terminalIndex: 2,
            ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected),
            new ServiceNativeCallTotals(1, 1, 1),
            new MonotonicTimestamp(30),
            preNativeSkippedCount: 1);
        var faulted = BatchReceipt.Terminated(
            Identity("outcomes.service", 3),
            new BatchId(3),
            actionCount: 2,
            committedCount: 0,
            terminalIndex: 0,
            ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault),
            default,
            new MonotonicTimestamp(40));

        Observe(projection, completed, 20);
        Observe(projection, rejected, 30);
        Observe(projection, faulted, 40);

        var snapshot = CopySingle(projection);
        Assert.Equal(9, snapshot.Planned);
        Assert.Equal(3, snapshot.Committed);
        Assert.Equal(2, snapshot.Skipped);
        Assert.Equal(1, snapshot.Rejected);
        Assert.Equal(1, snapshot.Faulted);
        Assert.Equal(ServiceActionOutcomeBoundaryKind.Faulted, snapshot.LastBoundary.Kind);
        Assert.Equal(CommonActionResultCodes.AdapterFault.Value, snapshot.LastBoundary.Code);
    }

    [Fact]
    public void WindowEvictsWholeOldObservationsPerService()
    {
        using var registry = Registry("rolling.service");
        using var registration = registry.Register(new SyntheticServiceDefinition("rolling.service"));
        var projection = ServiceActionOutcomeWindowProjection.Create(registry, 2);
        var first = BatchReceipt.Completed(
            Identity("rolling.service", 1),
            new BatchId(1),
            2,
            new ServiceNativeCallTotals(2, 2, 2),
            new MonotonicTimestamp(20));
        var second = BatchReceipt.Completed(
            Identity("rolling.service", 2),
            new BatchId(2),
            actionCount: 1,
            committedCount: 0,
            default,
            new MonotonicTimestamp(30),
            preNativeSkippedCount: 1);
        var third = BatchReceipt.Terminated(
            Identity("rolling.service", 3),
            new BatchId(3),
            actionCount: 1,
            committedCount: 0,
            terminalIndex: 0,
            ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected),
            default,
            new MonotonicTimestamp(40));

        Observe(projection, first, 20);
        Observe(projection, second, 30);
        Observe(projection, third, 40);

        var snapshot = CopySingle(projection);
        Assert.Equal(2, snapshot.ObservationCount);
        Assert.Equal(2, snapshot.Planned);
        Assert.Equal(0, snapshot.Committed);
        Assert.Equal(1, snapshot.Skipped);
        Assert.Equal(1, snapshot.Rejected);
        Assert.Equal(ServiceActionOutcomeBoundaryKind.Rejected, snapshot.LastBoundary.Kind);
        Assert.Equal(CommonActionResultCodes.NativeRejected.Value, snapshot.LastBoundary.Code);
    }

    [Fact]
    public void CountsARealJournalFaultEvenWhenNoActionBatchWasCreated()
    {
        using var registry = Registry("fault-before-action.service");
        using var registration = registry.Register(
            new SyntheticServiceDefinition("fault-before-action.service"));
        var projection = ServiceActionOutcomeWindowProjection.Create(registry, 2);
        var state = default(ServiceStateProjectionSnapshot);
        var terminal = default(BatchReceipt);
        var fault = new ServiceFault(
            ServiceFaultCategory.Evaluation,
            CommonActionResultCodes.AdapterFault,
            occurrenceCount: 3,
            new MonotonicTimestamp(20));
        var observation = new DecisionJournalObservation(
            new ServiceCycleTraceServiceId(1),
            lifecycle: 1,
            configuration: 1,
            strategy: 0,
            cycle: 0,
            new MonotonicTimestamp(19),
            new MonotonicTimestamp(20),
            startDecisionCode: 0,
            captureDecisionCode: 0,
            hasWake: false,
            default,
            hasProjection: false,
            in state,
            in fault,
            in terminal);

        projection.Observe(in observation);

        var snapshot = CopySingle(projection);
        Assert.Equal(0, snapshot.Planned);
        Assert.Equal(1, snapshot.Faulted);
        Assert.Equal(ServiceActionOutcomeBoundaryKind.Faulted, snapshot.LastBoundary.Kind);
        Assert.Equal(CommonActionResultCodes.AdapterFault.Value, snapshot.LastBoundary.Code);
        Assert.Equal(ServiceFaultCategory.Evaluation, snapshot.LastBoundary.FaultCategory);
    }

    [Fact]
    public void RegisteredRosterIncludesEveryShapeAndNamesInfrastructureExplicitly()
    {
        using var registry = new ServiceCycleRegistry(
            3,
            new LifecycleGeneration(1),
            new ThreadSafeTestClock(1));
        using var source = registry.RegisterSource(
            new SourceServiceDefinition("looks.player.meaningful"));
        using var first = registry.Register(
            new SyntheticServiceDefinition("contains.world-collection.but-is-ordinary"));
        using var second = registry.Register(
            new SyntheticServiceDefinition("automation.second"));
        var projection = ServiceActionOutcomeWindowProjection.Create(registry, 2);
        var snapshots = new ServiceActionOutcomeSnapshot[3];

        var copied = projection.CopyTo(snapshots);

        Assert.True(copied.IsComplete);
        Assert.Equal(3, copied.AvailableCount);
        Assert.Equal(
            new[]
            {
                "looks.player.meaningful",
                "contains.world-collection.but-is-ordinary",
                "automation.second",
            },
            snapshots.Select(snapshot => snapshot.Service.Value));
        Assert.Equal(ServiceShape.Source, snapshots[0].Shape);
        Assert.All(snapshots.Skip(1), snapshot => Assert.Equal(ServiceShape.Ordinary, snapshot.Shape));
        Assert.All(snapshots, snapshot =>
        {
            Assert.Equal(0, snapshot.ObservationCount);
            Assert.Equal(0, snapshot.Planned);
        });
    }

    [Fact]
    public void LiveProjectionPublishesWithoutAConfiguredDiskJournal()
    {
        using var registry = Registry("live.service");
        using var registration = registry.Register(new SyntheticServiceDefinition("live.service"));
        registry.Seal();
        var source = new ServiceActionOutcomeWindowRegistry();
        using var pump = new SuiteFramePump(registry, null, source);
        var snapshots = new ServiceActionOutcomeSnapshot[1];

        var copied = source.CopyTo(snapshots);

        Assert.True(copied.IsComplete);
        Assert.Equal(1, copied.WrittenCount);
        Assert.Equal("live.service", snapshots[0].Service.Value);
    }

    private static ServiceCycleRegistry Registry(string _) => new(
        1,
        new LifecycleGeneration(1),
        new ThreadSafeTestClock(1));

    private static void Observe(
        ServiceActionOutcomeWindowProjection projection,
        BatchReceipt terminal,
        long observedAt)
    {
        var state = default(ServiceStateProjectionSnapshot);
        var fault = default(ServiceFault);
        var observation = new DecisionJournalObservation(
            new ServiceCycleTraceServiceId(1),
            terminal.Cycle.Lifecycle.Value,
            terminal.Cycle.Config.Value,
            terminal.Cycle.Strategy.Value,
            terminal.Cycle.Cycle.Value,
            new MonotonicTimestamp(observedAt - 1),
            new MonotonicTimestamp(observedAt),
            CommonServiceDecisionCodes.Ready.Value,
            0,
            true,
            WakePolicy.AfterBatch(new MonotonicDuration(1)),
            false,
            in state,
            in fault,
            in terminal);
        projection.Observe(in observation);
    }

    private static ServiceCycleIdentity Identity(string service, ulong cycle) => new(
        new ServiceId(service),
        new LifecycleGeneration(1),
        new ConfigGeneration(1),
        new StrategyGeneration(1),
        new WorldGeneration(1),
        new CycleId(cycle));

    private static ServiceActionOutcomeSnapshot CopySingle(
        ServiceActionOutcomeWindowProjection projection)
    {
        var snapshots = new ServiceActionOutcomeSnapshot[1];
        var copied = projection.CopyTo(snapshots);
        Assert.True(copied.IsComplete);
        Assert.Equal(1, copied.WrittenCount);
        return snapshots[0];
    }
}
