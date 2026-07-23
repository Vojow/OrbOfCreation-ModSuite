using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal static partial class ServiceCycleReplayTypedPlanDecoder
{
    private static CycleDecodeResult<TCycleInputRecord, TStateRecord, TActionRecord> DecodeCycle<
        TCycleInputRecord,
        TStateRecord,
        TActionRecord>(
        ServiceCycleReplayArtifactCycle wire,
        ServiceId service,
        IServiceCycleReplayCodec<TCycleInputRecord> inputCodec,
        IServiceCycleReplayCodec<TStateRecord> stateCodec,
        IServiceCycleReplayCodec<TActionRecord> actionCodec,
        ServiceCycleReplayRecordDecodeScratch scratch)
        where TCycleInputRecord : struct, IServiceCycleReplayRecord
        where TStateRecord : struct, IServiceCycleReplayRecord
        where TActionRecord : struct, IServiceCycleReplayRecord
    {
        var key = wire.Key;
        if (!wire.IsComplete || wire.RecordCount < 3)
            return CycleDecodeResult<TCycleInputRecord, TStateRecord, TActionRecord>.Fail(
                Failure(key, ServiceCycleReplayFaultCode.ExecutionFaulted,
                    ServiceCycleReplayFailureLocation.Execution,
                    ServiceCycleReplayExecutionDetailCode.ArtifactNotComplete));

        var input = DecodeAt(
            wire,
            0,
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0),
            inputCodec,
            scratch);
        if (!input.Succeeded)
            return CycleDecodeResult<TCycleInputRecord, TStateRecord, TActionRecord>.Fail(
                new ServiceCycleReplayCycleFailure(key, input.Fault));
        var previous = DecodeAt(
            wire,
            1,
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.PreviousState, 0),
            stateCodec,
            scratch);
        if (!previous.Succeeded)
            return CycleDecodeResult<TCycleInputRecord, TStateRecord, TActionRecord>.Fail(
                new ServiceCycleReplayCycleFailure(key, previous.Fault));

        var nextIndex = wire.RecordCount - 1;
        var next = DecodeAt(
            wire,
            nextIndex,
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.NextState, 0),
            stateCodec,
            scratch);
        if (!next.Succeeded)
            return CycleDecodeResult<TCycleInputRecord, TStateRecord, TActionRecord>.Fail(
                new ServiceCycleReplayCycleFailure(key, next.Fault));

        var actionCount = wire.Footer.ExpectedActionCount;
        if (actionCount < 0 || wire.RecordCount != checked(actionCount + 3))
            return CycleDecodeResult<TCycleInputRecord, TStateRecord, TActionRecord>.Fail(
                Failure(key, ServiceCycleReplayFaultCode.ExecutionFaulted,
                    ServiceCycleReplayFailureLocation.Execution,
                    ServiceCycleReplayExecutionDetailCode.DetachedRecordBytesRejected));
        var actions = new TActionRecord[actionCount];
        for (var index = 0; index < actionCount; index++)
        {
            var action = DecodeAt(
                wire,
                index + 2,
                new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.Action, index),
                actionCodec,
                scratch);
            if (!action.Succeeded)
                return CycleDecodeResult<TCycleInputRecord, TStateRecord, TActionRecord>.Fail(
                    new ServiceCycleReplayCycleFailure(key, action.Fault));
            actions[index] = action.Record;
        }

        var footer = wire.Footer;
        var artifactContext = footer.Context;
        var context = ServiceCycleReplayArtifactContextAdapter.Create(service, in artifactContext);
        if (!TryFindStatePublication(wire, out var publication, out var projectedAt))
            return CycleDecodeResult<TCycleInputRecord, TStateRecord, TActionRecord>.Fail(
                Failure(key, ServiceCycleReplayFaultCode.ExecutionFaulted,
                    ServiceCycleReplayFailureLocation.Execution,
                    ServiceCycleReplayExecutionDetailCode.SemanticEventBytesRejected));
        var cycle = new ServiceCycleReplayDecodedCycle<TCycleInputRecord, TStateRecord, TActionRecord>(
            context,
            input.Record,
            previous.Record,
            next.Record,
            actions,
            footer.ReturnedWake,
            footer.Projection,
            publication,
            projectedAt);
        return CycleDecodeResult<TCycleInputRecord, TStateRecord, TActionRecord>.Success(cycle);
    }

    private static ServiceCycleReplayRecordDecodeResult<TRecord> DecodeAt<TRecord>(
        ServiceCycleReplayArtifactCycle cycle,
        int index,
        ServiceCycleReplayRecordIdentity expected,
        IServiceCycleReplayCodec<TRecord> codec,
        ServiceCycleReplayRecordDecodeScratch scratch)
        where TRecord : struct, IServiceCycleReplayRecord
    {
        var wire = cycle.GetRecord(index);
        var encoded = new ServiceCycleReplayEncodedRecord(wire.Identity, wire.SchemaVersion, wire.PayloadView);
        return ServiceCycleReplayRecordDecoder.Decode(in encoded, expected, codec, scratch);
    }

    private static bool TryFindStatePublication(
        ServiceCycleReplayArtifactCycle cycle,
        out StatePublicationId publication,
        out MonotonicTimestamp projectedAt)
    {
        for (var index = 0; index < cycle.SemanticEventCount; index++)
        {
            var semantic = cycle.GetSemanticEvent(index);
            if (semantic.Kind != ServiceCycleSemanticEventKind.StatePublished) continue;
            publication = new StatePublicationId(semantic.Payload.StatePublication);
            projectedAt = new MonotonicTimestamp(semantic.Payload.TimestampTicks);
            return true;
        }
        publication = default;
        projectedAt = default;
        return false;
    }

    private readonly struct CycleDecodeResult<TCycleInputRecord, TStateRecord, TActionRecord>
        where TCycleInputRecord : struct, IServiceCycleReplayRecord
        where TStateRecord : struct, IServiceCycleReplayRecord
        where TActionRecord : struct, IServiceCycleReplayRecord
    {
        private CycleDecodeResult(
            bool succeeded,
            ServiceCycleReplayDecodedCycle<TCycleInputRecord, TStateRecord, TActionRecord> cycle,
            ServiceCycleReplayCycleFailure failure)
        {
            Succeeded = succeeded;
            Cycle = cycle;
            Failure = failure;
        }

        internal bool Succeeded { get; }
        internal ServiceCycleReplayDecodedCycle<TCycleInputRecord, TStateRecord, TActionRecord> Cycle { get; }
        internal ServiceCycleReplayCycleFailure Failure { get; }

        internal static CycleDecodeResult<TCycleInputRecord, TStateRecord, TActionRecord> Success(
            ServiceCycleReplayDecodedCycle<TCycleInputRecord, TStateRecord, TActionRecord> cycle) =>
            new(true, cycle, default);

        internal static CycleDecodeResult<TCycleInputRecord, TStateRecord, TActionRecord> Fail(
            ServiceCycleReplayCycleFailure failure) => new(false, default, failure);
    }
}
