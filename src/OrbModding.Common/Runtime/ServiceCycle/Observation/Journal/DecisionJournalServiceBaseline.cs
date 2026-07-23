using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal readonly struct DecisionJournalServiceBaseline
{
    internal DecisionJournalServiceBaseline(
        LifecycleGeneration lifecycle,
        ConfigGeneration configuration,
        StrategyGeneration strategy,
        ServiceFault fault,
        long lifecycleSemanticVersion,
        long lifecycleTerminalSequence,
        long constructionDeferralSequence)
    {
        Lifecycle = lifecycle;
        Configuration = configuration;
        Strategy = strategy;
        Fault = fault;
        LifecycleSemanticVersion = lifecycleSemanticVersion;
        LifecycleTerminalSequence = lifecycleTerminalSequence;
        ConstructionDeferralSequence = constructionDeferralSequence;
    }

    internal LifecycleGeneration Lifecycle { get; }
    internal ConfigGeneration Configuration { get; }
    internal StrategyGeneration Strategy { get; }
    internal ServiceFault Fault { get; }
    internal long LifecycleSemanticVersion { get; }
    internal long LifecycleTerminalSequence { get; }
    internal long ConstructionDeferralSequence { get; }
}
