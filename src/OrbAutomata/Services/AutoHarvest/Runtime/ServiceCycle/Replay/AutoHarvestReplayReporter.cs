using System;
using BepInEx.Logging;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Observability;

namespace OrbAutomata;

internal sealed class AutoHarvestReplayReporter : IAutomataReplayCaptureObserver
{
    private readonly ManualLogSource _log;

    internal AutoHarvestReplayReporter(ManualLogSource log) =>
        _log = log ?? throw new ArgumentNullException(nameof(log));

    public void Armed() => _log.LogAutomataInfo(
        "Auto Harvest replay capture armed for one finite window.");

    public void CloseRequested(AutomataReplayCloseReason reason) =>
        _log.LogAutomataInfo(
            "Auto Harvest replay capture is closing: " + CloseReason(reason) + ".");

    public void CaptureDiscarded(AutomataReplayDiscardReason reason) =>
        _log.LogAutomataWarning(
            "Auto Harvest replay capture was discarded: " + DiscardReason(reason) + ".");

    public void ArtifactCommitted(int ordinal, int bytes) =>
        _log.LogAutomataInfo(
            "Auto Harvest replay artifact committed: " +
            $"{AutoHarvestReplayPathPolicy.FormatRelativeArtifactPath(ordinal)} ({bytes} bytes).");

    public void ArtifactDiscarded(
        int ordinal,
        ServiceCycleReplayArtifactDiscardReason reason) =>
        _log.LogAutomataError(
            $"Auto Harvest replay artifact {ordinal:D6} was discarded: " +
            ArtifactDiscardReason(reason) + ".");

    public void ExporterFaulted(ServiceCycleReplayExporterFaultReason reason) =>
        _log.LogAutomataError(
            "Auto Harvest replay exporter stopped: " + ExporterFaultReason(reason) + ".");

    private static string CloseReason(AutomataReplayCloseReason reason) => reason switch
    {
        AutomataReplayCloseReason.ActionAttempted => "the first native action was attempted",
        AutomataReplayCloseReason.LifecycleBoundary => "the gameplay lifecycle changed",
        AutomataReplayCloseReason.EventLimit => "the finite event limit was reached",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    private static string DiscardReason(AutomataReplayDiscardReason reason) => reason switch
    {
        AutomataReplayDiscardReason.IncompleteWindow => "semantic evidence became incomplete",
        AutomataReplayDiscardReason.InvalidatedWindow => "the settled boundary was invalidated",
        AutomataReplayDiscardReason.HeadroomExhausted => "settlement exhausted the reserved event headroom",
        AutomataReplayDiscardReason.ExporterUnavailable => "the background exporter became unavailable",
        AutomataReplayDiscardReason.ExporterException => "the exporter rejected the snapshot",
        AutomataReplayDiscardReason.DisposedBeforeExport => "the runtime stopped before export admission",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    private static string ArtifactDiscardReason(
        ServiceCycleReplayArtifactDiscardReason reason) => reason switch
    {
        ServiceCycleReplayArtifactDiscardReason.WriteFailed => "encoding or storage failed before commit",
        ServiceCycleReplayArtifactDiscardReason.ExporterFaulted => "the exporter faulted before this queued artifact was written",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    private static string ExporterFaultReason(
        ServiceCycleReplayExporterFaultReason reason) => reason switch
    {
        ServiceCycleReplayExporterFaultReason.SourceFault => "the frozen semantic source was invalid",
        ServiceCycleReplayExporterFaultReason.StartupFailure => "storage reconciliation or worker startup failed",
        ServiceCycleReplayExporterFaultReason.EncodingOrStorageFailure => "artifact encoding or storage failed",
        ServiceCycleReplayExporterFaultReason.RetentionFailure => "the artifact committed but retention cleanup failed",
        ServiceCycleReplayExporterFaultReason.WorkerFailure => "the background worker failed",
        ServiceCycleReplayExporterFaultReason.OrdinalExhausted => "the artifact ordinal space was exhausted",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
