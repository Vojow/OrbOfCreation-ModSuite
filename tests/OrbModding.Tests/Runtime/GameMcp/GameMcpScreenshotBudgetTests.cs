using System;
using System.IO;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMcp;

public sealed class GameMcpScreenshotBudgetTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "orb-mcp-screenshot-budget-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void BeforeCaptureRejectsTheThirdOwnedScreenshotWithoutEncodingAnotherFrame()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Path.Combine(_directory, "mcp-first.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(_directory, "mcp-second.png"), new byte[] { 2 });
        File.WriteAllBytes(Path.Combine(_directory, "someone-elses.png"), new byte[] { 3 });

        var admission = GameMcpScreenshotBudget.BeforeCapture(_directory);

        Assert.Equal(GameMcpScreenshotBudgetStatus.FileLimitReached, admission.Status);
        Assert.False(admission.IsAvailable);
    }

    [Fact]
    public void BeforeCommitAllowsTheEnvelopeBoundaryAndRejectsOneByteBeyondIt()
    {
        Directory.CreateDirectory(_directory);
        var existing = Path.Combine(_directory, "mcp-first.png");
        using (var stream = File.Create(existing))
            stream.SetLength(GameMcpScreenshotBudget.RetainedBytes - 100);

        var atLimit = GameMcpScreenshotBudget.BeforeCommit(_directory, incomingBytes: 100);
        var beyondLimit = GameMcpScreenshotBudget.BeforeCommit(_directory, incomingBytes: 101);

        Assert.True(atLimit.IsAvailable);
        Assert.Equal(GameMcpScreenshotBudgetStatus.ByteLimitReached, beyondLimit.Status);
    }

    [Fact]
    public void AnAbsentOwnedDirectoryHasItsFirstCaptureSlot()
    {
        Assert.True(GameMcpScreenshotBudget.BeforeCapture(_directory).IsAvailable);
    }
}
