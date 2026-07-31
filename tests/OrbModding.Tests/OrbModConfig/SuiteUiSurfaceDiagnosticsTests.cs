using System.Collections.Generic;
using OrbModConfig;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class SuiteUiSurfaceDiagnosticsTests
{
    [Fact]
    public void BothUiSurfacesShareRetryThenTerminalFailureDiscipline()
    {
        var retry = new UiInstallationRetryState();

        var first = retry.ObserveFailure();
        var second = retry.ObserveFailure();
        var third = retry.ObserveFailure();

        Assert.Equal(1, first.Attempt);
        Assert.True(first.ShouldLogRetry);
        Assert.False(first.IsTerminal);
        Assert.Equal(2, second.Attempt);
        Assert.False(second.ShouldLogRetry);
        Assert.False(second.IsTerminal);
        Assert.Equal(UiInstallationRetryState.TerminalAttempt, third.Attempt);
        Assert.False(third.ShouldLogRetry);
        Assert.True(third.IsTerminal);

        retry.Reset();
        Assert.True(retry.ObserveFailure().ShouldLogRetry);
    }

    [Theory]
    [InlineData(0, "Quick controls: native state frames or icons failed: ")]
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
        var capability = surface == SuiteUiSurface.QuickControls
            ? snapshot.Capabilities[0]
            : snapshot.Capabilities[1];
        Assert.Equal(FeatureStatusState.Faulted, capability.State);
        Assert.Equal(FeatureStatusReasonCode.RuntimeFailure, capability.Reason.Code);
        Assert.Equal("audited sprite mismatch", capability.Reason.Summary);
    }

    [Theory]
    [InlineData(0, "Quick controls: native state frames and icons active")]
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
