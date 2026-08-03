using System;
using System.Globalization;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing;

namespace OrbAutomata;

internal sealed class AutomataFullTracePathPolicy : IAutomataFullTraceSessionSource
{
    private static long _nextIdentity = DateTime.UtcNow.Ticks;
    private readonly string _rootDirectory;

    private AutomataFullTracePathPolicy(string rootDirectory) => _rootDirectory = rootDirectory;

    internal static AutomataFullTraceOptions CreateOptions()
    {
        var root = AutomataTraceRunRoot.Child("full");
        return new AutomataFullTraceOptions(new AutomataFullTracePathPolicy(root));
    }

    public AutomataFullTraceSessionSpec Create()
    {
        var session = new FullTraceSessionId(NextIdentity());
        var semanticSession = new ServiceCycleTraceSessionId(NextIdentity());
        var artifactName = "session-" + session.Value.ToString("x16", CultureInfo.InvariantCulture);
        return new AutomataFullTraceSessionSpec(
            session,
            semanticSession,
            new AtomicSegmentSessionStorage(
                _rootDirectory,
                artifactName,
                ".oscs",
                "manifest.oscm"),
            artifactName);
    }

    internal static string FormatRelativeArtifactPath(string artifactName)
    {
        if (!AutomataFullTraceSessionSpec.IsSafeArtifactName(artifactName))
            throw new ArgumentException("A full-trace artifact basename is required.", nameof(artifactName));
        return AutomataTraceRunRoot.FormatRelativePath("full/" + artifactName);
    }

    private static ulong NextIdentity() =>
        checked((ulong)Interlocked.Increment(ref _nextIdentity));
}
