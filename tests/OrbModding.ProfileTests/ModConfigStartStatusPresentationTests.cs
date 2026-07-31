using System;
using OrbModConfig;
using OrbModding.Common;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ModConfigStartStatusPresentationTests
{
    [Fact]
    public void PerformanceDebugBuildRetainsTheFullStatusCard()
    {
        var backup = AutomaticSaveBackupStatus.Ready(
            backupCreated: false,
            trigger: AutomaticSaveBackupTrigger.None,
            backupPath: "/save/backups/auto-modsuite-backup-20260731T101112Z",
            fileCount: 2,
            prunedBackupCount: 0,
            retentionFailures: Array.Empty<string>());
        var presentation = ModConfigStartStatusPresenter.Build(
            "0.5.0-beta.1",
            controlPlaneReady: true,
            auditedBuild: true,
            runtimeActivationAllowed: true,
            saveBackup: backup,
            gameMcpServerReady: true,
            processId: 4242);

        Assert.Equal(
            new[]
            {
                "Orb ModSuite  ·  v0.5.0-beta.1",
                "Performance-debug build",
                "MCP ready  ·  Audited game verified",
                "Save backup ready · 2 files · /save/backups/auto-modsuite-backup-20260731T101112Z",
                "Agent: 127.0.0.1:19106/mcp  ·  PID 4242  ·  Localhost only",
            },
            presentation.Rows);
        Assert.Equal(ModConfigStartStatusTone.Ready, presentation.Tone);
        Assert.Equal(5, presentation.Rows.Count);
    }
}
