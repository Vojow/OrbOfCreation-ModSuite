using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>Adapts a fully joined non-generic Format document to one typed replay registration.</summary>
internal static partial class ServiceCycleReplayTypedPlanDecoder
{
    internal static ServiceCycleReplayTypedArtifactResult<TCycleInputRecord, TStateRecord, TActionRecord> Decode<
        TCycleInputRecord,
        TStateRecord,
        TActionRecord>(
        ServiceCycleReplayArtifactDocument artifact,
        int traceServiceKey,
        ServiceId service,
        IServiceCycleReplayCodec<TCycleInputRecord> inputCodec,
        IServiceCycleReplayCodec<TStateRecord> stateCodec,
        IServiceCycleReplayCodec<TActionRecord> actionCodec,
        ServiceCycleReplayProductionArtifactPlan? productionPlan = null)
        where TCycleInputRecord : struct, IServiceCycleReplayRecord
        where TStateRecord : struct, IServiceCycleReplayRecord
        where TActionRecord : struct, IServiceCycleReplayRecord
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        if (traceServiceKey <= 0) throw new ArgumentOutOfRangeException(nameof(traceServiceKey));
        if (!service.IsValid) throw new ArgumentException("A valid service identity is required.", nameof(service));
        if (inputCodec is null) throw new ArgumentNullException(nameof(inputCodec));
        if (stateCodec is null) throw new ArgumentNullException(nameof(stateCodec));
        if (actionCodec is null) throw new ArgumentNullException(nameof(actionCodec));

        // This is the sole feature-codec admission. The Format decoder has already validated the complete
        // container, section checksums, semantic graph, joins, coverage and coherence at this point.
        if (!artifact.IsComplete)
            return Failed<TCycleInputRecord, TStateRecord, TActionRecord>(
                FirstCycleFor(artifact, traceServiceKey),
                ServiceCycleReplayFaultCode.ExecutionFaulted,
                ServiceCycleReplayFailureLocation.Execution,
                ServiceCycleReplayExecutionDetailCode.ArtifactNotComplete);

        var serviceCycleCount = productionPlan is null
            ? CountServiceCycles(artifact, traceServiceKey)
            : productionPlan.ServiceCycleCount(traceServiceKey);
        if (serviceCycleCount == 0)
            return Failed<TCycleInputRecord, TStateRecord, TActionRecord>(
                FirstCycleFor(artifact, traceServiceKey),
                ServiceCycleReplayFaultCode.ExecutionFaulted,
                ServiceCycleReplayFailureLocation.Execution,
                ServiceCycleReplayExecutionDetailCode.RegistrationMissing);

        var decoded = new ServiceCycleReplayDecodedCycle<
            TCycleInputRecord, TStateRecord, TActionRecord>[serviceCycleCount];
        var scratch = new ServiceCycleReplayRecordDecodeScratch();
        var output = 0;
        var candidateCount = productionPlan is null ? artifact.CycleCount : serviceCycleCount;
        for (var candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
        {
            var cycleIndex = productionPlan is null
                ? candidateIndex
                : productionPlan.GetArtifactCycleIndex(traceServiceKey, candidateIndex);
            var wire = artifact.GetCycle(cycleIndex);
            if (wire.Key.TraceServiceKey != traceServiceKey) continue;
            var result = DecodeCycle(wire, service, inputCodec, stateCodec, actionCodec, scratch);
            if (!result.Succeeded)
                return new ServiceCycleReplayTypedArtifactResult<
                    TCycleInputRecord, TStateRecord, TActionRecord>(result.Failure);
            decoded[output++] = result.Cycle;
        }
        return new ServiceCycleReplayTypedArtifactResult<
            TCycleInputRecord, TStateRecord, TActionRecord>(decoded);
    }

    private static int CountServiceCycles(ServiceCycleReplayArtifactDocument artifact, int traceServiceKey)
    {
        var count = 0;
        for (var index = 0; index < artifact.CycleCount; index++)
            if (artifact.GetCycle(index).Key.TraceServiceKey == traceServiceKey) count++;
        return count;
    }

    private static ServiceCycleReplayCycleKey FirstCycleFor(
        ServiceCycleReplayArtifactDocument artifact,
        int traceServiceKey)
    {
        for (var index = 0; index < artifact.CycleCount; index++)
        {
            var key = artifact.GetCycle(index).Key;
            if (key.TraceServiceKey == traceServiceKey) return key;
        }
        // An execution-level failure still needs a stable cycle location under the existing outcome
        // contract. Use the artifact's first structurally valid cycle when the requested key is absent.
        if (artifact.CycleCount != 0) return artifact.GetCycle(0).Key;
        throw new InvalidOperationException("A replay artifact without cycles cannot be dispatched.");
    }

    private static ServiceCycleReplayTypedArtifactResult<TCycleInputRecord, TStateRecord, TActionRecord> Failed<
        TCycleInputRecord,
        TStateRecord,
        TActionRecord>(
        ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayFaultCode code,
        ServiceCycleReplayFailureLocation location,
        ServiceCycleReplayExecutionDetailCode detail)
        where TCycleInputRecord : struct, IServiceCycleReplayRecord
        where TStateRecord : struct, IServiceCycleReplayRecord
        where TActionRecord : struct, IServiceCycleReplayRecord => new(
        Failure(cycle, code, location, detail));

    private static ServiceCycleReplayCycleFailure Failure(
        ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayFaultCode code,
        ServiceCycleReplayFailureLocation location,
        ServiceCycleReplayExecutionDetailCode detail) => new(
        cycle,
        new ServiceCycleReplayFault(code, location, (int)detail));

}
