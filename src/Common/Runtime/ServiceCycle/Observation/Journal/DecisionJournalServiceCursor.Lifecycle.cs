using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal partial struct DecisionJournalServiceCursor
{
    internal DecisionJournalObservation ConstructionDeferred(
        in ServiceLifecycleConstructionDeferralFact fact,
        ConfigGeneration configuration,
        MonotonicTimestamp observedAt)
    {
        var projection = default(ServiceStateProjectionSnapshot);
        var fault = default(ServiceFault);
        var terminal = default(BatchReceipt);
        return new DecisionJournalObservation(
            Service,
            fact.Lifecycle.Value,
            configuration.Value,
            0,
            0,
            observedAt,
            observedAt,
            CommonServiceDecisionCodes.TransientContention.Value,
            0,
            false,
            default,
            false,
            in projection,
            in fault,
            in terminal);
    }

    internal bool TryConstructionFault(
        ServiceFault fault,
        LifecycleGeneration lifecycle,
        ConfigGeneration configuration,
        MonotonicTimestamp observedAt,
        out DecisionJournalObservation observation)
    {
        if (SameFault(in fault, in _constructionFault))
        {
            observation = default;
            return false;
        }
        _constructionFault = fault;
        if (!fault.IsValid)
        {
            if (_faultState.Category == ServiceFaultCategory.LifecycleConstruction)
                _faultState = default;
            observation = default;
            return false;
        }
        _faultState = fault;
        var projection = default(ServiceStateProjectionSnapshot);
        var terminal = default(BatchReceipt);
        observation = new DecisionJournalObservation(
            Service,
            lifecycle.Value,
            configuration.Value,
            0,
            0,
            observedAt,
            observedAt,
            0,
            0,
            false,
            default,
            false,
            in projection,
            in fault,
            in terminal);
        return true;
    }
}
