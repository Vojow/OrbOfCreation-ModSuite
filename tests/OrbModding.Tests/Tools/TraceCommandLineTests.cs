using OrbModding.ServiceCycleTrace;
using Xunit;

namespace OrbModding.Tests.Tools;

public sealed class TraceCommandLineTests
{
    [Fact]
    public void ManualFullTraceIsTheDefaultModeAndJournalIsOptIn()
    {
        Assert.True(TraceCommandLine.TryParse(
            new[] { "--input", "session-0000000000000001" },
            out var implicitFull));
        Assert.True(TraceCommandLine.TryParse(
            new[] { "--full", "--input", "session-0000000000000001", "--output", "report.md" },
            out var full));
        Assert.True(TraceCommandLine.TryParse(
            new[] { "--journal", "--input", "journal", "--output", "report.md" },
            out var journal));

        Assert.Equal(TraceInputKind.ManualFullTrace, implicitFull.InputKind);
        Assert.Equal(TraceInputKind.ManualFullTrace, full.InputKind);
        Assert.Equal(TraceInputKind.DecisionJournal, journal.InputKind);
        Assert.Equal("report.md", full.OutputPath);
    }

    [Theory]
    [InlineData("--full", "--input", "session-0000000000000001", "--profile", "generic")]
    [InlineData("--full", "--full", "--input", "session-0000000000000001")]
    [InlineData("--full", "--input")]
    [InlineData("--journal", "--input", "journal", "--profile", "generic")]
    [InlineData("--journal", "--full", "--input", "journal")]
    [InlineData("--journal", "--journal", "--input", "journal")]
    public void UnknownArgumentsDuplicateModesAndMissingValuesAreRejected(params string[] args) =>
        Assert.False(TraceCommandLine.TryParse(args, out _));
}
