using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// The source shape, composed from the callbacks a feature supplies.
/// </summary>
/// <remarks>
/// Separate from <see cref="ComposedAutomataServiceDefinition{TState, TAction}"/> rather than
/// a flag on it: the two shapes take different callbacks and hand back different worker contracts,
/// and one class holding both would have to accept a capture it might not use.
/// </remarks>
internal sealed class ComposedAutomataSourceDefinition<TState, TAction> :
    IServiceCycleSourceDefinition<TState, TAction>
{
    private readonly AutomataSourceWorkerFactory<TState, TAction> _createWorker;
    private readonly AutomataStartPolicy _shouldStart;
    private readonly AutomataSourceCapture _capture;
    private readonly AutomataExecute<TAction> _execute;

    internal ComposedAutomataSourceDefinition(
        in AutomataServiceMetadata metadata,
        AutomataSourceWorkerFactory<TState, TAction> createWorker,
        AutomataStartPolicy shouldStart,
        AutomataSourceCapture capture,
        AutomataExecute<TAction> execute)
    {
        ServiceId = metadata.ServiceId;
        DefaultWakePolicy = metadata.DefaultWakePolicy;
        FaultRecoveryPolicy = metadata.FaultRecoveryPolicy;
        _createWorker = createWorker ?? throw new ArgumentNullException(nameof(createWorker));
        _shouldStart = shouldStart ?? throw new ArgumentNullException(nameof(shouldStart));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public ServiceId ServiceId { get; }
    public WakePolicy DefaultWakePolicy { get; }
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; }

    public IServiceCycleSourceWorkerDefinition<TState, TAction> CreateWorkerDefinition() =>
        _createWorker() ??
        throw new InvalidOperationException("The service did not create a worker definition.");

    public ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context) =>
        _shouldStart(in config, in context);

    public ServiceCaptureResult Capture(
        GameWorldCycleFrame frame,
        in SuiteRuntimeConfiguration config,
        in ServiceCaptureContext context) =>
        _capture(frame, in config, in context);

    public ServiceActionResult TryExecute(
        in TAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context) =>
        _execute(in action, in config, in context);
}
