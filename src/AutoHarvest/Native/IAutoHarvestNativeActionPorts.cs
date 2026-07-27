#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
#endif

namespace OrbAutomata;

internal interface IAutoHarvestBindingPort
{
    AutoHarvestResolvedPairSet ResolvePairSet();
}

/// <summary>
/// The action boundary's view of the live game: what it must re-check before mutating, and the
/// instance it mutates.
/// </summary>
/// <remarks>
/// There is no capture-side counterpart. Everything a harvest pair's decision rests on comes from
/// the world snapshot — through <see cref="AutoHarvestWorldFacts"/> and
/// <see cref="AutoHarvestActionSafety"/> — and rides on the action; the two things that cannot — the
/// live action queue, and the instance object to submit into — are read here, where acting on them is
/// the next statement.
/// </remarks>
internal interface IAutoHarvestSubmissionStatePort
{
    AutoHarvestSubmissionState CaptureSubmissionState(in ResolvedAutoHarvestPair resolved);

    /// <summary>
    /// The plot's one instance of the pair's action, or <c>null</c> when the plot holds no such
    /// instance, holds more than one, or holds something this contract cannot identify.
    /// </summary>
    object? ReadPrototype(in ResolvedAutoHarvestPair resolved);
}

internal interface IAutoHarvestMutationPort
{
    AutoHarvestSubmissionResult Submit(
        in ResolvedAutoHarvestPair resolved,
        in AutoHarvestPairFacts facts,
        AutoHarvestActionSafetyState safety
#if SERVICE_CYCLE_PROFILE
        , in ServiceActionContext context
#endif
        );
}

internal interface IAutoHarvestContractCircuit
{
    AutoHarvestNativeFailure FailureFor(AutoHarvestPair pair);
    void Block(AutoHarvestPair pair, AutoHarvestRuntimeFailureScope scope);
}

internal interface IAutoHarvestGatePort
{
    void ObserveResolvedPairs(in AutoHarvestResolvedPairSet pairs);
    bool IsQuarantined(AutoHarvestPair pair);
    void Quarantine(AutoHarvestPair pair);
}
