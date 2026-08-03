using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;

namespace OrbAutomata;

internal sealed class AutomataDiagnosticsRuntimeEvidence
{
    internal AutomataDiagnosticsRuntimeEvidence(
        HostTraceSnapshot? hostTrace,
        DecisionJournalStatus journal,
        string journalDirectory,
        string unavailableReason)
    {
        HostTrace = hostTrace;
        Journal = journal;
        JournalDirectory = journalDirectory ?? string.Empty;
        UnavailableReason = unavailableReason ?? string.Empty;
    }

    internal HostTraceSnapshot? HostTrace { get; }
    internal DecisionJournalStatus Journal { get; }
    internal string JournalDirectory { get; }
    internal string UnavailableReason { get; }

    internal AutomataDiagnosticsRuntimeEvidence WithJournal(DecisionJournalStatus journal) => new(
        HostTrace,
        journal,
        JournalDirectory,
        UnavailableReason);

    internal static AutomataDiagnosticsRuntimeEvidence Unavailable(string reason) => new(
        null,
        DecisionJournalStatus.Unavailable,
        string.Empty,
        string.IsNullOrWhiteSpace(reason) ? "ServiceCycle evidence is unavailable." : reason);
}
