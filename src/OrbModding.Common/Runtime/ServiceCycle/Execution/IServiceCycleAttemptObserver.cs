using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

/// <summary>
/// Optional owner-thread observation seam for facts that must be published before a feature callback
/// can reenter suite control. Implementations must not throw into gameplay.
/// </summary>
internal interface IServiceCycleAttemptObserver
{
    void StartAttempted(
        int ordinal,
        in ServiceCycleStartContext context,
        MonotonicTimestamp observedAt);
    void StartReady(
        int ordinal,
        in ServiceCycleStartContext context,
        in ServiceStartDecision decision,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration);
    void CaptureStarted(int ordinal, in ServiceCaptureContext context);
    void ActionAttempted(int ordinal, in ServiceActionContext context);
}
