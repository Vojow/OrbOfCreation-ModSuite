using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplaySemanticMatch
{
    internal static bool HasFullCycleIdentity(ServiceCycleSemanticEvent item) =>
        (item.Payload.Fields & ServiceCycleSemanticPayload.CycleFields) == ServiceCycleSemanticPayload.CycleFields;

    internal static bool HasCaptureIdentity(ServiceCycleSemanticEvent item) =>
        (item.Payload.Fields & ServiceCycleSemanticPayload.CaptureFields) ==
        ServiceCycleSemanticPayload.CaptureFields;

    internal static ServiceCycleReplayCycleKey KeyFrom(ServiceCycleSemanticEvent item) => new(
        checked((int)item.Payload.Service), item.Payload.Lifecycle, item.Payload.Configuration,
        item.Payload.Strategy, item.Payload.Capture, item.Payload.Cycle);

    internal static bool Matches(ServiceCycleSemanticEvent item, ServiceCycleReplayCycleKey key) =>
        item.Payload.Service == (ulong)key.TraceServiceKey && item.Payload.Lifecycle == key.Lifecycle &&
        item.Payload.Configuration == key.Configuration && item.Payload.Strategy == key.Strategy &&
        item.Payload.Capture == key.Capture && item.Payload.Cycle == key.Cycle;
}
