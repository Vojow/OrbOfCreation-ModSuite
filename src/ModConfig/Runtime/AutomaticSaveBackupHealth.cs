using System;
using System.Globalization;
using OrbModding.Common;
using OrbModding.Common.Runtime;

namespace OrbModConfig;

internal static class AutomaticSaveBackupWording
{
    internal static string ReleaseStartSummary(AutomaticSaveBackupStatus status)
    {
        if (status is null) throw new ArgumentNullException(nameof(status));
        if (!status.AllowsAutomation) return FailedStartSummary(status);
        if (status.HasRetentionFailure) return RetentionStartSummary(status);
        return "Saves are backed up automatically on startup.";
    }

    internal static string PerformanceStartSummary(AutomaticSaveBackupStatus status)
    {
        if (status is null) throw new ArgumentNullException(nameof(status));
        if (!status.AllowsAutomation) return FailedStartSummary(status);
        if (status.HasRetentionFailure) return RetentionStartSummary(status);
        return "Save backup " +
               (status.BackupCreated ? "created" : "ready") +
               " · " +
               FileCount(status.FileCount);
    }

    internal static string BlockingReason(AutomaticSaveBackupStatus status) =>
        "Automatic save backup failed; automation remains blocked until the next launch succeeds: " +
        NormalizeFailure(status.FailureReason);

    internal static string RetentionReason(AutomaticSaveBackupStatus status) =>
        "Automatic save backup is ready at " +
        status.BackupPath +
        ", but retention pruning failed: " +
        string.Join(" ", status.RetentionFailures);

    internal static string Implementation(AutomaticSaveBackupStatus status) =>
        status.AllowsAutomation
            ? FileCount(status.FileCount) + " verified at " + status.BackupPath
            : "Fail-closed startup gate before runtime composition";

    private static string FileCount(int count) =>
        count.ToString("N0", CultureInfo.InvariantCulture) +
        (count == 1 ? " file" : " files");

    private static string FailedStartSummary(AutomaticSaveBackupStatus status) =>
        "Save backup failed · automation blocked · " +
        NormalizeFailure(status.FailureReason);

    private static string RetentionStartSummary(AutomaticSaveBackupStatus status) =>
        "Save backup " +
        (status.BackupCreated ? "created" : "ready") +
        " · " +
        FileCount(status.FileCount) +
        " · " +
        status.BackupPath +
        " · retention warning";

    private static string NormalizeFailure(string failure)
    {
        var normalized = (failure ?? string.Empty).Trim();
        return normalized.Length == 0
            ? "the backup failed without a filesystem reason"
            : normalized;
    }
}

/// <summary>
/// Publishes the immutable startup-backup result onto both Runtime health layers. Failure is a
/// suite-wide unavailable contract, while retention trouble is degraded evidence only: the new
/// verified backup already exists and automation remains admitted.
/// </summary>
internal sealed class AutomaticSaveBackupHealth : IDisposable
{
    internal const string FeatureId = "AutomaticSaveBackup";
    private readonly FeatureStatusRegistration _feature;
    private readonly RuntimeDiagnosticsRegistration _runtime;

    internal AutomaticSaveBackupHealth(
        AutomaticSaveBackupStatus status,
        FeatureStatusRegistry featureStatuses,
        RuntimeDiagnosticsRegistry runtimeDiagnostics,
        long lifecycleGeneration)
    {
        if (status is null) throw new ArgumentNullException(nameof(status));
        if (featureStatuses is null) throw new ArgumentNullException(nameof(featureStatuses));
        if (runtimeDiagnostics is null) throw new ArgumentNullException(nameof(runtimeDiagnostics));
        var state = State(status);
        var reason = Reason(status);
        var key = new FeatureStatusKey(PluginIds.SuiteGuid, FeatureId);
        _feature = featureStatuses.Register(new FeatureStatusSnapshot(
            key,
            "Automatic save backup",
            configuredEnabled: true,
            state,
            reason,
            lifecycleGeneration));
        _runtime = runtimeDiagnostics.Register(new RuntimeServiceDiagnosticsSnapshot(
            key,
            "Automatic save backup",
            AutomaticSaveBackupWording.Implementation(status),
            lifecycleGeneration,
            new[]
            {
                new RuntimeCapabilityDiagnostics(
                    "VerifiedCopy",
                    "Pre-update save copy",
                    configuredEnabled: true,
                    state,
                    reason),
            }));
    }

    public void Dispose()
    {
        _runtime.Dispose();
        _feature.Dispose();
    }

    private static FeatureStatusState State(AutomaticSaveBackupStatus status) =>
        !status.AllowsAutomation
            ? FeatureStatusState.ContractUnavailable
            : status.HasRetentionFailure
                ? FeatureStatusState.Degraded
                : FeatureStatusState.Operational;

    private static FeatureStatusReason Reason(AutomaticSaveBackupStatus status) =>
        !status.AllowsAutomation
            ? new FeatureStatusReason(
                FeatureStatusReasonCode.ContractUnavailable,
                AutomaticSaveBackupWording.BlockingReason(status))
            : status.HasRetentionFailure
                ? new FeatureStatusReason(
                    FeatureStatusReasonCode.PartialCapabilityUnavailable,
                    AutomaticSaveBackupWording.RetentionReason(status))
                : default;
}
