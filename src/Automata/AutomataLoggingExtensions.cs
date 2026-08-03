using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using BepInEx.Logging;

namespace OrbAutomata;

internal static class AutomataLoggingExtensions
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz";
    private static readonly TimeSpan RepeatHeartbeat = TimeSpan.FromMinutes(1);
    private static readonly ConditionalWeakTable<ManualLogSource, AutomataRepeatCollapser> RepeatCollapsers =
        new();

    public static void LogAutomataInfo(this ManualLogSource log, object data) =>
        Write(log, AutomataLogSeverity.Info, data);

    public static void LogAutomataWarning(this ManualLogSource log, object data) =>
        Write(log, AutomataLogSeverity.Warning, data);

    public static void LogAutomataError(this ManualLogSource log, object data) =>
        Write(log, AutomataLogSeverity.Error, data);

    internal static string WithTimestamp(object data, DateTimeOffset timestamp) =>
        $"[{timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture)}] {data}";

    internal static void Flush(ManualLogSource log)
    {
        if (log is null) throw new ArgumentNullException(nameof(log));
        if (RepeatCollapsers.TryGetValue(log, out var collapser)) collapser.FlushAll();
    }

    private static void Write(ManualLogSource log, AutomataLogSeverity severity, object data)
    {
        var message = Convert.ToString(data, CultureInfo.CurrentCulture) ?? string.Empty;
        RepeatCollapsers.GetValue(log, CreateCollapser).Write(severity, message, DateTimeOffset.Now);
    }

    private static AutomataRepeatCollapser CreateCollapser(ManualLogSource log) =>
        new(RepeatHeartbeat, (severity, message, timestamp) => Emit(log, severity, message, timestamp));

    private static void Emit(
        ManualLogSource log,
        AutomataLogSeverity severity,
        string message,
        DateTimeOffset timestamp)
    {
        var timestamped = WithTimestamp(message, timestamp);
        switch (severity)
        {
            case AutomataLogSeverity.Info:
                log.LogInfo(timestamped);
                break;
            case AutomataLogSeverity.Warning:
                log.LogWarning(timestamped);
                break;
            case AutomataLogSeverity.Error:
                log.LogError(timestamped);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(severity), severity, null);
        }
    }
}
