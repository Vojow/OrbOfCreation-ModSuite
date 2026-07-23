using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

public sealed partial class ServiceCycleReplayEvaluatorOracle<
    TFrame,
    TConfig,
    TState,
    TAction,
    TCycleInputRecord,
    TStateRecord,
    TActionRecord>
{
    private ServiceCycleReplayExecutionResult VerifyCore(
        in ServiceCycleReplayDecodedCycle<TCycleInputRecord, TStateRecord, TActionRecord> expected,
        ref TFrame frame,
        ref TState state,
        ref bool hasFrame,
        ref bool hasState)
    {
        var replayContext = expected.Context;
        var expectedInput = expected.Input;
        var expectedPrevious = expected.PreviousState;
        var expectedNext = expected.NextState;
        var expectedActionCount = expected.ActionCount;
        var expectedWake = expected.Wake;
        var expectedProjection = expected.Projection;
        var cycle = replayContext.Cycle;
        TConfig config;
        try
        {
            _hydrator.HydrateFrame(in expectedInput, in replayContext, ref frame);
            hasFrame = true;
            config = _hydrator.HydrateConfiguration(in expectedInput, in replayContext);
            state = _hydrator.HydratePreviousState(in expectedPrevious, in replayContext);
            hasState = true;
        }
        catch (Exception exception) when (ServiceCycleReplayContainedRunner.IsContainable(exception))
        {
            return Fault(
                cycle,
                ServiceCycleReplayFaultCode.CycleContextRejected,
                ServiceCycleReplayFailureLocation.Cycle,
                ServiceCycleReplayExecutionDetailCode.HydrationRejected);
        }

        TCycleInputRecord recreated;
        TStateRecord previous;
        try
        {
            recreated = _hydrator.RecreateCycleInputRecord(in frame, in config, in replayContext);
            previous = _evaluator.CreateStateRecord(in state);
        }
        catch (Exception exception) when (ServiceCycleReplayContainedRunner.IsContainable(exception))
        {
            return Fault(
                cycle,
                ServiceCycleReplayFaultCode.CycleContextRejected,
                ServiceCycleReplayFailureLocation.Cycle,
                ServiceCycleReplayExecutionDetailCode.HydrationRejected);
        }

        var compared = Compare(
            cycle,
            _inputComparer,
            in expectedInput,
            in recreated,
            ServiceCycleReplayMismatchCode.CycleInput,
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0));
        if (compared.HasValue) return compared.Value;
        compared = Compare(
            cycle,
            _stateComparer,
            in expectedPrevious,
            in previous,
            ServiceCycleReplayMismatchCode.PreviousState,
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.PreviousState, 0));
        if (compared.HasValue) return compared.Value;

        var verificationCapacity = checked(expectedActionCount + 1);
        var gameplay = new ReusableActionStore<TAction>(verificationCapacity);
        gameplay.BeginWrite();
        var sink = new VerificationActionSink(verificationCapacity);
        var actions = new ServiceCycleReplayActionWriter<TAction, TActionRecord>(
            new ServiceActionWriter<TAction>(gameplay),
            sink);
        WakePolicy actualWake;
        ServiceStateProjectionSnapshot actualProjection;
        try
        {
            var context = ServiceCycleReplayContextFactory.Create(_service, in replayContext);
            actualWake = _evaluator.Evaluate(
                in frame,
                in config,
                in context,
                ref state,
                actions);
            actualWake = ServiceWakeSchedule.Resolve(actualWake, _defaultWakePolicy);
            var projectionBuffer = new ServiceStateProjectionWriteBuffer(
                ServiceStateProjectionSnapshot.MaximumEntryCount);
            var builder = new ServiceStateProjectionBuilder(projectionBuffer);
            var projectionContext = new ServiceProjectionContext(
                context.Identity,
                expected.StatePublication,
                expected.ProjectedAt);
            _evaluator.ProjectState(in state, in projectionContext, builder);
            actualProjection = projectionBuffer.CreateSnapshot();
        }
        catch (Exception exception) when (ServiceCycleReplayContainedRunner.IsContainable(exception))
        {
            gameplay.AbortWorkerWrite();
            if (sink.Count > expectedActionCount)
                return Mismatch(cycle, ServiceCycleReplayMismatchCode.ActionCount, default, 2);
            return Fault(
                cycle,
                ServiceCycleReplayFaultCode.EvaluatorFaulted,
                ServiceCycleReplayFailureLocation.Cycle);
        }

        if (sink.Count != expectedActionCount)
            return Mismatch(
                cycle,
                ServiceCycleReplayMismatchCode.ActionCount,
                default,
                sink.Count < expectedActionCount ? 1 : 2);
        for (var index = 0; index < sink.Count; index++)
        {
            var expectedAction = expected.GetAction(index);
            var actualAction = sink[index];
            compared = Compare(
                cycle,
                _actionComparer,
                in expectedAction,
                in actualAction,
                ServiceCycleReplayMismatchCode.Action,
                new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.Action, index));
            if (compared.HasValue) return compared.Value;
        }

        TStateRecord next;
        try
        {
            next = _evaluator.CreateStateRecord(in state);
        }
        catch (Exception exception) when (ServiceCycleReplayContainedRunner.IsContainable(exception))
        {
            return Fault(
                cycle,
                ServiceCycleReplayFaultCode.EvaluatorFaulted,
                ServiceCycleReplayFailureLocation.Cycle);
        }
        compared = Compare(
            cycle,
            _stateComparer,
            in expectedNext,
            in next,
            ServiceCycleReplayMismatchCode.NextState,
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.NextState, 0));
        if (compared.HasValue) return compared.Value;

        if (actualWake.Kind != expectedWake.Kind)
            return Mismatch(cycle, ServiceCycleReplayMismatchCode.WakePolicy, default, 1);
        if (actualWake.Delay != expectedWake.Delay)
            return Mismatch(cycle, ServiceCycleReplayMismatchCode.WakePolicy, default, 2);
        if (actualWake.DueTime != expectedWake.DueTime)
            return Mismatch(cycle, ServiceCycleReplayMismatchCode.WakePolicy, default, 3);
        var projectionMismatch = CompareProjection(cycle, in expectedProjection, in actualProjection);
        if (projectionMismatch.HasValue) return projectionMismatch.Value;
        return ServiceCycleReplayExecutionResult.Success(1);
    }
}
