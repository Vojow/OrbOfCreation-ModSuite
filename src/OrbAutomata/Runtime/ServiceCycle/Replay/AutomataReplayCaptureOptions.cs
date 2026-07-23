using System;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing;

namespace OrbAutomata;

internal readonly struct AutomataReplayCaptureOptions
{
    public AutomataReplayCaptureOptions(
        ServiceCycleTraceSessionId traceSession,
        Func<IRestartAwareTraceSegmentStorage> createStorage,
        IAutomataReplayCaptureObserver observer)
    {
        if (!traceSession.IsValid)
            throw new ArgumentException("A valid trace session is required.", nameof(traceSession));
        TraceSession = traceSession;
        CreateStorage = createStorage ?? throw new ArgumentNullException(nameof(createStorage));
        Observer = observer ?? throw new ArgumentNullException(nameof(observer));
        Enabled = true;
    }

    public bool Enabled { get; }
    public ServiceCycleTraceSessionId TraceSession { get; }
    public Func<IRestartAwareTraceSegmentStorage>? CreateStorage { get; }
    public IAutomataReplayCaptureObserver? Observer { get; }
}
