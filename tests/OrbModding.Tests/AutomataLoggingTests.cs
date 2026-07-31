using System;
using System.Collections.Generic;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataLoggingTests
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(60);
    private static readonly DateTimeOffset Start =
        new(2026, 7, 31, 17, 23, 50, TimeSpan.FromHours(1));

    [Theory]
    [InlineData((int)AutomataLogSeverity.Info)]
    [InlineData((int)AutomataLogSeverity.Warning)]
    [InlineData((int)AutomataLogSeverity.Error)]
    public void FirstOccurrenceIsImmediateAtEverySeverity(int severityValue)
    {
        var severity = (AutomataLogSeverity)severityValue;
        var entries = new List<Entry>();
        var collapser = Collapser(entries);

        collapser.Write(severity, "first", Start);

        Assert.Equal(new Entry(severity, "first", Start), Assert.Single(entries));
    }

    [Fact]
    public void ConsecutiveByteIdenticalOccurrencesAreHeld()
    {
        var entries = new List<Entry>();
        var collapser = Collapser(entries);

        collapser.Write(AutomataLogSeverity.Info, "same state", Start);
        collapser.Write(AutomataLogSeverity.Info, "same state", Start.AddMilliseconds(300));
        collapser.Write(AutomataLogSeverity.Info, "same state", Start.AddMilliseconds(600));

        Assert.Equal(new Entry(AutomataLogSeverity.Info, "same state", Start), Assert.Single(entries));
    }

    [Fact]
    public void DifferentLineFlushesSummaryBeforeNewState()
    {
        var entries = new List<Entry>();
        var collapser = Collapser(entries);

        collapser.Write(AutomataLogSeverity.Info, "waiting", Start);
        collapser.Write(AutomataLogSeverity.Info, "waiting", Start.AddSeconds(1));
        collapser.Write(AutomataLogSeverity.Info, "ready", Start.AddSeconds(2));

        Assert.Collection(
            entries,
            entry => Assert.Equal("waiting", entry.Message),
            entry => Assert.Equal(
                "Previous info line repeated 1 more time over 1s: waiting",
                entry.Message),
            entry => Assert.Equal("ready", entry.Message));
    }

    [Fact]
    public void ContinuingRepeatFlushesAtHeartbeat()
    {
        var entries = new List<Entry>();
        var collapser = Collapser(entries);

        collapser.Write(AutomataLogSeverity.Warning, "still blocked", Start);
        collapser.Write(AutomataLogSeverity.Warning, "still blocked", Start.AddSeconds(59.999));
        Assert.Single(entries);

        collapser.Write(AutomataLogSeverity.Warning, "still blocked", Start.AddSeconds(60));

        Assert.Collection(
            entries,
            entry => Assert.Equal("still blocked", entry.Message),
            entry => Assert.Equal(
                "Previous warning line repeated 2 more times over 60s: still blocked",
                entry.Message));
    }

    [Fact]
    public void SummaryUsesLastRepeatTimestampAndTruthfulSpan()
    {
        var entries = new List<Entry>();
        var collapser = Collapser(entries);
        var repeatedAt = Start.AddMilliseconds(1_250);

        collapser.Write(AutomataLogSeverity.Error, "native fault", Start);
        collapser.Write(AutomataLogSeverity.Error, "native fault", repeatedAt);
        collapser.Write(AutomataLogSeverity.Error, "recovered", Start.AddSeconds(2));

        Assert.Equal(repeatedAt, entries[1].Timestamp);
        Assert.Equal(
            "[2026-07-31 17:23:51.250 +01:00] " +
            "Previous error line repeated 1 more time over 1.25s: native fault",
            AutomataLoggingExtensions.WithTimestamp(entries[1].Message, entries[1].Timestamp));
    }

    [Fact]
    public void MatchingIsOrdinalAndByteIdenticalOnly()
    {
        var entries = new List<Entry>();
        var collapser = Collapser(entries);

        collapser.Write(AutomataLogSeverity.Info, "state=ready", Start);
        collapser.Write(AutomataLogSeverity.Info, "state=ready", Start.AddSeconds(1));
        collapser.Write(AutomataLogSeverity.Info, "state=ready ", Start.AddSeconds(2));
        collapser.Write(AutomataLogSeverity.Info, "State=ready ", Start.AddSeconds(3));

        Assert.Collection(
            entries,
            entry => Assert.Equal("state=ready", entry.Message),
            entry => Assert.EndsWith(": state=ready", entry.Message, StringComparison.Ordinal),
            entry => Assert.Equal("state=ready ", entry.Message),
            entry => Assert.Equal("State=ready ", entry.Message));
    }

    [Fact]
    public void InterleavedSeveritiesCollapseTheirOwnConsecutiveStates()
    {
        var entries = new List<Entry>();
        var collapser = Collapser(entries);

        collapser.Write(AutomataLogSeverity.Info, "info wait", Start);
        collapser.Write(AutomataLogSeverity.Warning, "warning wait", Start.AddMilliseconds(100));
        collapser.Write(AutomataLogSeverity.Info, "info wait", Start.AddMilliseconds(300));
        collapser.Write(AutomataLogSeverity.Warning, "warning wait", Start.AddMilliseconds(400));
        collapser.Write(AutomataLogSeverity.Info, "info commit", Start.AddMilliseconds(600));
        collapser.Write(AutomataLogSeverity.Warning, "warning changed", Start.AddMilliseconds(700));

        Assert.Collection(
            entries,
            entry => Assert.Equal(new Entry(AutomataLogSeverity.Info, "info wait", Start), entry),
            entry => Assert.Equal(
                new Entry(AutomataLogSeverity.Warning, "warning wait", Start.AddMilliseconds(100)),
                entry),
            entry => Assert.Equal(AutomataLogSeverity.Info, entry.Severity),
            entry => Assert.Equal("info commit", entry.Message),
            entry => Assert.Equal(AutomataLogSeverity.Warning, entry.Severity),
            entry => Assert.Equal("warning changed", entry.Message));
        Assert.Contains(": info wait", entries[2].Message, StringComparison.Ordinal);
        Assert.Contains(": warning wait", entries[4].Message, StringComparison.Ordinal);
    }

    private static AutomataRepeatCollapser Collapser(List<Entry> entries) =>
        new(Heartbeat, (severity, message, timestamp) => entries.Add(new Entry(severity, message, timestamp)));

    private readonly record struct Entry(
        AutomataLogSeverity Severity,
        string Message,
        DateTimeOffset Timestamp);
}
