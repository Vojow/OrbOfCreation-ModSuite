using System;
using OrbModConfig;
using OrbModding.Common;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ModConfigStartStatusPresentationTests
{
    [Fact]
    public void PerformanceDebugBuildKeepsCompactBackupStatusAndExactWarnings()
    {
        foreach (var backupCreated in new[] { true, false })
        {
            var backup = AutomaticSaveBackupStatus.Ready(
                backupCreated,
                trigger: backupCreated
                    ? AutomaticSaveBackupTrigger.FreshInstall
                    : AutomaticSaveBackupTrigger.None,
                backupPath: "/save/backups/auto-modsuite-backup-20260731T101112Z",
                fileCount: 2,
                prunedBackupCount: 0,
                retentionFailures: Array.Empty<string>());
            var presentation = ModConfigStartStatusPresenter.Build(
                "0.5.0",
                controlPlaneReady: true,
                auditedBuild: true,
                runtimeActivationAllowed: true,
                saveBackup: backup,
                gameMcpServerReady: true,
                processId: 4242);

            Assert.Equal(
                new[]
                {
                    "Orb ModSuite  ·  v0.5.0",
                    "Performance-debug build",
                    "MCP ready  ·  Audited game verified",
                    "Save backup " + (backupCreated ? "created" : "ready") + " · 2 files",
                    "Agent: 127.0.0.1:19106/mcp  ·  PID 4242  ·  Localhost only",
                },
                presentation.Rows);
            Assert.Equal(ModConfigStartStatusTone.Ready, presentation.Tone);
            Assert.Equal(5, presentation.Rows.Count);
            Assert.DoesNotContain(
                backup.BackupPath,
                string.Join("\n", presentation.Rows),
                StringComparison.Ordinal);
        }

        var failure = AutomaticSaveBackupStatus.Failed(
            AutomaticSaveBackupTrigger.VersionChanged,
            "Could not read active save file 'ooc_save_1.sav' cleanly.");
        var failurePresentation = ModConfigStartStatusPresenter.Build(
            "0.5.0",
            controlPlaneReady: true,
            auditedBuild: true,
            runtimeActivationAllowed: false,
            saveBackup: failure,
            gameMcpServerReady: true,
            processId: 4242);
        Assert.Contains(
            "Save backup failed · automation blocked · Could not read active save file 'ooc_save_1.sav' cleanly.",
            failurePresentation.Rows);
        Assert.Equal(ModConfigStartStatusTone.Failure, failurePresentation.Tone);

        var retention = AutomaticSaveBackupStatus.Ready(
            backupCreated: true,
            trigger: AutomaticSaveBackupTrigger.FreshInstall,
            backupPath: "/save/backups/auto-modsuite-backup-20260731T101112Z",
            fileCount: 2,
            prunedBackupCount: 0,
            retentionFailures: new[] { "Could not prune one owned automatic backup." });
        var retentionPresentation = ModConfigStartStatusPresenter.Build(
            "0.5.0",
            controlPlaneReady: true,
            auditedBuild: true,
            runtimeActivationAllowed: true,
            saveBackup: retention,
            gameMcpServerReady: true,
            processId: 4242);
        Assert.Contains(
            "Save backup created · 2 files · /save/backups/auto-modsuite-backup-20260731T101112Z · retention warning",
            retentionPresentation.Rows);
        Assert.Equal(ModConfigStartStatusTone.Attention, retentionPresentation.Tone);
    }
}
