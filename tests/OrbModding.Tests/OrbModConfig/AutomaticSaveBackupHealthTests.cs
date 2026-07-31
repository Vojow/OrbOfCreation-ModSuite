using System;
using OrbModConfig;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class AutomaticSaveBackupHealthTests
{
    [Fact]
    public void SuccessfulBackupPublishesItsVerifiedCountAndPath()
    {
        var featureStatuses = new FeatureStatusRegistry();
        var runtimeDiagnostics = new RuntimeDiagnosticsRegistry();
        var status = AutomaticSaveBackupStatus.Ready(
            backupCreated: true,
            trigger: AutomaticSaveBackupTrigger.VersionChanged,
            backupPath: "/save/backups/auto-modsuite-backup-20260731T101112Z",
            fileCount: 3,
            prunedBackupCount: 1,
            retentionFailures: Array.Empty<string>());

        using var health = new AutomaticSaveBackupHealth(
            status,
            featureStatuses,
            runtimeDiagnostics,
            lifecycleGeneration: 7);

        var feature = Assert.Single(featureStatuses.GetSnapshot());
        Assert.Equal(FeatureStatusState.Operational, feature.State);
        Assert.Equal("Automatic save backup", feature.DisplayName);
        var runtime = Assert.Single(runtimeDiagnostics.GetSnapshot());
        Assert.Equal(
            "3 files verified at /save/backups/auto-modsuite-backup-20260731T101112Z",
            runtime.Implementation);
        Assert.Equal(FeatureStatusState.Operational, Assert.Single(runtime.Capabilities).State);
    }

    [Fact]
    public void FailedBackupPublishesTheExactSuiteWideBlockingReason()
    {
        var featureStatuses = new FeatureStatusRegistry();
        var runtimeDiagnostics = new RuntimeDiagnosticsRegistry();
        var status = AutomaticSaveBackupStatus.Failed(
            AutomaticSaveBackupTrigger.CorruptStamp,
            "Could not read active save file 'ooc_save_2.sav' cleanly.");

        using var health = new AutomaticSaveBackupHealth(
            status,
            featureStatuses,
            runtimeDiagnostics,
            lifecycleGeneration: 9);

        const string expected =
            "Automatic save backup failed; automation remains blocked until the next launch succeeds: " +
            "Could not read active save file 'ooc_save_2.sav' cleanly.";
        var feature = Assert.Single(featureStatuses.GetSnapshot());
        Assert.Equal(FeatureStatusState.ContractUnavailable, feature.State);
        Assert.Equal(FeatureStatusReasonCode.ContractUnavailable, feature.Reason.Code);
        Assert.Equal(expected, feature.Reason.Summary);
        var capability = Assert.Single(Assert.Single(runtimeDiagnostics.GetSnapshot()).Capabilities);
        Assert.Equal(FeatureStatusState.ContractUnavailable, capability.State);
        Assert.Equal(expected, capability.Reason.Summary);
    }

    [Fact]
    public void RetentionFailurePublishesDegradedHealthWithoutBlockingAutomation()
    {
        var featureStatuses = new FeatureStatusRegistry();
        var runtimeDiagnostics = new RuntimeDiagnosticsRegistry();
        var status = AutomaticSaveBackupStatus.Ready(
            backupCreated: true,
            trigger: AutomaticSaveBackupTrigger.FreshInstall,
            backupPath: "/save/backups/auto-modsuite-backup-20260731T101112Z",
            fileCount: 1,
            prunedBackupCount: 0,
            retentionFailures: new[] { "Could not prune one owned automatic backup." });

        using var health = new AutomaticSaveBackupHealth(
            status,
            featureStatuses,
            runtimeDiagnostics,
            lifecycleGeneration: 1);

        Assert.True(status.AllowsAutomation);
        var feature = Assert.Single(featureStatuses.GetSnapshot());
        Assert.Equal(FeatureStatusState.Degraded, feature.State);
        Assert.Equal(FeatureStatusReasonCode.PartialCapabilityUnavailable, feature.Reason.Code);
        Assert.Contains(status.BackupPath, feature.Reason.Summary, StringComparison.Ordinal);
        Assert.Contains("retention pruning failed", feature.Reason.Summary, StringComparison.Ordinal);
    }
}
