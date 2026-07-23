namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal struct SuiteFramePumpFrameMetrics
{
    internal int Responses { get; set; }
    internal int Actions { get; set; }
    internal int Captures { get; set; }
    internal int EmergencyRejections { get; set; }
    internal long ResponseTicks { get; set; }
    internal long ActionTicks { get; set; }
    internal long CaptureTicks { get; set; }
}
