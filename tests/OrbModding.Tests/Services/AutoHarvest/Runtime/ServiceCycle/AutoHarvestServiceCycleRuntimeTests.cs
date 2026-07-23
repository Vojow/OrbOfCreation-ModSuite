using System;
using System.Collections.Generic;
using System.Threading;
using BepInEx.Logging;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;
using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutoHarvestServiceCycleRuntimeTests
{
    [Fact]
    public void ProductionRuntimePublishesConfigurationAndLifecycleWithoutDisabledNativeReads()
    {
        var configuration = new MutableConfiguration();
        var frame = 0L;
        var lifecycle = 7L;
        var nativeRegistryReads = 0;
        var resolver = new TypedRegistryResolver(
            () => lifecycle,
            () =>
            {
                nativeRegistryReads++;
                return TypedRegistrySourceSnapshot.NotReady("not needed while disabled");
            },
            _ => null);
        var runtimeDiagnostics = new RuntimeDiagnosticsRegistry();
        var dependencies = new AutoHarvestServiceCycleDependencies(
            () => ++frame,
            () => lifecycle,
            resolver,
            ownsActionFamily: () => true,
            tryCaptureMutationPermit: () => true,
            runtimeDiagnostics: runtimeDiagnostics);
        using var runtime = AutoHarvestServiceCycleFactory.Create(
            configuration.Snapshot(),
            dependencies,
            new ManualLogSource());

        Assert.Equal(
            AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName,
            Assert.Single(runtimeDiagnostics.GetSnapshot()).Implementation);

        runtime.Tick(0);
        configuration.EvaluationIntervalSeconds = 2;
        configuration.EmergencyDisabled = true;
        runtime.Tick(0);

        Assert.Equal(TimeSpan.FromSeconds(1).Ticks, runtime.CurrentConfiguration.AutoHarvest.EvaluationInterval.Ticks);
        runtime.PublishSavedConfiguration(configuration.Snapshot());
        runtime.Tick(0);
        Assert.Equal(TimeSpan.FromSeconds(2).Ticks, runtime.CurrentConfiguration.AutoHarvest.EvaluationInterval.Ticks);
        Assert.True(runtime.EmergencyStopEngaged);
        Assert.Equal(3, frame);
        Assert.Equal(0, nativeRegistryReads);

        lifecycle = 8;
        runtime.InvalidateLifecycle();
        Assert.Equal(new LifecycleGeneration(8), runtime.CurrentLifecycle);

        configuration.EmergencyDisabled = false;
        runtime.PublishSavedConfiguration(configuration.Snapshot());
        runtime.Tick(0);
        Assert.False(runtime.EmergencyStopEngaged);

        runtime.Dispose();
        Assert.Empty(runtimeDiagnostics.GetSnapshot());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ProductionConstructionRejectsInvalidNativeLifecycle(long lifecycle)
    {
        var dependencies = new AutoHarvestServiceCycleDependencies(
            () => 1,
            () => lifecycle,
            new TypedRegistryResolver(
                () => lifecycle,
                () => TypedRegistrySourceSnapshot.NotReady("unused"),
                _ => null),
            ownsActionFamily: () => true,
            tryCaptureMutationPermit: () => true);

        Assert.Throws<InvalidOperationException>(() =>
            AutoHarvestServiceCycleFactory.Create(
                new MutableConfiguration().Snapshot(),
                dependencies,
                new ManualLogSource()));
    }

    [Fact]
    public void FailedConstructionAfterJournalStartupReleasesJournalAndPumpOwnership()
    {
        var configuration = new MutableConfiguration();
        const long lifecycle = 7;
        var runtimeDiagnostics = new RuntimeDiagnosticsRegistry();
        using var existingPublisher = new AutoHarvestRuntimeDiagnosticsPublisher(
            lifecycle,
            AutoHarvestPairHealth.NotObserved(AutoHarvestPair.FruitTree),
            AutoHarvestPairHealth.NotObserved(AutoHarvestPair.TreasureTree),
            AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName,
            runtimeDiagnostics);
        using var storage = new DecisionJournalRuntimeTestStorage();
        var journalSource = new JournalSource(storage);
        var status = new DecisionJournalStatusRegistry();
        var dependencies = new AutoHarvestServiceCycleDependencies(
            () => 1,
            () => lifecycle,
            new TypedRegistryResolver(
                () => lifecycle,
                () => TypedRegistrySourceSnapshot.NotReady("unused"),
                _ => null),
            ownsActionFamily: () => true,
            tryCaptureMutationPermit: () => true,
            runtimeDiagnostics: runtimeDiagnostics,
            observability: new AutomataServiceCycleObservabilityOptions(
                default,
                new AutomataDecisionJournalOptions(
                    status,
                    journalSource,
                    "journal")));

        Assert.Throws<InvalidOperationException>(() =>
            AutoHarvestServiceCycleFactory.Create(
                configuration.Snapshot(),
                dependencies,
                new ManualLogSource()));

        Assert.Equal(1, journalSource.CreateCount);
        Assert.Equal(DecisionJournalStatusState.Unavailable, status.Status.State);
        Assert.Single(runtimeDiagnostics.GetSnapshot());
    }

    [Fact]
    public void EnabledProductionReplayExportsTheAcceptedLifecycleBoundaryOffThread()
    {
        var configuration = new MutableConfiguration();
        var frame = 0L;
        var lifecycle = 7L;
        using var storage = new MemoryStorage();
        var dependencies = new AutoHarvestServiceCycleDependencies(
            () => ++frame,
            () => lifecycle,
            new TypedRegistryResolver(
                () => lifecycle,
                () => TypedRegistrySourceSnapshot.NotReady("disabled"),
                _ => null),
            ownsActionFamily: () => true,
            tryCaptureMutationPermit: () => true,
            replay: new AutomataReplayCaptureOptions(
                new ServiceCycleTraceSessionId(902),
                () => storage,
                new AutomataReplayTestObserver()));
        using var runtime = AutoHarvestServiceCycleFactory.Create(
            configuration.Snapshot(),
            dependencies,
            new ManualLogSource());

        Assert.True(storage.WaitUntilInitialized(TimeSpan.FromMilliseconds(500)));
        lifecycle = 8;
        runtime.InvalidateLifecycle();
        var committed = false;
        for (var frameAttempt = 0; frameAttempt < 4 && !committed; frameAttempt++)
        {
            runtime.Tick(0);
            committed = storage.WaitUntilCommitted(TimeSpan.FromMilliseconds(125));
        }
        Assert.True(committed);

        var artifact = ServiceCycleReplayArtifactCodec.Decode(storage.Latest);
        var foundBoundary = false;
        for (var index = 0; index < artifact.SemanticTrace.Count; index++)
        {
            if (artifact.SemanticTrace[index].Kind != ServiceCycleSemanticEventKind.LifecycleRequested)
                continue;
            foundBoundary = true;
            break;
        }
        Assert.True(foundBoundary);
        Assert.DoesNotContain(Environment.CurrentManagedThreadId, storage.WriterThreadIds);
    }

    [Fact]
    public void EnabledProductionReplayClosesAnIdleCompleteWindowBeforeSemanticOverwrite()
    {
        var configuration = new MutableConfiguration();
        var frame = 0L;
        const long lifecycle = 7;
        using var storage = new MemoryStorage();
        var dependencies = new AutoHarvestServiceCycleDependencies(
            () => ++frame,
            () => lifecycle,
            new TypedRegistryResolver(
                () => lifecycle,
                () => TypedRegistrySourceSnapshot.NotReady("disabled"),
                _ => null),
            ownsActionFamily: () => true,
            tryCaptureMutationPermit: () => true,
            replay: new AutomataReplayCaptureOptions(
                new ServiceCycleTraceSessionId(903),
                () => storage,
                new AutomataReplayTestObserver()));
        using var runtime = AutoHarvestServiceCycleFactory.Create(
            configuration.Snapshot(),
            dependencies,
            new ManualLogSource());

        Assert.True(storage.WaitUntilInitialized(TimeSpan.FromMilliseconds(500)));
        for (var attempt = 0; attempt < 4_096; attempt++)
            runtime.Tick(0);
        Assert.True(storage.WaitUntilCommitted(TimeSpan.FromMilliseconds(500)));

        var artifact = ServiceCycleReplayArtifactCodec.Decode(storage.Latest);
        Assert.True(artifact.SemanticTrace.IsComplete);
        Assert.False(artifact.SemanticTrace.Dropped.IsPresent);
        Assert.Equal(ServiceCycleReplayArtifactEligibilityCode.Complete, artifact.Eligibility);
        Assert.True(artifact.IsComplete);
        Assert.InRange(artifact.SemanticTrace.Count, 3_584, 4_031);
    }

    [Fact]
    public void ProductionHostCompositionOwnsTheDecisionJournal()
    {
        var configuration = new MutableConfiguration();
        var frame = 0L;
        const long lifecycle = 7;
        using var storage = new DecisionJournalRuntimeTestStorage();
        var status = new DecisionJournalStatusRegistry();
        var dependencies = new AutoHarvestServiceCycleDependencies(
            () => ++frame,
            () => lifecycle,
            new TypedRegistryResolver(
                () => lifecycle,
                () => TypedRegistrySourceSnapshot.NotReady("disabled"),
                _ => null),
            ownsActionFamily: () => true,
            tryCaptureMutationPermit: () => true,
            observability: new AutomataServiceCycleObservabilityOptions(
                default,
                new AutomataDecisionJournalOptions(
                    status,
                    new JournalSource(storage),
                    "journal")));
        using var runtime = AutoHarvestServiceCycleFactory.Create(
            configuration.Snapshot(),
            dependencies,
            new ManualLogSource());

        Assert.True(SpinWait.SpinUntil(() =>
        {
            runtime.Tick(0);
            return status.Status.State == DecisionJournalStatusState.Recording;
        }, TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(() =>
        {
            runtime.Tick(0);
            return status.Status.WrittenRecords > 0;
        }, TimeSpan.FromSeconds(2)));

        Assert.Equal("journal", status.Status.ArtifactName);
        Assert.NotEmpty(storage.ReadRecords());
        runtime.Dispose();
        Assert.Equal(DecisionJournalStatusState.Unavailable, status.Status.State);
    }

    [Fact]
    public void ProductionJournalWaitsForTheLiveEmergencyStateBeforeAttaching()
    {
        var configuration = new MutableConfiguration { EmergencyDisabled = true };
        var frame = 0L;
        const long lifecycle = 7;
        using var storage = new DecisionJournalRuntimeTestStorage(blockReconcile: true);
        var status = new DecisionJournalStatusRegistry();
        var dependencies = new AutoHarvestServiceCycleDependencies(
            () => ++frame,
            () => lifecycle,
            new TypedRegistryResolver(
                () => lifecycle,
                () => TypedRegistrySourceSnapshot.NotReady("disabled"),
                _ => null),
            ownsActionFamily: () => true,
            tryCaptureMutationPermit: () => true,
            observability: new AutomataServiceCycleObservabilityOptions(
                default,
                new AutomataDecisionJournalOptions(
                    status,
                    new JournalSource(storage),
                    "journal")));
        using var runtime = AutoHarvestServiceCycleFactory.Create(
            configuration.Snapshot(),
            dependencies,
            new ManualLogSource());
        Assert.True(storage.ReconcileEntered.Wait(TimeSpan.FromSeconds(2)));
        storage.ReconcileRelease.Set();
        Assert.True(storage.ReconcileCompleted.Wait(TimeSpan.FromSeconds(2)));

        runtime.Tick(0);

        Assert.Equal(DecisionJournalStatusState.Arming, status.Status.State);
        Assert.Empty(storage.ReadRecords());

        configuration.EmergencyDisabled = false;
        runtime.PublishSavedConfiguration(configuration.Snapshot());
        Assert.True(SpinWait.SpinUntil(() =>
        {
            runtime.Tick(0);
            return status.Status.State == DecisionJournalStatusState.Recording;
        }, TimeSpan.FromSeconds(2)));
        runtime.Dispose();
    }

    private sealed class MutableConfiguration
    {
        public bool MasterEnabled { get; set; }
        public bool EmergencyDisabled { get; set; }
        public bool ActiveMode { get; set; }
        public bool FruitSelected { get; set; } = true;
        public bool TreasureSelected { get; set; } = true;
        public float EvaluationIntervalSeconds { get; set; } = 1;
        public AutomataConfiguration Snapshot() => AutoHarvestConfigurationFactory.Create(
            MasterEnabled,
            EmergencyDisabled,
            ActiveMode,
            FruitSelected,
            TreasureSelected,
            MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(EvaluationIntervalSeconds)));
    }

    private sealed class MemoryStorage : IRestartAwareTraceSegmentStorage, IDisposable
    {
        private readonly object _gate = new();
        private readonly List<byte[]> _committed = new();
        private readonly HashSet<int> _writerThreadIds = new();
        private readonly ManualResetEventSlim _initialized = new();
        private readonly ManualResetEventSlim _committedSignal = new();

        public byte[] Latest
        {
            get { lock (_gate) return _committed[^1]; }
        }

        public int[] WriterThreadIds
        {
            get { lock (_gate) return new List<int>(_writerThreadIds).ToArray(); }
        }

        public TraceSegmentStorageRecovery Reconcile(int maximumCommittedSegments)
        {
            _initialized.Set();
            return new TraceSegmentStorageRecovery(0, 0, 0, 0);
        }

        public object BeginSegment(int ordinal)
        {
            RecordThread();
            return new List<byte>();
        }

        public void Append(object segment, ReadOnlySpan<byte> record)
        {
            RecordThread();
            ((List<byte>)segment).AddRange(record.ToArray());
        }

        public void CommitSegment(object segment)
        {
            lock (_gate)
            {
                _writerThreadIds.Add(Environment.CurrentManagedThreadId);
                _committed.Add(((List<byte>)segment).ToArray());
            }
            _committedSignal.Set();
        }

        public void DiscardSegment(object segment) => RecordThread();

        public void DeleteOldestCommitted()
        {
            lock (_gate)
            {
                _writerThreadIds.Add(Environment.CurrentManagedThreadId);
                _committed.RemoveAt(0);
            }
        }

        private void RecordThread()
        {
            lock (_gate) _writerThreadIds.Add(Environment.CurrentManagedThreadId);
        }

        public bool WaitUntilInitialized(TimeSpan timeout) => _initialized.Wait(timeout);

        public bool WaitUntilCommitted(TimeSpan timeout) => _committedSignal.Wait(timeout);

        public void Dispose()
        {
            _initialized.Dispose();
            _committedSignal.Dispose();
        }
    }

    private sealed class JournalSource : IAutomataDecisionJournalSource
    {
        private readonly DecisionJournalRuntimeTestStorage _storage;

        internal JournalSource(DecisionJournalRuntimeTestStorage storage) => _storage = storage;

        internal int CreateCount { get; private set; }

        public AutomataDecisionJournalSpec Create()
        {
            CreateCount++;
            return new AutomataDecisionJournalSpec(
                _storage,
                new DecisionJournalRunId(1),
                maximumCommittedSegments: 4,
                blockCount: 3,
                new MonotonicDuration(1));
        }
    }
}
