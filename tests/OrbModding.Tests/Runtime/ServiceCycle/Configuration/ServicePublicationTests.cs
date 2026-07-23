using System;
using System.Threading;
using System.Threading.Tasks;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;
using RuntimeLifecycleGeneration = OrbModding.Common.Runtime.LifecycleGeneration;

namespace OrbModding.Tests.Runtime.ServiceCycle.Configuration;

public sealed class ServicePublicationTests
{
    [Fact]
    public void DraftAndFailedSavesNeverPublishButSuccessfulSavesAdvance()
    {
        using var registry = new ServiceCycleRegistry(1);
        using var registration = registry.Register(
            new SyntheticServiceDefinition("test.config"),
            new SyntheticConfig(10),
            new RuntimeLifecycleGeneration(1));
        var publisher = registration.Configuration;

        Assert.False(publisher.CompleteSave(
            ConfigurationSaveResult<SyntheticConfig>.NotSaved(ConfigurationSaveDisposition.Draft)));
        Assert.False(publisher.CompleteSave(
            ConfigurationSaveResult<SyntheticConfig>.NotSaved(ConfigurationSaveDisposition.ValidationFailed)));
        Assert.False(publisher.CompleteSave(
            ConfigurationSaveResult<SyntheticConfig>.NotSaved(ConfigurationSaveDisposition.PersistenceFailed)));
        Assert.Equal(1UL, publisher.ReadLatest().Generation.Value);
        Assert.Equal(10, publisher.ReadLatest().Snapshot.Value);

        Assert.True(publisher.CompleteSave(ConfigurationSaveResult<SyntheticConfig>.Saved(new SyntheticConfig(20))));
        Assert.Equal(2UL, publisher.ReadLatest().Generation.Value);
        Assert.Equal(20, publisher.ReadLatest().Snapshot.Value);
    }

    [Fact]
    public void ConfigurationAndStrategyAdvanceIndependently()
    {
        using var registry = new ServiceCycleRegistry(1);
        using var registration = registry.Register(
            new SyntheticServiceDefinition("test.independent"),
            new SyntheticConfig(1),
            new RuntimeLifecycleGeneration(1));
        using var strategy = new ServiceStrategyPublisher<StrategyBulletin>(new StrategyBulletin(1));

        var strategyTwo = strategy.Publish(new StrategyBulletin(2));
        registration.Configuration.CompleteSave(
            ConfigurationSaveResult<SyntheticConfig>.Saved(new SyntheticConfig(2)));
        var strategyThree = strategy.Publish(new StrategyBulletin(3));

        Assert.Equal(2UL, strategyTwo.Value);
        Assert.Equal(3UL, strategyThree.Value);
        Assert.Equal(2UL, registration.Configuration.ReadLatest().Generation.Value);
    }

    [Fact]
    public void RegistrationAcceptsExactlyOneStrategySourceBeforeCompositionIsSealed()
    {
        using var registry = new ServiceCycleRegistry(1);
        using var registration = registry.Register(
            new SyntheticServiceDefinition("test.strategy.binding"),
            new SyntheticConfig(1),
            new RuntimeLifecycleGeneration(1));
        using var strategy = new ServiceStrategyPublisher<StrategyBulletin>(new StrategyBulletin(1));
        using var replacement = new ServiceStrategyPublisher<StrategyBulletin>(new StrategyBulletin(2));

        registration.BindStrategy(strategy);

        Assert.Throws<InvalidOperationException>(() => registration.BindStrategy(replacement));
        registry.Seal();
    }

    [Fact]
    public void StrategyBindingAfterCompositionIsSealedIsRejected()
    {
        using var registry = new ServiceCycleRegistry(1);
        using var registration = registry.Register(
            new SyntheticServiceDefinition("test.strategy.late-binding"),
            new SyntheticConfig(1),
            new RuntimeLifecycleGeneration(1));
        using var strategy = new ServiceStrategyPublisher<StrategyBulletin>(new StrategyBulletin(1));
        registry.Seal();

        Assert.Throws<InvalidOperationException>(() => registration.BindStrategy(strategy));
    }

    [Fact]
    public void StrategyCaptureReportsTheGenerationWhoseFactsWereCopied()
    {
        using var publisher = new ServiceStrategyPublisher<StrategyBulletin>(new StrategyBulletin(10));
        var capture = new StrategyCapture(publisher);
        var frame = new SyntheticFrame();

        publisher.Publish(new StrategyBulletin(25));
        var generation = capture.Capture(ref frame);

        Assert.Equal(2UL, generation.Value);
        Assert.Equal(25, frame.StrategyValue);
    }

    [Fact]
    public void MutableStrategyPublicationShapeIsRejectedBeforeItCanAlias()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceStrategyPublisher<MutableStrategy>(new MutableStrategy { Value = 1 }));
    }

    [Fact]
    public void StrategyGraphsCannotHideStaticOrMethodSignatureBoundaries()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceStrategyPublisher<StaticNativeStrategy>(new StaticNativeStrategy(1)));
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceStrategyPublisher<MethodBoundaryStrategy>(new MethodBoundaryStrategy(1)));
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public async Task ConcurrentConfigurationAndStrategyReadsRemainGenerationCoherent()
    {
        using var configuration = new ServiceConfigurationPublisher<SyntheticConfig>(new SyntheticConfig(1));
        using var strategy = new ServiceStrategyPublisher<StrategyBulletin>(new StrategyBulletin(1));
        using var gate = new ManualResetEventSlim(false);

        var configWriter = Task.Run(() =>
        {
            gate.Wait();
            for (var value = 2; value <= 2000; value++)
            {
                if (value % 19 == 0)
                {
                    Assert.False(configuration.CompleteSave(
                        ConfigurationSaveResult<SyntheticConfig>.NotSaved(
                            ConfigurationSaveDisposition.PersistenceFailed)));
                }
                configuration.CompleteSave(
                    ConfigurationSaveResult<SyntheticConfig>.Saved(new SyntheticConfig(value)));
                if (value % 8 == 0) Thread.Yield();
            }
        });
        var configReader = Task.Run(() =>
        {
            gate.Wait();
            ulong previous = 0;
            for (var read = 0; read < 10000; read++)
            {
                var publication = configuration.ReadLatest();
                Assert.True(publication.Generation.Value >= previous);
                Assert.Equal((int)publication.Generation.Value, publication.Snapshot.Value);
                previous = publication.Generation.Value;
                if (read % 16 == 0) Thread.Yield();
            }
        });
        var strategyWriter = Task.Run(() =>
        {
            gate.Wait();
            for (var value = 2; value <= 2000; value++)
            {
                strategy.Publish(new StrategyBulletin(value));
                if (value % 8 == 0) Thread.Yield();
            }
        });
        var strategyReader = Task.Run(() =>
        {
            gate.Wait();
            ulong previous = 0;
            for (var read = 0; read < 10000; read++)
            {
                var publication = strategy.ReadLatest();
                Assert.True(publication.Generation.Value >= previous);
                Assert.Equal((int)publication.Generation.Value, publication.Bulletin.Value);
                previous = publication.Generation.Value;
                if (read % 16 == 0) Thread.Yield();
            }
        });

        gate.Set();
        await Task.WhenAll(configWriter, configReader, strategyWriter, strategyReader);
        Assert.Equal(2000UL, configuration.ReadLatest().Generation.Value);
        Assert.Equal(2000, configuration.ReadLatest().Snapshot.Value);
        Assert.Equal(2000UL, strategy.ReadLatest().Generation.Value);
        Assert.Equal(2000, strategy.ReadLatest().Bulletin.Value);
    }

    private readonly struct StrategyBulletin
    {
        internal StrategyBulletin(int value) => Value = value;
        internal int Value { get; }
    }

    private sealed class MutableStrategy
    {
        internal int Value { get; set; }
    }

    private sealed class StaticNativeStrategy
    {
        private static UnityEngine.Object? _cache = null;
        private readonly int _value;
        internal StaticNativeStrategy(int value) => _value = value;
        internal int Value => _value;
        private static UnityEngine.Object? Cache => _cache;
    }

    private sealed class MethodBoundaryStrategy
    {
        private readonly int _value;
        internal MethodBoundaryStrategy(int value) => _value = value;
        internal int Value => _value;
        public StrategyWrapper<UnityEngine.Object?> ReadNative() => new(null);
    }

    private readonly struct StrategyWrapper<T>
    {
        private readonly T _value;
        internal StrategyWrapper(T value) => _value = value;
    }

    private sealed class StrategyCapture : ServiceStrategyCapture<SyntheticFrame, StrategyBulletin>
    {
        internal StrategyCapture(ServiceStrategyPublisher<StrategyBulletin> publisher) : base(publisher) { }

        protected override void CopyToFrame(in StrategyBulletin bulletin, ref SyntheticFrame frame) =>
            frame.StrategyValue = bulletin.Value;
    }
}
