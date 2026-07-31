#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>
/// Owner-thread evidence copied out of the ServiceCycle after its frame is idle.
/// </summary>
internal sealed class GameMcpRuntimeState
{
    private GameMcpRuntimeState(
        WorldPublication<GameWorldState> world,
        bool emergencyStopEngaged,
        long acceptedFrameCount,
        long currentLifecycle,
        GameMcpServiceRuntimeState[] services)
    {
        World = world;
        EmergencyStopEngaged = emergencyStopEngaged;
        AcceptedFrameCount = acceptedFrameCount;
        CurrentLifecycle = currentLifecycle;
        Services = services;
    }

    internal WorldPublication<GameWorldState> World { get; }
    internal bool EmergencyStopEngaged { get; }
    internal long AcceptedFrameCount { get; }
    internal long CurrentLifecycle { get; }
    internal GameMcpServiceRuntimeState[] Services { get; }

    internal static GameMcpRuntimeState Capture(AutomataServiceCycleHost host)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));
        var pump = host.Pump;
        var registry = pump.DiagnosticsRegistry;
        var services = new GameMcpServiceRuntimeState[registry.OrdinalCount];
        for (var ordinal = 0; ordinal < services.Length; ordinal++)
        {
            var id = registry.GetServiceId(ordinal);
            var slot = registry.GetSlot(ordinal);
            services[ordinal] = slot.TryGetRunnerSnapshot(out var runner)
                ? new GameMcpServiceRuntimeState(
                    id.Value,
                    AutomataServiceCycleTraceRoster.DisplayName(id),
                    true,
                    runner)
                : new GameMcpServiceRuntimeState(
                    id.Value,
                    AutomataServiceCycleTraceRoster.DisplayName(id),
                    false,
                    default);
        }

        var diagnostics = pump.DiagnosticsSnapshot;
        return new GameMcpRuntimeState(
            registry.World.ReadLatest(),
            host.EmergencyStopEngaged,
            diagnostics.AcceptedFrameCount,
            checked((long)host.CurrentLifecycle.Value),
            services);
    }
}

internal readonly struct GameMcpServiceRuntimeState
{
    internal GameMcpServiceRuntimeState(
        string serviceId,
        string displayName,
        bool hasRunner,
        ServiceRunnerSnapshot runner)
    {
        ServiceId = serviceId;
        DisplayName = displayName;
        HasRunner = hasRunner;
        Runner = runner;
    }

    internal string ServiceId { get; }
    internal string DisplayName { get; }
    internal bool HasRunner { get; }
    internal ServiceRunnerSnapshot Runner { get; }
}
#endif
