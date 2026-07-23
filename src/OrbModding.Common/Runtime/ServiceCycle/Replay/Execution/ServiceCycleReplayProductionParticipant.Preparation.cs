using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using ArtifactCodecRole = OrbModding.Common.Runtime.ServiceCycle.Replay.Format.ServiceCycleReplayCodecRole;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed partial class ServiceCycleReplayProductionParticipant<
    TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>
{
    internal static IServiceCycleReplayProductionParticipant Create(
        ServiceCycleReplayProductionArtifactPlan artifactPlan,
        ServiceCycleReplayExecutionRegistration<
            TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord> registration)
    {
        var artifact = artifactPlan.Artifact;
        if (!HasReplayableConfigurationPublications(
                artifactPlan.GetService(registration.TraceServiceKey)))
        {
            var first = FindFirstCycle(artifact, registration.TraceServiceKey);
            var rejected = ServiceCycleReplayProductionResult.Fault(
                first,
                ServiceCycleReplayExecutionDetailCode.ConfigurationEvidenceMissing);
            return new ServiceCycleReplayProductionParticipant<
                TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>(
                registration.TraceServiceKey,
                rejected,
                null,
                0,
                first);
        }
        var factory = registration.Factory;
        var inputCodec = Required(factory.CreateCycleInputCodec());
        var stateCodec = Required(factory.CreateStateCodec());
        var actionCodec = Required(factory.CreateActionCodec());
        if (!ServiceCycleReplayFrozenCodec<TCycleInputRecord>.TryCreate(
                artifact,
                registration.TraceServiceKey,
                ArtifactCodecRole.CycleInput,
                inputCodec,
                out var frozenInput) ||
            !ServiceCycleReplayFrozenCodec<TStateRecord>.TryCreate(
                artifact,
                registration.TraceServiceKey,
                ArtifactCodecRole.State,
                stateCodec,
                out var frozenState) ||
            !ServiceCycleReplayFrozenCodec<TActionRecord>.TryCreate(
                artifact,
                registration.TraceServiceKey,
                ArtifactCodecRole.Action,
                actionCodec,
                out var frozenAction))
        {
            var first = FindFirstCycle(artifact, registration.TraceServiceKey);
            var preparation = ServiceCycleReplayProductionResult.Fault(
                first,
                ServiceCycleReplayExecutionDetailCode.CodecDescriptorRejected);
            return new ServiceCycleReplayProductionParticipant<
                TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>(
                registration.TraceServiceKey,
                preparation,
                null,
                0,
                first);
        }
        var decoded = ServiceCycleReplayTypedPlanDecoder.Decode(
            artifact,
            registration.TraceServiceKey,
            factory.ServiceId,
            frozenInput!,
            frozenState!,
            frozenAction!,
            artifactPlan);
        if (!decoded.Succeeded)
        {
            var failure = decoded.Failure;
            var preparation = ServiceCycleReplayExecutionResult.Faulted(0, in failure);
            return new ServiceCycleReplayProductionParticipant<
                TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>(
                registration.TraceServiceKey,
                preparation,
                null,
                0,
                failure.Cycle);
        }
        var verification = VerifyDecoded(factory, decoded);
        if (!verification.Succeeded)
            return new ServiceCycleReplayProductionParticipant<
                TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>(
                registration.TraceServiceKey,
                verification,
                null,
                0,
                verification.Failure.IsValid ? verification.Failure.Cycle : verification.Mismatch.Cycle);
        var native = artifactPlan.CreateNativeScript(registration.TraceServiceKey);
        var source = new ServiceCycleReplayProductionSource<
            TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>(
            factory,
            Required(factory.CreateHydrator()),
            decoded,
            native,
            artifactPlan,
            registration.TraceServiceKey);
        return new ServiceCycleReplayProductionParticipant<
            TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>(
            registration.TraceServiceKey,
            verification,
            source,
            decoded.CycleCount,
            decoded.GetCycle(0).Context.Cycle);
    }

    private static ServiceCycleReplayExecutionResult VerifyDecoded(
        IServiceCycleReplayExecutionFactory<
            TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord> factory,
        ServiceCycleReplayTypedArtifactResult<TCycleInputRecord, TStateRecord, TActionRecord> decoded)
    {
        var oracle = new ServiceCycleReplayEvaluatorOracle<
            TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>(
            factory.ServiceId,
            factory.DefaultWakePolicy,
            Required(factory.CreateEvaluatorPort()),
            Required(factory.CreateHydrator()),
            Required(factory.CreateCycleInputComparer()),
            Required(factory.CreateStateComparer()),
            Required(factory.CreateActionComparer()));
        for (var index = 0; index < decoded.CycleCount; index++)
        {
            var cycle = decoded.GetCycle(index);
            var result = oracle.Verify(in cycle);
            if (result.Succeeded) continue;
            var failure = result.Failure;
            var mismatch = result.Mismatch;
            return failure.IsValid
                ? ServiceCycleReplayExecutionResult.Faulted(index, in failure)
                : ServiceCycleReplayExecutionResult.Diverged(index, in mismatch);
        }
        return ServiceCycleReplayExecutionResult.Success(decoded.CycleCount);
    }

    private static T Required<T>(T value) where T : class =>
        value ?? throw new InvalidOperationException("The replay execution factory returned a null component.");

    private static bool HasReplayableConfigurationPublications(ServiceCycleReplayServiceEvidence evidence)
    {
        ulong expected = 1;
        if (evidence.ConfigurationCount == 0) return false;
        for (var index = 0; index < evidence.ConfigurationCount; index++)
        {
            if (evidence.GetConfiguration(index) != expected || expected == ulong.MaxValue) return false;
            expected++;
        }
        return true;
    }

    private static ServiceCycleReplayCycleKey FindFirstCycle(
        ServiceCycleReplayArtifactDocument artifact,
        int traceServiceKey)
    {
        for (var index = 0; index < artifact.CycleCount; index++)
        {
            var cycle = artifact.GetCycle(index).Key;
            if (cycle.TraceServiceKey == traceServiceKey) return cycle;
        }
        throw new InvalidOperationException("A production participant requires cycle evidence.");
    }
}
