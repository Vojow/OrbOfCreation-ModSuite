using System;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal enum DecisionJournalRuntimeState
{
    Initializing = 0,
    Arming = 1,
    Recording = 2,
    Stopping = 3,
    Stopped = 4,
    Faulted = 5,
}

internal readonly struct DecisionJournalRuntimeSnapshot
{
    internal DecisionJournalRuntimeSnapshot(
        DecisionJournalRuntimeState state,
        bool attached,
        in BufferedSegmentMetrics transport,
        in DecisionJournalConsumerMetrics consumer,
        Exception? faultException = null,
        string? faultSite = null)
    {
        State = state;
        Attached = attached;
        Transport = transport;
        Consumer = consumer;
        FaultException = faultException;
        FaultSite = faultSite;
    }

    internal DecisionJournalRuntimeState State { get; }
    internal bool Attached { get; }
    internal BufferedSegmentMetrics Transport { get; }
    internal DecisionJournalConsumerMetrics Consumer { get; }
    internal Exception? FaultException { get; }
    internal string? FaultSite { get; }
}
