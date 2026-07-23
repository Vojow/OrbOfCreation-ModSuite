using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal readonly struct DecisionJournalObservation
{
    internal DecisionJournalObservation(
        ServiceCycleTraceServiceId service,
        ulong lifecycle,
        ulong configuration,
        ulong strategy,
        ulong capture,
        ulong cycle,
        MonotonicTimestamp firstObservedAt,
        MonotonicTimestamp lastObservedAt,
        int startDecisionCode,
        int captureDecisionCode,
        bool hasWake,
        WakePolicy wake,
        bool hasProjection,
        in ServiceStateProjectionSnapshot projection,
        in ServiceFault fault,
        in BatchReceipt terminal)
    {
        if (!service.IsValid) throw new ArgumentException("A valid journal service is required.", nameof(service));
        if (lifecycle == 0) throw new ArgumentOutOfRangeException(nameof(lifecycle));
        if (configuration == 0) throw new ArgumentOutOfRangeException(nameof(configuration));
        if ((capture == 0) != (cycle == 0))
            throw new ArgumentException("Capture and cycle identity must be present together.", nameof(capture));
        if (lastObservedAt < firstObservedAt)
            throw new ArgumentOutOfRangeException(nameof(lastObservedAt));
        ValidateDecisionCode(startDecisionCode, nameof(startDecisionCode));
        ValidateDecisionCode(captureDecisionCode, nameof(captureDecisionCode));
        if (capture == 0 && captureDecisionCode != 0)
            throw new ArgumentException("A capture decision requires a capture identity.", nameof(captureDecisionCode));
        if (capture != 0)
        {
            if (captureDecisionCode == CommonServiceDecisionCodes.Captured.Value && strategy == 0)
                throw new ArgumentException("A captured decision requires its strategy generation.", nameof(strategy));
            if (captureDecisionCode == CommonServiceDecisionCodes.CaptureUnavailable.Value && strategy != 0)
                throw new ArgumentException("An unavailable capture cannot claim a strategy generation.", nameof(strategy));
            if (captureDecisionCode == 0 &&
                (strategy != 0 || !fault.IsValid || fault.Category != ServiceFaultCategory.Capture))
            {
                throw new ArgumentException("A capture without a decision code must be a capture fault.", nameof(fault));
            }
        }
        if (hasWake && !wake.IsValid) throw new ArgumentException("The journal wake is invalid.", nameof(wake));
        if (terminal.IsPresent)
        {
            var identity = terminal.Cycle;
            if (capture == 0 || strategy == 0 ||
                identity.Lifecycle.Value != lifecycle ||
                identity.Config.Value != configuration ||
                identity.Strategy.Value != strategy ||
                identity.Capture.Value != capture ||
                identity.Cycle.Value != cycle)
            {
                throw new ArgumentException("The terminal receipt belongs to another journal cycle.", nameof(terminal));
            }
        }

        Service = service;
        Lifecycle = lifecycle;
        Configuration = configuration;
        Strategy = strategy;
        Capture = capture;
        Cycle = cycle;
        FirstObservedAt = firstObservedAt;
        LastObservedAt = lastObservedAt;
        StartDecisionCode = startDecisionCode;
        CaptureDecisionCode = captureDecisionCode;
        HasWake = hasWake;
        Wake = wake;
        HasProjection = hasProjection;
        Projection = projection;
        Fault = fault;
        Terminal = terminal;
    }

    internal ServiceCycleTraceServiceId Service { get; }
    internal ulong Lifecycle { get; }
    internal ulong Configuration { get; }
    internal ulong Strategy { get; }
    internal ulong Capture { get; }
    internal ulong Cycle { get; }
    internal MonotonicTimestamp FirstObservedAt { get; }
    internal MonotonicTimestamp LastObservedAt { get; }
    internal int StartDecisionCode { get; }
    internal int CaptureDecisionCode { get; }
    internal bool HasWake { get; }
    internal WakePolicy Wake { get; }
    internal bool HasProjection { get; }
    internal ServiceStateProjectionSnapshot Projection { get; }
    internal ServiceFault Fault { get; }
    internal BatchReceipt Terminal { get; }

    private static void ValidateDecisionCode(int value, string parameterName)
    {
        if (value != 0 && value is not (>= 1 and <= 5) && value < ServiceDecisionCode.FirstFeatureCode)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}
