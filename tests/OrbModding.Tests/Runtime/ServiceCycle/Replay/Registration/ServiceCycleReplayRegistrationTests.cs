using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Registration;

public sealed class ServiceCycleReplayRegistrationTests
{
    [Fact]
    public void OfflineWaitIsOwnerThreadAffineAndDisposedRegistrationFailsClosed()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1), clock);
        var session = new ServiceCycleReplaySession(
            new ServiceCycleTraceSessionId(92),
            new ServiceCycleReplaySessionOptions(true, 128, 32, 4));
        var replay = registry.RegisterReplay(
            new ReplayDefinition(new ReplayControl()),
            new ReplayConfig(2),
            session);

        Exception? wrongThreadFailure = null;
        var wrongThread = new Thread(() =>
        {
            try { replay.WaitForResponseReady(TimeSpan.Zero); }
            catch (Exception ex) { wrongThreadFailure = ex; }
        });
        wrongThread.Start();
        Assert.True(
            wrongThread.Join(TimeSpan.FromSeconds(2)),
            "The foreign-thread registration probe did not complete.");
        Assert.IsType<InvalidOperationException>(wrongThreadFailure);
        replay.Dispose();
        Assert.Throws<ObjectDisposedException>(() => replay.WaitForResponseReady(TimeSpan.Zero));
    }

    [Fact]
    public void RegistrationDelegatesOrdinaryTypesAndBindsTraceOrdinalPlusOne()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, new LifecycleGeneration(1), clock);
        using var ordinary = registry.Register(
            new ExecutionServiceDefinition("test.replay-registration.ordinary"),
            new ExecutionConfig(1));
        var session = new ServiceCycleReplaySession(
            new ServiceCycleTraceSessionId(91),
            new ServiceCycleReplaySessionOptions(true, 128, 32, 4, serviceCapacity: 2));
        using var replay = registry.RegisterReplay(
            new ReplayDefinition(new ReplayControl()),
            new ReplayConfig(2),
            session);

        Assert.Equal(1, replay.Ordinal);
        Assert.IsType<ServiceRunner<ReplayFrame, ReplayConfig, ReplayState, ReplayAction>>(replay.Runner);
        Assert.DoesNotContain(
            replay.Runner.GetType().GetGenericArguments(),
            type => type == typeof(ReplayInputRecord) ||
                type == typeof(ReplayStateRecord) ||
                type == typeof(ReplayActionRecord));

        Assert.True(replay.Runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(replay.Runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(replay.Runner.TryAcquireResponse());
        Assert.True(session.TryReadHighWaterFence(out var fence));
        Assert.True(fence.RecordCount > 0);
        Assert.Equal(2, session.ReadRecordHeader(0, in fence).Cycle.TraceServiceKey);
        Assert.Equal((ulong)91, session.TraceSession.Value);
        Assert.True(session.TryReadCodecManifest(2, out var manifest));
        Assert.True(manifest.CanonicalEncodingRequired);
        Assert.Equal(2, manifest.TraceServiceKey);
        Assert.True(manifest.GetDescriptor(ServiceCycleReplayCodecRole.CycleInput).IsValid);
        Assert.True(manifest.GetDescriptor(ServiceCycleReplayCodecRole.State).IsValid);
        Assert.True(manifest.GetDescriptor(ServiceCycleReplayCodecRole.Action).IsValid);
        Assert.True(session.TryReadSnapshot(out var snapshot));
        Assert.Equal(session.TraceSession, snapshot.TraceSession);
        Assert.Equal(1, snapshot.CodecManifests.Count);
        Assert.True(session.TryReadCodecManifestAt(0, snapshot.CodecManifests, out var exportedManifest));
        Assert.Equal(manifest, exportedManifest);
    }
}
