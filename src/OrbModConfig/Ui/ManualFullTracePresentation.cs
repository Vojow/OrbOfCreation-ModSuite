using System;
using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Control;

namespace OrbModConfig;

internal readonly struct ManualFullTracePresentation
{
    public ManualFullTracePresentation(
        string body,
        string buttonLabel,
        bool buttonEnabled,
        ManualFullTraceCommand command)
    {
        Body = body;
        ButtonLabel = buttonLabel;
        ButtonEnabled = buttonEnabled;
        Command = command;
    }

    public string Body { get; }
    public string ButtonLabel { get; }
    public bool ButtonEnabled { get; }
    public ManualFullTraceCommand Command { get; }
}

internal static class ManualFullTracePresenter
{
    public static ManualFullTracePresentation Build(
        ManualFullTraceStatus status,
        ManualFullTraceCommand pendingCommand)
    {
        if (pendingCommand != ManualFullTraceCommand.None)
        {
            return new ManualFullTracePresentation(
                Body(status),
                pendingCommand == ManualFullTraceCommand.Start ? "Starting..." : "Stopping...",
                false,
                ManualFullTraceCommand.None);
        }

        return status.State switch
        {
            ManualFullTraceState.Unavailable => new ManualFullTracePresentation(
                Body(status), "Start full trace", false, ManualFullTraceCommand.None),
            ManualFullTraceState.Idle => new ManualFullTracePresentation(
                Body(status), "Start full trace", true, ManualFullTraceCommand.Start),
            ManualFullTraceState.Arming or ManualFullTraceState.Recording => new ManualFullTracePresentation(
                Body(status), "Stop trace", true, ManualFullTraceCommand.Stop),
            ManualFullTraceState.Stopping => new ManualFullTracePresentation(
                Body(status), "Stopping...", false, ManualFullTraceCommand.None),
            ManualFullTraceState.Complete or ManualFullTraceState.Incomplete => new ManualFullTracePresentation(
                Body(status), "Start new trace", true, ManualFullTraceCommand.Start),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
    }

    private static string Body(ManualFullTraceStatus status)
    {
        return status.State switch
        {
            ManualFullTraceState.Unavailable =>
                "Unavailable. Auto Harvest's ServiceCycle runtime is not active.",
            ManualFullTraceState.Idle =>
                "Ready. Start before reproducing the problem; tracing owns no writer or buffers while idle.",
            ManualFullTraceState.Arming =>
                "Starting. Waiting for the writer and a safe between-cycle boundary.\nSession: " + status.ArtifactName,
            ManualFullTraceState.Recording =>
                "Recording | " + Metrics(status) + "\nSession: " + status.ArtifactName,
            ManualFullTraceState.Stopping =>
                "Stopping. Accepted data is draining to disk.\n" + Metrics(status) +
                "\nSession: " + status.ArtifactName,
            ManualFullTraceState.Complete =>
                "Complete | " + Metrics(status) + "\nManifest committed | Session: " + status.ArtifactName,
            ManualFullTraceState.Incomplete => IncompleteBody(status),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
    }

    private static string IncompleteBody(ManualFullTraceStatus status)
    {
        var session = status.ArtifactName.Length == 0 ? string.Empty : " | Session: " + status.ArtifactName;
        return "Incomplete: " + Result(status.Result) + " | " + Metrics(status) +
            "\nFirst missing sequence: " +
            status.FirstIncompleteSequence.ToString("N0", CultureInfo.InvariantCulture) +
            " | Manifest: " + (status.ManifestCommitted ? "committed" : "not committed") + session;
    }

    private static string Metrics(ManualFullTraceStatus status) =>
        Duration(status.Duration) + " | " +
        status.AcceptedRecords.ToString("N0", CultureInfo.InvariantCulture) + " accepted | " +
        status.WrittenRecords.ToString("N0", CultureInfo.InvariantCulture) + " written | " +
        Bytes(status.BytesWritten) + " | " +
        status.SegmentCount.ToString("N0", CultureInfo.InvariantCulture) + " segments";

    private static string Duration(TimeSpan duration) => string.Format(
        CultureInfo.InvariantCulture,
        "{0:00}:{1:00}:{2:00}",
        (long)duration.TotalHours,
        duration.Minutes,
        duration.Seconds);

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

    private static string Result(ManualFullTraceResult result) => result switch
    {
        ManualFullTraceResult.RuntimeShutdown => "runtime shut down before a normal stop",
        ManualFullTraceResult.BufferExhausted => "all trace buffers were occupied",
        ManualFullTraceResult.SequenceExhausted => "the trace sequence range was exhausted",
        ManualFullTraceResult.InitializationFailed => "the writer could not initialize",
        ManualFullTraceResult.WriteFailed => "the background write failed",
        ManualFullTraceResult.CompletionFailed => "the terminal manifest could not be committed",
        ManualFullTraceResult.SemanticFault => "the semantic source faulted",
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };
}
