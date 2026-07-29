using System;
using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace.Control;

namespace OrbModConfig;

internal readonly struct HostTraceDumpPresentation
{
    public HostTraceDumpPresentation(string body, string buttonLabel, bool buttonEnabled)
    {
        Body = body;
        ButtonLabel = buttonLabel;
        ButtonEnabled = buttonEnabled;
    }

    public string Body { get; }
    public string ButtonLabel { get; }
    public bool ButtonEnabled { get; }
}

internal static class HostTraceDumpPresenter
{
    public static HostTraceDumpPresentation Build(HostTraceDumpStatus status, bool dumpRequested)
    {
        if (dumpRequested) return new HostTraceDumpPresentation(Body(status), "Writing...", false);
        return new HostTraceDumpPresentation(
            Body(status),
            status.State == HostTraceDumpState.Written ? "Dump again" : "Dump recent events",
            status.State != HostTraceDumpState.Unavailable);
    }

    private static string Body(HostTraceDumpStatus status) => status.State switch
    {
        HostTraceDumpState.Unavailable =>
            "Unavailable. Auto Harvest's ServiceCycle runtime is not active.",
        HostTraceDumpState.Idle =>
            "The suite always keeps the most recent events in memory. Dump them straight after " +
            "something goes wrong; nothing is written until you do.",
        HostTraceDumpState.Written =>
            "Written | " + status.WrittenEvents.ToString("N0", CultureInfo.InvariantCulture) +
            " events | " + Bytes(status.BytesWritten) + Dropped(status) +
            "\nArtifact: " + status.ArtifactName,
        HostTraceDumpState.Failed =>
            "The dump could not be written. The suite is unaffected; the events are still in memory.",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static string Dropped(HostTraceDumpStatus status) =>
        status.OverwrittenEvents == 0
            ? string.Empty
            : " | " + status.OverwrittenEvents.ToString("N0", CultureInfo.InvariantCulture) +
                " older events had already been overwritten";

    private static string Bytes(long bytes)
    {
        const double kibibyte = 1024d;
        const double mebibyte = kibibyte * 1024d;
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        if (bytes < mebibyte) return (bytes / kibibyte).ToString("0.0", CultureInfo.InvariantCulture) + " KiB";
        return (bytes / mebibyte).ToString("0.0", CultureInfo.InvariantCulture) + " MiB";
    }
}
