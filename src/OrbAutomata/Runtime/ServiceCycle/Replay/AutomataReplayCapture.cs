using System;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbAutomata;

internal sealed class AutomataReplayCapture : IDisposable
{
    private readonly ServiceCycleReplaySession _recording;
    private readonly IAutomataReplayExportPort _exporter;
    private readonly IAutomataReplayWindow _window;
    private readonly IAutomataReplayCaptureObserver _observer;
    private readonly int _captureLimit;
    private readonly int _failureLimit;
    private CaptureState _state;
    private bool _disposed;

    internal AutomataReplayCapture(
        ServiceCycleReplaySession recording,
        IAutomataReplayExportPort exporter,
        IAutomataReplayWindow window,
        IAutomataReplayCaptureObserver observer,
        int captureLimit,
        int failureLimit)
    {
        _recording = recording ?? throw new ArgumentNullException(nameof(recording));
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        if (captureLimit <= 0 || captureLimit >= window.EventCapacity)
            throw new ArgumentOutOfRangeException(nameof(captureLimit));
        if (failureLimit <= captureLimit || failureLimit >= window.EventCapacity)
            throw new ArgumentOutOfRangeException(nameof(failureLimit));
        _captureLimit = captureLimit;
        _failureLimit = failureLimit;
        NotifyArmed();
    }

    public void ObserveFrame(in SuiteFramePumpReport report)
    {
        if (_disposed || _state == CaptureState.Terminal) return;
        if (_state == CaptureState.ExportPending)
        {
            TryRequest();
            return;
        }
        if (_state == CaptureState.Closing)
        {
            TryFreeze();
            if (_state == CaptureState.ExportPending) TryRequest();
            return;
        }
        if (!_window.IsComplete)
        {
            FailCapture(AutomataReplayDiscardReason.IncompleteWindow);
            return;
        }
        if (report.ActionsAttempted != 0)
        {
            BeginClose(AutomataReplayCloseReason.ActionAttempted);
            if (_state == CaptureState.ExportPending) TryRequest();
        }
        else if (_window.EventCount >= _captureLimit)
        {
            BeginClose(AutomataReplayCloseReason.EventLimit);
            if (_state == CaptureState.ExportPending) TryRequest();
        }
    }

    public void ObserveLifecycleBoundary()
    {
        if (_disposed || _state == CaptureState.Terminal) return;
        if (_state == CaptureState.Open)
            BeginClose(AutomataReplayCloseReason.LifecycleBoundary);
        else if (_state == CaptureState.Closing) TryFreeze();
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            if (_state != CaptureState.Terminal)
                FailCapture(AutomataReplayDiscardReason.DisposedBeforeExport);
        }
        finally
        {
            _disposed = true;
            if (_state != CaptureState.Terminal) StopExporter();
        }
    }

    private void TryRequest()
    {
        if (_state != CaptureState.ExportPending) return;
        TryRequestCore();
    }

    private void TryRequestCore()
    {
        AutomataReplayExportStepResult result;
        try
        {
            result = _exporter.ContinueSnapshot();
        }
        catch (Exception exception) when (IsContainedReplayFailure(exception))
        {
            FailCapture(AutomataReplayDiscardReason.ExporterException);
            return;
        }
        switch (result)
        {
            case AutomataReplayExportStepResult.Accepted:
                _state = CaptureState.Terminal;
                StopExporter();
                break;
            case AutomataReplayExportStepResult.Pending:
                break;
            default:
                FailCapture(AutomataReplayDiscardReason.ExporterUnavailable);
                break;
        }
    }

    private void BeginClose(AutomataReplayCloseReason reason)
    {
        _state = CaptureState.Closing;
        NotifyCloseRequested(reason);
        TryFreeze();
    }

    private void TryFreeze()
    {
        if (!_window.IsComplete)
        {
            FailCapture(AutomataReplayDiscardReason.IncompleteWindow);
            return;
        }
        var close = _window.TryFreezeAtSettledBoundary();
        if (close == ServiceCycleSemanticTraceCloseResult.Closed)
        {
            _recording.CloseRecordingAdmission();
            _state = CaptureState.ExportPending;
            return;
        }
        if (close == ServiceCycleSemanticTraceCloseResult.Invalidated)
        {
            FailCapture(AutomataReplayDiscardReason.InvalidatedWindow);
            return;
        }
        if (_window.EventCount >= _failureLimit)
            FailCapture(AutomataReplayDiscardReason.HeadroomExhausted);
    }

    private void FailCapture(AutomataReplayDiscardReason reason)
    {
        _recording.CloseRecordingAdmission();
        _window.Discard();
        _state = CaptureState.Terminal;
        try { NotifyCaptureDiscarded(reason); }
        finally { StopExporter(); }
    }

    private void StopExporter()
    {
        try { _exporter.Stop(); }
        catch (Exception exception) when (IsContainedReplayFailure(exception)) { }
    }

    private void NotifyArmed()
    {
        try { _observer.Armed(); }
        catch (Exception exception) when (IsContainedReplayFailure(exception)) { }
    }

    private void NotifyCloseRequested(AutomataReplayCloseReason reason)
    {
        try { _observer.CloseRequested(reason); }
        catch (Exception exception) when (IsContainedReplayFailure(exception)) { }
    }

    private void NotifyCaptureDiscarded(AutomataReplayDiscardReason reason)
    {
        try { _observer.CaptureDiscarded(reason); }
        catch (Exception exception) when (IsContainedReplayFailure(exception)) { }
    }

    private static bool IsContainedReplayFailure(Exception exception) =>
        exception is not StackOverflowException and
        not OutOfMemoryException and
        not AccessViolationException;

    private enum CaptureState
    {
        Open = 0,
        Closing = 1,
        ExportPending = 2,
        Terminal = 3,
    }
}
