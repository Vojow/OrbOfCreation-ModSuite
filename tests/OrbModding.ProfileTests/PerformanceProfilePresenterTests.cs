using System;
using OrbModConfig;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Control;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class PerformanceProfilePresenterTests
{
    [Fact]
    public void IdleAndRecordingExposeIndependentStartAndStopActions()
    {
        var idle = PerformanceProfilePresenter.Build(
            PerformanceProfileControlStatus.Idle,
            PerformanceProfileCommand.None);
        var recording = PerformanceProfilePresenter.Build(
            Active(PerformanceProfileControlState.Recording),
            PerformanceProfileCommand.None);

        Assert.True(idle.ButtonEnabled);
        Assert.Equal("Start profile", idle.ButtonLabel);
        Assert.Equal(PerformanceProfileCommand.Start, idle.Command);
        Assert.Contains("stop whenever", idle.Body, StringComparison.OrdinalIgnoreCase);

        Assert.True(recording.ButtonEnabled);
        Assert.Equal("Stop profile", recording.ButtonLabel);
        Assert.Equal(PerformanceProfileCommand.Stop, recording.Command);
        Assert.Contains("27:04:05", recording.Body, StringComparison.Ordinal);
        Assert.Contains("1,234 written", recording.Body, StringComparison.Ordinal);
        Assert.Contains("2.0 MiB", recording.Body, StringComparison.Ordinal);
        Assert.Contains("profile-0000000000000001", recording.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PerformanceProfileControlState.Unavailable, "Start profile")]
    [InlineData(PerformanceProfileControlState.Stopping, "Stopping...")]
    [InlineData(PerformanceProfileControlState.Faulted, "Restart required")]
    public void NonActionableStatesKeepTheCommandDisabled(
        PerformanceProfileControlState state,
        string expectedLabel)
    {
        var status = state switch
        {
            PerformanceProfileControlState.Unavailable => PerformanceProfileControlStatus.Unavailable,
            PerformanceProfileControlState.Stopping => Active(state),
            PerformanceProfileControlState.Faulted => Faulted(PerformanceProfileResult.ProbeFailed),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

        var presentation = PerformanceProfilePresenter.Build(status, PerformanceProfileCommand.None);

        Assert.False(presentation.ButtonEnabled);
        Assert.Equal(expectedLabel, presentation.ButtonLabel);
        Assert.Equal(PerformanceProfileCommand.None, presentation.Command);
    }

    [Fact]
    public void CompleteAllowsANewProfileAndFaultRequiresRestart()
    {
        var complete = PerformanceProfilePresenter.Build(Complete(), PerformanceProfileCommand.None);
        var faulted = PerformanceProfilePresenter.Build(
            Faulted(PerformanceProfileResult.WriteFailed),
            PerformanceProfileCommand.None);

        Assert.True(complete.ButtonEnabled);
        Assert.Equal("Start new profile", complete.ButtonLabel);
        Assert.Equal(PerformanceProfileCommand.Start, complete.Command);
        Assert.Contains("stopped by user", complete.Body, StringComparison.Ordinal);

        Assert.False(faulted.ButtonEnabled);
        Assert.Equal(PerformanceProfileCommand.None, faulted.Command);
        Assert.Contains("background write failed", faulted.Body, StringComparison.Ordinal);
        Assert.Contains("Restart the game", faulted.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PerformanceProfileCommand.Start, "Starting...")]
    [InlineData(PerformanceProfileCommand.Stop, "Stopping...")]
    public void PendingCommandDisablesTheButtonBeforeAcknowledgement(
        PerformanceProfileCommand pending,
        string expectedLabel)
    {
        var status = pending == PerformanceProfileCommand.Start
            ? PerformanceProfileControlStatus.Idle
            : Active(PerformanceProfileControlState.Recording);

        var presentation = PerformanceProfilePresenter.Build(status, pending);

        Assert.False(presentation.ButtonEnabled);
        Assert.Equal(expectedLabel, presentation.ButtonLabel);
        Assert.Equal(PerformanceProfileCommand.None, presentation.Command);
    }

    private static PerformanceProfileControlStatus Active(PerformanceProfileControlState state) => new(
        state,
        new TimeSpan(1, 3, 4, 5),
        1234,
        2 * 1024 * 1024,
        PerformanceProfileResult.None,
        "profile-0000000000000001");

    private static PerformanceProfileControlStatus Complete() => new(
        PerformanceProfileControlState.Complete,
        TimeSpan.FromMinutes(3),
        1234,
        4096,
        PerformanceProfileResult.UserStopped,
        "profile-0000000000000001");

    private static PerformanceProfileControlStatus Faulted(PerformanceProfileResult result) => new(
        PerformanceProfileControlState.Faulted,
        TimeSpan.FromMinutes(2),
        900,
        4096,
        result,
        "profile-0000000000000001");
}
