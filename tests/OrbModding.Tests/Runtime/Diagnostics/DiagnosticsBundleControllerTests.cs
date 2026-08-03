using System;
using System.IO;
using System.Threading;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using Xunit;

namespace OrbModding.Tests.Runtime.Diagnostics;

public sealed class DiagnosticsBundleControllerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "orb-diagnostics-controller-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RevealFailureKeepsTheBundleAndPublishesItsFullPath()
    {
        var paths = Fixture();
        var registry = new DiagnosticsBundleRegistry();
        using var controller = Assert.IsType<DiagnosticsBundleController>(
            DiagnosticsBundleController.TryCreate(
                registry,
                Options(paths.Output, paths),
                new ManualLogSource(),
                new RejectingRevealer()));

        Assert.Equal(DiagnosticsBundleRequestResult.Accepted, registry.RequestBundle());
        Assert.True(SpinWait.SpinUntil(() =>
        {
            controller.Tick();
            return registry.Status.State == DiagnosticsBundleState.WrittenRevealUnavailable;
        }, TimeSpan.FromSeconds(10)));

        Assert.True(Path.IsPathFullyQualified(registry.Status.Path));
        Assert.True(File.Exists(registry.Status.Path));
        Assert.InRange(registry.Status.BytesWritten, 1, DiagnosticsBundleBuilder.MaximumBundleBytes);
    }

    [Fact]
    public void BuildFailurePublishesThatNoShareableFileExists()
    {
        var paths = Fixture();
        var outputCollision = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(outputCollision, "collision");
        var registry = new DiagnosticsBundleRegistry();
        using var controller = Assert.IsType<DiagnosticsBundleController>(
            DiagnosticsBundleController.TryCreate(
                registry,
                Options(outputCollision, paths),
                new ManualLogSource(),
                new RejectingRevealer()));

        Assert.Equal(DiagnosticsBundleRequestResult.Accepted, registry.RequestBundle());
        Assert.True(SpinWait.SpinUntil(() =>
        {
            controller.Tick();
            return registry.Status.State == DiagnosticsBundleState.Failed;
        }, TimeSpan.FromSeconds(10)));

        Assert.Empty(registry.Status.Path);
        Assert.Equal(0, registry.Status.BytesWritten);
        Assert.NotEmpty(registry.Status.FailureReason);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private FixturePaths Fixture()
    {
        Directory.CreateDirectory(_root);
        var paths = new FixturePaths(
            Path.Combine(_root, "out"),
            Path.Combine(_root, "suite.cfg"),
            Path.Combine(_root, "save"),
            Path.Combine(_root, "LogOutput.log"));
        Directory.CreateDirectory(paths.Save);
        File.WriteAllText(paths.Config, "Enabled=true\n");
        File.WriteAllText(paths.Log, "ready\n");
        File.WriteAllBytes(Path.Combine(paths.Save, "fixture.sav"), new byte[] { 1, 2, 3, 4 });
        return paths;
    }

    private static DiagnosticsBundleControllerOptions Options(
        string output,
        FixturePaths paths) => new(
        output,
        paths.Config,
        paths.Save,
        paths.Log,
        "1.2.3",
        "audited fixture",
        () => Array.Empty<FeatureStatusSnapshot>(),
        () => Array.Empty<RuntimeServiceDiagnosticsSnapshot>(),
        () => AutomataDiagnosticsRuntimeEvidence.Unavailable("fixture runtime unavailable"),
        () => DecisionJournalStatus.Unavailable,
        () => { },
        new DiagnosticsTextRedactor(),
        () => new DateTime(2026, 8, 3, 12, 34, 56, DateTimeKind.Utc));

    private sealed class RejectingRevealer : IDiagnosticsBundleRevealer
    {
        public bool TryReveal(string path) => false;
    }

    private readonly record struct FixturePaths(
        string Output,
        string Config,
        string Save,
        string Log);
}
