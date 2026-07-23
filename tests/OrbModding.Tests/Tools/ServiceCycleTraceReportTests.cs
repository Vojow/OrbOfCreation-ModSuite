using System.IO;
using OrbModding.ServiceCycleTrace;
using OrbModding.Tests.Runtime.ServiceCycle.Replay.Execution;
using Xunit;

namespace OrbModding.Tests.Tools;

public sealed class ServiceCycleTraceReportTests
{
    [Fact]
    public void CanonicalArtifactRendersHumanReadableTimingAndCausalEvents()
    {
        var artifact = ServiceCycleReplayProductionScenarioFixture.Capture(1).Artifact;

        var report = ServiceCycleTraceReport.Render("/machine-specific/sample.oscr", artifact);

        Assert.Contains("# ServiceCycle trace report", report);
        Assert.Contains("- Eligibility: Complete", report);
        Assert.Contains("- Completeness: Complete", report);
        Assert.Contains("These timings have different scopes and are not additive.", report);
        Assert.Contains("### Unity main thread", report);
        Assert.Contains("| Work | Samples | Total ms | Average ms | Max ms |", report);
        Assert.Contains("| Main-thread pump | 3 |", report);
        Assert.Contains("Pump phase rows are contained within the main-thread pump row.", report);
        Assert.Contains("### Per-operation main-thread samples", report);
        Assert.Contains("| Capture attempt | 1 |", report);
        Assert.Contains("| Action attempt terminal | 1 |", report);
        Assert.Contains("the same capture and action time grouped by operation instead of by pump", report);
        Assert.Contains("### Worker and elapsed time", report);
        Assert.Contains("| Worker processing through state projection | 1 |", report);
        Assert.Contains(
            "Worker-processing time starts after request dequeue and includes state preparation, evaluation, " +
            "state projection, detached replay record construction, and enabled recording work. It excludes " +
            "response construction and handoff publication.",
            report);
        Assert.Contains("| Replay record encode/retain subset | 1 |", report);
        Assert.Contains(
            "Replay record encode/retain is a contained subset of worker-processing time; it excludes detached " +
            "record construction and the cycle-footer append.",
            report);
        Assert.Contains("| Capture-to-batch terminal elapsed | 1 |", report);
        Assert.Contains("- Committed: 1", report);
        Assert.Contains("- Native calls / attempts / commits: 1 / 1 / 1", report);
        Assert.Contains("CaptureCompleted", report);
        Assert.Contains("EvaluationCompleted", report);
        Assert.Contains("ActionCommitted", report);
        Assert.DoesNotContain("/machine-specific/", report);
    }

    [Fact]
    public void ExplicitAutoHarvestProfileRejectsAnIncompatibleArtifact()
    {
        var artifact = ServiceCycleReplayProductionScenarioFixture.Capture(1).Artifact;

        var error = Assert.Throws<InvalidDataException>(() => ServiceCycleTraceReport.Render(
            "not-auto-harvest.oscr",
            artifact,
            ServiceCycleTraceProfile.AutoHarvest));

        Assert.Contains("no service compatible with the Auto Harvest profile", error.Message);
    }
}
