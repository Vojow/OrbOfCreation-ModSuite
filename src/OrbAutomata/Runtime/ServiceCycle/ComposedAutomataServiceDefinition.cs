using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbAutomata;

internal sealed class ComposedAutomataServiceDefinition<TFrame, TState, TAction> :
    IAutomataServiceDefinition<TFrame, TState, TAction>
{
    private readonly AutomataFrameFactory<TFrame> _createFrame;
    private readonly AutomataWorkerFactory<TFrame, TState, TAction> _createWorker;
    private readonly AutomataStartPolicy _shouldStart;
    private readonly AutomataCapture<TFrame> _capture;
    private readonly AutomataExecute<TAction> _execute;

    internal ComposedAutomataServiceDefinition(
        in AutomataServiceMetadata metadata,
        AutomataFrameFactory<TFrame> createFrame,
        AutomataWorkerFactory<TFrame, TState, TAction> createWorker,
        AutomataStartPolicy shouldStart,
        AutomataCapture<TFrame> capture,
        AutomataExecute<TAction> execute)
    {
        ServiceId = metadata.ServiceId;
        DefaultWakePolicy = metadata.DefaultWakePolicy;
        FaultRecoveryPolicy = metadata.FaultRecoveryPolicy;
        _createFrame = createFrame ?? throw new ArgumentNullException(nameof(createFrame));
        _createWorker = createWorker ?? throw new ArgumentNullException(nameof(createWorker));
        _shouldStart = shouldStart ?? throw new ArgumentNullException(nameof(shouldStart));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public ServiceId ServiceId { get; }
    public WakePolicy DefaultWakePolicy { get; }
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; }

    public TFrame CreateFrame() => _createFrame();

    public IServiceCycleWorkerDefinition<
        TFrame,
        AutomataConfiguration,
        TState,
        TAction> CreateWorkerDefinition() =>
        _createWorker() ??
        throw new InvalidOperationException("The service did not create a worker definition.");

    public ServiceStartDecision ShouldStart(
        in AutomataConfiguration config,
        in ServiceCycleStartContext context) =>
        _shouldStart(in config, in context);

    public ServiceCaptureResult Capture(
        ref TFrame frame,
        in AutomataConfiguration config,
        in ServiceCaptureContext context) =>
        _capture(ref frame, in config, in context);

    public ServiceActionResult TryExecute(
        in TAction action,
        in AutomataConfiguration config,
        in ServiceActionContext context) =>
        _execute(in action, in config, in context);
}
