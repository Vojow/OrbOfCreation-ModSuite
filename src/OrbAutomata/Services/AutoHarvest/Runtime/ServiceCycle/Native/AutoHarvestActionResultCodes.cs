using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoHarvestActionResultCodes
{
    public static ServiceActionResultCode ActionFamilyUnavailable => new(1024);
}
