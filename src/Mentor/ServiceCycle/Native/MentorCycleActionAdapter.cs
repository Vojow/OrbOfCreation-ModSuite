using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbMentor;

internal sealed class MentorCycleActionAdapter : IMentorCycleActionPort
{
    private readonly IMentorNativePort _native;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<MasteryExperienceDomain, bool> _captureMutationPermit;

    internal MentorCycleActionAdapter(
        IMentorNativePort native,
        Func<long> readLifecycleEpoch,
        Func<MasteryExperienceDomain, bool> captureMutationPermit)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _readLifecycleEpoch = readLifecycleEpoch ??
                              throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _captureMutationPermit = captureMutationPermit ??
                                 throw new ArgumentNullException(nameof(captureMutationPermit));
    }

    public ServiceActionResult TryExecute(
        in MentorCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
    {
        if (!MentorConfigurationPolicy.IsOperational(config) ||
            !MentorConfigurationPolicy.DomainEnabled(config, action.Domain))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);
        if (_readLifecycleEpoch() != action.CollectedAtEpoch)
            return ServiceActionResult.Rejected(CommonActionResultCodes.LifecycleReplaced);
        if (!_captureMutationPermit(action.Domain))
            return ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);

        var grant = _native.Grant(in action);
        return grant.Status switch
        {
            MentorNativeGrantStatus.Committed => ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(grant.Outcome, grant.CallOutcome)),
            MentorNativeGrantStatus.PostconditionFailed => ServiceActionResult.Faulted(
                CommonActionResultCodes.AdapterFault,
                ServiceNativeMutationEvidence.Observed(grant.Outcome, grant.CallOutcome)),
            MentorNativeGrantStatus.ContractUnavailable =>
                ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault),
            _ => ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected),
        };
    }
}
