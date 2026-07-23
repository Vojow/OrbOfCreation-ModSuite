using System;
using OrbModding.Common.Runtime;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal sealed partial class ServiceCycleDecisionJournalObserver
{
    public void Advance(MonotonicTimestamp observedAt)
    {
        if (IsFaulted) return;
        try { _journal.Advance(observedAt); }
        catch (Exception exception) when (CanContain(exception)) { _faulted = true; }
    }

    public void Stop(MonotonicTimestamp observedAt)
    {
        if (!_faulted)
        {
            try
            {
                for (var ordinal = 0; ordinal < _services.Length; ordinal++)
                {
                    ref var service = ref BoundService(ordinal);
                    if (!service.HasPending) continue;
                    var decision = service.CompleteWithoutTerminal(observedAt);
                    _journal.Observe(in decision);
                }
            }
            catch (Exception exception) when (CanContain(exception)) { _faulted = true; }
        }
        try { _journal.Stop(observedAt); }
        catch (Exception exception) when (CanContain(exception)) { _faulted = true; }
    }
}
