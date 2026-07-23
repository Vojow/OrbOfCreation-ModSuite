using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using RuntimeLifecycleGeneration = OrbModding.Common.Runtime.LifecycleGeneration;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

/// <summary>
/// Complete feature-owned service boundary. Every operation stays paired through all four generic types;
/// the Common registry erases only the complete runner slot, never individual values.
/// </summary>
public interface IServiceCycleDefinition<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    ServiceId ServiceId { get; }
    WakePolicy DefaultWakePolicy { get; }
    ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; }

    /// <summary>
    /// Creates the frame resource under the runtime's serialized reference-factory admission.
    /// The callback must finish in finite time and must not synchronously depend on another
    /// reference factory succeeding. A returned reference remains valid through the runtime's
    /// immediate ownership finalization.
    /// </summary>
    TFrame CreateFrame();

    /// <summary>
    /// Creates the worker resource under the runtime's serialized reference-factory admission.
    /// The callback must finish in finite time and must not synchronously depend on another
    /// reference factory succeeding. The returned reference remains valid through the runtime's
    /// immediate ownership finalization.
    /// </summary>
    IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction> CreateWorkerDefinition();

    ServiceStartDecision ShouldStart(
        in TConfig config,
        in ServiceCycleStartContext context);
    ServiceCaptureResult Capture(
        ref TFrame frame,
        in TConfig config,
        in ServiceCaptureContext context);
    ServiceActionResult TryExecute(
        in TAction action,
        in TConfig config,
        in ServiceActionContext context);
}

/// <summary>
/// Worker-only feature port. It is a distinct object from the main-thread definition so a worker
/// cannot retain or invoke native capture/action adapters through its service dependency.
/// </summary>
public interface IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    /// <summary>
    /// Creates worker state under the runtime's serialized reference-factory admission.
    /// The callback must finish in finite time and must not synchronously depend on another
    /// reference factory succeeding. A returned reference remains valid through the runtime's
    /// immediate ownership finalization.
    /// </summary>
    TState CreateState(RuntimeLifecycleGeneration lifecycle);
    void ReleaseState(ref TState state);
    void ReleaseFrame(ref TFrame frame);

    WakePolicy Evaluate(
        in TFrame frame,
        in TConfig config,
        in ServiceCycleContext context,
        ref TState state,
        ServiceActionWriter<TAction> actions);
    void ProjectState(
        in TState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output);
}
