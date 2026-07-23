using OrbModding.Common.Runtime.ServiceCycle.Replay.Observability;

namespace OrbAutomata;

internal interface IAutomataReplayCaptureObserver : IServiceCycleReplayExportObserver
{
    void Armed();
    void CloseRequested(AutomataReplayCloseReason reason);
    void CaptureDiscarded(AutomataReplayDiscardReason reason);
}

internal enum AutomataReplayCloseReason
{
    ActionAttempted = 0,
    LifecycleBoundary = 1,
    EventLimit = 2,
}

internal enum AutomataReplayDiscardReason
{
    IncompleteWindow = 0,
    InvalidatedWindow = 1,
    HeadroomExhausted = 2,
    ExporterUnavailable = 3,
    ExporterException = 4,
    DisposedBeforeExport = 5,
}
