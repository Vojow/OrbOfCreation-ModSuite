using System.Collections.Generic;
using OrbModConfig;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class SuiteUiSurfaceDiagnosticsTests
{
    [Theory]
    [InlineData(0, "Quick strip: native icon visuals failed: ")]
    [InlineData(1, "Mods rail: native visuals failed: ")]
    public void EveryTerminalCaptureFailureLogsTheExactReasonAndPublishesRuntimeFailure(
        int surfaceValue,
        string prefix)
    {
        var surface = (SuiteUiSurface)surfaceValue;
        var registry = new RuntimeDiagnosticsRegistry();
        var errors = new List<string>();
        using var diagnostics = new SuiteUiSurfaceDiagnostics(
            registry,
            _ => { },
            errors.Add);

        diagnostics.ReportFailure(surface, "audited sprite mismatch");

        Assert.Equal(prefix + "audited sprite mismatch", Assert.Single(errors));
        var snapshot = Assert.Single(registry.GetSnapshot());
        var capability = surface == SuiteUiSurface.QuickStrip
            ? snapshot.Capabilities[0]
            : snapshot.Capabilities[1];
        Assert.Equal(FeatureStatusState.Faulted, capability.State);
        Assert.Equal(FeatureStatusReasonCode.RuntimeFailure, capability.Reason.Code);
        Assert.Equal("audited sprite mismatch", capability.Reason.Summary);
    }

    [Theory]
    [InlineData(0, "Quick strip: native icon visuals active")]
    [InlineData(1, "Mods rail: native visuals active")]
    public void SuccessfulInstallSelfReportsOnce(int surfaceValue, string expected)
    {
        var surface = (SuiteUiSurface)surfaceValue;
        var registry = new RuntimeDiagnosticsRegistry();
        var info = new List<string>();
        using var diagnostics = new SuiteUiSurfaceDiagnostics(
            registry,
            info.Add,
            _ => { });

        diagnostics.ReportSuccess(surface);
        diagnostics.ReportSuccess(surface);

        Assert.Equal(expected, Assert.Single(info));
    }
}
