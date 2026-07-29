using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;

internal sealed class DecisionJournalStatusRegistration : IDisposable
{
    private DecisionJournalStatusRegistry? _registry;

    internal DecisionJournalStatusRegistration(DecisionJournalStatusRegistry registry) => _registry = registry;

    internal bool Publish(DecisionJournalStatus status) => Registry().Publish(this, status);

    public void Dispose()
    {
        var registry = _registry;
        if (registry is null) return;
        registry.Remove(this);
        _registry = null;
    }

    private DecisionJournalStatusRegistry Registry() =>
        _registry ?? throw new ObjectDisposedException(nameof(DecisionJournalStatusRegistration));
}
