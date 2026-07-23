using System;
using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;

namespace OrbModConfig;

internal static class DecisionJournalPresenter
{
    internal static string Build(DecisionJournalStatus status) => status.State switch
    {
        DecisionJournalStatusState.Unavailable =>
            "Unavailable. No ServiceCycle decision-journal producer is active.",
        DecisionJournalStatusState.Initializing =>
            "Starting the background journal store.\nStore: " + status.ArtifactName,
        DecisionJournalStatusState.Arming =>
            "Ready to record; waiting for one settled lifecycle boundary.\n" + Metrics(status),
        DecisionJournalStatusState.Recording =>
            "Recording | " + Metrics(status),
        DecisionJournalStatusState.Stopping =>
            "Stopping; accepted records are draining off-thread.\n" + Metrics(status),
        DecisionJournalStatusState.Stopped =>
            "Stopped cleanly | " + Metrics(status),
        DecisionJournalStatusState.Faulted =>
            "Stopped after " + Result(status.Result) + ". Gameplay is unaffected.\n" + Metrics(status) +
            "\nFirst missing sequence: " +
            status.FirstIncompleteSequence.ToString("N0", CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static string Metrics(DecisionJournalStatus status) =>
        status.AcceptedRecords.ToString("N0", CultureInfo.InvariantCulture) + " accepted | " +
        status.WrittenRecords.ToString("N0", CultureInfo.InvariantCulture) + " written | " +
        Bytes(status.BytesWritten) + " | " +
        status.WrittenSegments.ToString("N0", CultureInfo.InvariantCulture) + " segments\n" +
        status.RetainedSegments.ToString("N0", CultureInfo.InvariantCulture) + " retained | " +
        status.EvictedSegments.ToString("N0", CultureInfo.InvariantCulture) + " evicted | buffers " +
        status.PendingBlocks.ToString(CultureInfo.InvariantCulture) + " pending / " +
        status.PeakPendingBlocks.ToString(CultureInfo.InvariantCulture) + " peak | Store: " +
        status.ArtifactName + "\n" +
        status.StartupPrunedSegments.ToString("N0", CultureInfo.InvariantCulture) +
        " pruned at startup | " +
        status.StaleTemporaryFilesRemoved.ToString("N0", CultureInfo.InvariantCulture) +
        " stale temporary files removed";

    private static string Bytes(long bytes)
    {
        const double kibibyte = 1024d;
        const double mebibyte = kibibyte * 1024d;
        const double gibibyte = mebibyte * 1024d;
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        if (bytes < mebibyte) return (bytes / kibibyte).ToString("0.0", CultureInfo.InvariantCulture) + " KiB";
        if (bytes < gibibyte) return (bytes / mebibyte).ToString("0.0", CultureInfo.InvariantCulture) + " MiB";
        return (bytes / gibibyte).ToString("0.0", CultureInfo.InvariantCulture) + " GiB";
    }

    private static string Result(DecisionJournalStatusResult result) => result switch
    {
        DecisionJournalStatusResult.BufferExhausted => "all journal buffers were occupied",
        DecisionJournalStatusResult.SequenceExhausted => "the journal sequence range was exhausted",
        DecisionJournalStatusResult.InitializationFailed => "storage initialization failed",
        DecisionJournalStatusResult.WriteFailed => "a background write failed",
        DecisionJournalStatusResult.CompletionFailed => "journal completion failed",
        DecisionJournalStatusResult.ProducerFailed => "journal observation failed",
        DecisionJournalStatusResult.RetentionFailed => "rolling retention failed",
        DecisionJournalStatusResult.OrdinalExhausted => "the persistent segment ordinal was exhausted",
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };
}
