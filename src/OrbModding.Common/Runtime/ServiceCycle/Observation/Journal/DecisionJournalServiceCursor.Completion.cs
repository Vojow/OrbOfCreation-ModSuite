using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal partial struct DecisionJournalServiceCursor
{
    internal bool ApplyResponse(
        in ServiceResponseAcquisition acquisition,
        MonotonicTimestamp observedAt,
        out DecisionJournalObservation observation)
    {
        if (!acquisition.Acquired)
        {
            observation = default;
            return false;
        }
        var response = acquisition.Response;
        RequirePending(response.Cycle);
        if (response.TransientContention)
        {
            _pending.SetOutcome(default, false, default, false, _faultState);
            observation = Complete(default, observedAt);
            return true;
        }
        if (!response.Succeeded)
        {
            _pending.SetOutcome(
                response.EvaluationWakePolicy,
                response.HasEvaluationOutcome,
                default,
                false,
                _faultState);
            observation = Complete(default, observedAt);
            return true;
        }

        _pending.SetOutcome(response.WakePolicy, true, response.Projection, true, _faultState);
        if (!acquisition.TerminalReceipt.IsPresent)
        {
            observation = default;
            return false;
        }
        observation = Complete(acquisition.TerminalReceipt, observedAt);
        return true;
    }

    internal bool ApplyTerminal(
        BatchReceipt terminal,
        ServiceFault fault,
        MonotonicTimestamp observedAt,
        out DecisionJournalObservation observation)
    {
        if (!terminal.IsPresent)
        {
            observation = default;
            return false;
        }
        RequirePending(terminal.Cycle);
        if (fault.IsValid) _pending.Fault = fault;
        else _pending.Fault = _faultState;
        observation = Complete(terminal, observedAt);
        return true;
    }

    internal DecisionJournalObservation CompleteWithoutTerminal(MonotonicTimestamp observedAt) =>
        Complete(default, observedAt);

    private DecisionJournalObservation Complete(
        BatchReceipt terminal,
        MonotonicTimestamp observedAt)
    {
        if (!_hasPending) throw new InvalidOperationException("No journal cycle is pending.");
        var pending = _pending;
        _pending = default;
        _hasPending = false;
        return pending.ToObservation(Service, observedAt, in terminal);
    }

    private void RequirePending(ServiceCycleIdentity cycle)
    {
        if (!_hasPending || !cycle.IsValid || _pending.Cycle != cycle)
            throw new InvalidOperationException("Journal facts do not match the pending service cycle.");
    }
}
