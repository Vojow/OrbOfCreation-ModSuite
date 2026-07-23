using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Observability;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutomataReplayCaptureTests
{
    [Fact]
    public void SettledBoundaryFreezesImmediatelyButCopiesOnlyOnAFrame()
    {
        var recording = Recording();
        var exporter = new ScriptedExporter(AutomataReplayExportStepResult.Accepted);
        var window = new ScriptedWindow(eventCapacity: 6);
        var observer = new AutomataReplayTestObserver();
        using var capture = Capture(recording, exporter, window, observer);

        capture.ObserveLifecycleBoundary();

        Assert.True(recording.RecordingAdmissionClosed);
        Assert.Equal(1, window.FreezeAttempts);
        Assert.Equal(0, exporter.Requests);

        capture.ObserveFrame(default);

        Assert.Equal(1, exporter.Requests);
        Assert.Equal(1, exporter.Stops);
        Assert.Equal(1, observer.ArmedCount);
        Assert.Equal(
            new[] { AutomataReplayCloseReason.LifecycleBoundary },
            observer.CloseReasons.ToArray());
    }

    [Fact]
    public void FrozenEvidenceRetriesTransientExporterStateWithoutMoreSemanticWork()
    {
        var recording = Recording();
        var exporter = new ScriptedExporter(
            AutomataReplayExportStepResult.Pending,
            AutomataReplayExportStepResult.Pending,
            AutomataReplayExportStepResult.Accepted);
        var window = new ScriptedWindow(eventCapacity: 6);
        using var capture = Capture(recording, exporter, window);

        capture.ObserveLifecycleBoundary();
        capture.ObserveFrame(default);
        capture.ObserveFrame(default);
        capture.ObserveFrame(default);

        Assert.Equal(3, exporter.Requests);
        Assert.Equal(1, window.FreezeAttempts);
        Assert.Equal(1, exporter.Stops);
    }

    [Fact]
    public void EventLimitWaitsForASettledBoundaryWithoutBlockingGameplay()
    {
        var recording = Recording();
        var exporter = new ScriptedExporter(AutomataReplayExportStepResult.Accepted);
        var window = new ScriptedWindow(eventCapacity: 6) { EventCount = 3, Settled = false };
        var observer = new AutomataReplayTestObserver();
        using var capture = Capture(recording, exporter, window, observer);

        capture.ObserveFrame(default);
        Assert.False(recording.RecordingAdmissionClosed);
        Assert.Equal(0, exporter.Requests);

        window.EventCount = 4;
        capture.ObserveFrame(default);
        window.Settled = true;
        capture.ObserveFrame(default);

        Assert.True(recording.RecordingAdmissionClosed);
        Assert.Equal(1, exporter.Requests);
        Assert.Equal(3, window.FreezeAttempts);
        Assert.Equal(0, window.Discards);
        Assert.Equal(
            new[] { AutomataReplayCloseReason.EventLimit },
            observer.CloseReasons.ToArray());
    }

    [Fact]
    public void UnsettledWindowIsDiscardedBeforeItsReservedHeadroomCanBeOverwritten()
    {
        var recording = Recording();
        var exporter = new ScriptedExporter(AutomataReplayExportStepResult.Accepted);
        var window = new ScriptedWindow(eventCapacity: 6) { EventCount = 3, Settled = false };
        var observer = new AutomataReplayTestObserver();
        using var capture = Capture(recording, exporter, window, observer);

        capture.ObserveFrame(default);
        window.EventCount = 5;
        capture.ObserveFrame(default);

        Assert.Equal(0, exporter.Requests);
        Assert.Equal(1, window.Discards);
        Assert.Equal(1, exporter.Stops);
        Assert.Equal(
            new[] { AutomataReplayDiscardReason.HeadroomExhausted },
            observer.DiscardReasons.ToArray());
    }

    [Fact]
    public void IncompleteWindowIsDiscardedWithoutRequestingAnArtifact()
    {
        var recording = Recording();
        var exporter = new ScriptedExporter(AutomataReplayExportStepResult.Accepted);
        var window = new ScriptedWindow(eventCapacity: 6) { IsComplete = false };
        var observer = new AutomataReplayTestObserver();
        using var capture = Capture(recording, exporter, window, observer);

        capture.ObserveFrame(default);

        Assert.True(recording.RecordingAdmissionClosed);
        Assert.Equal(0, exporter.Requests);
        Assert.Equal(1, window.Discards);
        Assert.Equal(1, exporter.Stops);
        Assert.Equal(
            new[] { AutomataReplayDiscardReason.IncompleteWindow },
            observer.DiscardReasons.ToArray());
    }

    [Fact]
    public void RemovedCompositionInvalidatesRatherThanExportingTheRetainedPrefix()
    {
        var recording = Recording();
        var exporter = new ScriptedExporter(AutomataReplayExportStepResult.Accepted);
        var window = new ScriptedWindow(eventCapacity: 6)
        {
            CloseResult = ServiceCycleSemanticTraceCloseResult.Invalidated,
        };
        var observer = new AutomataReplayTestObserver();
        using var capture = Capture(recording, exporter, window, observer);

        capture.ObserveLifecycleBoundary();

        Assert.Equal(0, exporter.Requests);
        Assert.Equal(1, window.Discards);
        Assert.Equal(1, exporter.Stops);
        Assert.Equal(
            new[] { AutomataReplayDiscardReason.InvalidatedWindow },
            observer.DiscardReasons.ToArray());
    }

    [Fact]
    public void RepeatedLifecycleCallbacksCannotCopyMoreThanTheNextFrameChunk()
    {
        var recording = Recording();
        var exporter = new ScriptedExporter(
            AutomataReplayExportStepResult.Pending,
            AutomataReplayExportStepResult.Accepted);
        var window = new ScriptedWindow(eventCapacity: 6);
        var observer = new AutomataReplayTestObserver();
        using var capture = Capture(recording, exporter, window, observer);

        capture.ObserveLifecycleBoundary();
        capture.ObserveLifecycleBoundary();
        Assert.Equal(0, exporter.Requests);

        capture.ObserveFrame(default);
        Assert.Equal(1, exporter.Requests);
        Assert.Single(observer.CloseReasons);
    }

    [Fact]
    public void DisposalDiscardsAnUnfinishedSnapshotWithoutCopyingIt()
    {
        var recording = Recording();
        var exporter = new ScriptedExporter(AutomataReplayExportStepResult.Pending);
        var window = new ScriptedWindow(eventCapacity: 6);
        var observer = new AutomataReplayTestObserver();
        var capture = Capture(recording, exporter, window, observer);
        capture.ObserveLifecycleBoundary();

        capture.Dispose();

        Assert.Equal(0, exporter.Requests);
        Assert.Equal(1, window.Discards);
        Assert.Equal(1, exporter.Stops);
        Assert.Equal(
            new[] { AutomataReplayDiscardReason.DisposedBeforeExport },
            observer.DiscardReasons.ToArray());
    }

    [Fact]
    public void NonFatalExporterFailureCannotEscapeIntoGameplay()
    {
        var recording = Recording();
        var exporter = new ScriptedExporter(new InvalidOperationException("diagnostic failure"));
        var window = new ScriptedWindow(eventCapacity: 6);
        var observer = new AutomataReplayTestObserver();
        using var capture = Capture(recording, exporter, window, observer);

        capture.ObserveLifecycleBoundary();
        capture.ObserveFrame(default);

        Assert.True(recording.RecordingAdmissionClosed);
        Assert.Equal(1, exporter.Requests);
        Assert.Equal(1, window.Discards);
        Assert.Equal(1, exporter.Stops);
        Assert.Equal(
            new[] { AutomataReplayDiscardReason.ExporterException },
            observer.DiscardReasons.ToArray());
    }

    [Fact]
    public void UnavailableExporterDiscardsTheCaptureWithItsExactReason()
    {
        var recording = Recording();
        var exporter = new ScriptedExporter(AutomataReplayExportStepResult.Unavailable);
        var window = new ScriptedWindow(eventCapacity: 6);
        var observer = new AutomataReplayTestObserver();
        using var capture = Capture(recording, exporter, window, observer);

        capture.ObserveLifecycleBoundary();
        capture.ObserveFrame(default);

        Assert.Equal(1, window.Discards);
        Assert.Equal(1, exporter.Stops);
        Assert.Equal(
            new[] { AutomataReplayDiscardReason.ExporterUnavailable },
            observer.DiscardReasons.ToArray());
    }

    [Fact]
    public void ObserverFailuresCannotEscapeIntoGameplayOrCaptureCleanup()
    {
        var recording = Recording();
        var exporter = new ScriptedExporter(AutomataReplayExportStepResult.Unavailable);
        var window = new ScriptedWindow(eventCapacity: 6);
        using var capture = Capture(recording, exporter, window, new ThrowingObserver());

        capture.ObserveLifecycleBoundary();
        capture.ObserveFrame(default);

        Assert.True(recording.RecordingAdmissionClosed);
        Assert.Equal(1, window.Discards);
        Assert.Equal(1, exporter.Stops);
    }

    [Fact]
    public void FatalExporterFailureKeepsTheProcessFatalBoundary()
    {
        var recording = Recording();
        var exporter = new ScriptedExporter(new OutOfMemoryException("fatal"));
        var window = new ScriptedWindow(eventCapacity: 6);
        using var capture = Capture(recording, exporter, window);

        capture.ObserveLifecycleBoundary();

        Assert.Throws<OutOfMemoryException>(() => capture.ObserveFrame(default));
    }

    [Fact]
    public void ActionCloseReasonWinsWhenTheEventLimitIsAlsoReached()
    {
        var recording = Recording();
        var exporter = new ScriptedExporter(AutomataReplayExportStepResult.Accepted);
        var window = new ScriptedWindow(eventCapacity: 6) { EventCount = 3 };
        var observer = new AutomataReplayTestObserver();
        using var capture = Capture(recording, exporter, window, observer);

        capture.ObserveFrame(new SuiteFramePumpReport(
            frameIdentity: 1,
            accepted: true,
            startingOrdinal: 0,
            responsesAcquired: 0,
            actionsAttempted: 1,
            capturesAttempted: 0,
            emergencyBatchesRejected: 0,
            lifecyclePositionTransitions: 0,
            responseDuration: default,
            actionDuration: default,
            captureDuration: default,
            totalDuration: default));

        Assert.Equal(
            new[] { AutomataReplayCloseReason.ActionAttempted },
            observer.CloseReasons.ToArray());
    }

    private static AutomataReplayCapture Capture(
        ServiceCycleReplaySession recording,
        IAutomataReplayExportPort exporter,
        IAutomataReplayWindow window,
        IAutomataReplayCaptureObserver? observer = null) =>
        new(
            recording,
            exporter,
            window,
            observer ?? new AutomataReplayTestObserver(),
            captureLimit: 3,
            failureLimit: 5);

    private static ServiceCycleReplaySession Recording() => new(
        new ServiceCycleTraceSessionId(901),
        new ServiceCycleReplaySessionOptions(
            true,
            byteCapacity: 256,
            recordCapacity: 16,
            cycleFooterCapacity: 4));

    private sealed class ScriptedExporter : IAutomataReplayExportPort
    {
        private readonly Queue<AutomataReplayExportStepResult> _results = new();
        private Exception? _failure;

        public ScriptedExporter(params AutomataReplayExportStepResult[] results)
        {
            foreach (var result in results) _results.Enqueue(result);
        }

        public ScriptedExporter(Exception failure) => _failure = failure;

        public int Requests { get; private set; }
        public int Stops { get; private set; }

        public AutomataReplayExportStepResult ContinueSnapshot()
        {
            Requests++;
            if (_failure is not null)
            {
                var failure = _failure;
                _failure = null;
                throw failure;
            }
            return _results.Count == 0
                ? AutomataReplayExportStepResult.Unavailable
                : _results.Dequeue();
        }

        public void Stop() => Stops++;
    }

    private sealed class ScriptedWindow : IAutomataReplayWindow
    {
        public ScriptedWindow(int eventCapacity) => EventCapacity = eventCapacity;

        public int EventCount { get; set; }
        public int EventCapacity { get; }
        public bool IsComplete { get; set; } = true;
        public bool Settled
        {
            set => CloseResult = value
                ? ServiceCycleSemanticTraceCloseResult.Closed
                : ServiceCycleSemanticTraceCloseResult.Pending;
        }
        public ServiceCycleSemanticTraceCloseResult CloseResult { get; set; } =
            ServiceCycleSemanticTraceCloseResult.Closed;
        public int FreezeAttempts { get; private set; }
        public int Discards { get; private set; }

        public ServiceCycleSemanticTraceCloseResult TryFreezeAtSettledBoundary()
        {
            FreezeAttempts++;
            return CloseResult;
        }

        public void Discard() => Discards++;
    }

    private sealed class ThrowingObserver : IAutomataReplayCaptureObserver
    {
        public void Armed() => throw new InvalidOperationException();
        public void CloseRequested(AutomataReplayCloseReason reason) =>
            throw new InvalidOperationException();
        public void CaptureDiscarded(AutomataReplayDiscardReason reason) =>
            throw new InvalidOperationException();
        public void ArtifactCommitted(int ordinal, int bytes) =>
            throw new InvalidOperationException();
        public void ArtifactDiscarded(
            int ordinal,
            ServiceCycleReplayArtifactDiscardReason reason) =>
            throw new InvalidOperationException();
        public void ExporterFaulted(ServiceCycleReplayExporterFaultReason reason) =>
            throw new InvalidOperationException();
    }
}
