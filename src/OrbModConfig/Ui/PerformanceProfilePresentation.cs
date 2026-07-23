#if SERVICE_CYCLE_PROFILE
using System;
using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Control;

namespace OrbModConfig;

internal readonly struct PerformanceProfilePresentation
{
    public PerformanceProfilePresentation(
        string body,
        string buttonLabel,
        bool buttonEnabled,
        PerformanceProfileCommand command)
    {
        Body = body;
        ButtonLabel = buttonLabel;
        ButtonEnabled = buttonEnabled;
        Command = command;
    }

    public string Body { get; }
    public string ButtonLabel { get; }
    public bool ButtonEnabled { get; }
    public PerformanceProfileCommand Command { get; }
}

internal static class PerformanceProfilePresenter
{
    public static PerformanceProfilePresentation Build(
        PerformanceProfileControlStatus status,
        PerformanceProfileCommand pendingCommand)
    {
        if (pendingCommand != PerformanceProfileCommand.None)
        {
            return new PerformanceProfilePresentation(
                Body(status),
                pendingCommand == PerformanceProfileCommand.Start ? "Starting..." : "Stopping...",
                false,
                PerformanceProfileCommand.None);
        }

        return status.State switch
        {
            PerformanceProfileControlState.Unavailable => new PerformanceProfilePresentation(
                Body(status), "Start profile", false, PerformanceProfileCommand.None),
            PerformanceProfileControlState.Idle => new PerformanceProfilePresentation(
                Body(status), "Start profile", true, PerformanceProfileCommand.Start),
            PerformanceProfileControlState.Recording => new PerformanceProfilePresentation(
                Body(status), "Stop profile", true, PerformanceProfileCommand.Stop),
            PerformanceProfileControlState.Stopping => new PerformanceProfilePresentation(
                Body(status), "Stopping...", false, PerformanceProfileCommand.None),
            PerformanceProfileControlState.Complete => new PerformanceProfilePresentation(
                Body(status), "Start new profile", true, PerformanceProfileCommand.Start),
            PerformanceProfileControlState.Faulted => new PerformanceProfilePresentation(
                Body(status), "Restart required", false, PerformanceProfileCommand.None),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
    }

    private static string Body(PerformanceProfileControlStatus status)
    {
        return status.State switch
        {
            PerformanceProfileControlState.Unavailable =>
                "Unavailable. This build does not have an active ServiceCycle profile source.",
            PerformanceProfileControlState.Idle =>
                "Ready. Start when the workload is representative, then stop whenever enough evidence has been captured.",
            PerformanceProfileControlState.Recording =>
                "Recording | " + Metrics(status) + Artifact(status.ArtifactName),
            PerformanceProfileControlState.Stopping =>
                "Stopping. Accepted profile data is draining to disk.\n" + Metrics(status) +
                Artifact(status.ArtifactName),
            PerformanceProfileControlState.Complete =>
                "Complete: " + Result(status.Result) + " | " + Metrics(status) +
                Artifact(status.ArtifactName),
            PerformanceProfileControlState.Faulted =>
                "Faulted: " + Result(status.Result) + ". Restart the game before profiling again.\n" +
                Metrics(status) + Artifact(status.ArtifactName),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
    }

    private static string Metrics(PerformanceProfileControlStatus status) =>
        Duration(status.Duration) + " | " +
        status.WrittenRecords.ToString("N0", CultureInfo.InvariantCulture) + " written | " +
        Bytes(status.BytesWritten);

    private static string Artifact(string artifactName) =>
        artifactName.Length == 0 ? string.Empty : "\nProfile: " + artifactName;

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

    private static string Result(PerformanceProfileResult result) => result switch
    {
        PerformanceProfileResult.UserStopped => "stopped by user",
        PerformanceProfileResult.RuntimeShutdown => "runtime shut down",
        PerformanceProfileResult.BufferExhausted => "all profile buffers were occupied",
        PerformanceProfileResult.SequenceExhausted => "the profile sequence range was exhausted",
        PerformanceProfileResult.WriteFailed => "the background write failed",
        PerformanceProfileResult.ProbeFailed => "the performance probe failed",
        PerformanceProfileResult.InitializationFailed => "the profile writer could not initialize",
        PerformanceProfileResult.None => "no terminal result was reported",
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };
}
#endif
