using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

internal sealed partial class ServiceRunnerFactory<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    private readonly IServiceCycleDefinition<TFrame, TConfig, TState, TAction> _definition;
    private readonly ServiceConfigurationPublisher<TConfig> _configuration;
    private readonly ServiceId _serviceId;
    private readonly WakePolicy _defaultWakePolicy;
    private readonly ServiceFaultRecoveryPolicy _faultRecoveryPolicy;
    private readonly IMonotonicClock _clock;
    private readonly bool _measureWorkerAllocations;
    private readonly ServiceResourceClaimLedger _resourceClaims;
    private readonly IServiceCycleWorkerStarter? _workerStarter;
    private readonly IServiceCycleWorkerExitObserver? _workerExitObserver;

    internal ServiceRunnerFactory(
        IServiceCycleDefinition<TFrame, TConfig, TState, TAction> definition,
        ServiceConfigurationPublisher<TConfig> configuration,
        ServiceId serviceId,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        IMonotonicClock clock,
        bool measureWorkerAllocations,
        ServiceResourceClaimLedger resourceClaims,
        IServiceCycleWorkerStarter? workerStarter,
        IServiceCycleWorkerExitObserver? workerExitObserver)
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
    }

    internal ServiceRunner<TFrame, TConfig, TState, TAction> CreateRequired(
        LifecycleGeneration lifecycle) => CreateRequired(
            _definition,
            _configuration,
            lifecycle,
            _serviceId,
            _defaultWakePolicy,
            _faultRecoveryPolicy,
            _clock,
            _measureWorkerAllocations,
            workerStarter: _workerStarter,
            resourceClaims: _resourceClaims,
            workerExitObserver: _workerExitObserver);

    internal ServiceRunnerConstructionResult<TFrame, TConfig, TState, TAction> TryCreate(
        LifecycleGeneration lifecycle) => TryCreate(
            _definition,
            _configuration,
            lifecycle,
            _serviceId,
            _defaultWakePolicy,
            _faultRecoveryPolicy,
            _clock,
            _measureWorkerAllocations,
            workerStarter: _workerStarter,
            resourceClaims: _resourceClaims,
            workerExitObserver: _workerExitObserver);

    internal bool MustEscape(Exception exception) =>
        ServiceCycleFatalExceptionPolicy.MustEscape(_definition, exception);

    internal static ServiceRunnerConstructionResult<TFrame, TConfig, TState, TAction> TryCreate(
        IServiceCycleDefinition<TFrame, TConfig, TState, TAction> definition,
        ServiceConfigurationPublisher<TConfig> configuration,
        LifecycleGeneration lifecycle,
        ServiceId serviceId,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        IMonotonicClock clock,
        bool measureWorkerAllocations,
        ServiceCycleHandoff<TConfig>? handoff = null,
        IServiceCycleWorkerStarter? workerStarter = null,
        ServiceRunnerLifetime? lifetime = null,
        ServiceResourceClaimLedger? resourceClaims = null,
        IServiceCycleWorkerExitObserver? workerExitObserver = null)
    {
        ValidateRegistration(
            definition,
            configuration,
            lifecycle,
            serviceId,
            defaultWakePolicy,
            faultRecoveryPolicy,
            clock);
        var claims = resourceClaims ?? new ServiceResourceClaimLedger(1);
        var preparation = ServiceRunnerResourcePreparer<TFrame, TConfig, TState, TAction>.TryPrepare(
            definition,
            claims,
            out var prepared);
        if (preparation == ServiceResourceClaimResult.Contended)
            return new ServiceRunnerConstructionResult<TFrame, TConfig, TState, TAction>(null, true);
        if (preparation != ServiceResourceClaimResult.Claimed)
            throw new InvalidOperationException("The live service resource claim ledger is at capacity.");

        var parts = Build(
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
            prepared!,
            workerExitObserver);
        var runner = new ServiceRunner<TFrame, TConfig, TState, TAction>(
            configuration,
            lifecycle,
            serviceId,
            defaultWakePolicy,
            faultRecoveryPolicy,
            in parts);
        return new ServiceRunnerConstructionResult<TFrame, TConfig, TState, TAction>(runner, false);
    }

    internal static ServiceRunner<TFrame, TConfig, TState, TAction> CreateRequired(
        IServiceCycleDefinition<TFrame, TConfig, TState, TAction> definition,
        ServiceConfigurationPublisher<TConfig> configuration,
        LifecycleGeneration lifecycle,
        ServiceId serviceId,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        IMonotonicClock clock,
        bool measureWorkerAllocations,
        ServiceCycleHandoff<TConfig>? handoff = null,
        IServiceCycleWorkerStarter? workerStarter = null,
        ServiceRunnerLifetime? lifetime = null,
        ServiceResourceClaimLedger? resourceClaims = null,
        IServiceCycleWorkerExitObserver? workerExitObserver = null)
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
            workerExitObserver);
        return result.Runner ?? throw new ServiceRunnerResourceContentionException("resource");
    }

}
