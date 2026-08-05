#if SERVICE_CYCLE_PROFILE
using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

[Flags]
internal enum GameMcpFrameData
{
    None = 0,
    World = 1 << 0,
    Configuration = 1 << 1,
    FeatureHealth = 1 << 2,
    ServiceHealth = 1 << 3,
    TraceWriterHealth = 1 << 4,
    WritableConfiguration = 1 << 5,
    Scene = 1 << 6,
    NativeContractHealth = 1 << 7,
}

/// <summary>
/// One request-batch view assembled on Unity's main thread after the ServiceCycle pump. It pins
/// existing immutable publications by reference and carries only owner-thread facts requested by
/// the claimed operations. It is never cached, periodically refreshed, or read by HTTP directly.
/// </summary>
internal sealed class GameMcpFrameContext
{
    internal GameMcpFrameContext(
        WorldPublication<GameWorldState>? world,
        AutomataRuntimeFrameFacts? runtime,
        ConfigurationPublication configuration,
        long lifecycleGeneration,
        string sceneName,
        bool nativeContractsAvailable,
        FeatureStatusSnapshot[] featureStatuses,
        DecisionJournalStatus traceWriterStatus,
        long traceWriterRevision,
        GameMcpWritableSettingDescriptor[] writableConfiguration,
        bool modalDismissAvailable = false,
        string modalDismissUnavailableReason = "the modal action boundary was not composed")
    {
        World = world;
        Runtime = runtime;
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        LifecycleGeneration = lifecycleGeneration;
        SceneName = sceneName ?? string.Empty;
        NativeContractsAvailable = nativeContractsAvailable;
        FeatureStatuses = featureStatuses ?? Array.Empty<FeatureStatusSnapshot>();
        TraceWriterStatus = traceWriterStatus;
        TraceWriterRevision = traceWriterRevision;
        WritableConfiguration = writableConfiguration ??
            Array.Empty<GameMcpWritableSettingDescriptor>();
        ModalDismissAvailable = modalDismissAvailable;
        ModalDismissUnavailableReason = modalDismissUnavailableReason ?? string.Empty;
    }

    internal WorldPublication<GameWorldState>? World { get; }
    internal AutomataRuntimeFrameFacts? Runtime { get; }
    internal ConfigurationPublication Configuration { get; }
    internal ConfigGeneration ConfigurationGeneration => Configuration.Generation;
    internal long LifecycleGeneration { get; }
    internal string SceneName { get; }
    internal bool NativeContractsAvailable { get; }
    internal FeatureStatusSnapshot[] FeatureStatuses { get; }
    internal DecisionJournalStatus TraceWriterStatus { get; }
    internal long TraceWriterRevision { get; }
    internal GameMcpWritableSettingDescriptor[] WritableConfiguration { get; }
    internal bool ModalDismissAvailable { get; }
    internal string ModalDismissUnavailableReason { get; }
    internal bool RuntimeAvailable => Runtime is not null;
    internal string RuntimeNotAvailableReason => Runtime is null
        ? "the ServiceCycle runtime has not published a world in this scene"
        : string.Empty;
}

internal sealed class GameMcpWritableSettingDescriptor
{
    internal GameMcpWritableSettingDescriptor(
        string section,
        string key,
        string settingType,
        string description,
        GameMcpConfigurationConstraint constraint)
    {
        Section = section ?? string.Empty;
        Key = key ?? string.Empty;
        SettingType = settingType ?? string.Empty;
        Description = description ?? string.Empty;
        Constraint = constraint ?? throw new ArgumentNullException(nameof(constraint));
    }

    internal string Section { get; }
    internal string Key { get; }
    internal string SettingType { get; }
    internal string Description { get; }
    internal GameMcpConfigurationConstraint Constraint { get; }
}
#endif
