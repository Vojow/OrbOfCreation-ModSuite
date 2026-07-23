using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal sealed class AutoHarvestCycleActionAdapter : IAutoHarvestCycleActionPort
{
    private readonly IAutoHarvestBindingPort _bindings;
    private readonly IAutoHarvestMutationPort _mutation;
    private readonly IAutoHarvestGatePort _gates;
    private readonly Func<bool> _ownsActionFamily;
    private readonly Func<bool> _tryCaptureMutationPermit;

    public AutoHarvestCycleActionAdapter(
        IAutoHarvestBindingPort bindings,
        IAutoHarvestMutationPort mutation,
        IAutoHarvestGatePort gates,
        Func<bool> ownsActionFamily,
        Func<bool> tryCaptureMutationPermit)
    {
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
        _gates = gates ?? throw new ArgumentNullException(nameof(gates));
        _ownsActionFamily = ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
    }

    public ServiceActionResult TryExecute(
        in AutoHarvestCycleAction action,
        in AutomataConfiguration config,
        in ServiceActionContext context)
    {
        if (!AutoHarvestConfigurationPolicy.IsOperational(config) ||
            !AutoHarvestConfigurationPolicy.IsSelected(config, action.Pair))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);

        if (_gates.IsQuarantined(action.Pair))
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        if (!_ownsActionFamily() || !_tryCaptureMutationPermit())
            return ServiceActionResult.Rejected(AutoHarvestActionResultCodes.ActionFamilyUnavailable);

        ResolvedAutoHarvestPair resolved;
        try
        {
            var pairs = _bindings.ResolvePairSet();
            var resolution = pairs.For(action.Pair);
            if (!resolution.Succeeded)
                return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
            resolved = resolution.Pair;
            if (!AutoHarvestNativeLifecycle.Matches(
                    resolved.LifecycleGeneration,
                    context.Cycle.Lifecycle))
                return ServiceActionResult.Rejected(CommonActionResultCodes.LifecycleReplaced);
            _gates.ObserveResolvedPairs(pairs);
        }
        catch (Exception ex) when (AutoHarvestReflectionAccess.IsExpectedFailure(ex))
        {
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        }

        var result = _mutation.Submit(
            resolved
#if SERVICE_CYCLE_PROFILE
            , in context
#endif
            );
        if (!result.Verified && result.MutationAttempted)
            _gates.Quarantine(action.Pair);
        return AutoHarvestActionResultMapper.FromMutation(result);
    }
}
