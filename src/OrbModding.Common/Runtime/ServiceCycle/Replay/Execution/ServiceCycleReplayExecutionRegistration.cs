using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using ArtifactCodecRole = OrbModding.Common.Runtime.ServiceCycle.Replay.Format.ServiceCycleReplayCodecRole;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal interface IServiceCycleReplayExecutionRegistration
{
    int TraceServiceKey { get; }
    ServiceCycleReplayExecutionResult VerifyEvaluator(ServiceCycleReplayArtifactDocument artifact);
    IServiceCycleReplayProductionParticipant PrepareProduction(ServiceCycleReplayProductionArtifactPlan plan);
}

/// <summary>Numeric trace-keyed typed execution registration. It never stores created feature components.</summary>
public sealed class ServiceCycleReplayExecutionRegistration<
    TFrame,
    TConfig,
    TState,
    TAction,
    TCycleInputRecord,
    TStateRecord,
    TActionRecord> : IServiceCycleReplayExecutionRegistration
    where TConfig : notnull
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    private readonly IServiceCycleReplayExecutionFactory<
        TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord> _factory;

    public ServiceCycleReplayExecutionRegistration(
        int traceServiceKey,
        IServiceCycleReplayExecutionFactory<
            TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord> factory)
    {
        if (traceServiceKey <= 0) throw new ArgumentOutOfRangeException(nameof(traceServiceKey));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        if (!factory.ServiceId.IsValid)
            throw new ArgumentException("The execution factory requires a valid service identity.", nameof(factory));
        TraceServiceKey = traceServiceKey;
    }

    public int TraceServiceKey { get; }

    internal IServiceCycleReplayExecutionFactory<
        TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord> Factory => _factory;

    public ServiceCycleReplayExecutionResult VerifyEvaluator(ServiceCycleReplayArtifactDocument artifact)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        if (!artifact.IsComplete)
            return ArtifactFailure(artifact, ServiceCycleReplayExecutionDetailCode.ArtifactNotComplete);

        try
        {
            return VerifyEvaluatorCore(artifact);
        }
        catch (Exception exception) when (ServiceCycleReplayContainedRunner.IsContainable(exception))
        {
            return ArtifactFailure(
                artifact,
                ServiceCycleReplayExecutionDetailCode.DetachedPreparationRejected);
        }
    }

    private ServiceCycleReplayExecutionResult VerifyEvaluatorCore(
        ServiceCycleReplayArtifactDocument artifact)
    {
        // All feature constructors and descriptor reads remain behind the complete artifact gate.
        var inputCodec = Required(_factory.CreateCycleInputCodec(), "cycle-input codec");
        var stateCodec = Required(_factory.CreateStateCodec(), "state codec");
        var actionCodec = Required(_factory.CreateActionCodec(), "action codec");
        if (ReferenceEquals(inputCodec, stateCodec) || ReferenceEquals(inputCodec, actionCodec) ||
            ReferenceEquals(stateCodec, actionCodec))
        {
            return ArtifactFailure(artifact, ServiceCycleReplayExecutionDetailCode.RegistrationDuplicated);
        }
        if (!ServiceCycleReplayFrozenCodec<TCycleInputRecord>.TryCreate(
                artifact, TraceServiceKey, ArtifactCodecRole.CycleInput,
                inputCodec, out var frozenInput) ||
            !ServiceCycleReplayFrozenCodec<TStateRecord>.TryCreate(
                artifact, TraceServiceKey, ArtifactCodecRole.State,
                stateCodec, out var frozenState) ||
            !ServiceCycleReplayFrozenCodec<TActionRecord>.TryCreate(
                artifact, TraceServiceKey, ArtifactCodecRole.Action,
                actionCodec, out var frozenAction))
            return ArtifactFailure(artifact, ServiceCycleReplayExecutionDetailCode.CodecDescriptorRejected);
        var decoded = ServiceCycleReplayTypedPlanDecoder.Decode(
            artifact,
            TraceServiceKey,
            _factory.ServiceId,
            frozenInput!,
            frozenState!,
            frozenAction!);
        if (!decoded.Succeeded)
        {
            var decodeFailure = decoded.Failure;
            return ServiceCycleReplayExecutionResult.Faulted(0, in decodeFailure);
        }

        var inputComparer = Required(_factory.CreateCycleInputComparer(), "cycle-input comparer");
        var stateComparer = Required(_factory.CreateStateComparer(), "state comparer");
        var actionComparer = Required(_factory.CreateActionComparer(), "action comparer");
        var hydrator = Required(_factory.CreateHydrator(), "hydrator");
        var evaluator = Required(_factory.CreateEvaluatorPort(), "evaluator port");
        var oracle = new ServiceCycleReplayEvaluatorOracle<
            TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>(
            _factory.ServiceId,
            _factory.DefaultWakePolicy,
            evaluator,
            hydrator,
            inputComparer,
            stateComparer,
            actionComparer);
        for (var index = 0; index < decoded.CycleCount; index++)
        {
            var cycle = decoded.GetCycle(index);
            var result = oracle.Verify(in cycle);
            if (result.Succeeded) continue;
            var failure = result.Failure;
            var mismatch = result.Mismatch;
            return result.Failure.IsValid
                ? ServiceCycleReplayExecutionResult.Faulted(index, in failure)
                : ServiceCycleReplayExecutionResult.Diverged(index, in mismatch);
        }
        return ServiceCycleReplayExecutionResult.Success(decoded.CycleCount);
    }

    IServiceCycleReplayProductionParticipant IServiceCycleReplayExecutionRegistration.PrepareProduction(
        ServiceCycleReplayProductionArtifactPlan plan) =>
        ServiceCycleReplayProductionParticipant<
            TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>.Create(
            plan, this);

    private ServiceCycleReplayExecutionResult ArtifactFailure(
        ServiceCycleReplayArtifactDocument artifact,
        ServiceCycleReplayExecutionDetailCode detail)
    {
        ServiceCycleReplayCycleKey cycle = default;
        for (var index = 0; index < artifact.CycleCount; index++)
        {
            var candidate = artifact.GetCycle(index).Key;
            if (candidate.TraceServiceKey != TraceServiceKey) continue;
            cycle = candidate;
            break;
        }
        if (!cycle.IsValid && artifact.CycleCount != 0) cycle = artifact.GetCycle(0).Key;
        if (!cycle.IsValid)
            cycle = new ServiceCycleReplayCycleKey(TraceServiceKey, 1, 1, 1, 1, 1);
        var fault = new ServiceCycleReplayFault(
            ServiceCycleReplayFaultCode.ExecutionFaulted,
            ServiceCycleReplayFailureLocation.Execution,
            (int)detail);
        var failure = new ServiceCycleReplayCycleFailure(cycle, fault);
        return ServiceCycleReplayExecutionResult.Faulted(0, in failure);
    }

    private static T Required<T>(T value, string role) where T : class =>
        value ?? throw new InvalidOperationException($"The replay execution factory returned no {role}.");
}
