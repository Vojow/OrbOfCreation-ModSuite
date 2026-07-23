using System;
using System.IO;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing;

namespace OrbAutomata;

internal static class AutoHarvestReplayPathPolicy
{
    private const string FilePrefix = "auto-harvest";
    private const string Extension = ".oscr";
    private static long _nextSession = DateTime.UtcNow.Ticks;

    public static AutomataReplayCaptureOptions Create(bool enabled, ManualLogSource log)
    {
        if (!enabled) return default;
        if (log is null) throw new ArgumentNullException(nameof(log));
        var directory = Path.Combine(
            Paths.ConfigPath,
            "OrbOfCreation-ModSuite",
            "replay",
            "auto-harvest");
        var session = new ServiceCycleTraceSessionId(
            checked((ulong)Interlocked.Increment(ref _nextSession)));
        return new AutomataReplayCaptureOptions(
            session,
            () => new FileTraceSegmentStorage(
                directory,
                filePrefix: FilePrefix,
                extension: Extension),
            new AutoHarvestReplayReporter(log));
    }

    internal static string FormatRelativeArtifactPath(int ordinal)
    {
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
        return "BepInEx/config/OrbOfCreation-ModSuite/replay/auto-harvest/" +
            $"{FilePrefix}-{ordinal:D6}{Extension}";
    }
}
