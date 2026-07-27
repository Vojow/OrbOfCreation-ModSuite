using System;
using OrbModConfig;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace.Control;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class HostTraceDumpPresenterTests
{
    [Fact]
    public void AnUnavailableRuntimeOffersNoButtonToPress()
    {
        var presentation = HostTraceDumpPresenter.Build(HostTraceDumpStatus.Unavailable, dumpRequested: false);

        Assert.False(presentation.ButtonEnabled);
        Assert.Contains("Unavailable", presentation.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIdleRuntimeSaysNothingIsWrittenUntilTheUserAsks()
    {
        var presentation = HostTraceDumpPresenter.Build(HostTraceDumpStatus.Idle, dumpRequested: false);

        Assert.True(presentation.ButtonEnabled);
        Assert.Equal("Dump recent events", presentation.ButtonLabel);
        Assert.Contains("nothing is written until you do", presentation.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void APendingRequestDisablesTheButtonUntilTheFrameTakesIt()
    {
        var presentation = HostTraceDumpPresenter.Build(HostTraceDumpStatus.Idle, dumpRequested: true);

        Assert.False(presentation.ButtonEnabled);
        Assert.Equal("Writing...", presentation.ButtonLabel);
    }

    [Fact]
    public void AWrittenDumpNamesItsArtifactAndAdmitsWhatTheRingHadAlreadyLost()
    {
        var presentation = HostTraceDumpPresenter.Build(
            new HostTraceDumpStatus(
                HostTraceDumpState.Written,
                writtenEvents: 8_192,
                bytesWritten: 2_097_152,
                overwrittenEvents: 1_024,
                "session-00000000000000ff"),
            dumpRequested: false);

        Assert.Equal("Dump again", presentation.ButtonLabel);
        Assert.Contains("8,192 events", presentation.Body, StringComparison.Ordinal);
        Assert.Contains("2.0 MiB", presentation.Body, StringComparison.Ordinal);
        Assert.Contains("1,024 older events", presentation.Body, StringComparison.Ordinal);
        Assert.Contains("session-00000000000000ff", presentation.Body, StringComparison.Ordinal);
    }
}
