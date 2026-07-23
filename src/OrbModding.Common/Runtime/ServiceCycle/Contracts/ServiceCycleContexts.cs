using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

public readonly struct ServiceCycleIdentity : IEquatable<ServiceCycleIdentity>
{
    public ServiceCycleIdentity(
        ServiceId service,
        LifecycleGeneration lifecycle,
        ConfigGeneration config,
        StrategyGeneration strategy,
        CaptureSequence capture,
        CycleId cycle)
    {
        if (!service.IsValid) throw new ArgumentException("A valid service identity is required.", nameof(service));
        if (lifecycle.Value == 0) throw new ArgumentException("A valid lifecycle generation is required.", nameof(lifecycle));
        if (!config.IsValid) throw new ArgumentException("A valid configuration generation is required.", nameof(config));
        if (strategy.Value == 0) throw new ArgumentException("A valid strategy generation is required.", nameof(strategy));
        if (!capture.IsValid) throw new ArgumentException("A valid capture sequence is required.", nameof(capture));
        if (!cycle.IsValid) throw new ArgumentException("A valid cycle identity is required.", nameof(cycle));
        Service = service;
        Lifecycle = lifecycle;
        Config = config;
        Strategy = strategy;
        Capture = capture;
        Cycle = cycle;
    }

    public ServiceId Service { get; }
    public LifecycleGeneration Lifecycle { get; }
    public ConfigGeneration Config { get; }
    public StrategyGeneration Strategy { get; }
    public CaptureSequence Capture { get; }
    public CycleId Cycle { get; }
    public bool IsValid =>
        Service.IsValid && Lifecycle.Value != 0 && Config.IsValid && Strategy.Value != 0 && Capture.IsValid && Cycle.IsValid;
    public bool Equals(ServiceCycleIdentity other) =>
        Service == other.Service &&
        Lifecycle == other.Lifecycle &&
        Config == other.Config &&
        Strategy == other.Strategy &&
        Capture == other.Capture &&
        Cycle == other.Cycle;
    public override bool Equals(object? obj) => obj is ServiceCycleIdentity other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Service, Lifecycle, Config, Strategy, Capture, Cycle);
    public static bool operator ==(ServiceCycleIdentity left, ServiceCycleIdentity right) => left.Equals(right);
    public static bool operator !=(ServiceCycleIdentity left, ServiceCycleIdentity right) => !left.Equals(right);
}

public readonly struct ServiceCycleStartContext
{
    public ServiceCycleStartContext(
        LifecycleGeneration lifecycle,
        ConfigGeneration latestConfig,
        BatchReceipt previousReceipt,
        MonotonicTimestamp now)
    {
        if (lifecycle.Value == 0) throw new ArgumentException("A valid lifecycle generation is required.", nameof(lifecycle));
        if (!latestConfig.IsValid) throw new ArgumentException("A valid configuration generation is required.", nameof(latestConfig));
        Lifecycle = lifecycle;
        LatestConfig = latestConfig;
        PreviousReceipt = previousReceipt;
        Now = now;
    }

    public LifecycleGeneration Lifecycle { get; }
    public ConfigGeneration LatestConfig { get; }
    public BatchReceipt PreviousReceipt { get; }
    public MonotonicTimestamp Now { get; }
}

public readonly struct ServiceCaptureContext
{
    public ServiceCaptureContext(
        ServiceId service,
        LifecycleGeneration lifecycle,
        ConfigGeneration config,
        CaptureSequence capture,
        CycleId cycle,
        MonotonicTimestamp capturedAt)
    {
        if (!service.IsValid) throw new ArgumentException("A valid service identity is required.", nameof(service));
        if (lifecycle.Value == 0) throw new ArgumentException("A valid lifecycle generation is required.", nameof(lifecycle));
        if (!config.IsValid) throw new ArgumentException("A valid configuration generation is required.", nameof(config));
        if (!capture.IsValid) throw new ArgumentException("A valid capture sequence is required.", nameof(capture));
        if (!cycle.IsValid) throw new ArgumentException("A valid cycle identity is required.", nameof(cycle));
        Service = service;
        Lifecycle = lifecycle;
        Config = config;
        Capture = capture;
        Cycle = cycle;
        CapturedAt = capturedAt;
#if SERVICE_CYCLE_PROFILE
        ProfileCoordinates = default;
#endif
    }

#if SERVICE_CYCLE_PROFILE
    internal ServiceCaptureContext(
        ServiceId service,
        LifecycleGeneration lifecycle,
        ConfigGeneration config,
        CaptureSequence capture,
        CycleId cycle,
        MonotonicTimestamp capturedAt,
        in ServiceCycleProfileCoordinates profileCoordinates)
        : this(service, lifecycle, config, capture, cycle, capturedAt)
    {
        ProfileCoordinates = profileCoordinates;
    }
#endif

    public ServiceId Service { get; }
    public LifecycleGeneration Lifecycle { get; }
    public ConfigGeneration Config { get; }
    public CaptureSequence Capture { get; }
    public CycleId Cycle { get; }
    public MonotonicTimestamp CapturedAt { get; }
#if SERVICE_CYCLE_PROFILE
    internal ServiceCycleProfileCoordinates ProfileCoordinates { get; }
#endif
}

public readonly struct ServiceCycleContext
{
    public ServiceCycleContext(ServiceCycleIdentity identity, BatchReceipt previousReceipt, MonotonicTimestamp decisionAt)
    {
        if (!identity.IsValid) throw new ArgumentException("A valid cycle identity is required.", nameof(identity));
        Identity = identity;
        PreviousReceipt = previousReceipt;
        DecisionAt = decisionAt;
    }

    public ServiceCycleIdentity Identity { get; }
    public BatchReceipt PreviousReceipt { get; }
    public MonotonicTimestamp DecisionAt { get; }
}

public readonly struct ServiceActionContext
{
    public ServiceActionContext(
        ServiceCycleIdentity cycle,
        BatchId batch,
        ActionId action,
        int actionIndex,
        MonotonicTimestamp attemptedAt)
    {
        if (!cycle.IsValid) throw new ArgumentException("A valid cycle identity is required.", nameof(cycle));
        if (!batch.IsValid) throw new ArgumentException("A valid batch identity is required.", nameof(batch));
        if (!action.IsValid) throw new ArgumentException("A valid action identity is required.", nameof(action));
        if (actionIndex < 0) throw new ArgumentOutOfRangeException(nameof(actionIndex));
        Cycle = cycle;
        Batch = batch;
        Action = action;
        ActionIndex = actionIndex;
        AttemptedAt = attemptedAt;
#if SERVICE_CYCLE_PROFILE
        ProfileCoordinates = default;
#endif
    }

#if SERVICE_CYCLE_PROFILE
    internal ServiceActionContext(
        ServiceCycleIdentity cycle,
        BatchId batch,
        ActionId action,
        int actionIndex,
        MonotonicTimestamp attemptedAt,
        in ServiceCycleProfileCoordinates profileCoordinates)
        : this(cycle, batch, action, actionIndex, attemptedAt)
    {
        ProfileCoordinates = profileCoordinates;
    }
#endif

    public ServiceCycleIdentity Cycle { get; }
    public BatchId Batch { get; }
    public ActionId Action { get; }
    public int ActionIndex { get; }
    public MonotonicTimestamp AttemptedAt { get; }
#if SERVICE_CYCLE_PROFILE
    internal ServiceCycleProfileCoordinates ProfileCoordinates { get; }
#endif
}
