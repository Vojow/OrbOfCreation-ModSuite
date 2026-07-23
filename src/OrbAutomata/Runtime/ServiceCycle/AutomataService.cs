using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbAutomata;

internal delegate TFrame AutomataFrameFactory<TFrame>();

internal delegate IServiceCycleWorkerDefinition<
    TFrame,
    AutomataConfiguration,
    TState,
    TAction> AutomataWorkerFactory<TFrame, TState, TAction>();

internal delegate ServiceStartDecision AutomataStartPolicy(
    in AutomataConfiguration configuration,
    in ServiceCycleStartContext context);

internal delegate ServiceCaptureResult AutomataCapture<TFrame>(
    ref TFrame frame,
    in AutomataConfiguration configuration,
    in ServiceCaptureContext context);

internal delegate ServiceActionResult AutomataExecute<TAction>(
    in TAction action,
    in AutomataConfiguration configuration,
    in ServiceActionContext context);

internal readonly struct AutomataServiceMetadata
{
    internal AutomataServiceMetadata(
        ServiceId serviceId,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy)
    {
        if (!serviceId.IsValid)
            throw new ArgumentException("A valid service identity is required.", nameof(serviceId));
        if (!defaultWakePolicy.IsValid || defaultWakePolicy.Kind == WakePolicyKind.Default)
            throw new ArgumentException("A concrete default wake policy is required.", nameof(defaultWakePolicy));
        if (!faultRecoveryPolicy.IsValid)
            throw new ArgumentException("A valid fault recovery policy is required.", nameof(faultRecoveryPolicy));

        ServiceId = serviceId;
        DefaultWakePolicy = defaultWakePolicy;
        FaultRecoveryPolicy = faultRecoveryPolicy;
    }

    internal ServiceId ServiceId { get; }
    internal WakePolicy DefaultWakePolicy { get; }
    internal ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; }
}

internal static class AutomataService
{
    internal static IAutomataServiceDefinition<TFrame, TState, TAction> Define<
        TFrame,
        TState,
        TAction>(
        in AutomataServiceMetadata metadata,
        AutomataFrameFactory<TFrame> createFrame,
        AutomataWorkerFactory<TFrame, TState, TAction> createWorker,
        AutomataStartPolicy shouldStart,
        AutomataCapture<TFrame> capture,
        AutomataExecute<TAction> execute) =>
        new ComposedAutomataServiceDefinition<TFrame, TState, TAction>(
            in metadata,
            createFrame,
            createWorker,
            shouldStart,
            capture,
            execute);
}
