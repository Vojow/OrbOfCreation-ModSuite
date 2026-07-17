using System;
using System.Globalization;
using BepInEx.Logging;

namespace OrbAutomata;

internal static class AutomataLoggingExtensions
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz";

    public static void LogAutomataInfo(this ManualLogSource log, object data) =>
        log.LogInfo(WithTimestamp(data, DateTimeOffset.Now));

    public static void LogAutomataWarning(this ManualLogSource log, object data) =>
        log.LogWarning(WithTimestamp(data, DateTimeOffset.Now));

    public static void LogAutomataError(this ManualLogSource log, object data) =>
        log.LogError(WithTimestamp(data, DateTimeOffset.Now));

    internal static string WithTimestamp(object data, DateTimeOffset timestamp) =>
        $"[{timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture)}] {data}";
}
