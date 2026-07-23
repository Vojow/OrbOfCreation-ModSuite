using System;
using System.Collections.Generic;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.FullTrace;

public sealed class FullTraceRuntimeSessionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    [Fact]
    public void IdleSessionOwnsNoStorageAndStartArmsUntilWorkerInitializationCompletes()
    {
        using var registry = Registry("trace.session.initialization");
        using var pump = new SuiteFramePump(registry);
        using var storage = new MemorySessionStorage(blockInitialization: true);
        using var session = new FullTraceRuntimeSession(pump, 1);

        Assert.Equal(FullTraceRuntimeSessionState.Idle, session.Snapshot.State);
        Assert.False(storage.InitializeEntered.IsSet);

        session.Start(new FullTraceSessionId(601), new ServiceCycleTraceSessionId(602), storage);
        Assert.True(storage.InitializeEntered.Wait(Deadline));
        session.Tick();
        Assert.Equal(FullTraceRuntimeSessionState.Arming, session.Snapshot.State);

        storage.InitializeRelease.Set();
        AdvanceTo(session, FullTraceRuntimeSessionState.Recording);
    }

    [Fact]
    public void UserStopDetachesAndPublishesACompleteDiagnosticSession()
    {
        using var registry = Registry("trace.session.complete");
        using var pump = new SuiteFramePump(registry);
        using var storage = new MemorySessionStorage();
        using var session = new FullTraceRuntimeSession(pump, 1);
        session.Start(new FullTraceSessionId(701), new ServiceCycleTraceSessionId(702), storage);
        AdvanceTo(session, FullTraceRuntimeSessionState.Recording);

        pump.PumpFrame(1);
        session.RequestStop();
        AdvanceTo(session, FullTraceRuntimeSessionState.Complete);

        var snapshot = session.Snapshot;
        var segment = FullTraceSegmentCodec.Decode(Assert.Single(storage.Segments));
        var manifest = FullTraceManifestCodec.Decode(Assert.IsType<byte[]>(storage.Manifest));
        Assert.True(snapshot.ManifestCommitted);
        Assert.Equal(1, snapshot.SegmentCount);
        Assert.Equal(segment.Events.Length, snapshot.WrittenRecords);
        Assert.Contains(segment.Events, item => item.Kind == ServiceCycleSemanticEventKind.PumpCompleted);
        Assert.Equal(FullTraceCompleteness.Complete, manifest.Completeness);
        Assert.Equal(FullTraceTerminalReason.UserStopped, manifest.Reason);
    }

    [Fact]
    public void SegmentWriteFailureStopsOnlyTracingAndPublishesTheDurablePrefix()
    {
        using var registry = Registry("trace.session.write-failure");
        using var pump = new SuiteFramePump(registry);
        using var storage = new MemorySessionStorage(failSegmentWrite: true);
        using var session = new FullTraceRuntimeSession(pump, 1);
        session.Start(new FullTraceSessionId(801), new ServiceCycleTraceSessionId(802), storage);
        AdvanceTo(session, FullTraceRuntimeSessionState.Recording);

        pump.PumpFrame(1);
        session.RequestStop();
        AdvanceTo(session, FullTraceRuntimeSessionState.Incomplete);

        Assert.True(pump.PumpFrame(2).Accepted);
        var manifest = FullTraceManifestCodec.Decode(Assert.IsType<byte[]>(storage.Manifest));
        Assert.Equal(FullTraceTerminalReason.WriteFailed, manifest.Reason);
        Assert.Equal(0UL, manifest.WrittenRecords);
        Assert.Equal(1UL, manifest.FirstIncompleteTransportSequence);
    }

    [Fact]
    public void ShutdownDuringACyclePublishesIncompleteEvidenceAndReleasesPumpOwnership()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.session.shutdown") { ActionCount = 0 };
        using var registration = registry.Register(
            definition,
            new ExecutionConfig(1),
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        using var firstStorage = new MemorySessionStorage();
        using var session = new FullTraceRuntimeSession(pump, 1);
        session.Start(new FullTraceSessionId(901), new ServiceCycleTraceSessionId(902), firstStorage);
        AdvanceTo(session, FullTraceRuntimeSessionState.Recording);

        var frame = 1L;
        PumpUntil(pump, ref frame, report => report.CapturesAttempted != 0, "capture admission");
        session.Dispose();

        Assert.True(firstStorage.ManifestPublished.Wait(Deadline));
        var interrupted = FullTraceManifestCodec.Decode(Assert.IsType<byte[]>(firstStorage.Manifest));
        Assert.Equal(FullTraceCompleteness.Incomplete, interrupted.Completeness);
        Assert.Equal(FullTraceTerminalReason.RuntimeShutdown, interrupted.Reason);

        definition.StartDecision = ServiceStartDecision.Wait(
            CommonServiceDecisionCodes.NotReady,
            WakePolicy.AfterDecision(new MonotonicDuration(1)));
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        PumpUntil(pump, ref frame, report => report.ResponsesAcquired != 0, "response acquisition");
        PumpUntil(
            pump,
            ref frame,
            _ => registration.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty,
            "cycle release");
        using var secondStorage = new MemorySessionStorage();
        using var next = new FullTraceRuntimeSession(pump, 1);
        next.Start(new FullTraceSessionId(903), new ServiceCycleTraceSessionId(904), secondStorage);
        AdvanceTo(next, FullTraceRuntimeSessionState.Recording);
        next.RequestStop();
        AdvanceTo(next, FullTraceRuntimeSessionState.Complete);
    }

    private static void AdvanceTo(
        FullTraceRuntimeSession session,
        FullTraceRuntimeSessionState expected)
    {
        Assert.True(
            SpinWait.SpinUntil(() =>
            {
                session.Tick();
                return session.Snapshot.State == expected;
            }, Deadline),
            $"Expected {expected}; observed {session.Snapshot.State}.");
    }

    private static SuiteFramePumpReport PumpUntil(
        SuiteFramePump pump,
        ref long frame,
        Func<SuiteFramePumpReport, bool> predicate,
        string transition)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var report = pump.PumpFrame(frame++);
            if (predicate(report)) return report;
        }
        throw new Xunit.Sdk.XunitException("Timed out waiting for " + transition + ".");
    }

    private static ServiceCycleRegistry Registry(string serviceId)
    {
        var registry = new ServiceCycleRegistry(1, new ThreadSafeTestClock(100));
        registry.Register(
            new ExecutionServiceDefinition(serviceId)
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

    private sealed class MemorySessionStorage : ISegmentSessionStorage, IDisposable
    {
        private readonly bool _blockInitialization;
        private readonly bool _failSegmentWrite;

        internal MemorySessionStorage(
            bool blockInitialization = false,
            bool failSegmentWrite = false)
        {
            _blockInitialization = blockInitialization;
            _failSegmentWrite = failSegmentWrite;
        }

        internal ManualResetEventSlim InitializeEntered { get; } = new();
        internal ManualResetEventSlim InitializeRelease { get; } = new();
        internal ManualResetEventSlim ManifestPublished { get; } = new();
        internal List<byte[]> Segments { get; } = new();
        internal byte[]? Manifest { get; private set; }

        public void Initialize()
        {
            InitializeEntered.Set();
            if (_blockInitialization) InitializeRelease.Wait();
        }

        public void CommitSegment(long ordinal, ReadOnlySpan<byte> bytes)
        {
            if (_failSegmentWrite) throw new InvalidOperationException("Injected segment write failure.");
            Assert.Equal(Segments.Count, ordinal);
            Segments.Add(bytes.ToArray());
        }

        public void CommitManifest(ReadOnlySpan<byte> bytes)
        {
            Manifest = bytes.ToArray();
            ManifestPublished.Set();
        }

        public void Dispose()
        {
            InitializeEntered.Dispose();
            InitializeRelease.Dispose();
            ManifestPublished.Dispose();
        }
    }
}
