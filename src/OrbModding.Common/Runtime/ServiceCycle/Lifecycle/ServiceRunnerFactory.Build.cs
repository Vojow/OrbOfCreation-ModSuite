using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

internal sealed partial class ServiceRunnerFactory<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    private static ServiceRunnerParts<TFrame, TConfig, TState, TAction> Build(
        IServiceCycleDefinition<TFrame, TConfig, TState, TAction> definition,
        ServiceConfigurationPublisher<TConfig> configuration,
        LifecycleGeneration lifecycle,
        ServiceId serviceId,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        IMonotonicClock clock,
        bool measureWorkerAllocations,
        ServiceCycleHandoff<TConfig>? handoff,
        IServiceCycleWorkerStarter? workerStarter,
        ServiceRunnerLifetime? lifetime,
        ServiceRunnerPreparedResources<TFrame, TConfig, TState, TAction> prepared,
        IServiceCycleWorkerExitObserver? workerExitObserver)
    {
        var workerDefinition = prepared.WorkerDefinition;
        var frameValue = prepared.Frame;
        var claims = prepared.Claims;
        var workerDefinitionClaim = prepared.WorkerDefinitionClaim;
        var frameClaim = prepared.FrameClaim;
        ServiceCycleHandoff<TConfig>? actualHandoff = null;
        try
        {
            var actualLifetime = lifetime ?? new ServiceRunnerLifetime();
            var main = new ServiceCycleMainState<TConfig>
            {
                LatestConfigGeneration = configuration.ReadLatest().Generation,
            };
            var frame = new ServiceFrameStorage<TFrame>(frameValue, lifecycle);
            var actions = new ReusableActionStore<TAction>(lifecycle: lifecycle);
            actualHandoff =
                handoff ?? new ServiceCycleHandoff<TConfig>(lifecycle);
            actualHandoff.BindLifecycle(lifecycle);
            var starts =
                new ServiceCycleStartCoordinator<TFrame, TConfig, TState, TAction>(
                    definition,
                    configuration,
                    frame,
                    actualHandoff,
                    main,
                    serviceId,
                    lifecycle,
                    faultRecoveryPolicy,
                    clock,
                    actualLifetime);
            var worker =
                new ServiceCycleWorker<TFrame, TConfig, TState, TAction>(
                    workerDefinition,
                    serviceId,
                    frame,
                    actions,
                    actualHandoff,
                    clock,
                    defaultWakePolicy,
                    faultRecoveryPolicy,
                    measureWorkerAllocations,
                    lifecycle,
                    claims,
                    workerDefinitionClaim,
                    frameClaim,
                    workerExitObserver);
            var batchRuntime =
                new ServiceBatchRuntime<TFrame, TConfig, TState, TAction>(
                    definition,
                    configuration,
                    actions,
                    actualHandoff,
                    main,
                    starts,
                    faultRecoveryPolicy,
                    clock,
                    lifecycle,
                    actualLifetime);
            var batchCompletion =
                new ServiceBatchCompletion<TFrame, TConfig, TState, TAction>(
                    batchRuntime);
            var responses =
                new ServiceBatchResponseHandler<TFrame, TConfig, TState, TAction>(
                    batchRuntime,
                    batchCompletion);
            var actionExecutor =
                new ServiceBatchActionExecutor<TFrame, TConfig, TState, TAction>(
                    batchRuntime,
                    batchCompletion);
            var diagnostics =
                new ServiceRunnerDiagnosticsAssembler<TFrame, TConfig, TState, TAction>(
                    configuration,
                    actions,
                    actualHandoff,
                    worker,
                    main,
                    starts);
            var resourceIdentity = new ServiceRunnerResourceIdentity(
                workerDefinition,
                typeof(TFrame).IsValueType ? null : (object?)frameValue,
                actions,
                actualHandoff,
                worker,
                main,
                starts,
                batchCompletion);
            worker.Start(workerStarter);
            return new ServiceRunnerParts<TFrame, TConfig, TState, TAction>(
                actions,
                actualHandoff,
                worker,
                main,
                starts,
                responses,
                actionExecutor,
                batchCompletion,
                diagnostics,
                actualLifetime,
                resourceIdentity);
        }
        catch (Exception primary)
        {
            try
            {
                actualHandoff?.DisposeNeverStarted();
            }
            catch
            {
            }
            Exception? cleanupFailure = null;
            try
            {
                workerDefinition.ReleaseFrame(ref frameValue);
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
            claims.Release(frameClaim);
            claims.Release(workerDefinitionClaim);
            if (!ServiceCycleFatalExceptionPolicy.MustEscape(
                    workerDefinition,
                    primary) &&
                cleanupFailure is not null &&
                ServiceCycleFatalExceptionPolicy.MustEscape(
                    workerDefinition,
                    cleanupFailure))
                throw cleanupFailure;
            throw;
        }
    }

    private static void ValidateRegistration(
        IServiceCycleDefinition<TFrame, TConfig, TState, TAction> definition,
        ServiceConfigurationPublisher<TConfig> configuration,
        LifecycleGeneration lifecycle,
        ServiceId serviceId,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        IMonotonicClock clock)
    {
        if (definition is null)
            throw new ArgumentNullException(nameof(definition));
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));
        if (!serviceId.IsValid)
            throw new ArgumentException(
                "A valid service identity is required.",
                nameof(serviceId));
        if (lifecycle.Value == 0)
            throw new ArgumentException(
                "A valid lifecycle generation is required.",
                nameof(lifecycle));
        if (!defaultWakePolicy.IsValid ||
            defaultWakePolicy.Kind == WakePolicyKind.Default)
        {
            throw new ArgumentException(
                "Registration requires a concrete default wake policy.",
                nameof(defaultWakePolicy));
        }
        if (!faultRecoveryPolicy.IsValid)
            throw new ArgumentException(
                "Registration requires a valid fault-recovery policy.",
                nameof(faultRecoveryPolicy));
        if (clock is null) throw new ArgumentNullException(nameof(clock));
    }
}
