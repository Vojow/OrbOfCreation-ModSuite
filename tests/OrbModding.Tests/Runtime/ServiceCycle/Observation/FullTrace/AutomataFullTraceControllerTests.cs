using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Control;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.FullTrace;

public sealed class AutomataFullTraceControllerTests
{
    private static readonly TimeSpan Deadline = ServiceCycleTestDeadline.Value;

    private static readonly ServiceCycleTraceRoster TestRoster = new(new[]
    {
        new ServiceCycleTraceRosterEntry(
            ServiceCycleTraceRoster.ServiceKind,
            1,
            "orbautomata.auto-harvest",
            "Auto Harvest"),
    });

    /// <summary>
    /// The roster lands as soon as the session is recording, before any event and before the manifest
    /// that seals the directory, so a capture stopped immediately still says what it was watching.
    /// </summary>
    [Fact]
    public void AnArmedSessionRecordsWhatItCallsItsServices()
    {
        var clock = new VirtualMonotonicClock(new MonotonicTimestamp(100));
        using var registry = Registry(clock);
        using var pump = new SuiteFramePump(registry);
        var control = new ManualFullTraceControlRegistry();
        using var storage = new MemoryStorage();
        var sessions = new SessionSource(storage);
        var options = new AutomataFullTraceOptions(control, sessions);
        using var controller = Assert.IsType<AutomataFullTraceController>(
            AutomataFullTraceController.TryCreate(pump, 1, TestRoster, clock, in options));

        Assert.Equal(ManualFullTraceCommandResult.Accepted, control.RequestStart());
        AdvanceTo(controller, control, ManualFullTraceState.Recording);

        Assert.True(storage.SideArtifacts.TryGetValue(TraceRosterFormat.FileName, out var written));
        var decoded = TraceRosterFormat.Decode(Encoding.UTF8.GetString(written!));
        Assert.Equal(1, decoded.Count);
        Assert.Equal(1UL, decoded[0].Identity);
        Assert.Equal("Auto Harvest", decoded[0].DisplayName);
    }

    [Fact]
    public void IdleControllerAllocatesNoSessionAndStartStopPublishesExactStatus()
    {
        var clock = new VirtualMonotonicClock(new MonotonicTimestamp(100));
        using var registry = Registry(clock);
        using var pump = new SuiteFramePump(registry);
        var control = new ManualFullTraceControlRegistry();
        using var storage = new MemoryStorage();
        var sessions = new SessionSource(storage);
        var options = new AutomataFullTraceOptions(control, sessions);
        using var controller = Assert.IsType<AutomataFullTraceController>(
            AutomataFullTraceController.TryCreate(pump, 1, TestRoster, clock, in options));

        Assert.Equal(0, sessions.CreateCount);
        Assert.Equal(ManualFullTraceState.Idle, control.Status.State);
        Assert.Equal(ManualFullTraceCommandResult.Accepted, control.RequestStart());
        AdvanceTo(controller, control, ManualFullTraceState.Recording);
        Assert.Equal(1, sessions.CreateCount);
        Assert.Equal("session-0000000000000065", control.Status.ArtifactName);

        var revision = control.Revision;
        pump.PumpFrame(1);
        controller.AfterPump();
        pump.PumpFrame(2);
        controller.AfterPump();
        Assert.Equal(revision, control.Revision);
        clock.Advance(MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(3)));
        controller.BeforePump();
        Assert.Equal(revision + 1, control.Revision);
        Assert.Equal(ManualFullTraceCommandResult.Accepted, control.RequestStop());
        AdvanceTo(controller, control, ManualFullTraceState.Complete);

        Assert.Equal(TimeSpan.FromSeconds(3), control.Status.Duration);
        Assert.Equal(ManualFullTraceResult.UserStopped, control.Status.Result);
        Assert.True(control.Status.ManifestCommitted);
        Assert.True(control.Status.AcceptedRecords > 0);
        Assert.True(control.Status.BytesWritten > FullTraceManifestCodec.ManifestBytes);
    }

    [Fact]
    public void AutomaticSessionStartsWithoutACommandAndShutdownFlushesItsManifest()
    {
        var clock = new VirtualMonotonicClock(new MonotonicTimestamp(100));
        using var registry = Registry(clock);
        using var pump = new SuiteFramePump(registry);
        var control = new ManualFullTraceControlRegistry();
        using var storage = new MemoryStorage();
        var sessions = new SessionSource(storage);
        var options = new AutomataFullTraceOptions(control, sessions);
        var controller = Assert.IsType<AutomataFullTraceController>(
            AutomataFullTraceController.TryCreate(pump, 1, TestRoster, clock, in options));

        controller.StartAutomatically();
        AdvanceTo(controller, control, ManualFullTraceState.Recording);
        Assert.Equal(1, sessions.CreateCount);
        pump.PumpFrame(1);
        controller.AfterPump();

        controller.Dispose();

        Assert.True(storage.ManifestCommitted.Wait(Deadline));
        var manifest = FullTraceManifestCodec.Decode(Assert.IsType<byte[]>(storage.Manifest));
        Assert.Equal(FullTraceTerminalReason.RuntimeShutdown, manifest.Reason);
        Assert.True(manifest.WrittenRecords > 0);
    }

    [Fact]
    public void PrePumpTickCapturesTheRealSameFrameEmergencyTransition()
    {
        var clock = new VirtualMonotonicClock(new MonotonicTimestamp(100));
        using var registry = Registry(clock);
        using var pump = new SuiteFramePump(registry);
        var control = new ManualFullTraceControlRegistry();
        using var storage = new MemoryStorage();
        var sessions = new SessionSource(storage);
        var options = new AutomataFullTraceOptions(control, sessions);
        using var controller = Assert.IsType<AutomataFullTraceController>(
            AutomataFullTraceController.TryCreate(pump, 1, TestRoster, clock, in options));
        control.RequestStart();
        AdvanceTo(controller, control, ManualFullTraceState.Recording);

        controller.BeforePump();
        pump.SetEmergencyStop(true);
        pump.PumpFrame(1);
        controller.AfterPump();
        control.RequestStop();
        AdvanceTo(controller, control, ManualFullTraceState.Complete);

        var segment = FullTraceSegmentCodec.Decode(Assert.Single(storage.Segments));
        Assert.Contains(segment.Events, item =>
            item.Kind == ServiceCycleSemanticEventKind.EmergencyEntered);
    }

    [Fact]
    public void SynchronousStartFailureIsContainedAndASecondSessionCanStart()
    {
        var clock = new VirtualMonotonicClock(new MonotonicTimestamp(100));
        using var registry = Registry(clock);
        using var pump = new SuiteFramePump(registry);
        var control = new ManualFullTraceControlRegistry();
        using var storage = new MemoryStorage();
        var sessions = new SessionSource(storage, failFirst: true);
        var options = new AutomataFullTraceOptions(control, sessions);
        using var controller = Assert.IsType<AutomataFullTraceController>(
            AutomataFullTraceController.TryCreate(pump, 1, TestRoster, clock, in options));

        control.RequestStart();
        controller.BeforePump();
        Assert.Equal(ManualFullTraceState.Incomplete, control.Status.State);
        Assert.Equal(ManualFullTraceResult.InitializationFailed, control.Status.Result);

        Assert.Equal(ManualFullTraceCommandResult.Accepted, control.RequestStart());
        AdvanceTo(controller, control, ManualFullTraceState.Recording);
        control.RequestStop();
        AdvanceTo(controller, control, ManualFullTraceState.Complete);
    }

    [Fact]
    public void StoppingStatusHidesTerminalFaultEvidenceUntilTheWriterFinishes()
    {
        var clock = new VirtualMonotonicClock(new MonotonicTimestamp(100));
        using var registry = Registry(clock);
        using var pump = new SuiteFramePump(registry);
        var control = new ManualFullTraceControlRegistry();
        using var storage = new MemoryStorage(failSegmentWrite: true, blockManifest: true);
        var options = new AutomataFullTraceOptions(control, new SessionSource(storage));
        using var controller = Assert.IsType<AutomataFullTraceController>(
            AutomataFullTraceController.TryCreate(pump, 1, TestRoster, clock, in options));
        control.RequestStart();
        AdvanceTo(controller, control, ManualFullTraceState.Recording);

        pump.PumpFrame(1);
        control.RequestStop();
        controller.BeforePump();
        Assert.True(storage.ManifestEntered.Wait(Deadline));
        try
        {
            controller.BeforePump();
            Assert.Equal(ManualFullTraceState.Stopping, control.Status.State);
            Assert.Equal(0, control.Status.FirstIncompleteSequence);
            Assert.False(control.Status.ManifestCommitted);
        }
        finally
        {
            storage.ManifestRelease.Set();
        }

        AdvanceTo(controller, control, ManualFullTraceState.Incomplete);
        Assert.True(control.Status.FirstIncompleteSequence > 0);
        Assert.True(control.Status.ManifestCommitted);
    }

    [Fact]
    public void ExistingProducerPreventsOnlyTheOptionalController()
    {
        var clock = new VirtualMonotonicClock(new MonotonicTimestamp(100));
        using var registry = Registry(clock);
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var control = new ManualFullTraceControlRegistry();
        using var existing = control.Register();
        using var storage = new MemoryStorage();
        var options = new AutomataFullTraceOptions(control, new SessionSource(storage));

        Assert.Null(AutomataFullTraceController.TryCreate(pump, 1, TestRoster, clock, in options));
        Assert.True(pump.PumpFrame(1).Accepted);
    }

    [Fact]
    public void PostPumpPhaseNeverConsumesAControlCommand()
    {
        var clock = new VirtualMonotonicClock(new MonotonicTimestamp(100));
        using var registry = Registry(clock);
        using var pump = new SuiteFramePump(registry);
        var control = new ManualFullTraceControlRegistry();
        using var storage = new MemoryStorage();
        var options = new AutomataFullTraceOptions(control, new SessionSource(storage));
        using var controller = Assert.IsType<AutomataFullTraceController>(
            AutomataFullTraceController.TryCreate(pump, 1, TestRoster, clock, in options));
        control.RequestStart();
        AdvanceTo(controller, control, ManualFullTraceState.Recording);

        Assert.Equal(ManualFullTraceCommandResult.Accepted, control.RequestStop());
        controller.AfterPump();
        Assert.Equal(ManualFullTraceState.Recording, control.Status.State);
        controller.BeforePump();
        Assert.NotEqual(ManualFullTraceState.Recording, control.Status.State);
    }

    [Fact]
    public void RelativeArtifactPathNeverIncludesTheMachineRoot()
    {
        Assert.Equal(
            "BepInEx/config/OrbOfCreation-ModSuite/trace/" + AutomataTraceRunRoot.RunName +
                "/full/session-0000000000000001",
            AutomataFullTracePathPolicy.FormatRelativeArtifactPath("session-0000000000000001"));
        Assert.Throws<ArgumentException>(() =>
            AutomataFullTracePathPolicy.FormatRelativeArtifactPath("private/session"));
    }

    private static void AdvanceTo(
        AutomataFullTraceController controller,
        ManualFullTraceControlRegistry control,
        ManualFullTraceState expected)
    {
        Assert.True(
            SpinWait.SpinUntil(() =>
            {
                controller.BeforePump();
                controller.AfterPump();
                return control.Status.State == expected;
            }, Deadline),
            $"Expected {expected}; observed {control.Status.State}.");
    }

    private static ServiceCycleRegistry Registry(IMonotonicClock clock)
    {
        var registry = new ServiceCycleRegistry(1, clock);
        registry.Register(
            new ExecutionServiceDefinition("auto-harvest.full-trace")
            {
                StartDecision = ServiceStartDecision.Wait(
                    CommonServiceDecisionCodes.NotReady,
                    WakePolicy.AfterDecision(new MonotonicDuration(1))),
            },
            new LifecycleGeneration(1));
        registry.Seal();
        return registry;
    }

    private sealed class SessionSource : IAutomataFullTraceSessionSource
    {
        private readonly ISegmentSessionStorage _storage;
        private readonly bool _failFirst;

        internal SessionSource(ISegmentSessionStorage storage, bool failFirst = false)
        {
            _storage = storage;
            _failFirst = failFirst;
        }

        internal int CreateCount { get; private set; }

        public AutomataFullTraceSessionSpec Create()
        {
            CreateCount++;
            if (_failFirst && CreateCount == 1)
                throw new InvalidOperationException("Injected session creation failure.");
            var session = new FullTraceSessionId((ulong)(100 + CreateCount));
            return new AutomataFullTraceSessionSpec(
                session,
                new ServiceCycleTraceSessionId((ulong)(200 + CreateCount)),
                _storage,
                "session-" + session.Value.ToString("x16"));
        }
    }

    private sealed class MemoryStorage : ISegmentSessionStorage, ISessionSideArtifactSink, IDisposable
    {
        private readonly bool _blockManifest;
        private readonly bool _failSegmentWrite;

        internal MemoryStorage(bool failSegmentWrite = false, bool blockManifest = false)
        {
            _failSegmentWrite = failSegmentWrite;
            _blockManifest = blockManifest;
        }

        internal ManualResetEventSlim ManifestEntered { get; } = new();
        internal ManualResetEventSlim ManifestRelease { get; } = new();

        /// <summary>
        /// Set once <see cref="Manifest"/> holds the committed bytes.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="ManifestEntered"/>, which fires on the way in so a test can catch
        /// the commit while it is still blocked. A test that reads the bytes must wait for this one:
        /// entering the callback says nothing about the field the test is about to read.
        /// </remarks>
        internal ManualResetEventSlim ManifestCommitted { get; } = new();
        internal List<byte[]> Segments { get; } = new();
        internal byte[]? Manifest { get; private set; }
        internal Dictionary<string, byte[]> SideArtifacts { get; } = new(StringComparer.Ordinal);

        public void Initialize() { }

        public void CommitSideArtifact(string name, ReadOnlySpan<byte> bytes)
        {
            Assert.Null(Manifest);
            SideArtifacts[name] = bytes.ToArray();
        }

        public void CommitSegment(long ordinal, ReadOnlySpan<byte> bytes)
        {
            if (_failSegmentWrite) throw new InvalidOperationException("Injected segment write failure.");
            Assert.Equal(Segments.Count, ordinal);
            Segments.Add(bytes.ToArray());
        }

        public void CommitManifest(ReadOnlySpan<byte> bytes)
        {
            ManifestEntered.Set();
            if (_blockManifest) ManifestRelease.Wait();
            Manifest = bytes.ToArray();
            ManifestCommitted.Set();
        }

        public void Dispose()
        {
            ManifestRelease.Set();
            ManifestEntered.Dispose();
            ManifestRelease.Dispose();
            ManifestCommitted.Dispose();
        }
    }
}
