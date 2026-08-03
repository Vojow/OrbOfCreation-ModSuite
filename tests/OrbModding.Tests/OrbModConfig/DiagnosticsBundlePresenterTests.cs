using System;
using OrbModConfig;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class DiagnosticsBundlePresenterTests
{
    [Fact]
    public void ReadyMeansCaptureThePastInOneFile()
    {
        var presentation = DiagnosticsBundlePresenter.Build(
            DiagnosticsBundleStatus.Ready,
            bundleRequested: false);

        Assert.True(presentation.ButtonEnabled);
        Assert.Equal("Create bug report", presentation.ButtonLabel);
        Assert.Contains("captures what already happened", presentation.Body, StringComparison.Ordinal);
        Assert.Contains("does not start a recording", presentation.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingRequestDisablesTheOnlyBundleAction()
    {
        var presentation = DiagnosticsBundlePresenter.Build(
            DiagnosticsBundleStatus.Ready,
            bundleRequested: true);

        Assert.False(presentation.ButtonEnabled);
        Assert.Equal("Creating file...", presentation.ButtonLabel);
    }

    [Fact]
    public void RevealFailureShowsTheFullPath()
    {
        const string path = "/fixtures/orb-modsuite-diagnostics-20260803-123456Z.zip";
        var presentation = DiagnosticsBundlePresenter.Build(
            new DiagnosticsBundleStatus(
                DiagnosticsBundleState.WrittenRevealUnavailable,
                path,
                1024),
            bundleRequested: false);

        Assert.Contains("full file path", presentation.Body, StringComparison.Ordinal);
        Assert.Contains(path, presentation.Body, StringComparison.Ordinal);
        Assert.Equal("Create another", presentation.ButtonLabel);
    }

    [Fact]
    public void BuildFailureSaysThereIsNothingToShare()
    {
        var presentation = DiagnosticsBundlePresenter.Build(
            new DiagnosticsBundleStatus(
                DiagnosticsBundleState.Failed,
                string.Empty,
                0,
                "fixture refused the write"),
            bundleRequested: false);

        Assert.Contains("No bug report file was created", presentation.Body, StringComparison.Ordinal);
        Assert.Contains("fixture refused the write", presentation.Body, StringComparison.Ordinal);
    }
}
