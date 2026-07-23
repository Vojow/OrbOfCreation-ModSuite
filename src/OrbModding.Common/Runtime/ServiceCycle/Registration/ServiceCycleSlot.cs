using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

internal sealed partial class ServiceCycleSlot<TFrame, TConfig, TState, TAction> :
    IServiceCycleSlot
    where TConfig : notnull
{
    private readonly ServiceRunnerPosition<TFrame, TConfig, TState, TAction> _position0 = new(0);
    private readonly ServiceRunnerPosition<TFrame, TConfig, TState, TAction> _position1 = new(1);
    private readonly ServiceRunnerFactory<TFrame, TConfig, TState, TAction> _factory;
    private readonly ServiceFaultTracker _constructionFaults;
    private ServiceConfigurationPublisher<TConfig>? _configuration;
    private IServiceStrategyGenerationSource? _strategy;
    private LifecycleGeneration _desiredLifecycle;
    private ServiceLifecycleTerminalFact _latestTerminal;
    private ServiceLifecycleConstructionDeferralFact _latestConstructionDeferral;
    private ServiceFault _constructionFault;
    private MonotonicTimestamp _constructionRetryDue;
    private long _constructionAttemptCount;
    private long _constructionContentionTotal;
    private long _terminalSequence;
    private long _constructionDeferralSequence;
    private long _positionTransitionCount;
    private long _lifecycleSemanticVersion;
    private long _lastConstructionAttemptEpoch;
    private int _constructionContentionCount;
    private bool _hasConstructionRetry;

    internal ServiceCycleSlot(
        long registrationToken,
        int ordinal,
        IServiceCycleDefinition<TFrame, TConfig, TState, TAction> definition,
        ServiceId serviceId,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        ServiceConfigurationPublisher<TConfig> configuration,
        LifecycleGeneration lifecycle,
        IMonotonicClock clock,
        bool measureWorkerAllocations,
        IServiceCycleWorkerStarter? workerStarter,
        IServiceCycleWorkerExitObserver? workerExitObserver,
        ServiceResourceClaimLedger resourceClaims)
    {
        RegistrationToken = registrationToken;
        Ordinal = ordinal;
        ServiceId = serviceId;
        DefaultWakePolicy = defaultWakePolicy;
        FaultRecoveryPolicy = faultRecoveryPolicy;
        _configuration = configuration;
        _desiredLifecycle = lifecycle;
        _constructionFaults = new ServiceFaultTracker(faultRecoveryPolicy);
        _factory = new ServiceRunnerFactory<TFrame, TConfig, TState, TAction>(
            definition,
            configuration,
            serviceId,
            defaultWakePolicy,
            faultRecoveryPolicy,
            clock,
            measureWorkerAllocations,
            resourceClaims,
            workerStarter,
            workerExitObserver);
        _position0.InstallCurrent(_factory.CreateRequired(lifecycle));
        _positionTransitionCount = 1;
        _lifecycleSemanticVersion = 1;
    }

    public long RegistrationToken { get; }
    public int Ordinal { get; }
    public ServiceId ServiceId { get; }
    public bool IsDisposed { get; private set; }
    public long LifecyclePositionTransitionCount => _positionTransitionCount;
    public long LifecycleSemanticVersion => _lifecycleSemanticVersion;
    public ConfigGeneration LatestConfiguration => Configuration.ReadLatest().Generation;
    public StrategyGeneration LatestStrategy
    {
        get
        {
            return _strategy is not null && _strategy.TryGetLatestGeneration(out var generation)
                ? generation
                : default;
        }
    }
    internal WakePolicy DefaultWakePolicy { get; }
    internal ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; }
    internal ServiceConfigurationPublisher<TConfig> Configuration =>
        _configuration ?? throw new ObjectDisposedException(nameof(ServiceCycleSlot<TFrame, TConfig, TState, TAction>));
    internal ServiceRunner<TFrame, TConfig, TState, TAction> Runner =>
        CurrentRunner ?? throw new InvalidOperationException(
            "The service is paused while both physical runner positions retire or construction backs off.");

    public ServiceLifecycleSlotSnapshot LifecycleSnapshot => new(
        _desiredLifecycle,
        _position0.Snapshot,
        _position1.Snapshot,
        _latestTerminal,
        _latestConstructionDeferral,
        _constructionFault,
        _constructionRetryDue,
        _constructionAttemptCount,
        _constructionContentionTotal);

    private ServiceRunner<TFrame, TConfig, TState, TAction>? CurrentRunner =>
        _position0.State == ServiceRunnerPositionState.Current ? _position0.Runner :
        _position1.State == ServiceRunnerPositionState.Current ? _position1.Runner : null;

    public void BindStrategy(IServiceStrategyGenerationSource strategy)
    {
        if (strategy is null) throw new ArgumentNullException(nameof(strategy));
        if (_strategy is not null)
            throw new InvalidOperationException("A service can bind only one strategy generation source.");
        _strategy = strategy;
    }

}
