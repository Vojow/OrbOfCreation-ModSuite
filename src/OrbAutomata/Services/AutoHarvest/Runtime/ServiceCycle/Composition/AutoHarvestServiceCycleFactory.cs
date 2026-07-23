using System;
using BepInEx.Logging;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbAutomata;

internal static class AutoHarvestServiceCycleFactory
{
    private const int ReplayByteCapacity = 4 * 1024 * 1024;
    private const int ReplayRecordCapacity = 65_536;
    private const int ReplayFooterCapacity = 8_192;
    private const int ReplaySemanticCapacity = 4_096;
    private const int ReplaySemanticCloseHeadroom = 512;
    internal const int ReplayMaximumSemanticEventsPerFrame = 64;
    private const int ReplaySemanticCaptureLimit =
        ReplaySemanticCapacity - ReplaySemanticCloseHeadroom;
    private const int ReplaySemanticFailureLimit =
        ReplaySemanticCapacity - ReplayMaximumSemanticEventsPerFrame;

    public static AutoHarvestServiceCycleRuntime Create(
        AutomataConfiguration configuration,
        AutoHarvestServiceCycleDependencies dependencies,
        ManualLogSource log)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));
        if (log is null) throw new ArgumentNullException(nameof(log));

        var lifecycle = AutomataServiceCycleHost.ToLifecycle(dependencies.ReadLifecycleEpoch());
        ServiceCycleRegistry? registry = null;
        ServiceCycleReplayRegistration<
            AutoHarvestCycleFrame,
            AutomataConfiguration,
            AutoHarvestCycleState,
            AutoHarvestCycleAction>? registration = null;
        AutomataServiceCycleHost? host = null;
        AutomataServiceCycleObservability? observability = null;
        AutomataReplayCapture? replayCapture = null;
        AutoHarvestServiceCycleDiagnosticsBridge? diagnostics = null;
        try
        {
            registry = new ServiceCycleRegistry(1, lifecycle);
            var replay = dependencies.Replay;
            observability = AutomataServiceCycleObservability.Create(
                registry.Clock,
                replay.Enabled,
                log);
#if SERVICE_CYCLE_PROFILE
            var profileProbe = observability.ProfileProbe;
#endif
            var adapters = AutoHarvestServiceAdapterComposition.Create(
                dependencies
#if SERVICE_CYCLE_PROFILE
                , profileProbe
#endif
                );
            var bindings = adapters.Bindings;
            var gates = adapters.Gates;
            var traceSession = replay.Enabled
                ? replay.TraceSession
                : new ServiceCycleTraceSessionId(1);
            var recording = new ServiceCycleReplaySession(
                traceSession,
                replay.Enabled
                    ? new ServiceCycleReplaySessionOptions(
                        true,
                        ReplayByteCapacity,
                        ReplayRecordCapacity,
                        ReplayFooterCapacity)
                    : new ServiceCycleReplaySessionOptions(false, 0, 0, 0));
#if SERVICE_CYCLE_PROFILE
            registration = registry.RegisterReplayProfiled(
                adapters.Definition,
                configuration,
                recording,
                profileProbe);
#else
            registration = registry.RegisterReplay(adapters.Definition, configuration, recording);
#endif
            var semantic = replay.Enabled
                ? new ServiceCycleSemanticRecorder(traceSession, ReplaySemanticCapacity, 1)
                : null;
            host = new AutomataServiceCycleHost(
                registry,
                dependencies.ReadFrameIdentity,
                dependencies.PumpTiming,
                semantic
#if SERVICE_CYCLE_PROFILE
                , profileProbe
#endif
                );
            var pump = host.Pump;
            if (replay.Enabled)
            {
                var source = pump.SemanticTrace ??
                    throw new InvalidOperationException("Enabled replay capture requires semantic trace evidence.");
                var storage = replay.CreateStorage?.Invoke() ??
                    throw new InvalidOperationException("Enabled replay capture requires a storage port.");
                var observer = replay.Observer ??
                    throw new InvalidOperationException("Enabled replay capture requires an observer.");
                replayCapture = new AutomataReplayCapture(
                    recording,
                    new AutomataReplayExportPort(
                        new ServiceCycleReplayArtifactExporter(
                            source,
                            recording,
                            storage,
                            new ServiceCycleReplayExportOptions(true, maximumCommittedArtifacts: 4),
                            observer),
                        ReplayMaximumSemanticEventsPerFrame),
                    new AutomataReplayWindow(source, pump),
                    observer,
                    ReplaySemanticCaptureLimit,
                    ReplaySemanticFailureLimit);
            }
            var observabilityOptions = dependencies.Observability;
            host.AttachObservability(observability, in observabilityOptions);
            observability = null;
            diagnostics = new AutoHarvestServiceCycleDiagnosticsBridge(
                checked((long)lifecycle.Value),
                configuration,
                dependencies.OwnsActionFamily(),
                dependencies.RuntimeDiagnostics,
                dependencies.FeatureStatus);
            return new AutoHarvestServiceCycleRuntime(
                dependencies.ReadLifecycleEpoch,
                dependencies.OwnsActionFamily,
                configuration,
                bindings,
                gates,
                registration,
                host,
                replayCapture,
                diagnostics);
        }
        catch
        {
            DisposeFailedConstruction(
                diagnostics,
                observability,
                replayCapture,
                host,
                registry,
                registration);
            throw;
        }
    }

    private static void DisposeFailedConstruction(
        IDisposable? diagnostics,
        IDisposable? observability,
        IDisposable? replayCapture,
        AutomataServiceCycleHost? host,
        ServiceCycleRegistry? registry,
        IDisposable? registration)
    {
        try
        {
            diagnostics?.Dispose();
        }
        finally
        {
            try { observability?.Dispose(); }
            finally
            {
                try { replayCapture?.Dispose(); }
                finally
                {
                    try
                    {
                        if (host is not null)
                        {
                            host.Shutdown();
                        }
                        else
                        {
                            registry?.Dispose();
                        }
                    }
                    finally
                    {
                        registration?.Dispose();
                    }
                }
            }
        }
    }
}
