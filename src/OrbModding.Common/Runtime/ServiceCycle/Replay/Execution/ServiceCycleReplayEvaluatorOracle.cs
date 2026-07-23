using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>
/// Isolated evaluator oracle over the exact port used by the production replay worker. It verifies the
/// complete ordered action record sequence, next state, projection and wake policy and reports only the
/// first stable divergence.
/// </summary>
public sealed partial class ServiceCycleReplayEvaluatorOracle<
    TFrame,
    TConfig,
    TState,
    TAction,
    TCycleInputRecord,
    TStateRecord,
    TActionRecord>
    where TConfig : notnull
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    private readonly ServiceId _service;
    private readonly WakePolicy _defaultWakePolicy;
    private readonly IServiceCycleReplayEvaluatorPort<
        TFrame, TConfig, TState, TAction, TStateRecord, TActionRecord> _evaluator;
    private readonly IServiceCycleReplayHydrator<
        TFrame, TConfig, TState, TCycleInputRecord, TStateRecord> _hydrator;
    private readonly IServiceCycleReplayComparer<TCycleInputRecord> _inputComparer;
    private readonly IServiceCycleReplayComparer<TStateRecord> _stateComparer;
    private readonly IServiceCycleReplayComparer<TActionRecord> _actionComparer;

    public ServiceCycleReplayEvaluatorOracle(
        ServiceId service,
        WakePolicy defaultWakePolicy,
        IServiceCycleReplayEvaluatorPort<
            TFrame, TConfig, TState, TAction, TStateRecord, TActionRecord> evaluator,
        IServiceCycleReplayHydrator<
            TFrame, TConfig, TState, TCycleInputRecord, TStateRecord> hydrator,
        IServiceCycleReplayComparer<TCycleInputRecord> inputComparer,
        IServiceCycleReplayComparer<TStateRecord> stateComparer,
        IServiceCycleReplayComparer<TActionRecord> actionComparer)
    {
        if (!service.IsValid) throw new ArgumentException("A valid service identity is required.", nameof(service));
        if (!defaultWakePolicy.IsValid || defaultWakePolicy.Kind == WakePolicyKind.Default)
            throw new ArgumentException("A concrete valid default wake policy is required.", nameof(defaultWakePolicy));
        _service = service;
        _defaultWakePolicy = defaultWakePolicy;
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _hydrator = hydrator ?? throw new ArgumentNullException(nameof(hydrator));
        _inputComparer = inputComparer ?? throw new ArgumentNullException(nameof(inputComparer));
        _stateComparer = stateComparer ?? throw new ArgumentNullException(nameof(stateComparer));
        _actionComparer = actionComparer ?? throw new ArgumentNullException(nameof(actionComparer));
    }

    public ServiceCycleReplayExecutionResult Verify(
        in ServiceCycleReplayDecodedCycle<TCycleInputRecord, TStateRecord, TActionRecord> expected)
    {
        var cycle = expected.Context.Cycle;
        if (!cycle.IsValid || cycle.TraceServiceKey <= 0)
            throw new ArgumentException("A valid decoded replay cycle is required.", nameof(expected));

        var frame = default(TFrame)!;
        var state = default(TState)!;
        var hasFrame = false;
        var hasState = false;
        var completedNormally = false;
        var result = default(ServiceCycleReplayExecutionResult);
        try
        {
            result = VerifyCore(in expected, ref frame, ref state, ref hasFrame, ref hasState);
            completedNormally = true;
        }
        finally
        {
            ServiceCycleReplayExecutionResult? cleanupFailure = null;
            if (hasState)
            {
                try { _evaluator.ReleaseState(ref state); }
                catch (Exception exception) when (ServiceCycleReplayContainedRunner.IsContainable(exception))
                {
                    cleanupFailure = DetachedCleanupFailure(cycle);
                }
            }
            if (hasFrame)
            {
                try { _evaluator.ReleaseFrame(ref frame); }
                catch (Exception exception) when (ServiceCycleReplayContainedRunner.IsContainable(exception))
                {
                    cleanupFailure ??= DetachedCleanupFailure(cycle);
                }
            }
            if (completedNormally && result.Succeeded && cleanupFailure.HasValue)
                result = cleanupFailure.Value;
        }
        return result;
    }

}
