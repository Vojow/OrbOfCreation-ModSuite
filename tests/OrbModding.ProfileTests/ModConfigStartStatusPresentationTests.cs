using OrbModConfig;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ModConfigStartStatusPresentationTests
{
    [Fact]
    public void PerformanceDebugBuildRetainsTheFullStatusCard()
    {
        var presentation = ModConfigStartStatusPresenter.Build(
            "0.5.0-beta.1",
            controlPlaneReady: true,
            auditedBuild: true,
            runtimeActivationAllowed: true,
            gameMcpServerReady: true,
            processId: 4242);

        Assert.Equal(
            new[]
            {
                "Orb ModSuite  ·  v0.5.0-beta.1",
                "Performance-debug build",
                "MCP ready  ·  Audited game verified",
                "Agent: 127.0.0.1:19106/mcp",
                "PID 4242  ·  Localhost only",
            },
            presentation.Rows);
        Assert.Equal(ModConfigStartStatusTone.Ready, presentation.Tone);
        Assert.Equal(5, presentation.Rows.Count);
    }
}
