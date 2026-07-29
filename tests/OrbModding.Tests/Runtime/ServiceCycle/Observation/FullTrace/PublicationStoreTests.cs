using System;
using System.Collections.Generic;
using System.Text;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Stores;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.FullTrace;

public sealed class PublicationStoreTests
{
    [Fact]
    public void AStoredConfigurationNamesItsVersionGenerationAndEverySettingBehindIt()
    {
        var text = Encoding.UTF8.GetString(PublicationValueFormat.Encode(
            "configuration",
            generation: 42,
            SuiteRuntimeConfigurationDefaults.Empty));
        var lines = text.Split('\n');

        Assert.Equal("OSCV 1 configuration 000000000000002a", lines[0]);
        var values = new List<string>(lines).GetRange(1, lines.Length - 2);
        Assert.Contains("General.Enabled = false", values);
        Assert.Contains("Safety.EmergencyDisable = false", values);

        // Sorted, so two generations of the same store diff to what actually changed.
        var sorted = new List<string>(values);
        sorted.Sort(StringComparer.Ordinal);
        Assert.Equal(sorted, values);

        // Computed readings of the settings are not settings; recording them would say the same
        // thing twice and go stale differently.
        Assert.DoesNotContain(values, line => line.StartsWith("CanStart", StringComparison.Ordinal));
    }

    [Fact]
    public void AStoredStrategyCountsItsResourceTableRatherThanFlatteningItAway()
    {
        var text = Encoding.UTF8.GetString(PublicationValueFormat.Encode(
            "strategy",
            generation: 1,
            SuiteStrategyDefaults.Neutral));
        var values = new List<string>(text.Split('\n'));

        Assert.Equal("OSCV 1 strategy 0000000000000001", values[0]);
        Assert.Contains("Provenance = Neutral", values);
        Assert.Contains("Resources.Count = 0", values);
    }

    [Fact]
    public void AGenerationIsStoredOnceHoweverManyTimesTheSessionSeesIt()
    {
        var sink = new RecordingSink();
        var writer = new PublicationStoreWriter(sink);

        writer.ObserveConfiguration(1, SuiteRuntimeConfigurationDefaults.Empty);
        writer.ObserveConfiguration(1, SuiteRuntimeConfigurationDefaults.Empty);
        writer.ObserveConfiguration(2, SuiteRuntimeConfigurationDefaults.Empty);
        writer.ObserveStrategy(1, SuiteStrategyDefaults.Neutral);
        writer.ObserveStrategy(1, SuiteStrategyDefaults.Neutral);

        Assert.Equal(
            new[]
            {
                "configuration-0000000000000001.oscv",
                "configuration-0000000000000002.oscv",
                "strategy-0000000000000001.oscv",
            },
            sink.Names.ToArray());
        Assert.Equal(2, writer.ConfigurationCount);
        Assert.Equal(1, writer.StrategyCount);
        Assert.False(writer.IsFaulted);
    }

    [Fact]
    public void AStoreThatCannotBeWrittenStopsStoringRatherThanStoppingTheRecording()
    {
        var writer = new PublicationStoreWriter(new FailingSink());

        writer.ObserveConfiguration(1, SuiteRuntimeConfigurationDefaults.Empty);
        writer.ObserveConfiguration(2, SuiteRuntimeConfigurationDefaults.Empty);

        Assert.True(writer.IsFaulted);
        Assert.Equal(0, writer.ConfigurationCount);
    }

    private sealed class RecordingSink : ISessionSideArtifactSink
    {
        internal List<string> Names { get; } = new();

        public void CommitSideArtifact(string name, ReadOnlySpan<byte> bytes) => Names.Add(name);
    }

    private sealed class FailingSink : ISessionSideArtifactSink
    {
        public void CommitSideArtifact(string name, ReadOnlySpan<byte> bytes) =>
            throw new InvalidOperationException("Injected side-artifact failure.");
    }
}
