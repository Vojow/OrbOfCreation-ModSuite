using System.Collections.Concurrent;
using System.Threading;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal sealed class CrossRoleResource :
    IServiceCycleWorkerDefinition<
        CrossRoleFrame,
        CrossRoleConfig,
        CrossRoleResource,
        CrossRoleAction>
{
    internal CrossRoleResource() => Id = CrossRoleResourceRuntime.NextId();

    internal int Id { get; }

    CrossRoleResource IServiceCycleWorkerDefinition<
        CrossRoleFrame,
        CrossRoleConfig,
        CrossRoleResource,
        CrossRoleAction>.CreateState(LifecycleGeneration lifecycle) =>
        CrossRoleResourceRuntime.CreateState(Id);

    void IServiceCycleWorkerDefinition<
        CrossRoleFrame,
        CrossRoleConfig,
        CrossRoleResource,
        CrossRoleAction>.ReleaseState(ref CrossRoleResource state)
    {
        CrossRoleResourceRuntime.RecordStateRelease(Id);
        state = null!;
    }

    void IServiceCycleWorkerDefinition<
        CrossRoleFrame,
        CrossRoleConfig,
        CrossRoleResource,
        CrossRoleAction>.ReleaseFrame(ref CrossRoleFrame frame) => frame = null!;

    WakePolicy IServiceCycleWorkerDefinition<
        CrossRoleFrame,
        CrossRoleConfig,
        CrossRoleResource,
        CrossRoleAction>.Evaluate(
        in CrossRoleFrame frame,
        in CrossRoleConfig config,
        in ServiceCycleContext context,
        ref CrossRoleResource state,
        ServiceActionWriter<CrossRoleAction> actions) => WakePolicy.AfterBatch(new MonotonicDuration(1_000));

    void IServiceCycleWorkerDefinition<
        CrossRoleFrame,
        CrossRoleConfig,
        CrossRoleResource,
        CrossRoleAction>.ProjectState(
        in CrossRoleResource state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        output.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(state.Id));
}

internal sealed class CrossRoleFrame
{
    internal CrossRoleFrame(int id) => Id = id;
    internal int Id { get; }
}

internal sealed class CrossRoleState
{
    internal CrossRoleState(int id) => Id = id;
    internal int Id { get; }
}

internal readonly struct CrossRoleConfig
{
    internal CrossRoleConfig(int value) => Value = value;
    internal int Value { get; }
}

internal readonly struct CrossRoleAction
{
    internal CrossRoleAction(int value) => Value = value;
    internal int Value { get; }
}

internal sealed class CrossRoleServiceDefinition :
    IServiceCycleDefinition<CrossRoleFrame, CrossRoleConfig, CrossRoleResource, CrossRoleAction>
{
    internal CrossRoleServiceDefinition(string id)
    {
        ServiceId = new ServiceId(id);
        WorkerResource = new CrossRoleResource();
        FrameResource = new CrossRoleFrame(WorkerResource.Id);
        StateResource = new CrossRoleResource();
    }

    public ServiceId ServiceId { get; }
    public WakePolicy DefaultWakePolicy => WakePolicy.AfterBatch(new MonotonicDuration(1_000));
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
        new MonotonicDuration(10),
        new MonotonicDuration(80));
    internal CrossRoleResource WorkerResource { get; set; }
    internal CrossRoleFrame FrameResource { get; set; }
    internal CrossRoleResource StateResource { get; set; }
    internal bool Ready { get; set; } = true;
    internal int StateCreateCount => CrossRoleResourceRuntime.StateCreateCount(WorkerResource.Id);
    internal int StateReleaseCount => CrossRoleResourceRuntime.StateReleaseCount(WorkerResource.Id);

    public CrossRoleFrame CreateFrame() => FrameResource;

    public IServiceCycleWorkerDefinition<
        CrossRoleFrame,
        CrossRoleConfig,
        CrossRoleResource,
        CrossRoleAction> CreateWorkerDefinition()
    {
        CrossRoleResourceRuntime.SetState(WorkerResource.Id, StateResource);
        return WorkerResource;
    }

    public ServiceStartDecision ShouldStart(
        in CrossRoleConfig config,
        in ServiceCycleStartContext context) => Ready
        ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
        : ServiceStartDecision.Wait(
            CommonServiceDecisionCodes.NotReady,
            WakePolicy.AfterDecision(new MonotonicDuration(1_000)));

    public ServiceCaptureResult Capture(
        ref CrossRoleFrame frame,
        in CrossRoleConfig config,
        in ServiceCaptureContext context) =>
        ServiceCaptureResult.Captured(new StrategyGeneration(1), CommonServiceDecisionCodes.Captured);

    public ServiceActionResult TryExecute(
        in CrossRoleAction action,
        in CrossRoleConfig config,
        in ServiceActionContext context) =>
        ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(1, 1, 1)));
}

internal sealed class CrossRoleFrameOwnerDefinition :
    IServiceCycleDefinition<CrossRoleResource, CrossRoleConfig, CrossRoleState, CrossRoleAction>
{
    private readonly CrossRoleFrameOwnerWorker _worker = new();

    internal CrossRoleFrameOwnerDefinition(string id, CrossRoleResource frame)
    {
        ServiceId = new ServiceId(id);
        Frame = frame;
    }

    public ServiceId ServiceId { get; }
    public WakePolicy DefaultWakePolicy => WakePolicy.AfterBatch(new MonotonicDuration(1_000));
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
        new MonotonicDuration(10),
        new MonotonicDuration(80));
    internal CrossRoleResource Frame { get; }

    public CrossRoleResource CreateFrame() => Frame;

    public IServiceCycleWorkerDefinition<
        CrossRoleResource,
        CrossRoleConfig,
        CrossRoleState,
        CrossRoleAction> CreateWorkerDefinition() => _worker;

    public ServiceStartDecision ShouldStart(
        in CrossRoleConfig config,
        in ServiceCycleStartContext context) =>
        ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);

    public ServiceCaptureResult Capture(
        ref CrossRoleResource frame,
        in CrossRoleConfig config,
        in ServiceCaptureContext context) =>
        ServiceCaptureResult.Captured(new StrategyGeneration(1), CommonServiceDecisionCodes.Captured);

    public ServiceActionResult TryExecute(
        in CrossRoleAction action,
        in CrossRoleConfig config,
        in ServiceActionContext context) =>
        ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(1, 1, 1)));

    private sealed class CrossRoleFrameOwnerWorker :
        IServiceCycleWorkerDefinition<
            CrossRoleResource,
            CrossRoleConfig,
            CrossRoleState,
            CrossRoleAction>
    {
        public CrossRoleState CreateState(LifecycleGeneration lifecycle) =>
            new((int)lifecycle.Value);
        public void ReleaseState(ref CrossRoleState state) => state = null!;
        public void ReleaseFrame(ref CrossRoleResource frame) => frame = null!;

        public WakePolicy Evaluate(
            in CrossRoleResource frame,
            in CrossRoleConfig config,
            in ServiceCycleContext context,
            ref CrossRoleState state,
            ServiceActionWriter<CrossRoleAction> actions) =>
            WakePolicy.AfterBatch(new MonotonicDuration(1_000));

        public void ProjectState(
            in CrossRoleState state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output) =>
            output.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(state.Id));
    }
}

internal static class CrossRoleResourceRuntime
{
    private static readonly ConcurrentDictionary<int, CrossRoleResource> States = new();
    private static readonly ConcurrentDictionary<int, int> StateCreates = new();
    private static readonly ConcurrentDictionary<int, int> StateReleases = new();
    private static int _nextId;

    internal static int NextId() => Interlocked.Increment(ref _nextId);
    internal static void SetState(int workerId, CrossRoleResource state) => States[workerId] = state;
    internal static CrossRoleResource CreateState(int workerId)
    {
        StateCreates.AddOrUpdate(workerId, 1, static (_, count) => count + 1);
        return States.TryGetValue(workerId, out var state) ? state : new CrossRoleResource();
    }
    internal static void RecordStateRelease(int workerId) =>
        StateReleases.AddOrUpdate(workerId, 1, static (_, count) => count + 1);
    internal static int StateCreateCount(int workerId) =>
        StateCreates.TryGetValue(workerId, out var count) ? count : 0;
    internal static int StateReleaseCount(int workerId) =>
        StateReleases.TryGetValue(workerId, out var count) ? count : 0;
}
