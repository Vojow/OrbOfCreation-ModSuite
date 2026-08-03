using System;
using System.Globalization;
using System.IO;
using OrbModding.Common.Runtime;

namespace OrbModConfig;

internal readonly struct DiagnosticsBundlePresentation
{
    internal DiagnosticsBundlePresentation(string body, string buttonLabel, bool buttonEnabled)
    {
        Body = body;
        ButtonLabel = buttonLabel;
        ButtonEnabled = buttonEnabled;
    }

    internal string Body { get; }
    internal string ButtonLabel { get; }
    internal bool ButtonEnabled { get; }
}

internal static class DiagnosticsBundlePresenter
{
    internal static DiagnosticsBundlePresentation Build(
        DiagnosticsBundleStatus status,
        bool bundleRequested)
    {
        if (bundleRequested)
            return new DiagnosticsBundlePresentation(Body(status), "Creating file...", false);
        return new DiagnosticsBundlePresentation(
            Body(status),
            status.State is DiagnosticsBundleState.Written or
                DiagnosticsBundleState.WrittenRevealUnavailable
                ? "Create another"
                : "Create bug report",
            status.State != DiagnosticsBundleState.Unavailable);
    }

    private static string Body(DiagnosticsBundleStatus status) => status.State switch
    {
        DiagnosticsBundleState.Unavailable =>
            "Bug reports are unavailable because the suite's diagnostics did not start.",
        DiagnosticsBundleState.Ready =>
            "Creates one shareable file from the suite's recent activity, settings, log, and save files. " +
            "It captures what already happened; pressing the button does not start a recording.",
        DiagnosticsBundleState.Written =>
            "Bug report ready | " + Bytes(status.BytesWritten) +
            "\nThe file is selected in your file manager: " + Path.GetFileName(status.Path),
        DiagnosticsBundleState.WrittenRevealUnavailable =>
            "Bug report ready | " + Bytes(status.BytesWritten) +
            "\nYour file manager could not be opened. The full file path is:\n" + status.Path,
        DiagnosticsBundleState.Failed =>
            "No bug report file was created. The suite keeps running.\nReason: " + status.FailureReason,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static string Bytes(long bytes)
    {
        const double kibibyte = 1024d;
        const double mebibyte = kibibyte * 1024d;
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        if (bytes < mebibyte)
            return (bytes / kibibyte).ToString("0.0", CultureInfo.InvariantCulture) + " KiB";
        return (bytes / mebibyte).ToString("0.0", CultureInfo.InvariantCulture) + " MiB";
    }
}
