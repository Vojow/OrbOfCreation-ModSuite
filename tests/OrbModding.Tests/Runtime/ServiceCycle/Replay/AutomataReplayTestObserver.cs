using System.Collections.Concurrent;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Observability;

namespace OrbAutomata.Tests;

internal sealed class AutomataReplayTestObserver : IAutomataReplayCaptureObserver
{
    private int _armed;

    internal int ArmedCount => Volatile.Read(ref _armed);
    internal ConcurrentQueue<AutomataReplayCloseReason> CloseReasons { get; } = new();
    internal ConcurrentQueue<AutomataReplayDiscardReason> DiscardReasons { get; } = new();
    internal ConcurrentQueue<(int Ordinal, int Bytes)> Committed { get; } = new();
    internal ConcurrentQueue<(int Ordinal, ServiceCycleReplayArtifactDiscardReason Reason)>
        ArtifactsDiscarded { get; } = new();
    internal ConcurrentQueue<ServiceCycleReplayExporterFaultReason>
        ExporterFaults { get; } = new();

    public void Armed() => Interlocked.Increment(ref _armed);
    public void CloseRequested(AutomataReplayCloseReason reason) => CloseReasons.Enqueue(reason);
    public void CaptureDiscarded(AutomataReplayDiscardReason reason) => DiscardReasons.Enqueue(reason);
    public void ArtifactCommitted(int ordinal, int bytes) => Committed.Enqueue((ordinal, bytes));
    public void ArtifactDiscarded(
        int ordinal,
        ServiceCycleReplayArtifactDiscardReason reason) =>
        ArtifactsDiscarded.Enqueue((ordinal, reason));
    public void ExporterFaulted(ServiceCycleReplayExporterFaultReason reason) =>
        ExporterFaults.Enqueue(reason);
}
