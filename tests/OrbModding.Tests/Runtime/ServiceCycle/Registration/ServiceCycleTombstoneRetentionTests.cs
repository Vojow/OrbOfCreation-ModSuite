using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

public sealed class ServiceCycleTombstoneRetentionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposedSealedSlotRetainsOnlyTombstoneMetadataEvenWhenReleaseThrows(bool throwOnRelease)
    {
        var proof = CreateDisposedGraph(throwOnRelease);

        ForceCollection(proof.PayloadReferences);

        Assert.All(proof.PayloadReferences, reference => Assert.False(reference.IsAlive));
        Assert.Equal(0, proof.Registry.Count);
        Assert.Equal(1, proof.Registry.OrdinalCount);
        Assert.Equal(proof.Ordinal, proof.Registration.Ordinal);
        Assert.Equal(proof.ServiceId, proof.Registry.GetServiceId(proof.Ordinal));
        var slot = proof.Registry.GetSlot(proof.Ordinal);
        Assert.IsType<ServiceCycleTombstone>(slot);
        Assert.True(slot.IsDisposed);
        Assert.Equal(proof.Ordinal, slot.Ordinal);
        Assert.True(slot.RegistrationToken > 0);
        Assert.False(proof.ReleaseFailed);
        Assert.Throws<ObjectDisposedException>(() => proof.Registration.Configuration.ReadLatest());

        GC.KeepAlive(proof.Registry);
        GC.KeepAlive(proof.Registration);
        proof.Registry.Dispose();
    }

    [Fact]
    public void OrdinalHighWaterAllowsScanningPastPreSealTombstone()
    {
        using var registry = new ServiceCycleRegistry(2);
        var first = registry.Register(
            new RetainedDefinition("test.tombstone.preseal.a", new RetainedPayload(1),
                new RetainedFrame(new RetainedPayload(2)), new RetainedState(new RetainedPayload(3)), false),
            new ReferenceConfig(new RetainedPayload(4)),
            new LifecycleGeneration(1));
        using var second = registry.Register(
            new RetainedDefinition("test.tombstone.preseal.b", new RetainedPayload(5),
                new RetainedFrame(new RetainedPayload(6)), new RetainedState(new RetainedPayload(7)), false),
            new ReferenceConfig(new RetainedPayload(8)),
            new LifecycleGeneration(1));

        first.Dispose();
        registry.Seal();

        Assert.Equal(1, registry.Count);
        Assert.Equal(2, registry.OrdinalCount);
        Assert.True(registry.GetSlot(0).IsDisposed);
        Assert.False(registry.GetSlot(1).IsDisposed);
        Assert.Equal(1, second.Ordinal);
        Assert.Equal("test.tombstone.preseal.b", registry.GetServiceId(1).Value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TombstoneRetentionProof CreateDisposedGraph(bool throwOnRelease)
    {
        var definitionPayload = new RetainedPayload(1);
        var framePayload = new RetainedPayload(2);
        var statePayload = new RetainedPayload(3);
        var configPayload = new RetainedPayload(4);
        var frame = new RetainedFrame(framePayload);
        var state = new RetainedState(statePayload);
        var configuration = new ReferenceConfig(configPayload);
        var definition = new RetainedDefinition(
            throwOnRelease ? "test.tombstone.throw" : "test.tombstone.clean",
            definitionPayload,
            frame,
            state,
            throwOnRelease);
        var payloadReferences = new[]
        {
            new WeakReference(definitionPayload),
            new WeakReference(framePayload),
            new WeakReference(statePayload),
            new WeakReference(configPayload),
            new WeakReference(frame),
            new WeakReference(state),
            new WeakReference(configuration),
            new WeakReference(definition),
        };

        var registry = new ServiceCycleRegistry(1);
        var registration = registry.Register(
            definition,
            configuration,
            new LifecycleGeneration(1));
        registry.Seal();
        var releaseFailed = false;
        try { registration.Dispose(); }
        catch (InvalidOperationException) { releaseFailed = true; }
        if (!definition.WaitForRelease())
            throw new TimeoutException("The signal-only runner shutdown did not complete test cleanup.");

        return new TombstoneRetentionProof(
            registry,
            registration,
            payloadReferences,
            registration.Ordinal,
            new ServiceId(throwOnRelease ? "test.tombstone.throw" : "test.tombstone.clean"),
            releaseFailed);
    }

    private static void ForceCollection(WeakReference[] references)
    {
        for (var attempt = 0; attempt < 8 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
    }

    private sealed class TombstoneRetentionProof
    {
        internal TombstoneRetentionProof(
            ServiceCycleRegistry registry,
            ServiceRegistration<RetainedFrame, ReferenceConfig, RetainedState, RetainedAction> registration,
            WeakReference[] payloadReferences,
            int ordinal,
            ServiceId serviceId,
            bool releaseFailed)
        {
            Registry = registry;
            Registration = registration;
            PayloadReferences = payloadReferences;
            Ordinal = ordinal;
            ServiceId = serviceId;
            ReleaseFailed = releaseFailed;
        }

        internal ServiceCycleRegistry Registry { get; }
        internal ServiceRegistration<RetainedFrame, ReferenceConfig, RetainedState, RetainedAction> Registration { get; }
        internal WeakReference[] PayloadReferences { get; }
        internal int Ordinal { get; }
        internal ServiceId ServiceId { get; }
        internal bool ReleaseFailed { get; }
    }

    private sealed class RetainedPayload
    {
        private readonly int _value;
        internal RetainedPayload(int value) => _value = value;
    }
    private sealed class ReferenceConfig
    {
        private readonly RetainedPayload _payload;
        internal ReferenceConfig(RetainedPayload payload) => _payload = payload;
    }
    private sealed class RetainedFrame
    {
        private readonly RetainedPayload _payload;
        internal RetainedFrame(RetainedPayload payload) => _payload = payload;
    }
    private sealed class RetainedState
    {
        private readonly RetainedPayload _payload;
        internal RetainedState(RetainedPayload payload) => _payload = payload;
    }
    private readonly struct RetainedAction { }

    private sealed class RetainedDefinition :
        IServiceCycleDefinition<RetainedFrame, ReferenceConfig, RetainedState, RetainedAction>
    {
        private readonly ServiceId _serviceId;
        private readonly RetainedPayload _payload;
        private readonly RetainedFrame _frame;
        private readonly RetainedWorkerResources _worker;
        private readonly ManualResetEventSlim _released = new(false);

        internal RetainedDefinition(
            string serviceId,
            RetainedPayload payload,
            RetainedFrame frame,
            RetainedState state,
            bool throwOnRelease)
        {
            _serviceId = new ServiceId(serviceId);
            _payload = payload;
            _frame = frame;
            _worker = new RetainedWorkerResources(
                state,
                throwOnRelease,
                RetainedReleaseSignals.Register(_released));
        }

        public ServiceId ServiceId => _serviceId;
        public WakePolicy DefaultWakePolicy => WakePolicy.Immediate;
        public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
            MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(1)),
            MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
        public RetainedFrame CreateFrame()
        {
            GC.KeepAlive(_payload);
            return _frame;
        }
        public IServiceCycleWorkerDefinition<RetainedFrame, ReferenceConfig, RetainedState, RetainedAction>
            CreateWorkerDefinition() => new WorkerDefinition(_worker);
        internal bool WaitForRelease() => _released.Wait(TimeSpan.FromSeconds(2));
        public ServiceStartDecision ShouldStart(
            in ReferenceConfig config,
            in ServiceCycleStartContext context) =>
            ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
        public ServiceCaptureResult Capture(
            ref RetainedFrame frame,
            in ReferenceConfig config,
            in ServiceCaptureContext context) =>
            ServiceCaptureResult.Captured(new StrategyGeneration(1), CommonServiceDecisionCodes.Captured);
        public ServiceActionResult TryExecute(
            in RetainedAction action,
            in ReferenceConfig config,
            in ServiceActionContext context) =>
            ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);

        private sealed class WorkerDefinition :
            IServiceCycleWorkerDefinition<RetainedFrame, ReferenceConfig, RetainedState, RetainedAction>
        {
            private readonly RetainedWorkerResources _resources;
            internal WorkerDefinition(RetainedWorkerResources resources) => _resources = resources;
            public RetainedState CreateState(LifecycleGeneration lifecycle) => _resources.State;
            public void ReleaseState(ref RetainedState state)
            {
                if (_resources.ThrowOnRelease)
                    throw new InvalidOperationException("release failed without clearing state");
            }
            public void ReleaseFrame(ref RetainedFrame frame)
            {
                try
                {
                    if (_resources.ThrowOnRelease)
                        throw new InvalidOperationException("frame release failed without clearing frame");
                    frame = null!;
                }
                finally { RetainedReleaseSignals.Signal(_resources.ReleaseSignalId); }
            }
            public WakePolicy Evaluate(
                in RetainedFrame frame,
                in ReferenceConfig config,
                in ServiceCycleContext context,
                ref RetainedState state,
                ServiceActionWriter<RetainedAction> actions) => WakePolicy.Immediate;
            public void ProjectState(
                in RetainedState state,
                in ServiceProjectionContext context,
                ServiceStateProjectionBuilder output) { }
        }

        private sealed class RetainedWorkerResources
        {
            internal RetainedWorkerResources(RetainedState state, bool throwOnRelease, int releaseSignalId)
            {
                State = state;
                ThrowOnRelease = throwOnRelease;
                ReleaseSignalId = releaseSignalId;
            }
            internal RetainedState State { get; }
            internal bool ThrowOnRelease { get; }
            internal int ReleaseSignalId { get; }
        }
    }

    private static class RetainedReleaseSignals
    {
        private static readonly ConcurrentDictionary<int, ManualResetEventSlim> Signals = new();
        private static int _nextId;
        internal static int Register(ManualResetEventSlim signal)
        {
            var id = Interlocked.Increment(ref _nextId);
            if (!Signals.TryAdd(id, signal)) throw new InvalidOperationException("Duplicate test signal id.");
            return id;
        }
        internal static void Signal(int id)
        {
            if (Signals.TryRemove(id, out var signal)) signal.Set();
        }
    }
}
