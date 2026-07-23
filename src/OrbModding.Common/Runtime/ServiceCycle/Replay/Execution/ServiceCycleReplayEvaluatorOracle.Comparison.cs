using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
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
    private static ServiceCycleReplayExecutionResult DetachedCleanupFailure(
        ServiceCycleReplayCycleKey cycle) =>
        Fault(
            cycle,
            ServiceCycleReplayFaultCode.ExecutionFaulted,
            ServiceCycleReplayFailureLocation.Execution,
            ServiceCycleReplayExecutionDetailCode.DetachedCleanupRejected);

    private static ServiceCycleReplayExecutionResult? Compare<TRecord>(
        ServiceCycleReplayCycleKey cycle,
        IServiceCycleReplayComparer<TRecord> comparer,
        in TRecord expected,
        in TRecord actual,
        ServiceCycleReplayMismatchCode mismatchCode,
        ServiceCycleReplayRecordIdentity identity)
        where TRecord : struct, IServiceCycleReplayRecord
    {
        try
        {
            var comparison = comparer.Compare(in expected, in actual);
            if (!comparison.IsValid)
                return Fault(
                    cycle,
                    ServiceCycleReplayFaultCode.ComparerThrew,
                    ServiceCycleReplayFailureLocation.AtRecord(identity));
            return comparison.IsMatch
                ? null
                : Mismatch(cycle, mismatchCode, identity, comparison.FieldCode, comparison.ElementIndex);
        }
        catch (Exception exception) when (ServiceCycleReplayContainedRunner.IsContainable(exception))
        {
            return Fault(
                cycle,
                ServiceCycleReplayFaultCode.ComparerThrew,
                ServiceCycleReplayFailureLocation.AtRecord(identity));
        }
    }

    private static ServiceCycleReplayExecutionResult? CompareProjection(
        ServiceCycleReplayCycleKey cycle,
        in ServiceStateProjectionSnapshot expected,
        in ServiceStateProjectionSnapshot actual)
    {
        if (expected.Count != actual.Count)
            return Mismatch(cycle, ServiceCycleReplayMismatchCode.SemanticEvent, default, 1);
        for (var index = 0; index < expected.Count; index++)
        {
            var left = expected.GetEntry(index);
            var right = actual.GetEntry(index);
            if (left.Key != right.Key)
                return Mismatch(cycle, ServiceCycleReplayMismatchCode.SemanticEvent, default, 2, index);
            if (left.Value.Kind != right.Value.Kind)
                return Mismatch(cycle, ServiceCycleReplayMismatchCode.SemanticEvent, default, 3, index);
            if (left.Value.Integer != right.Value.Integer)
                return Mismatch(cycle, ServiceCycleReplayMismatchCode.SemanticEvent, default, 4, index);
            if (BitConverter.DoubleToInt64Bits(left.Value.FloatingPoint) !=
                BitConverter.DoubleToInt64Bits(right.Value.FloatingPoint))
                return Mismatch(cycle, ServiceCycleReplayMismatchCode.SemanticEvent, default, 5, index);
        }
        return null;
    }

    private static ServiceCycleReplayExecutionResult Fault(
        ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayFaultCode code,
        ServiceCycleReplayFailureLocation location,
        ServiceCycleReplayExecutionDetailCode detail = 0)
    {
        var fault = new ServiceCycleReplayFault(code, location, (int)detail);
        var failure = new ServiceCycleReplayCycleFailure(cycle, fault);
        return ServiceCycleReplayExecutionResult.Faulted(0, in failure);
    }

    private static ServiceCycleReplayExecutionResult Mismatch(
        ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayMismatchCode code,
        ServiceCycleReplayRecordIdentity identity,
        int fieldCode,
        int elementIndex = 0)
    {
        var divergence = new ServiceCycleReplayMismatch(code, identity, fieldCode, elementIndex);
        var mismatch = new ServiceCycleReplayCycleMismatch(cycle, divergence);
        return ServiceCycleReplayExecutionResult.Diverged(0, in mismatch);
    }

    private sealed class VerificationActionSink : IServiceCycleReplayActionSink<TActionRecord>
    {
        private readonly TActionRecord[] _records;

        internal VerificationActionSink(int capacity) => _records = new TActionRecord[capacity];

        internal int Count { get; private set; }
        internal TActionRecord this[int index] => _records[index];

        public void Offer(in TActionRecord record, int actualGameplayIndex)
        {
            if (actualGameplayIndex != Count || Count == _records.Length)
                throw new InvalidOperationException("The evaluator action writer produced an incoherent order.");
            _records[Count++] = record;
        }
    }
}
