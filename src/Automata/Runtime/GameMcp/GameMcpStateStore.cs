#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbChronicle;

namespace OrbAutomata.GameMcp;

/// <summary>
/// Latest-wins immutable handoff from Unity's main thread to MCP HTTP workers.
/// </summary>
internal sealed class GameMcpStateStore
{
    private GameMcpStateSnapshot _latest = GameMcpStateSnapshot.Unavailable(
        "the Unity main thread has not published MCP state yet");

    internal GameMcpStateSnapshot ReadLatest() => Volatile.Read(ref _latest);

    internal void Capture(
        SuiteRuntimeConfiguration configuration,
        ConfigGeneration configurationGeneration,
        string writableConfigurationJson,
        long lifecycleGeneration,
        string sceneName,
        bool nativeContractsAvailable,
        IReadOnlyList<FeatureStatusSnapshot> featureStatuses,
        DecisionJournalStatus journalStatus,
        long journalRevision,
        GameMcpRuntimeState? runtime,
        ChronicleRunSnapshot chronicle,
        ChronicleHistorySnapshot? chronicleHistory = null)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (writableConfigurationJson is null)
            throw new ArgumentNullException(nameof(writableConfigurationJson));
        if (featureStatuses is null) throw new ArgumentNullException(nameof(featureStatuses));
        if (chronicle is null) throw new ArgumentNullException(nameof(chronicle));

        var featureArray = new FeatureStatusSnapshot[featureStatuses.Count];
        for (var index = 0; index < featureArray.Length; index++)
            featureArray[index] = featureStatuses[index];

        var configurationJson = GameMcpObjectProjector.Project(configuration)
            .ToString(Formatting.None);
        var health = BuildHealth(
            configurationGeneration,
            lifecycleGeneration,
            sceneName,
            nativeContractsAvailable,
            featureArray,
            runtime);
        var traceHealth = GameMcpObjectProjector.Project(journalStatus) as JObject ?? new JObject();
        traceHealth["revision"] = journalRevision;
        var chronicleObject = GameMcpObjectProjector.Project(chronicle) as JObject ?? new JObject();
        if (chronicleHistory is not null)
            chronicleObject["history"] = GameMcpObjectProjector.Project(chronicleHistory);
        CompactRuneTimelines(chronicleObject);
        var chronicleJson = chronicleObject.ToString(Formatting.None);

        Volatile.Write(
            ref _latest,
            new GameMcpStateSnapshot(
                runtime?.World,
                configurationGeneration,
                lifecycleGeneration,
                DateTime.UtcNow.Ticks,
                configurationJson,
                writableConfigurationJson,
                health.ToString(Formatting.None),
                traceHealth.ToString(Formatting.None),
                runtime is not null,
                runtime is null
                    ? "the ServiceCycle runtime has not published a world in this scene"
                    : string.Empty,
                chronicleJson,
                chronicle,
                chronicleHistory));
    }

    private static void CompactRuneTimelines(JObject value)
    {
        if (value["runeTimeline"] is JArray timeline)
        {
            value["runeEventCount"] = timeline.Count;
            value.Remove("runeTimeline");
        }
        if (value["history"] is not JObject history) return;
        if (history["personalBest"] is JObject personalBest) CompactRecordedRun(personalBest);
        if (history["comparison"] is JObject comparison) CompactRecordedRun(comparison);
        if (history["runs"] is JArray runs)
        {
            foreach (var run in runs.OfType<JObject>()) CompactRecordedRun(run);
        }
    }

    private static void CompactRecordedRun(JObject run)
    {
        if (run["runeTimeline"] is not JArray timeline) return;
        run["runeEventCount"] = timeline.Count;
        run.Remove("runeTimeline");
    }

    private static JObject BuildHealth(
        ConfigGeneration configurationGeneration,
        long lifecycleGeneration,
        string sceneName,
        bool nativeContractsAvailable,
        FeatureStatusSnapshot[] featureStatuses,
        GameMcpRuntimeState? runtime)
    {
        var services = new JArray();
        if (runtime is not null)
        {
            for (var index = 0; index < runtime.Services.Length; index++)
            {
                var service = runtime.Services[index];
                var item = new JObject
                {
                    ["serviceId"] = service.ServiceId,
                    ["displayName"] = service.DisplayName,
                    ["hasRunner"] = service.HasRunner,
                };
                if (service.HasRunner)
                    item["runner"] = GameMcpObjectProjector.Project(service.Runner);
                else
                    item["notAvailableReason"] = "the service has no active lifecycle runner";
                services.Add(item);
            }
        }

        var features = new JArray();
        for (var index = 0; index < featureStatuses.Length; index++)
            features.Add(GameMcpObjectProjector.Project(featureStatuses[index]));

        return new JObject
        {
            ["runtimeAvailable"] = runtime is not null,
            ["runtimeNotAvailableReason"] = runtime is null
                ? "the ServiceCycle runtime has not published a world in this scene"
                : string.Empty,
            ["scene"] = sceneName ?? string.Empty,
            ["nativeContractsAvailable"] = nativeContractsAvailable,
            ["configurationGeneration"] = configurationGeneration.Value,
            ["lifecycleGeneration"] = lifecycleGeneration,
            ["emergencyStopEngaged"] = runtime?.EmergencyStopEngaged ?? false,
            ["acceptedFrameCount"] = runtime?.AcceptedFrameCount ?? 0,
            ["runtimeLifecycle"] = runtime?.CurrentLifecycle ?? 0,
            ["pendingMcpCommands"] = 0,
            ["features"] = features,
            ["services"] = services,
        };
    }
}

internal sealed class GameMcpStateSnapshot
{
    internal GameMcpStateSnapshot(
        ServiceWorldPublication? world,
        ConfigGeneration configurationGeneration,
        long lifecycleGeneration,
        long capturedAtUtcTicks,
        string configurationJson,
        string writableConfigurationJson,
        string healthJson,
        string traceHealthJson,
        bool runtimeAvailable,
        string runtimeNotAvailableReason,
        string chronicleJson = "{}",
        ChronicleRunSnapshot? chronicle = null,
        ChronicleHistorySnapshot? chronicleHistory = null)
    {
        World = world;
        ConfigurationGeneration = configurationGeneration;
        LifecycleGeneration = lifecycleGeneration;
        CapturedAtUtcTicks = capturedAtUtcTicks;
        ConfigurationJson = configurationJson;
        WritableConfigurationJson = writableConfigurationJson;
        HealthJson = healthJson;
        TraceHealthJson = traceHealthJson;
        RuntimeAvailable = runtimeAvailable;
        RuntimeNotAvailableReason = runtimeNotAvailableReason;
        ChronicleJson = chronicleJson ?? "{}";
        Chronicle = chronicle;
        ChronicleHistory = chronicleHistory;
    }

    internal GameMcpStateSnapshot(
        OrbModding.Common.Runtime.ServiceCycle.Configuration.WorldPublication<
            OrbModding.Common.Runtime.World.GameWorldState>? world,
        ConfigGeneration configurationGeneration,
        long lifecycleGeneration,
        long capturedAtUtcTicks,
        string configurationJson,
        string writableConfigurationJson,
        string healthJson,
        string traceHealthJson,
        bool runtimeAvailable,
        string runtimeNotAvailableReason,
        string chronicleJson = "{}",
        ChronicleRunSnapshot? chronicle = null,
        ChronicleHistorySnapshot? chronicleHistory = null)
        : this(
            world is null ? null : new ServiceWorldPublication(world),
            configurationGeneration,
            lifecycleGeneration,
            capturedAtUtcTicks,
            configurationJson,
            writableConfigurationJson,
            healthJson,
            traceHealthJson,
            runtimeAvailable,
            runtimeNotAvailableReason,
            chronicleJson,
            chronicle,
            chronicleHistory)
    {
    }

    internal ServiceWorldPublication? World { get; }
    internal ConfigGeneration ConfigurationGeneration { get; }
    internal long LifecycleGeneration { get; }
    internal long CapturedAtUtcTicks { get; }
    internal string ConfigurationJson { get; }
    internal string WritableConfigurationJson { get; }
    internal string HealthJson { get; }
    internal string TraceHealthJson { get; }
    internal bool RuntimeAvailable { get; }
    internal string RuntimeNotAvailableReason { get; }
    internal string ChronicleJson { get; }
    internal ChronicleRunSnapshot? Chronicle { get; }
    internal ChronicleHistorySnapshot? ChronicleHistory { get; }

    internal static GameMcpStateSnapshot Unavailable(string reason) =>
        new(
            (ServiceWorldPublication?)null,
            default,
            0,
            DateTime.UtcNow.Ticks,
            "{}",
            "[]",
            "{}",
            "{}",
            false,
            reason,
            "{}");
}

/// <summary>Non-generic wrapper so the state snapshot exposes one simple nullable world slot.</summary>
internal sealed class ServiceWorldPublication
{
    internal ServiceWorldPublication(
        OrbModding.Common.Runtime.ServiceCycle.Configuration.WorldPublication<
            OrbModding.Common.Runtime.World.GameWorldState> publication)
    {
        Generation = publication.Generation.Value;
        Snapshot = publication.Snapshot;
    }

    internal ulong Generation { get; }
    internal OrbModding.Common.Runtime.World.GameWorldState Snapshot { get; }
}
#endif
