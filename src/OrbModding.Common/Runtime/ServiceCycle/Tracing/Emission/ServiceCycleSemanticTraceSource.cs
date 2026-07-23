using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

/// <summary>
/// Read-only owner-thread view of the suite semantic stream. Runtime event emission remains private to
/// the Common frame-pump boundary; diagnostics and export code can only inspect or capture committed facts.
/// </summary>
public sealed class ServiceCycleSemanticTraceSource
{
    private readonly ServiceCycleSemanticRecorder _recorder;
    private bool _emissionFaulted;
    private long _emissionFaultCount;

    internal ServiceCycleSemanticTraceSource(ServiceCycleSemanticRecorder recorder) =>
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));

    public ServiceCycleTraceSessionId Session => _recorder.Session;
    public int Capacity => _recorder.Capacity;
    public int Count => _recorder.Count;
    public int ServiceCapacity => _recorder.ServiceCapacity;
    public ulong OverwrittenTotal => _recorder.OverwrittenTotal;
    public ServiceCycleTraceDropRange OverwrittenRange => _recorder.OverwrittenRange;
    public ServiceCycleTraceCursor Cursor => _recorder.Cursor;
    public bool EmissionFaulted => _emissionFaulted;
    public long EmissionFaultCount => _emissionFaultCount;

    public ServiceCycleEventDrain DrainSince(
        ServiceCycleTraceCursor after,
        Span<ServiceCycleSemanticEvent> destination) => _recorder.DrainSince(after, destination);

    public ServiceCycleTraceCapture CreateCapture(int capacity) => _recorder.CreateCapture(capacity);

    public ServiceCycleEventDrain PullCapture(ServiceCycleTraceCapture capture, int maximumEvents) =>
        _recorder.PullCapture(capture, maximumEvents);

    internal ServiceCycleSemanticRecorder Recorder => _recorder;

    internal void RecordEmissionFault()
    {
        _emissionFaulted = true;
        if (_emissionFaultCount != long.MaxValue) _emissionFaultCount++;
    }
}
