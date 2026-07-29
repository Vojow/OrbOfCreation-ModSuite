using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

/// <summary>
/// Builds a service's runner, and rebuilds it whenever its lifecycle generation changes.
/// </summary>
/// <remarks>
/// Abstract because the two service shapes differ in what they build, not in how they build it: each
/// mints its own worker definition, its own start coordinator, and its own worker, and a source pair
/// additionally shares a capture buffer that an ordinary pair has no use for. Everything around that
/// — the claim admission, the handoff, the action store, the batch machinery, the failure unwind — is
/// one implementation here, so neither shape can drift from the other by accident.
/// </remarks>
internal abstract partial class ServiceRunnerFactory<TState, TAction>
{
    private readonly IServiceCycleMainThreadDefinition<TAction> _definition;
    private readonly ServiceConfigurationPublisher _configuration;
    private readonly ServiceId _serviceId;
    private readonly WakePolicy _defaultWakePolicy;
    private readonly ServiceFaultRecoveryPolicy _faultRecoveryPolicy;
    private readonly IMonotonicClock _clock;
    private readonly bool _measureWorkerAllocations;
    private readonly ServiceResourceClaimLedger _resourceClaims;
    private readonly IServiceCycleWorkerStarter? _workerStarter;
    private readonly IServiceCycleWorkerExitObserver? _workerExitObserver;
    private readonly ServiceStrategyPublisher _strategy;
    private readonly ServiceWorldPublisher<GameWorldState> _world;

    private protected ServiceRunnerFactory(
        IServiceCycleMainThreadDefinition<TAction> definition,
        ServiceConfigurationPublisher configuration,
        ServiceId serviceId,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        IMonotonicClock clock,
        bool measureWorkerAllocations,
        ServiceResourceClaimLedger resourceClaims,
        IServiceCycleWorkerStarter? workerStarter,
        IServiceCycleWorkerExitObserver? workerExitObserver,
        ServiceStrategyPublisher? strategy = null,
        ServiceWorldPublisher<GameWorldState>? world = null)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _serviceId = serviceId;
        _defaultWakePolicy = defaultWakePolicy;
        _faultRecoveryPolicy = faultRecoveryPolicy;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _measureWorkerAllocations = measureWorkerAllocations;
        _resourceClaims = resourceClaims ?? throw new ArgumentNullException(nameof(resourceClaims));
        _workerStarter = workerStarter;
        _workerExitObserver = workerExitObserver;
        _strategy = strategy ?? SharedNeutralStrategy;
        _world = world ?? SharedEmptyWorld;
    }

    private protected ServiceRunnerFactory(
        IServiceCycleMainThreadDefinition<TAction> definition,
        in ServiceRunnerFactoryParts parts)
        : this(
            definition,
            parts.Configuration,
            parts.ServiceId,
            parts.DefaultWakePolicy,
            parts.FaultRecoveryPolicy,
            parts.Clock,
            parts.MeasureWorkerAllocations,
            parts.ResourceClaims,
            parts.WorkerStarter,
            parts.WorkerExitObserver,
            parts.Strategy,
            parts.World)
    {
    }

    /// <summary>
    /// The world a runner built outside a registry reads. Tests construct runners directly and never
    /// publish into it, so it stays the empty snapshot — which is what a service that starts before
    /// any collection sees in production too.
    /// </summary>
    private static readonly ServiceWorldPublisher<GameWorldState> SharedEmptyWorld =
        new(GameWorldStateDefaults.Empty);

    /// <summary>
    /// The strategy a runner built outside a registry reads: the neutral bulletin, which is what a
    /// production suite reads too until a strategist publishes something else.
    /// </summary>
    private static readonly ServiceStrategyPublisher SharedNeutralStrategy =
        new(SuiteStrategyDefaults.Neutral);

    private protected IServiceCycleMainThreadDefinition<TAction> Definition => _definition;
    private protected ServiceConfigurationPublisher Configuration => _configuration;
    private protected ServiceId ServiceIdentity => _serviceId;
    private protected WakePolicy DefaultWake => _defaultWakePolicy;
    private protected ServiceFaultRecoveryPolicy FaultRecoveryPolicy => _faultRecoveryPolicy;
    private protected IMonotonicClock Clock => _clock;
    private protected bool MeasureWorkerAllocations => _measureWorkerAllocations;
    private protected IServiceCycleWorkerExitObserver? WorkerExitObserver => _workerExitObserver;
    private protected ServiceStrategyPublisher Strategy => _strategy;
    private protected ServiceWorldPublisher<GameWorldState> World => _world;

    internal ServiceRunner<TState, TAction> CreateRequired(
        LifecycleGeneration lifecycle) =>
        TryCreate(lifecycle).Runner ?? throw new ServiceRunnerResourceContentionException("resource");

    internal ServiceRunnerConstructionResult<TState, TAction> TryCreate(
        LifecycleGeneration lifecycle) => TryCreate(lifecycle, handoff: null, lifetime: null);

    private ServiceRunnerConstructionResult<TState, TAction> TryCreate(
        LifecycleGeneration lifecycle,
        ServiceCycleHandoff? handoff,
        ServiceRunnerLifetime? lifetime)
    {
        ValidateRegistration(
            _definition,
            _configuration,
            lifecycle,
            _serviceId,
            _defaultWakePolicy,
            _faultRecoveryPolicy,
            _clock);
        var preparation = TryPrepare(_resourceClaims, out var prepared);
        if (preparation == ServiceResourceClaimResult.Contended)
            return new ServiceRunnerConstructionResult<TState, TAction>(null, true);
        if (preparation != ServiceResourceClaimResult.Claimed)
            throw new InvalidOperationException("The live service resource claim ledger is at capacity.");

        var parts = Build(lifecycle, handoff, lifetime, prepared!);
        var runner = new ServiceRunner<TState, TAction>(
            _configuration,
            lifecycle,
            _serviceId,
            _defaultWakePolicy,
            _faultRecoveryPolicy,
            in parts);
        return new ServiceRunnerConstructionResult<TState, TAction>(runner, false);
    }

    internal static ServiceRunnerConstructionResult<TState, TAction> TryCreate(
        IServiceCycleDefinition<TState, TAction> definition,
        ServiceConfigurationPublisher configuration,
        LifecycleGeneration lifecycle,
        ServiceId serviceId,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        IMonotonicClock clock,
        bool measureWorkerAllocations,
        ServiceCycleHandoff? handoff = null,
        IServiceCycleWorkerStarter? workerStarter = null,
        ServiceRunnerLifetime? lifetime = null,
        ServiceResourceClaimLedger? resourceClaims = null,
        IServiceCycleWorkerExitObserver? workerExitObserver = null,
        ServiceStrategyPublisher? strategy = null,
        ServiceWorldPublisher<GameWorldState>? world = null) =>
        new ServiceOrdinaryRunnerFactory<TState, TAction>(
            definition,
            configuration,
            serviceId,
            defaultWakePolicy,
            faultRecoveryPolicy,
            clock,
            measureWorkerAllocations,
            resourceClaims ?? new ServiceResourceClaimLedger(1),
            workerStarter,
            workerExitObserver,
            strategy,
            world).TryCreate(lifecycle, handoff, lifetime);

    internal static ServiceRunner<TState, TAction> CreateRequired(
        IServiceCycleDefinition<TState, TAction> definition,
        ServiceConfigurationPublisher configuration,
        LifecycleGeneration lifecycle,
        ServiceId serviceId,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        IMonotonicClock clock,
        bool measureWorkerAllocations,
        ServiceCycleHandoff? handoff = null,
        IServiceCycleWorkerStarter? workerStarter = null,
        ServiceRunnerLifetime? lifetime = null,
        ServiceResourceClaimLedger? resourceClaims = null,
        IServiceCycleWorkerExitObserver? workerExitObserver = null,
        ServiceStrategyPublisher? strategy = null,
        ServiceWorldPublisher<GameWorldState>? world = null)
    {
        var result = TryCreate(
            definition,
            configuration,
            lifecycle,
            serviceId,
            defaultWakePolicy,
            faultRecoveryPolicy,
            clock,
            measureWorkerAllocations,
            handoff,
            workerStarter,
            lifetime,
            resourceClaims,
            workerExitObserver,
            strategy,
            world);
        return result.Runner ?? throw new ServiceRunnerResourceContentionException("resource");
    }
}
