using System;
using System.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

internal interface IServiceCycleReplayActionSink<TActionRecord>
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    void Offer(in TActionRecord record, int actualGameplayIndex);
}

internal sealed class ServiceCycleReplayWorkerRecorder<TCycleInputRecord, TStateRecord, TActionRecord> :
    IServiceCycleReplayActionSink<TActionRecord>
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    private readonly ServiceCycleReplaySession _session;
    private readonly IServiceCycleReplayCodec<TCycleInputRecord> _cycleInputCodec;
    private readonly IServiceCycleReplayCodec<TStateRecord> _stateCodec;
    private readonly IServiceCycleReplayCodec<TActionRecord> _actionCodec;
    private readonly ServiceCycleReplayCodecDescriptor _cycleInputDescriptor;
    private readonly ServiceCycleReplayCodecDescriptor _stateDescriptor;
    private readonly ServiceCycleReplayCodecDescriptor _actionDescriptor;
    private readonly byte[] _scratch;
    private ServiceCycleReplayContext _context;
    private ServiceCycleReplayCompleteness _completeness;
    private WakePolicy _returnedWake;
    private long _firstRecordSequence;
    private long _lastRecordSequence;
    private long _encodingDurationTicks;
    private long _encodingAllocatedBytes;
    private int _retainedRecordCount;
    private bool _hasReturnedWake;
    private bool _active;

    internal ServiceCycleReplayWorkerRecorder(
        ServiceCycleReplaySession session,
        IServiceCycleReplayCodec<TCycleInputRecord> cycleInputCodec,
        IServiceCycleReplayCodec<TStateRecord> stateCodec,
        IServiceCycleReplayCodec<TActionRecord> actionCodec,
        in ServiceCycleReplayCodecDescriptor cycleInputDescriptor,
        in ServiceCycleReplayCodecDescriptor stateDescriptor,
        in ServiceCycleReplayCodecDescriptor actionDescriptor)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _cycleInputCodec = cycleInputCodec ?? throw new ArgumentNullException(nameof(cycleInputCodec));
        _stateCodec = stateCodec ?? throw new ArgumentNullException(nameof(stateCodec));
        _actionCodec = actionCodec ?? throw new ArgumentNullException(nameof(actionCodec));
        _cycleInputDescriptor = cycleInputDescriptor;
        _stateDescriptor = stateDescriptor;
        _actionDescriptor = actionDescriptor;
        var maximum = Math.Max(
            cycleInputDescriptor.MaximumEncodedBytes,
            Math.Max(stateDescriptor.MaximumEncodedBytes, actionDescriptor.MaximumEncodedBytes));
        _scratch = new byte[maximum];
        _completeness = ServiceCycleReplayCompleteness.Complete;
    }

    internal object CycleInputCodecIdentity => _cycleInputCodec;
    internal object StateCodecIdentity => _stateCodec;
    internal object ActionCodecIdentity => _actionCodec;

    internal void Begin(in ServiceCycleContext context, int traceServiceKey)
    {
        if (_active)
            throw new InvalidOperationException("The previous replay recording transaction was not finalized.");
        if (!_session.TryBeginRecordingCycle()) return;
        _context = new ServiceCycleReplayContext(traceServiceKey, in context);
        _completeness = ServiceCycleReplayCompleteness.Complete;
        _returnedWake = default;
        _firstRecordSequence = 0;
        _lastRecordSequence = 0;
        _encodingDurationTicks = 0;
        _encodingAllocatedBytes = 0;
        _retainedRecordCount = 0;
        _hasReturnedWake = false;
        _active = true;
    }

    internal void RecordCycleInput(in TCycleInputRecord record) => Encode(
        in record,
        _cycleInputCodec,
        in _cycleInputDescriptor,
        new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0));

    internal void RecordPreviousState(in TStateRecord record) => Encode(
        in record,
        _stateCodec,
        in _stateDescriptor,
        new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.PreviousState, 0));

    internal void RecordNextState(in TStateRecord record) => Encode(
        in record,
        _stateCodec,
        in _stateDescriptor,
        new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.NextState, 0));

    internal void RecordAction(in TActionRecord record, int actualGameplayIndex) => Encode(
        in record,
        _actionCodec,
        in _actionDescriptor,
        new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.Action, actualGameplayIndex));

    void IServiceCycleReplayActionSink<TActionRecord>.Offer(
        in TActionRecord record,
        int actualGameplayIndex) => RecordAction(in record, actualGameplayIndex);

    internal void MarkRecordProductionFailed(ServiceCycleReplayRecordIdentity identity)
    {
        if (!_active || !_completeness.IsComplete) return;
        var cycle = _context.Cycle;
        _session.MarkRequiredRecordMissing(in cycle, identity);
        _completeness = ServiceCycleReplayCompleteness.Incomplete(
            ServiceCycleReplayCompletenessCode.RequiredRecordMissing,
            ServiceCycleReplayFailureLocation.AtRecord(identity));
    }

    internal void RecordReturnedWake(WakePolicy returnedWake)
    {
        if (!_active) return;
        _returnedWake = returnedWake;
        _hasReturnedWake = true;
    }

    internal void SealProvisional(
        in ServiceStateProjectionSnapshot projection,
        int expectedActionCount) => Seal(
            ServiceCycleReplayCycleFooterDisposition.Provisional,
            projection,
            true,
            expectedActionCount);

    internal void AbortEvaluation(int expectedActionCount) => Seal(
        ServiceCycleReplayCycleFooterDisposition.EvaluationAborted,
        default,
        false,
        expectedActionCount);

    internal void AbortProjection(int expectedActionCount) => Seal(
        ServiceCycleReplayCycleFooterDisposition.ProjectionAborted,
        default,
        false,
        expectedActionCount);

    private void Encode<TRecord>(
        in TRecord record,
        IServiceCycleReplayCodec<TRecord> codec,
        in ServiceCycleReplayCodecDescriptor descriptor,
        ServiceCycleReplayRecordIdentity identity)
        where TRecord : struct, IServiceCycleReplayRecord
    {
        if (!_active || !_completeness.IsComplete) return;
        if (!_session.CanInvokeCodec)
        {
            InheritStoppedSession(identity);
            return;
        }

        var beforeTicks = Stopwatch.GetTimestamp();
        var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            var encodedLength = codec.Encode(in record, _scratch.AsSpan(0, descriptor.MaximumEncodedBytes));
            var contract = ServiceCycleReplayCodecContract.ValidateEncodeResult(
                in descriptor,
                descriptor.MaximumEncodedBytes,
                encodedLength);
            if (contract != ServiceCycleReplayCodecContractCode.Valid)
            {
                var cycle = _context.Cycle;
                _session.MarkCodecContractRejected(in cycle, identity, contract);
                _completeness = ServiceCycleReplayCompleteness.Incomplete(
                    ServiceCycleReplayCompletenessCode.CodecContractRejected,
                    ServiceCycleReplayFailureLocation.AtRecord(identity));
                return;
            }

            var currentCycle = _context.Cycle;
            if (!_session.TryAppendRecord(
                    in currentCycle,
                    identity,
                    in descriptor,
                    _scratch,
                    encodedLength,
                    out var sequence))
            {
                InheritStoppedSession(identity);
                return;
            }

            if (_retainedRecordCount == 0) _firstRecordSequence = sequence;
            _lastRecordSequence = sequence;
            _retainedRecordCount++;
        }
        // Live codec failures remain observational. Strict production replay promotes the fatal
        // exception triple and lets the worker root relay it across the offline boundary.
        catch (Exception exception) when (
            exception is not StackOverflowException &&
            !ServiceCycleFatalExceptionPolicy.MustEscape(this, exception))
        {
            var cycle = _context.Cycle;
            _session.MarkCodecThrew(in cycle, identity);
            _completeness = ServiceCycleReplayCompleteness.Incomplete(
                ServiceCycleReplayCompletenessCode.CodecFaulted,
                ServiceCycleReplayFailureLocation.AtRecord(identity));
        }
        finally
        {
            _encodingDurationTicks = SaturatingAdd(
                _encodingDurationTicks,
                Stopwatch.GetTimestamp() - beforeTicks);
            _encodingAllocatedBytes = SaturatingAdd(
                _encodingAllocatedBytes,
                GC.GetAllocatedBytesForCurrentThread() - beforeAllocated);
        }
    }

    private void InheritStoppedSession(ServiceCycleReplayRecordIdentity identity)
    {
        if (!_session.EncodingEnabled) return;
        if (!_session.TryReadFailure(out _, out var completeness, out _))
        {
            // Another worker may have won first-failure publication but still be between the
            // stopping CAS and the final release write. Never let this transaction claim a
            // complete footer after it skipped a required record.
            _completeness = ServiceCycleReplayCompleteness.Incomplete(
                ServiceCycleReplayCompletenessCode.CycleIncomplete,
                ServiceCycleReplayFailureLocation.Cycle);
            return;
        }
        var location = completeness.FailureLocation.Scope == ServiceCycleReplayFailureScope.Record
            ? ServiceCycleReplayFailureLocation.AtRecord(identity)
            : completeness.FailureLocation;
        _completeness = ServiceCycleReplayCompleteness.Incomplete(completeness.Code, location);
    }

    private void Seal(
        ServiceCycleReplayCycleFooterDisposition disposition,
        ServiceStateProjectionSnapshot projection,
        bool hasProjection,
        int expectedActionCount)
    {
        if (!_active) return;
        var footer = new ServiceCycleReplayCycleFooter(
            0,
            _context,
            disposition,
            _returnedWake,
            _hasReturnedWake,
            projection,
            hasProjection,
            expectedActionCount,
            _firstRecordSequence,
            _lastRecordSequence,
            _retainedRecordCount,
            _completeness,
            _encodingDurationTicks,
            Stopwatch.Frequency,
            _encodingAllocatedBytes);
        _session.TryAppendFooter(in footer, out _);
        _active = false;
    }

    private static long SaturatingAdd(long left, long right)
    {
        if (right <= 0) return left;
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }
}
