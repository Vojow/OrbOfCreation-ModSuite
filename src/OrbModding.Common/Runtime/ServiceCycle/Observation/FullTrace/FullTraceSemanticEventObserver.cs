using System;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace;

internal sealed class FullTraceSemanticEventObserver : IServiceCycleSemanticEventObserver
{
    private readonly BufferedSegmentSink<ServiceCycleSemanticEvent> _sink;

    internal FullTraceSemanticEventObserver(BufferedSegmentSink<ServiceCycleSemanticEvent> sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public void Observe(in ServiceCycleSemanticEvent item)
    {
        var result = _sink.Append(in item);
        if (result == BufferedSegmentAppendResult.Accepted) return;
        throw new InvalidOperationException($"Manual full-trace admission stopped with {result}.");
    }
}
