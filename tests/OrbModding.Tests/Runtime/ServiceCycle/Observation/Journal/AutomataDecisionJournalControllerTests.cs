using System;
using System.Threading;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;
using OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

public sealed class AutomataDecisionJournalControllerTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    [Fact]
    public void ControllerRecordsAndPublishesTransportStatus()
    {
        using var registry = Registry(new IncrementingTestClock(100));
        using var pump = new SuiteFramePump(registry);
        using var storage = new DecisionJournalRuntimeTestStorage();
        var status = new DecisionJournalStatusRegistry();
        var source = new Source(storage);
        var options = Options(status, source);
        var controller = Assert.IsType<AutomataDecisionJournalController>(
            AutomataDecisionJournalController.TryCreate(
                pump,
                in options,
                new ManualLogSource()));
        AdvanceTo(controller, status, DecisionJournalStatusState.Recording);

        Assert.True(pump.PumpFrame(1).Accepted);
        Assert.True(SpinWait.SpinUntil(() =>
        {
            controller.Tick();
            return status.Status.WrittenRecords > 0;
        }, Deadline));

        Assert.Equal("journal", status.Status.ArtifactName);
        Assert.True(status.Status.AcceptedRecords > 0);
        Assert.True(status.Status.BytesWritten > 0);
        Assert.True(status.Status.WrittenSegments > 0);
        Assert.NotEmpty(storage.ReadRecords());
        Assert.Equal(1, source.CreateCount);

        Assert.True(controller.DisposeWithPump());
        Assert.True(pump.IsDisposed);
        Assert.Equal(DecisionJournalStatusState.Unavailable, status.Status.State);
    }

    [Fact]
    public void SynchronousJournalFailureLeavesThePumpOperational()
    {
        using var registry = Registry(new IncrementingTestClock(100));
        using var pump = new SuiteFramePump(registry);
        var status = new DecisionJournalStatusRegistry();
        var source = new Source();
        var options = Options(status, source);
        var controller = Assert.IsType<AutomataDecisionJournalController>(
            AutomataDecisionJournalController.TryCreate(
                pump,
                in options,
                new ManualLogSource()));

        Assert.Equal(DecisionJournalStatusState.Faulted, status.Status.State);
        Assert.Equal(DecisionJournalStatusResult.InitializationFailed, status.Status.Result);
        Assert.True(pump.PumpFrame(1).Accepted);
        Assert.Equal(1, source.CreateCount);
        Assert.False(controller.DisposeWithPump());
        pump.Dispose();
    }

    [Fact]
    public void ExistingProducerPreventsASecondStorageClaim()
    {
        using var registry = Registry(new IncrementingTestClock(100));
        using var pump = new SuiteFramePump(registry);
        using var storage = new DecisionJournalRuntimeTestStorage();
        var status = new DecisionJournalStatusRegistry();
        using var existing = status.Register();
        var source = new Source(storage);
        var options = Options(status, source);

        var controller = AutomataDecisionJournalController.TryCreate(
            pump,
            in options,
            new ManualLogSource());

        Assert.Null(controller);
        Assert.Equal(0, source.CreateCount);
        Assert.True(pump.PumpFrame(1).Accepted);
    }

    [Fact]
    public void BackgroundWriteFailureDetachesOnlyTheJournal()
    {
        using var registry = Registry(new IncrementingTestClock(100));
        using var pump = new SuiteFramePump(registry);
        using var storage = new DecisionJournalRuntimeTestStorage(failCommit: true);
        var status = new DecisionJournalStatusRegistry();
        var options = Options(status, new Source(storage));
        var controller = Assert.IsType<AutomataDecisionJournalController>(
            AutomataDecisionJournalController.TryCreate(
                pump,
                in options,
                new ManualLogSource()));
        AdvanceTo(controller, status, DecisionJournalStatusState.Recording);

        Assert.True(pump.PumpFrame(1).Accepted);
        Assert.True(storage.CommitEntered.Wait(Deadline));
        AdvanceTo(controller, status, DecisionJournalStatusState.Faulted);

        Assert.Equal(DecisionJournalStatusResult.WriteFailed, status.Status.Result);
        Assert.True(pump.PumpFrame(2).Accepted);
        Assert.True(controller.DisposeWithPump());
    }

    [Fact]
    public void ConsumerFailureIsPublishedOnlyAfterTransportFaults()
    {
        var runningTransport = Metrics(
            BufferedSegmentStatus.Running,
            BufferedSegmentFaultReason.None,
            firstIncompleteSequence: 0);
        var consumer = new DecisionJournalConsumerMetrics(
            retainedSegments: 1,
            startupPrunedSegments: 0,
            staleTemporaryFilesRemoved: 0,
            evictedSegments: 0,
            DecisionJournalConsumerFaultReason.RetentionFailed);
        var pending = new DecisionJournalRuntimeSnapshot(
            DecisionJournalRuntimeState.Recording,
            attached: true,
            in runningTransport,
            in consumer);

        var pendingStatus = AutomataDecisionJournalController.MapStatus(in pending, "journal");

        Assert.Equal(DecisionJournalStatusState.Recording, pendingStatus.State);
        Assert.Equal(DecisionJournalStatusResult.None, pendingStatus.Result);
        Assert.Equal(0, pendingStatus.FirstIncompleteSequence);

        var faultingTransport = Metrics(
            BufferedSegmentStatus.Faulting,
            BufferedSegmentFaultReason.ProducerFailed,
            firstIncompleteSequence: 2);
        var faulting = new DecisionJournalRuntimeSnapshot(
            DecisionJournalRuntimeState.Stopping,
            attached: false,
            in faultingTransport,
            in consumer);

        var faultingStatus = AutomataDecisionJournalController.MapStatus(in faulting, "journal");

        Assert.Equal(DecisionJournalStatusState.Stopping, faultingStatus.State);
        Assert.Equal(DecisionJournalStatusResult.RetentionFailed, faultingStatus.Result);
        Assert.Equal(2, faultingStatus.FirstIncompleteSequence);
    }

    [Fact]
    public void RelativeJournalPathNeverIncludesTheMachineRoot()
    {
        Assert.Equal(
            "BepInEx/config/OrbOfCreation-ModSuite/trace/journal",
            AutomataDecisionJournalPathPolicy.FormatRelativeArtifactPath("journal"));
        Assert.Throws<ArgumentException>(() =>
            AutomataDecisionJournalPathPolicy.FormatRelativeArtifactPath("private/journal"));
        Assert.Equal(10, AutomataDecisionJournalPathPolicy.LiveCandidateBlockCount);
        Assert.Equal(10_080, AutomataDecisionJournalPathPolicy.LiveCandidateMaximumCommittedSegments);
    }

    [Fact]
    public void ProductionPathPolicyCreatesTheLiveCandidateExactlyOnce()
    {
        var options = AutomataDecisionJournalPathPolicy.Create(new DecisionJournalStatusRegistry());

        var spec = Assert.IsAssignableFrom<IAutomataDecisionJournalSource>(options.Source).Create();

        Assert.IsType<OrbModding.Common.Runtime.Tracing.FileTraceSegmentStorage>(spec.Storage);
        Assert.True(spec.Run.IsValid);
        Assert.Equal(10, spec.BlockCount);
        Assert.Equal(10_080, spec.MaximumCommittedSegments);
        Assert.Equal(
            MonotonicDuration.FromTimeSpan(TimeSpan.FromMinutes(1)),
            spec.CheckpointInterval);
        Assert.Throws<InvalidOperationException>(() => options.Source!.Create());
    }

    private static AutomataDecisionJournalOptions Options(
        DecisionJournalStatusRegistry status,
        IAutomataDecisionJournalSource source) => new(status, source, "journal");

    private static BufferedSegmentMetrics Metrics(
        BufferedSegmentStatus state,
        BufferedSegmentFaultReason fault,
        long firstIncompleteSequence) => new(
        state,
        fault,
        acceptedRecords: 1,
        writtenRecords: 1,
        discardedRecords: 0,
        bytesWritten: 512,
        sealedBlocks: 1,
        writtenBlocks: 1,
        discardedBlocks: 0,
        pendingBlocks: 0,
        peakPendingBlocks: 1,
        firstIncompleteSequence);

    private static void AdvanceTo(
        AutomataDecisionJournalController controller,
        DecisionJournalStatusRegistry status,
        DecisionJournalStatusState expected)
    {
        Assert.True(
            SpinWait.SpinUntil(() =>
            {
                controller.Tick();
                return status.Status.State == expected;
            }, Deadline),
            $"Expected {expected}; observed {status.Status.State}.");
    }

    private static ServiceCycleRegistry Registry(IMonotonicClock clock)
    {
        var registry = new ServiceCycleRegistry(1, clock);
        registry.Register(
            new ExecutionServiceDefinition("auto-harvest.decision-journal")
            {
                StartDecision = ServiceStartDecision.Wait(
                    CommonServiceDecisionCodes.NotReady,
                    WakePolicy.AfterDecision(new MonotonicDuration(1))),
            },
            new ExecutionConfig(1),
            new LifecycleGeneration(1));
        registry.Seal();
        return registry;
    }

    private sealed class Source : IAutomataDecisionJournalSource
    {
        private readonly DecisionJournalRuntimeTestStorage? _storage;

        internal Source(DecisionJournalRuntimeTestStorage? storage = null) => _storage = storage;

        internal int CreateCount { get; private set; }

        public AutomataDecisionJournalSpec Create()
        {
            CreateCount++;
            if (_storage is null) throw new InvalidOperationException("Injected journal source failure.");
            return new AutomataDecisionJournalSpec(
                _storage,
                new DecisionJournalRunId(1),
                maximumCommittedSegments: 4,
                blockCount: 3,
                new MonotonicDuration(1));
        }
    }
}
