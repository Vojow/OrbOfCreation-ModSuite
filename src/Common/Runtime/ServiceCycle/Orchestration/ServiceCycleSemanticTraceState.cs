using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

/// <summary>Owner-thread observation cursors shared by the semantic fact translators.</summary>
internal sealed class ServiceCycleSemanticTraceState
{
    private readonly ServiceCycleSemanticServiceCursor[] _services;

    internal ServiceCycleSemanticTraceState(int serviceCount) =>
        _services = new ServiceCycleSemanticServiceCursor[serviceCount];

    internal ref ServiceCycleSemanticServiceCursor For(int ordinal) => ref _services[ordinal];
}

internal struct ServiceCycleSemanticServiceCursor
{
    internal LifecycleGeneration ActiveLifecycle;
    internal long LifecycleTerminalSequence;
    internal long LifecycleConstructionDeferralSequence;
    internal long LifecycleSemanticVersion;
    internal ServiceFault ConstructionFault;
    internal LifecycleGeneration ConstructionFaultLifecycle;
    internal BatchReceipt TerminalReceipt;
}
