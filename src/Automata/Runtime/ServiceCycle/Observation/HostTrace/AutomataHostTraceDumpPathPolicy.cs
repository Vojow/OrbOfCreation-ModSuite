using System;
using System.Globalization;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace.Control;
using OrbModding.Common.Runtime.Tracing;

namespace OrbAutomata;

/// <summary>
/// Where a dump of the recent-event ring lands: <c>run-&lt;timestamp&gt;/recent</c>, beside the armed
/// artifacts rather than among them.
/// </summary>
/// <remarks>
/// Its own directory because a run folder's <c>full</c> child holds exactly one session — that is what
/// lets the dashboard correlate a full trace with the profile beside it — and a dump is neither that
/// session nor a replacement for it. The format is identical, so the manual-trace reader opens a dump
/// directly.
/// </remarks>
internal sealed class AutomataHostTraceDumpPathPolicy : IAutomataHostTraceDumpSource
{
    private static long _nextIdentity = DateTime.UtcNow.Ticks;
    private readonly string _rootDirectory;

    private AutomataHostTraceDumpPathPolicy(string rootDirectory) => _rootDirectory = rootDirectory;

    internal static AutomataHostTraceOptions Create(HostTraceDumpRegistry control)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        var root = AutomataTraceRunRoot.Child("recent");
        return new AutomataHostTraceOptions(control, new AutomataHostTraceDumpPathPolicy(root));
    }

    public AutomataHostTraceDumpSpec Create()
    {
        var session = new FullTraceSessionId(NextIdentity());
        var artifactName = "session-" + session.Value.ToString("x16", CultureInfo.InvariantCulture);
        return new AutomataHostTraceDumpSpec(
            session,
            new AtomicSegmentSessionStorage(
                _rootDirectory,
                artifactName,
                ".oscs",
                "manifest.oscm"),
            artifactName);
    }

    internal static string FormatRelativeArtifactPath(string artifactName)
    {
        if (!HostTraceDumpStatus.IsSafeArtifactName(artifactName))
            throw new ArgumentException("A dump artifact basename is required.", nameof(artifactName));
        return AutomataTraceRunRoot.FormatRelativePath("recent/" + artifactName);
    }

    private static ulong NextIdentity() => checked((ulong)Interlocked.Increment(ref _nextIdentity));
}
