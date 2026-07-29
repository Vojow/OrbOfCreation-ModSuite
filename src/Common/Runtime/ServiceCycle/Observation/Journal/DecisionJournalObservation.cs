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
        if (lastObservedAt < firstObservedAt)
            throw new ArgumentOutOfRangeException(nameof(lastObservedAt));
        ValidateDecisionCode(startDecisionCode, nameof(startDecisionCode));
        ValidateDecisionCode(captureDecisionCode, nameof(captureDecisionCode));
        if (cycle == 0 && captureDecisionCode != 0)
            throw new ArgumentException("A capture decision requires a cycle identity.", nameof(captureDecisionCode));
        if (cycle != 0)
        {
            if (captureDecisionCode == CommonServiceDecisionCodes.Captured.Value && strategy == 0)
                throw new ArgumentException("A captured decision requires its strategy generation.", nameof(strategy));
            if (captureDecisionCode == CommonServiceDecisionCodes.CaptureUnavailable.Value && strategy != 0)
                throw new ArgumentException("An unavailable capture cannot claim a strategy generation.", nameof(strategy));
            // No capture decision is the ordinary shape, which has no capture at all — that record
            // still names the strategy generation its cycle ran against. Without one, the only thing
            // it can be is a capture that faulted before a cycle existed.
            if (captureDecisionCode == 0 && strategy == 0 &&
                (!fault.IsValid || fault.Category != ServiceFaultCategory.Capture))
            {
                throw new ArgumentException(
                    "A cycle with neither a strategy generation nor a capture decision must be a capture fault.",
                    nameof(fault));
            }
        }
        if (hasWake && !wake.IsValid) throw new ArgumentException("The journal wake is invalid.", nameof(wake));
        if (terminal.IsPresent)
        {
            var identity = terminal.Cycle;
            if (cycle == 0 || strategy == 0 ||
                identity.Lifecycle.Value != lifecycle ||
                identity.Config.Value != configuration ||
                identity.Strategy.Value != strategy ||
                identity.Cycle.Value != cycle)
            {
                throw new ArgumentException("The terminal receipt belongs to another journal cycle.", nameof(terminal));
            }
        }

        Service = service;
        Lifecycle = lifecycle;
        Configuration = configuration;
        Strategy = strategy;
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
