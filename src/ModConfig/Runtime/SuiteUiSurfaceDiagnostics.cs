using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;

namespace OrbModConfig;

internal enum SuiteUiSurface
{
    QuickControls = 0,
    ModsRail = 1,
}

/// <summary>
/// Publishes the two audited native UI surfaces into Runtime diagnostics and makes every terminal
/// capture outcome visible in the BepInEx log.
/// </summary>
internal sealed class SuiteUiSurfaceDiagnostics : IDisposable
{
    private const string FeatureId = "SuiteUi";
    private readonly Action<string> _logInfo;
    private readonly Action<string> _logError;
    private readonly RuntimeDiagnosticsRegistration _registration;
    private SurfaceState _quickControls;
    private SurfaceState _modsRail;

    internal SuiteUiSurfaceDiagnostics(
        RuntimeDiagnosticsRegistry registry,
        Action<string> logInfo,
        Action<string> logError)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        _logInfo = logInfo ?? throw new ArgumentNullException(nameof(logInfo));
        _logError = logError ?? throw new ArgumentNullException(nameof(logError));
        _quickControls = SurfaceState.Waiting(
            "Native quick-controls shell and drawer capture has not run yet.");
        _modsRail = SurfaceState.Waiting("Native Mods-rail capture has not run yet.");
        _registration = registry.Register(BuildSnapshot());
    }

    internal void ResetForScene()
    {
        _quickControls = SurfaceState.Waiting("Waiting for the native top-left HelpButtons anchor.");
        _modsRail = SurfaceState.Waiting("Waiting for the native Mods navigation host.");
        Publish();
    }

    internal void ReportWaiting(SuiteUiSurface surface, string reason)
    {
        Set(surface, SurfaceState.Waiting(RequireReason(reason)));
        Publish();
    }

    internal void ReportSuccess(SuiteUiSurface surface)
    {
        ref var state = ref State(surface);
        if (state.Status == FeatureStatusState.Operational) return;
        state = SurfaceState.Operational;
        _logInfo(surface switch
        {
            SuiteUiSurface.QuickControls =>
                "Quick controls: native state frames and icons active",
            _ => "Mods rail: native visuals active",
        });
        Publish();
    }

    internal void ReportFailure(SuiteUiSurface surface, string reason)
    {
        var exactReason = RequireReason(reason);
        ref var state = ref State(surface);
        if (state.Status == FeatureStatusState.Faulted &&
            string.Equals(state.Reason, exactReason, StringComparison.Ordinal))
            return;
        state = SurfaceState.Failed(exactReason);
        _logError(surface switch
        {
            SuiteUiSurface.QuickControls =>
                "Quick controls: native state frames or icons failed: " + exactReason,
            _ => "Mods rail: native visuals failed: " + exactReason,
        });
        Publish();
    }

    public void Dispose() => _registration.Dispose();

    private ref SurfaceState State(SuiteUiSurface surface)
    {
        if (surface == SuiteUiSurface.QuickControls) return ref _quickControls;
        return ref _modsRail;
    }

    private void Set(SuiteUiSurface surface, SurfaceState state)
    {
        if (surface == SuiteUiSurface.QuickControls) _quickControls = state;
        else _modsRail = state;
    }

    private void Publish() => _registration.Update(BuildSnapshot());

    private RuntimeServiceDiagnosticsSnapshot BuildSnapshot() => new(
        new FeatureStatusKey(PluginIds.SuiteGuid, FeatureId),
        "Suite UI",
        "Audited native visual capture",
        GameLifecycleMonitor.Shared.Current.Generation,
        new[]
        {
            Capability(
                "QuickControls",
                "Quick controls shell and drawer native visuals",
                _quickControls),
            Capability("ModsRail", "Mods rail native visuals", _modsRail),
        });

    private static RuntimeCapabilityDiagnostics Capability(
        string id,
        string name,
        in SurfaceState state) =>
        state.Status == FeatureStatusState.Operational
            ? new RuntimeCapabilityDiagnostics(
                id,
                name,
                configuredEnabled: true,
                FeatureStatusState.Operational)
            : new RuntimeCapabilityDiagnostics(
                id,
                name,
                configuredEnabled: true,
                state.Status,
                new FeatureStatusReason(
                    state.Status == FeatureStatusState.Faulted
                        ? FeatureStatusReasonCode.RuntimeFailure
                        : FeatureStatusReasonCode.GameplayNotReady,
                    state.Reason));

    private static string RequireReason(string reason)
    {
        var normalized = (reason ?? string.Empty).Trim();
        return normalized.Length == 0 ? "Native capture failed without a reason." : normalized;
    }

    private readonly struct SurfaceState
    {
        private SurfaceState(FeatureStatusState status, string reason)
        {
            Status = status;
            Reason = reason;
        }

        internal FeatureStatusState Status { get; }
        internal string Reason { get; }

        internal static SurfaceState Operational { get; } =
            new(FeatureStatusState.Operational, string.Empty);

        internal static SurfaceState Waiting(string reason) =>
            new(FeatureStatusState.NotReady, reason);

        internal static SurfaceState Failed(string reason) =>
            new(FeatureStatusState.Faulted, reason);
    }
}
