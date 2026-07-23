#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
#endif

namespace OrbAutomata;

internal interface IAutoHarvestBindingPort
{
    AutoHarvestResolvedPairSet ResolvePairSet();
}

internal interface IAutoHarvestStatePort :
    IAutoHarvestCaptureStatePort,
    IAutoHarvestSubmissionStatePort
{
}

internal interface IAutoHarvestCaptureStatePort
{
    AutoHarvestActiveActionSnapshot CaptureActiveActions(in ResolvedAutoHarvestPair resolved);
    void ReadFacts(
        in ResolvedAutoHarvestPair resolved,
        in AutoHarvestSubmissionState activeState,
        out AutoHarvestPairFacts facts,
        out object? prototype);
}

internal interface IAutoHarvestSubmissionStatePort
{
    AutoHarvestSubmissionState CaptureSubmissionState(in ResolvedAutoHarvestPair resolved);
}

internal interface IAutoHarvestMutationPort
{
    AutoHarvestSubmissionResult Submit(
        in ResolvedAutoHarvestPair resolved
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
