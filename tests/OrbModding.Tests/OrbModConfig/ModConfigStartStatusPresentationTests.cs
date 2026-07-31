using System;
using OrbModConfig;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class ModConfigStartStatusPresentationTests
{
    [Fact]
    public void ReleaseBuildContainsOnlyIdentityModeAndCompatibility()
    {
        var presentation = ModConfigStartStatusPresenter.Build(
            "0.5.0-beta.1",
            controlPlaneReady: true,
            auditedBuild: true,
            runtimeActivationAllowed: true);

        Assert.Equal(
            new[]
            {
                "Orb ModSuite  ·  v0.5.0-beta.1",
                "Release build",
                "Audited game verified",
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
        Assert.Equal(3, presentation.Rows.Count);
        Assert.All(presentation.Rows, row => Assert.False(string.IsNullOrWhiteSpace(row)));
    }
}
