using System;
using OrbModConfig;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Control;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class ManualFullTracePresenterTests
{
    [Fact]
    public void UnavailableAndIdleStatesExposeOnlyAValidStartAction()
    {
        var unavailable = ManualFullTracePresenter.Build(
            ManualFullTraceStatus.Unavailable,
            ManualFullTraceCommand.None);
        var idle = ManualFullTracePresenter.Build(
            ManualFullTraceStatus.Idle,
            ManualFullTraceCommand.None);

        Assert.False(unavailable.ButtonEnabled);
        Assert.Equal(ManualFullTraceCommand.None, unavailable.Command);
        Assert.Contains("not active", unavailable.Body, StringComparison.Ordinal);
        Assert.True(idle.ButtonEnabled);
        Assert.Equal("Start full trace", idle.ButtonLabel);
        Assert.Equal(ManualFullTraceCommand.Start, idle.Command);
        Assert.Contains("no writer or buffers", idle.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingCommandDisablesItsButtonBeforeProducerAcknowledgement()
    {
        var presentation = ManualFullTracePresenter.Build(
            ManualFullTraceStatus.Idle,
            ManualFullTraceCommand.Start);

        Assert.False(presentation.ButtonEnabled);
        Assert.Equal("Starting...", presentation.ButtonLabel);
        Assert.Equal(ManualFullTraceCommand.None, presentation.Command);
    }

    [Fact]
    public void ActiveStatusShowsBoundedCountersAndStopAction()
    {
        var presentation = ManualFullTracePresenter.Build(
            Status(ManualFullTraceState.Recording),
            ManualFullTraceCommand.None);

        Assert.True(presentation.ButtonEnabled);
        Assert.Equal("Stop trace", presentation.ButtonLabel);
        Assert.Equal(ManualFullTraceCommand.Stop, presentation.Command);
        Assert.Contains("27:04:05", presentation.Body, StringComparison.Ordinal);
        Assert.Contains("1,234 accepted", presentation.Body, StringComparison.Ordinal);
        Assert.Contains("2.0 MiB", presentation.Body, StringComparison.Ordinal);
        Assert.Contains("session-0000000000000001", presentation.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalStatusDistinguishesCompleteFromIncompleteEvidence()
    {
        var complete = ManualFullTracePresenter.Build(Complete(), ManualFullTraceCommand.None);
        var incomplete = ManualFullTracePresenter.Build(Incomplete(), ManualFullTraceCommand.None);

        Assert.Contains("Manifest committed", complete.Body, StringComparison.Ordinal);
        Assert.Equal(ManualFullTraceCommand.Start, complete.Command);
        Assert.Contains("background write failed", incomplete.Body, StringComparison.Ordinal);
        Assert.Contains("First missing sequence: 1,201", incomplete.Body, StringComparison.Ordinal);
        Assert.Contains("Manifest: not committed", incomplete.Body, StringComparison.Ordinal);
        Assert.Equal(ManualFullTraceCommand.Start, incomplete.Command);
    }

    /// <summary>
    /// A trace whose events all landed but whose settings stores did not is not the same artifact as
    /// a whole one, and the body a reader acts on has to say so.
    /// </summary>
    [Fact]
    public void CompleteStatusNamesLostPublicationStores()
    {
        var whole = ManualFullTracePresenter.Build(Complete(), ManualFullTraceCommand.None);
        var lost = ManualFullTracePresenter.Build(
            Complete(storesLost: true), ManualFullTraceCommand.None);

        Assert.DoesNotContain("stores lost", whole.Body, StringComparison.Ordinal);
        Assert.Contains("Manifest committed", lost.Body, StringComparison.Ordinal);
        Assert.Contains("settings stores lost", lost.Body, StringComparison.Ordinal);
    }

    private static ManualFullTraceStatus Status(ManualFullTraceState state) => new(
        state,
        new TimeSpan(1, 3, 4, 5),
        1234,
        1200,
        2 * 1024 * 1024,
        3,
        0,
        false,
        ManualFullTraceResult.None,
        "session-0000000000000001",
        storesLost: false);

    private static ManualFullTraceStatus Complete(bool storesLost = false) => new(
        ManualFullTraceState.Complete,
        TimeSpan.FromSeconds(3),
        1234,
        1234,
        4096,
        4,
        0,
        true,
        ManualFullTraceResult.UserStopped,
        "session-0000000000000001",
        storesLost: storesLost);

    private static ManualFullTraceStatus Incomplete() => new(
        ManualFullTraceState.Incomplete,
        TimeSpan.FromSeconds(3),
        1234,
        1200,
        4096,
        4,
        1201,
        false,
        ManualFullTraceResult.WriteFailed,
        "session-0000000000000001",
        storesLost: false);
}
