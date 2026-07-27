using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

internal sealed partial class ServiceCycleSlot<TState, TAction> :
    IServiceCycleSlot
{
    private readonly ServiceRunnerPosition<TState, TAction> _position0 = new(0);
    private readonly ServiceRunnerPosition<TState, TAction> _position1 = new(1);
    private readonly ServiceRunnerFactory<TState, TAction> _factory;
    private readonly ServiceFaultTracker _constructionFaults;
    private readonly ServiceStrategyPublisher _strategy;
    private readonly IServiceWorldGenerationSource _world;
    private ServiceConfigurationPublisher? _configuration;
    private long _worldGateFloor;
    private LifecycleGeneration _desiredLifecycle;
    private ServiceLifecycleTerminalFact _latestTerminal;
    private ServiceLifecycleConstructionDeferralFact _latestConstructionDeferral;
    private ServiceWorldGateDeferralFact _latestWorldGateDeferral;
    private ServiceFault _constructionFault;
    private MonotonicTimestamp _constructionRetryDue;
    private long _constructionAttemptCount;
    private long _constructionContentionTotal;
    private long _terminalSequence;
    private long _constructionDeferralSequence;
    private long _worldGateDeferralSequence;
    private long _positionTransitionCount;
    private long _lifecycleSemanticVersion;
    private long _lastConstructionAttemptEpoch;
    private int _constructionContentionCount;
    private bool _hasConstructionRetry;

    /// <summary>
    /// One slot for both service shapes; only the factory it is handed differs.
    /// </summary>
    /// <remarks>
    /// Everything a slot does — ordinals, lifecycle positions, construction backoff, the world
    /// freshness gate, dispatch — is the same work whichever shape the service is. Duplicating the
    /// family to carry one difference in how a runner is built would mean maintaining both.
    /// </remarks>
    internal ServiceCycleSlot(
        long registrationToken,
        int ordinal,
        ServiceRunnerFactory<TState, TAction> factory,
        ServiceId serviceId,
        ServiceActionDispatchPolicy actionDispatchPolicy,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        ServiceConfigurationPublisher configuration,
        LifecycleGeneration lifecycle,
        ServiceStrategyPublisher strategy,
        ServiceWorldPublisher<GameWorldState> world)
    {
        RegistrationToken = registrationToken;
        Ordinal = ordinal;
        ServiceId = serviceId;
        ActionDispatchPolicy = actionDispatchPolicy;
        DefaultWakePolicy = defaultWakePolicy;
        FaultRecoveryPolicy = faultRecoveryPolicy;
        _configuration = configuration;
        _strategy = strategy;
        _world = world;
        _desiredLifecycle = lifecycle;
        _constructionFaults = new ServiceFaultTracker(faultRecoveryPolicy);
        _factory = factory;
        _position0.InstallCurrent(_factory.CreateRequired(lifecycle));
        ArmWorldGate();
        _positionTransitionCount = 1;
        _lifecycleSemanticVersion = 1;
    }

    public long RegistrationToken { get; }
    public int Ordinal { get; }
    public ServiceId ServiceId { get; }
    public ServiceActionDispatchPolicy ActionDispatchPolicy { get; }
    public bool IsDisposed { get; private set; }
    public long LifecyclePositionTransitionCount => _positionTransitionCount;
    public long LifecycleSemanticVersion => _lifecycleSemanticVersion;
    public ServiceWorldGateDeferralFact LatestWorldGateDeferral => _latestWorldGateDeferral;
    public ConfigGeneration LatestConfiguration => Configuration.ReadLatest().Generation;
    public StrategyGeneration LatestStrategy => _strategy.ReadLatest().Generation;
    internal WakePolicy DefaultWakePolicy { get; }
    internal ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; }
    internal ServiceConfigurationPublisher Configuration =>
        _configuration ?? throw new ObjectDisposedException(nameof(ServiceCycleSlot<TState, TAction>));
    internal ServiceRunner<TState, TAction> Runner =>
        CurrentRunner ?? throw new InvalidOperationException(
            "The service is paused while both physical runner positions retire or construction backs off.");

    public ServiceLifecycleSlotSnapshot LifecycleSnapshot => new(
        _desiredLifecycle,
        _position0.Snapshot,
        _position1.Snapshot,
        _latestTerminal,
        _latestConstructionDeferral,
        _latestWorldGateDeferral,
        _constructionFault,
        _constructionRetryDue,
        _constructionAttemptCount,
        _constructionContentionTotal);

    private ServiceRunner<TState, TAction>? CurrentRunner =>
        _position0.State == ServiceRunnerPositionState.Current ? _position0.Runner :
        _position1.State == ServiceRunnerPositionState.Current ? _position1.Runner : null;

}
