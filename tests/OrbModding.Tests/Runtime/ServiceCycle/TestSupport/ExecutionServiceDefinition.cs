using System;
using System.Threading;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal class ExecutionServiceDefinition :
    IServiceCycleDefinition<ExecutionFrame, ExecutionConfig, ExecutionState, ExecutionAction>
{
    private readonly ExecutionWorkerSignals _signals;
    private readonly ExecutionWorkerControl _worker;

    internal ExecutionServiceDefinition(string id)
    {
        ServiceId = new ServiceId(id);
        _signals = ExecutionWorkerSignals.Create();
        _worker = new ExecutionWorkerControl(_signals.Id);
    }

    public ServiceId ServiceId { get; }
    public WakePolicy DefaultWakePolicy { get; set; } = WakePolicy.Immediate;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; set; } = new(
        new MonotonicDuration(10), new MonotonicDuration(80));

    internal int ActionCount { get => _worker.ActionCount; set => _worker.ActionCount = value; }
    internal int PartialActionCountBeforeFault
    {
        get => _worker.PartialActionCountBeforeFault;
        set => _worker.PartialActionCountBeforeFault = value;
    }
    internal int RejectAtIndex { get; set; } = -1;
    internal int FaultAtIndex { get; set; } = -1;
    internal NativeMutationCallOutcome CommittedNativeOutcome { get; set; } = new(1, 1, 1);
    internal WakePolicy EvaluationWake { get => _worker.EvaluationWake; set => _worker.EvaluationWake = value; }
    internal ManualResetEventSlim? EvaluationEntered
    {
        get => _signals.EvaluationEntered;
        set => _signals.EvaluationEntered = value;
    }
    internal ManualResetEventSlim? EvaluationRelease
    {
        get => _signals.EvaluationRelease;
        set => _signals.EvaluationRelease = value;
    }
    internal ManualResetEventSlim? ActionsAppended
    {
        get => _signals.ActionsAppended;
        set => _signals.ActionsAppended = value;
    }
    internal ManualResetEventSlim? ActionsRelease
    {
        get => _signals.ActionsRelease;
        set => _signals.ActionsRelease = value;
    }
    internal ActionPayload? Payload { get => _worker.Payload; set => _worker.Payload = value; }
    internal bool MeasureAppendAllocations
    {
        get => _worker.MeasureAppendAllocations;
        set => _worker.MeasureAppendAllocations = value;
    }
    internal long LastAppendAllocatedBytes => _worker.LastAppendAllocatedBytes;
    internal bool DuplicateProjectionKey
    {
        get => _worker.DuplicateProjectionKey;
        set => _worker.DuplicateProjectionKey = value;
    }
    internal int LastEvaluationConfig => _worker.LastEvaluationConfig;
    internal int LastExecutionConfig { get; private set; }
    internal int ActionExecutionCount { get; private set; }
    internal int CaptureCount { get; private set; }
    internal ServiceStartDecision StartDecision { get; set; } =
        ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
    internal ServiceCaptureResult CaptureResult { get; set; } =
        ServiceCaptureResult.Captured(new StrategyGeneration(1), CommonServiceDecisionCodes.Captured);
    internal Action? ShouldStartCallback { get; set; }
    internal Action? CaptureCallback { get; set; }
    internal Action? ActionCallback { get; set; }
    internal MonotonicTimestamp LastActionAttemptedAt { get; private set; }
    internal int StateCreateCount => _worker.StateCreateCount;
    internal int StateReleaseCount => _worker.StateReleaseCount;
    internal int EvaluationCount => _worker.EvaluationCount;
    internal ManualResetEventSlim ResourcesReleased => _signals.ResourcesReleased;

    internal void FailNextEvaluations(int count) => _worker.FailNextEvaluations(count);
    internal void FailNextProjections(int count) => _worker.FailNextProjections(count);
    internal void FailNextStateFactories(int count) => _worker.FailNextStateFactories(count);

    public ExecutionFrame CreateFrame() => new();

    public IServiceCycleWorkerDefinition<ExecutionFrame, ExecutionConfig, ExecutionState, ExecutionAction>
        CreateWorkerDefinition() => new ExecutionWorkerDefinition(_worker);

    public ServiceStartDecision ShouldStart(in ExecutionConfig config, in ServiceCycleStartContext context)
    {
        ShouldStartCallback?.Invoke();
        return StartDecision;
    }

    public ServiceCaptureResult Capture(
        ref ExecutionFrame frame,
        in ExecutionConfig config,
        in ServiceCaptureContext context)
    {
        CaptureCount++;
        CaptureCallback?.Invoke();
        frame.Value = config.Value * 10;
        return CaptureResult;
    }

    public ServiceActionResult TryExecute(
        in ExecutionAction action,
        in ExecutionConfig config,
        in ServiceActionContext context)
    {
        ActionExecutionCount++;
        LastActionAttemptedAt = context.AttemptedAt;
        ActionCallback?.Invoke();
        LastExecutionConfig = config.Value;
        if (context.ActionIndex == FaultAtIndex)
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        if (context.ActionIndex == RejectAtIndex)
            return ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);
        return ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.Verified, CommittedNativeOutcome));
    }
}
