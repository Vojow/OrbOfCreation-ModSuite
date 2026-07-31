using System;
using OrbModConfig;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class ModConfigStartStatusPresentationTests
{
    [Fact]
    public void ReleaseBuildContainsIdentityCompatibilityAndVerifiedBackupPath()
    {
        var backup = AutomaticSaveBackupStatus.Ready(
            backupCreated: true,
            trigger: AutomaticSaveBackupTrigger.FreshInstall,
            backupPath: "/save/backups/auto-modsuite-backup-20260731T101112Z",
            fileCount: 2,
            prunedBackupCount: 0,
            retentionFailures: Array.Empty<string>());
        var presentation = ModConfigStartStatusPresenter.Build(
            "0.5.0-beta.1",
            controlPlaneReady: true,
            auditedBuild: true,
            runtimeActivationAllowed: true,
            saveBackup: backup);

        Assert.Equal(
            new[]
            {
                "Orb ModSuite  ·  v0.5.0-beta.1",
                "Release build",
                "Audited game verified",
                "Save backup created · 2 files · /save/backups/auto-modsuite-backup-20260731T101112Z",
            },
            presentation.Rows);
        Assert.Equal(ModConfigStartStatusTone.Ready, presentation.Tone);

        var visibleText = string.Join("\n", presentation.Rows);
        foreach (var forbidden in new[]
                 {
                     "MCP",
                     "agent",
                     "perf-debug",
                     "performance-debug",
                     "PID",
                     "localhost",
                     "trace",
                     "probe",
                 })
        {
            Assert.DoesNotContain(forbidden, visibleText, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(4, presentation.Rows.Count);
        Assert.All(presentation.Rows, row => Assert.False(string.IsNullOrWhiteSpace(row)));
    }

    [Fact]
    public void BackupFailureIsNamedAsTheBlockingStartHealth()
    {
        var backup = AutomaticSaveBackupStatus.Failed(
            AutomaticSaveBackupTrigger.VersionChanged,
            "Could not read active save file 'ooc_save_1.sav' cleanly.");

        var presentation = ModConfigStartStatusPresenter.Build(
            "0.5.0-beta.1",
            controlPlaneReady: true,
            auditedBuild: true,
            runtimeActivationAllowed: false,
            saveBackup: backup);

        Assert.Equal(ModConfigStartStatusTone.Failure, presentation.Tone);
        Assert.Contains("Audited game verified", presentation.Rows);
        Assert.Contains(
            "Save backup failed · automation blocked · Could not read active save file 'ooc_save_1.sav' cleanly.",
            presentation.Rows);
    }
}
