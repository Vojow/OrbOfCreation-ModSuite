using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

/// <summary>
/// One service's attach-time state. Configuration and strategy are absent on purpose: the suite has
/// one of each and the observer keeps one baseline for both, not a copy per service.
/// </summary>
internal readonly struct DecisionJournalServiceBaseline
{
    internal DecisionJournalServiceBaseline(
        LifecycleGeneration lifecycle,
        ServiceFault fault,
        long lifecycleSemanticVersion,
        long lifecycleTerminalSequence,
        long constructionDeferralSequence,
        long worldGateDeferralSequence)
    {
        Lifecycle = lifecycle;
        Fault = fault;
        LifecycleSemanticVersion = lifecycleSemanticVersion;
        LifecycleTerminalSequence = lifecycleTerminalSequence;
        ConstructionDeferralSequence = constructionDeferralSequence;
        WorldGateDeferralSequence = worldGateDeferralSequence;
    }

    internal LifecycleGeneration Lifecycle { get; }
    internal ServiceFault Fault { get; }
    internal long LifecycleSemanticVersion { get; }
    internal long LifecycleTerminalSequence { get; }
    internal long ConstructionDeferralSequence { get; }
    internal long WorldGateDeferralSequence { get; }
}
