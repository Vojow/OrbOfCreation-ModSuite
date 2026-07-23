using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution.Validation;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

public sealed class ServiceCycleWorkerDefinitionSafetyTests
{
    [Fact]
    public void WorkerCannotRetainMainDefinition() => AssertRejected(main => new MainRetainingWorker(main));

    [Fact]
    public void WorkerCannotHideBoundaryBehindObject() => AssertRejected(_ => new ObjectRetainingWorker(new object()));

    [Fact]
    public void WorkerCannotHideBoundaryBehindInterface() => AssertRejected(_ => new InterfaceRetainingWorker(new HiddenValue()));

    [Fact]
    public void WorkerCannotRetainDelegate() => AssertRejected(_ => new DelegateRetainingWorker(() => { }));

    [Fact]
    public void WorkerCannotRetainOpaqueFrameworkReference() =>
        AssertRejected(_ => new FrameworkRetainingWorker(new ManualResetEventSlim(false)));

    [Fact]
    public void WorkerCannotRetainMutableCommonRuntimeObject()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertRejected(_ => new CommonRuntimeRetainingWorker(registry));
    }

    [Fact]
    public void WorkerCannotHideStorageInPrivateBaseOrStaticField()
    {
        AssertRejected(_ => new InheritedObjectRetainingWorker(new object()));
        AssertRejected(_ => new StaticObjectRetainingWorker());
    }

    [Fact]
    public void TrustedReplayBaseCannotHideUnsafeFeatureDerivedFields()
    {
        var main = new SafetyMainDefinition();
        var worker = new UnsafeReplayWorker(new object());
        Assert.Throws<InvalidOperationException>(() =>
            ServiceCycleWorkerDefinitionValidator.EnsureSeparated(main, worker));
    }

    [Fact]
    public void TrustedReplayBaseStillAuditsFeatureCodecGraphs()
    {
        var main = new SafetyMainDefinition();
        var worker = new UnsafeCodecReplayWorker();
        Assert.Throws<InvalidOperationException>(() =>
            ServiceCycleWorkerDefinitionValidator.EnsureSeparated(main, worker));
    }

    private static void AssertRejected(Func<SafetyMainDefinition, SafetyWorkerBase> factory)
    {
        var main = new SafetyMainDefinition();
        var worker = factory(main);
        Assert.Throws<InvalidOperationException>(() =>
            ServiceCycleWorkerDefinitionValidator.EnsureSeparated(main, worker));
    }

    private sealed class SafetyFrame { }
    private readonly struct SafetyConfig { }
    private sealed class SafetyState { }
    private readonly struct SafetyAction { }

    private sealed class SafetyMainDefinition :
        IServiceCycleDefinition<SafetyFrame, SafetyConfig, SafetyState, SafetyAction>
    {
        public ServiceId ServiceId => new("test.worker-safety");
        public WakePolicy DefaultWakePolicy => WakePolicy.Immediate;
        public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(new MonotonicDuration(1), new MonotonicDuration(2));
        public SafetyFrame CreateFrame() => new();
        public IServiceCycleWorkerDefinition<SafetyFrame, SafetyConfig, SafetyState, SafetyAction>
            CreateWorkerDefinition() => new SafeWorker();
        public ServiceStartDecision ShouldStart(in SafetyConfig config, in ServiceCycleStartContext context) =>
            ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
        public ServiceCaptureResult Capture(ref SafetyFrame frame, in SafetyConfig config, in ServiceCaptureContext context) =>
            ServiceCaptureResult.Captured(new StrategyGeneration(1), CommonServiceDecisionCodes.Captured);
        public ServiceActionResult TryExecute(in SafetyAction action, in SafetyConfig config, in ServiceActionContext context) =>
            ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);
    }

    private abstract class SafetyWorkerBase :
        IServiceCycleWorkerDefinition<SafetyFrame, SafetyConfig, SafetyState, SafetyAction>
    {
        public SafetyState CreateState(LifecycleGeneration lifecycle) => new();
        public void ReleaseState(ref SafetyState state) => state = null!;
        public void ReleaseFrame(ref SafetyFrame frame) => frame = null!;
        public WakePolicy Evaluate(
            in SafetyFrame frame,
            in SafetyConfig config,
            in ServiceCycleContext context,
            ref SafetyState state,
            ServiceActionWriter<SafetyAction> actions) => WakePolicy.Immediate;
        public void ProjectState(
            in SafetyState state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output) { }
    }

    private sealed class SafeWorker : SafetyWorkerBase { }
    private sealed class MainRetainingWorker : SafetyWorkerBase
    {
        private readonly SafetyMainDefinition _main;
        internal MainRetainingWorker(SafetyMainDefinition main) => _main = main;
    }
    private sealed class ObjectRetainingWorker : SafetyWorkerBase
    {
        private readonly object _hidden;
        internal ObjectRetainingWorker(object hidden) => _hidden = hidden;
    }
    private interface IHiddenValue { }
    private sealed class HiddenValue : IHiddenValue { }
    private sealed class InterfaceRetainingWorker : SafetyWorkerBase
    {
        private readonly IHiddenValue _hidden;
        internal InterfaceRetainingWorker(IHiddenValue hidden) => _hidden = hidden;
    }
    private sealed class DelegateRetainingWorker : SafetyWorkerBase
    {
        private readonly Action _hidden;
        internal DelegateRetainingWorker(Action hidden) => _hidden = hidden;
    }
    private sealed class FrameworkRetainingWorker : SafetyWorkerBase
    {
        private readonly ManualResetEventSlim _hidden;
        internal FrameworkRetainingWorker(ManualResetEventSlim hidden) => _hidden = hidden;
    }
    private sealed class CommonRuntimeRetainingWorker : SafetyWorkerBase
    {
        private readonly ServiceCycleRegistry _hidden;
        internal CommonRuntimeRetainingWorker(ServiceCycleRegistry hidden) => _hidden = hidden;
    }
    private abstract class ObjectRetainingWorkerBase : SafetyWorkerBase
    {
        private readonly object _hidden;
        protected ObjectRetainingWorkerBase(object hidden) => _hidden = hidden;
    }
    private sealed class InheritedObjectRetainingWorker : ObjectRetainingWorkerBase
    {
        internal InheritedObjectRetainingWorker(object hidden) : base(hidden) { }
    }
    private sealed class StaticObjectRetainingWorker : SafetyWorkerBase
    {
        private static readonly object Hidden = new();
    }

    private readonly struct SafetyReplayRecord : IServiceCycleReplayRecord
    {
        internal SafetyReplayRecord(int value) => Value = value;
        internal int Value { get; }
    }

    private sealed class SafetyReplayCodec : IServiceCycleReplayCodec<SafetyReplayRecord>
    {
        public ServiceCycleReplayCodecDescriptor Descriptor => new(1, 1);
        public int Encode(in SafetyReplayRecord record, Span<byte> destination)
        {
            destination[0] = unchecked((byte)record.Value);
            return 1;
        }
        public SafetyReplayRecord Decode(ReadOnlySpan<byte> source) => new(source[0]);
    }

    private sealed class UnsafeReplayWorker : ServiceCycleReplayWorker<
        SafetyFrame,
        SafetyConfig,
        SafetyState,
        SafetyAction,
        SafetyReplayRecord,
        SafetyReplayRecord,
        SafetyReplayRecord>
    {
        private readonly object _hidden;

        internal UnsafeReplayWorker(object hidden)
            : base(new SafetyReplayCodec(), new SafetyReplayCodec(), new SafetyReplayCodec()) => _hidden = hidden;

        protected override SafetyState CreateStateCore(LifecycleGeneration lifecycle) => new();
        protected override void ReleaseStateCore(ref SafetyState state) => state = null!;
        protected override void ReleaseFrameCore(ref SafetyFrame frame) => frame = null!;
        protected override SafetyReplayRecord CreateStateRecordCore(in SafetyState state) => new(1);
        protected override WakePolicy EvaluateCore(
            in SafetyFrame frame,
            in SafetyConfig config,
            in ServiceCycleContext context,
            ref SafetyState state,
            ServiceCycleReplayActionWriter<SafetyAction, SafetyReplayRecord> actions) => WakePolicy.Immediate;
        protected override void ProjectStateCore(
            in SafetyState state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output) { }
    }

    private sealed class UnsafeReplayCodec : IServiceCycleReplayCodec<SafetyReplayRecord>
    {
        private readonly object _hidden = new();
        public ServiceCycleReplayCodecDescriptor Descriptor => new(1, 1);
        public int Encode(in SafetyReplayRecord record, Span<byte> destination) => 0;
        public SafetyReplayRecord Decode(ReadOnlySpan<byte> source) => new(1);
    }

    private sealed class UnsafeCodecReplayWorker : ServiceCycleReplayWorker<
        SafetyFrame,
        SafetyConfig,
        SafetyState,
        SafetyAction,
        SafetyReplayRecord,
        SafetyReplayRecord,
        SafetyReplayRecord>
    {
        internal UnsafeCodecReplayWorker()
            : base(new UnsafeReplayCodec(), new SafetyReplayCodec(), new SafetyReplayCodec()) { }

        protected override SafetyState CreateStateCore(LifecycleGeneration lifecycle) => new();
        protected override void ReleaseStateCore(ref SafetyState state) => state = null!;
        protected override void ReleaseFrameCore(ref SafetyFrame frame) => frame = null!;
        protected override SafetyReplayRecord CreateStateRecordCore(in SafetyState state) => new(1);
        protected override WakePolicy EvaluateCore(
            in SafetyFrame frame,
            in SafetyConfig config,
            in ServiceCycleContext context,
            ref SafetyState state,
            ServiceCycleReplayActionWriter<SafetyAction, SafetyReplayRecord> actions) => WakePolicy.Immediate;
        protected override void ProjectStateCore(
            in SafetyState state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output) { }
    }
}
