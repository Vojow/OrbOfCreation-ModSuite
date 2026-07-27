using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

/// <summary>Translates lifecycle snapshots, construction recovery, and retained terminals.</summary>
internal sealed class ServiceCycleSemanticLifecycleEvents
{
    private readonly ServiceCycleSemanticRecorder _recorder;
    private readonly ServiceCycleSemanticExecutionEvents _execution;
    private readonly ServiceCycleSemanticTraceState _state;

    internal ServiceCycleSemanticLifecycleEvents(
        ServiceCycleSemanticRecorder recorder,
        ServiceCycleSemanticExecutionEvents execution,
        ServiceCycleSemanticTraceState state)
    {
        _recorder = recorder;
        _execution = execution;
        _state = state;
    }

    internal void Bind(
        int ordinal,
        LifecycleGeneration lifecycle,
        long lifecycleSemanticVersion,
        MonotonicTimestamp observedAt)
    {
        ref var cursor = ref _state.For(ordinal);
        if (lifecycle.Value != 0)
        {
            _recorder.LifecycleActivated(ordinal, lifecycle, observedAt);
            cursor.ActiveLifecycle = lifecycle;
        }
        cursor.LifecycleSemanticVersion = lifecycleSemanticVersion;
    }

    internal void Requested(
        int ordinal,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt) =>
        _recorder.LifecycleRequested(ordinal, lifecycle, observedAt);

    internal bool NeedsObservation(int ordinal, long lifecycleSemanticVersion) =>
        _state.For(ordinal).LifecycleSemanticVersion != lifecycleSemanticVersion;

    internal void Observe(
        int ordinal,
        in ServiceLifecycleSlotSnapshot snapshot,
        long lifecycleSemanticVersion,
        MonotonicTimestamp observedAt)
    {
        ref var cursor = ref _state.For(ordinal);
        var terminal = snapshot.LatestTerminal;
        if (terminal.IsPresent && terminal.Sequence > cursor.LifecycleTerminalSequence)
        {
            if (terminal.HasResponse)
            {
                var response = terminal.Response;
                var acquisition = new ServiceResponseAcquisition(in response, terminal.Receipt);
                _execution.EmitResponse(ordinal, in acquisition, emitTerminalReceipt: false);
            }
            if (terminal.HasReceipt)
            {
                var terminalReceipt = terminal.Receipt;
                var receiptAlreadyEmitted = ServiceCycleSemanticExecutionEvents.IsSameReceipt(
                    in terminalReceipt,
                    in cursor.TerminalReceipt);
                if (terminalReceipt.Disposition == BatchTerminalDisposition.Orphaned)
                {
                    _recorder.LifecycleRetired(ordinal, terminal.RetiredLifecycle, terminal.ObservedAt);
                    if (!receiptAlreadyEmitted)
                        _execution.EmitTerminalReceipt(ordinal, in terminalReceipt);
                }
                else
                {
                    if (!receiptAlreadyEmitted)
                    {
                        if (terminalReceipt.HasEmergencyStopContext)
                            _execution.EmergencyRejected(ordinal, in terminalReceipt);
                        else
                            _execution.EmitTerminalReceipt(ordinal, in terminalReceipt);
                    }
                    _recorder.LifecycleRetired(ordinal, terminal.RetiredLifecycle, terminal.ObservedAt);
                }
            }
            else
            {
                _recorder.LifecycleRetired(ordinal, terminal.RetiredLifecycle, terminal.ObservedAt);
                if (terminal.HasPublishedCycle && !terminal.HasResponse)
                {
                    var terminalCycle = terminal.Cycle;
                    _recorder.CycleOrphaned(ordinal, in terminalCycle, terminal.ObservedAt, default);
                }
            }
            _recorder.ClearRetainedEmergencyForService(ordinal);
            cursor.LifecycleTerminalSequence = terminal.Sequence;
        }

        var constructionDeferral = snapshot.LatestConstructionDeferral;
        if (constructionDeferral.IsPresent &&
            constructionDeferral.Sequence > cursor.LifecycleConstructionDeferralSequence)
        {
            _recorder.LifecycleConstructionDeferred(
                ordinal,
                constructionDeferral.Lifecycle,
                constructionDeferral.ObservedAt,
                constructionDeferral.RetryDue);
            cursor.LifecycleConstructionDeferralSequence = constructionDeferral.Sequence;
        }

        var active = CurrentLifecycle(in snapshot);
        if (active.Value != 0 && active != cursor.ActiveLifecycle)
        {
            _recorder.LifecycleActivated(ordinal, active, observedAt);
            cursor.ActiveLifecycle = active;
        }

        var constructionFault = snapshot.ConstructionFault;
        if (constructionFault.IsValid && !SameFault(in constructionFault, in cursor.ConstructionFault))
        {
            _recorder.FaultObserved(ordinal, snapshot.DesiredLifecycle, in constructionFault);
            _recorder.RetryScheduled(
                ordinal,
                snapshot.DesiredLifecycle,
                in constructionFault,
                snapshot.ConstructionRetryDue);
            cursor.ConstructionFault = constructionFault;
            cursor.ConstructionFaultLifecycle = snapshot.DesiredLifecycle;
        }
        else if (!constructionFault.IsValid && cursor.ConstructionFault.IsValid)
        {
            var recovered = cursor.ConstructionFault;
            _recorder.FaultRecovered(
                ordinal,
                cursor.ConstructionFaultLifecycle,
                in recovered,
                observedAt);
            cursor.ConstructionFault = default;
            cursor.ConstructionFaultLifecycle = default;
        }
        cursor.LifecycleSemanticVersion = lifecycleSemanticVersion;
    }

    private static LifecycleGeneration CurrentLifecycle(in ServiceLifecycleSlotSnapshot snapshot) =>
        snapshot.Position0.State == ServiceRunnerPositionState.Current ? snapshot.Position0.Lifecycle :
        snapshot.Position1.State == ServiceRunnerPositionState.Current ? snapshot.Position1.Lifecycle : default;

    private static bool SameFault(in ServiceFault left, in ServiceFault right) =>
        left.IsValid == right.IsValid &&
        (!left.IsValid || left.Category == right.Category && left.Code == right.Code &&
            left.OccurrenceCount == right.OccurrenceCount && left.ObservedAt == right.ObservedAt);
}
