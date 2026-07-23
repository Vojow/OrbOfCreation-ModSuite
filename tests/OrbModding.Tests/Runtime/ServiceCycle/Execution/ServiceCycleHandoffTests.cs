using System;
using System.Threading;
using System.Threading.Tasks;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

public sealed class ServiceCycleHandoffTests
{
    [Fact]
    public async Task OfflineWorkerReadyWaitObservesTheInitialParkWithoutPolling()
    {
        var handoff = new ServiceCycleHandoff<int>();
        Assert.False(handoff.WaitForWorkerReady(TimeSpan.Zero));

        var worker = Task.Run(() =>
        {
            Assert.Equal(ServiceWorkerWorkKind.Stop, handoff.WaitForWorkerWork(out _, out _, out _));
            handoff.PrepareWorkerExit();
        });

        Assert.True(handoff.WaitForWorkerReady(TimeSpan.FromSeconds(5)));
        handoff.SignalStop();
        await worker.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(handoff.TryAcknowledgeWorkerExited());
    }

    [Fact]
    public async Task OfflineWaitObservesResponsePublicationWithoutPollingOrLostWakeups()
    {
        var handoff = new ServiceCycleHandoff<int>();
        var configuration = new ConfigurationPublication<int>(new ConfigGeneration(1), 7);
        var identity = new ServiceCycleIdentity(
            new ServiceId("test.handoff.offline-wait"),
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new CaptureSequence(1),
            new CycleId(1));
        var context = new ServiceCycleContext(identity, default, new MonotonicTimestamp(10));
        var batch = new BatchId(1);

        Assert.True(handoff.TryPublishRequest(configuration, in context, batch, out var sequence));
        Assert.Equal(ServiceWorkerWorkKind.Evaluate, handoff.WaitForWorkerWork(out _, out _, out _));
        var wait = Task.Run(() => handoff.WaitForResponseReady(TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(() => handoff.OfflineResponseWaiterCount == 1, 1_000));

        var fault = new ServiceFault(
            ServiceFaultCategory.Evaluation,
            CommonActionResultCodes.AdapterFault,
            1,
            new MonotonicTimestamp(11));
        var response = ServiceWorkerResponse.Failure(
            sequence,
            identity,
            batch,
            new MonotonicTimestamp(10),
            new MonotonicTimestamp(11),
            fault,
            new MonotonicTimestamp(12),
            default);
        Assert.True(handoff.TryPublishResponse(sequence, in response));
        Assert.True(await wait);
        Assert.Equal(1, handoff.OfflineResponseWakePulseCount);
        Assert.True(handoff.TryAcquireResponse(out _));
        Assert.False(handoff.WaitForResponseReady(TimeSpan.FromMilliseconds(1)));
        handoff.CompleteMainOwnership();
        handoff.DisposeNeverStarted();
    }

    [Fact]
    public async Task ReplayWaitObservesTheWorkerSettledAfterResponsePublication()
    {
        var handoff = new ServiceCycleHandoff<int>();
        var configuration = new ConfigurationPublication<int>(new ConfigGeneration(1), 7);
        var identity = new ServiceCycleIdentity(
            new ServiceId("test.handoff.replay-settled"),
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new CaptureSequence(1),
            new CycleId(1));
        var context = new ServiceCycleContext(identity, default, new MonotonicTimestamp(10));
        var batch = new BatchId(1);
        var fault = new ServiceFault(
            ServiceFaultCategory.Evaluation,
            CommonActionResultCodes.AdapterFault,
            1,
            new MonotonicTimestamp(11));

        Assert.True(handoff.TryPublishRequest(configuration, in context, batch, out var sequence));
        using var responsePublished = new ManualResetEventSlim();
        using var allowWorkerToSettle = new ManualResetEventSlim();
        var worker = Task.Run(() =>
        {
            Assert.Equal(ServiceWorkerWorkKind.Evaluate, handoff.WaitForWorkerWork(out _, out _, out _));
            var response = ServiceWorkerResponse.Failure(
                sequence,
                identity,
                batch,
                new MonotonicTimestamp(10),
                new MonotonicTimestamp(11),
                fault,
                new MonotonicTimestamp(12),
                default);
            Assert.True(handoff.TryPublishResponse(sequence, in response));
            responsePublished.Set();
            Assert.True(allowWorkerToSettle.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal(ServiceWorkerWorkKind.Stop, handoff.WaitForWorkerWork(out _, out _, out _));
            handoff.PrepareWorkerExit();
        });

        Assert.True(responsePublished.Wait(TimeSpan.FromSeconds(2)));
        Assert.False(handoff.WaitForResponseReadyAndWorkerSettled(identity, TimeSpan.Zero));
        var wait = Task.Run(() =>
            handoff.WaitForResponseReadyAndWorkerSettled(identity, TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(() => handoff.OfflineResponseWaiterCount == 1, 1_000));
        allowWorkerToSettle.Set();

        Assert.True(await wait);
        Assert.True(handoff.OfflineResponseWakePulseCount >= 1);
        Assert.True(handoff.TryAcquireResponse(out _));
        handoff.CompleteMainOwnership();
        handoff.SignalStop();
        await worker.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(handoff.TryAcknowledgeWorkerExited());
    }

    [Fact]
    public async Task OfflineWaitStopsPromptlyAndRejectsUnboundedTimeout()
    {
        var handoff = new ServiceCycleHandoff<int>();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            handoff.WaitForResponseReady(TimeSpan.FromMilliseconds((double)int.MaxValue + 1)));
        Assert.False(handoff.WaitForResponseReady(TimeSpan.Zero));

        var wait = Task.Run(() => handoff.WaitForResponseReady(TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(() => handoff.OfflineResponseWaiterCount == 1, 1_000));
        handoff.SignalStop();
        Assert.False(await wait);
        Assert.Equal(1, handoff.OfflineResponseWakePulseCount);

        Assert.Equal(ServiceWorkerWorkKind.Stop, handoff.WaitForWorkerWork(out _, out _, out _));
        handoff.PrepareWorkerExit();
        Assert.True(handoff.TryAcknowledgeWorkerExited());
    }

    [Fact]
    public async Task OfflineWaitWakesWhenWorkerExitMakesResponseImpossible()
    {
        var handoff = new ServiceCycleHandoff<int>();
        var wait = Task.Run(() => handoff.WaitForResponseReady(TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(() => handoff.OfflineResponseWaiterCount == 1, 1_000));

        handoff.PrepareWorkerExit();

        Assert.False(await wait);
        Assert.Equal(1, handoff.OfflineResponseWakePulseCount);
        Assert.True(handoff.TryAcknowledgeWorkerExited());
    }

    [Fact]
    public void StopSignalAfterWorkerWakeDisposalIsIdempotent()
    {
        var handoff = new ServiceCycleHandoff<int>();
        handoff.PrepareWorkerExit();
        Assert.True(handoff.WorkerWakeDisposed);

        handoff.SignalStop();

        Assert.True(handoff.Snapshot.StopRequested);
        Assert.True(handoff.TryAcknowledgeWorkerExited());
    }

    [Fact]
    public void StaleAndDuplicateResponsesCannotCrossTheHalfDuplexBoundary()
    {
        var handoff = new ServiceCycleHandoff<int>();
        var configuration = new ConfigurationPublication<int>(new ConfigGeneration(1), 7);
        var identity = new ServiceCycleIdentity(
            new ServiceId("test.handoff.identity"),
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new CaptureSequence(1),
            new CycleId(1));
        var context = new ServiceCycleContext(identity, default, new MonotonicTimestamp(10));
        var batch = new BatchId(1);

        Assert.True(handoff.TryPublishRequest(configuration, in context, batch, out var sequence));
        Assert.Equal(ServiceWorkerWorkKind.Evaluate, handoff.WaitForWorkerWork(out _, out _, out _));
        var fault = new ServiceFault(
            ServiceFaultCategory.Evaluation,
            CommonActionResultCodes.AdapterFault,
            1,
            new MonotonicTimestamp(11));
        var stale = ServiceWorkerResponse.Failure(
            sequence + 1,
            identity,
            batch,
            new MonotonicTimestamp(10),
            new MonotonicTimestamp(11),
            fault,
            new MonotonicTimestamp(12),
            default);
        Assert.False(handoff.TryPublishResponse(sequence, in stale));

        var response = ServiceWorkerResponse.Failure(
            sequence,
            identity,
            batch,
            new MonotonicTimestamp(10),
            new MonotonicTimestamp(11),
            fault,
            new MonotonicTimestamp(12),
            default);
        Assert.True(handoff.TryPublishResponse(sequence, in response));
        Assert.False(handoff.TryPublishResponse(sequence, in response));
        Assert.True(handoff.TryAcquireResponse(out _));
        Assert.False(handoff.TryAcquireResponse(out _));
        handoff.CompleteMainOwnership();
        Assert.Equal(ServiceHandoffPhase.Empty, handoff.Snapshot.Phase);
        handoff.DisposeNeverStarted();
        Assert.True(handoff.WorkerWakeDisposed);
    }
}
