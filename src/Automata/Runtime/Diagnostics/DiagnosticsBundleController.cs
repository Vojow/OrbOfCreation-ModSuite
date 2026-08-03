using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BepInEx.Logging;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbAutomata;

internal sealed class DiagnosticsBundleControllerOptions
{
    internal DiagnosticsBundleControllerOptions(
        string outputDirectory,
        string configurationPath,
        string saveRoot,
        string logPath,
        string suiteVersion,
        string gameBuildIdentity,
        Func<IReadOnlyList<FeatureStatusSnapshot>> features,
        Func<IReadOnlyList<RuntimeServiceDiagnosticsSnapshot>> health,
        Func<AutomataDiagnosticsRuntimeEvidence> captureRuntime,
        Func<DecisionJournalStatus> journalStatus,
        Action flushLogs,
        DiagnosticsTextRedactor redactor,
        Func<DateTime>? utcNow = null)
    {
        OutputDirectory = outputDirectory;
        ConfigurationPath = configurationPath;
        SaveRoot = saveRoot;
        LogPath = logPath;
        SuiteVersion = suiteVersion;
        GameBuildIdentity = gameBuildIdentity;
        Features = features ?? throw new ArgumentNullException(nameof(features));
        Health = health ?? throw new ArgumentNullException(nameof(health));
        CaptureRuntime = captureRuntime ?? throw new ArgumentNullException(nameof(captureRuntime));
        JournalStatus = journalStatus ?? throw new ArgumentNullException(nameof(journalStatus));
        FlushLogs = flushLogs ?? throw new ArgumentNullException(nameof(flushLogs));
        Redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        UtcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    internal string OutputDirectory { get; }
    internal string ConfigurationPath { get; }
    internal string SaveRoot { get; }
    internal string LogPath { get; }
    internal string SuiteVersion { get; }
    internal string GameBuildIdentity { get; }
    internal Func<IReadOnlyList<FeatureStatusSnapshot>> Features { get; }
    internal Func<IReadOnlyList<RuntimeServiceDiagnosticsSnapshot>> Health { get; }
    internal Func<AutomataDiagnosticsRuntimeEvidence> CaptureRuntime { get; }
    internal Func<DecisionJournalStatus> JournalStatus { get; }
    internal Action FlushLogs { get; }
    internal DiagnosticsTextRedactor Redactor { get; }
    internal Func<DateTime> UtcNow { get; }
}

internal interface IDiagnosticsBundleRevealer
{
    bool TryReveal(string path);
}

internal sealed class PlatformDiagnosticsBundleRevealer : IDiagnosticsBundleRevealer
{
    internal static PlatformDiagnosticsBundleRevealer Instance { get; } = new();

    private PlatformDiagnosticsBundleRevealer() { }

    public bool TryReveal(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            ProcessStartInfo start;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                start = Start("open", "-R", path);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                start = Start("explorer.exe", "/select," + path);
            }
            else
            {
                var directory = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(directory)) return false;
                start = Start("xdg-open", directory);
            }
            return Process.Start(start) is not null;
        }
        catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
        {
            return false;
        }
    }

    private static ProcessStartInfo Start(string fileName, params string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        for (var index = 0; index < arguments.Length; index++)
            start.ArgumentList.Add(arguments[index]);
        return start;
    }
}

internal sealed class DiagnosticsBundleController : IDisposable
{
    private static readonly TimeSpan JournalFlushWedgeGuard = TimeSpan.FromSeconds(5);
    private readonly DiagnosticsBundleControllerOptions _options;
    private readonly DiagnosticsBundleRegistration _control;
    private readonly IDiagnosticsBundleRevealer _revealer;
    private readonly ManualLogSource _log;
    private PendingBundle? _pending;
    private Task<DiagnosticsBundleBuildResult>? _build;
    private bool _disposed;

    private DiagnosticsBundleController(
        DiagnosticsBundleControllerOptions options,
        DiagnosticsBundleRegistration control,
        IDiagnosticsBundleRevealer revealer,
        ManualLogSource log)
    {
        _options = options;
        _control = control;
        _revealer = revealer;
        _log = log;
    }

    internal static DiagnosticsBundleController? TryCreate(
        DiagnosticsBundleRegistry registry,
        DiagnosticsBundleControllerOptions options,
        ManualLogSource log,
        IDiagnosticsBundleRevealer? revealer = null)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (log is null) throw new ArgumentNullException(nameof(log));
        if (!registry.TryRegister(out var control) || control is null) return null;
        return new DiagnosticsBundleController(
            options,
            control,
            revealer ?? PlatformDiagnosticsBundleRevealer.Instance,
            log);
    }

    internal void Tick()
    {
        if (_disposed) return;
        if (_build is not null)
        {
            if (!_build.IsCompleted) return;
            CompleteBuild();
            return;
        }
        if (_pending is not null)
        {
            try
            {
                ContinueAfterJournalFlush();
            }
            catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
            {
                Fail(exception);
            }
            return;
        }
        if (!_control.TryTakeRequest()) return;
        try
        {
            _options.FlushLogs();
            _pending = new PendingBundle(
                _options.UtcNow(),
                _options.Features(),
                _options.Health(),
                _options.CaptureRuntime(),
                Stopwatch.GetTimestamp());
            ContinueAfterJournalFlush();
        }
        catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
        {
            Fail(exception);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _control.Dispose();
    }

    private void ContinueAfterJournalFlush()
    {
        var pending = _pending ?? throw new InvalidOperationException("No diagnostics bundle is pending.");
        var journal = _options.JournalStatus();
        var elapsedTicks = Stopwatch.GetTimestamp() - pending.FlushStarted;
        var timedOut = elapsedTicks >= JournalFlushWedgeGuard.TotalSeconds * Stopwatch.Frequency;
        if (journal.PendingBlocks != 0 &&
            journal.State is DecisionJournalStatusState.Arming or DecisionJournalStatusState.Recording &&
            !timedOut)
        {
            return;
        }

        var request = new DiagnosticsBundleBuildRequest(
            pending.UtcNow,
            _options.OutputDirectory,
            _options.ConfigurationPath,
            _options.SaveRoot,
            _options.LogPath,
            _options.SuiteVersion,
            _options.GameBuildIdentity,
            pending.Features,
            pending.Health,
            pending.RuntimeEvidence.WithJournal(journal),
            _options.Redactor);
        _pending = null;
        _build = Task.Run(() => DiagnosticsBundleBuilder.Build(request));
    }

    private void CompleteBuild()
    {
        var build = _build ?? throw new InvalidOperationException("No diagnostics build is running.");
        _build = null;
        try
        {
            var result = build.GetAwaiter().GetResult();
            var revealed = TryReveal(result.Path);
            _control.Publish(new DiagnosticsBundleStatus(
                revealed ? DiagnosticsBundleState.Written : DiagnosticsBundleState.WrittenRevealUnavailable,
                result.Path,
                result.BytesWritten));
            _log.LogAutomataInfo(
                "Diagnostics bundle created: " + result.Path + " (" + result.BytesWritten + " bytes)." +
                (revealed ? string.Empty : " The platform file manager could not reveal it."));
        }
        catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
        {
            Fail(exception);
        }
    }

    private void Fail(Exception exception)
    {
        _pending = null;
        _build = null;
        var reason = exception.GetBaseException().Message?.Trim();
        if (string.IsNullOrWhiteSpace(reason)) reason = exception.GetType().Name;
        reason = _options.Redactor.Redact(reason);
        _control.Publish(new DiagnosticsBundleStatus(
            DiagnosticsBundleState.Failed,
            string.Empty,
            0,
            reason));
        _log.LogAutomataError(
            "Diagnostics bundle could not be created; no bug-report file was written: " + reason);
    }

    private bool TryReveal(string path)
    {
        try { return _revealer.TryReveal(path); }
        catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
        {
            return false;
        }
    }

    private sealed class PendingBundle
    {
        internal PendingBundle(
            DateTime utcNow,
            IReadOnlyList<FeatureStatusSnapshot> features,
            IReadOnlyList<RuntimeServiceDiagnosticsSnapshot> health,
            AutomataDiagnosticsRuntimeEvidence runtimeEvidence,
            long flushStarted)
        {
            UtcNow = utcNow;
            Features = features;
            Health = health;
            RuntimeEvidence = runtimeEvidence;
            FlushStarted = flushStarted;
        }

        internal DateTime UtcNow { get; }
        internal IReadOnlyList<FeatureStatusSnapshot> Features { get; }
        internal IReadOnlyList<RuntimeServiceDiagnosticsSnapshot> Health { get; }
        internal AutomataDiagnosticsRuntimeEvidence RuntimeEvidence { get; }
        internal long FlushStarted { get; }
    }
}
