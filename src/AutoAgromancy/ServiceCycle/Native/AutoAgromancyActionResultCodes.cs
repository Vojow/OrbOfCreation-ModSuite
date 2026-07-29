using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoAgromancyActionResultCodes
{
    internal static ServiceActionResultCode ActionFamilyUnavailable => new(1800);
    internal static ServiceActionResultCode LiveConfigurationChanged => new(1801);
    internal static ServiceActionResultCode LiveFactsChanged => new(1802);
    internal static ServiceActionResultCode PairUnavailable => new(1803);
    internal static ServiceActionResultCode MutationQuarantined => new(1804);
    internal static ServiceActionResultCode SafetyRollback => new(1805);
}
