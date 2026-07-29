using System;
using System.Threading;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Configuration;

/// <summary>
/// The write half of the shared configuration publication, handed to whoever reads the settings.
/// </summary>
/// <remarks>
/// Separate from the publisher for the same reason the world's sink is: reading is the runtime's
/// job, which pins one snapshot per cycle and hands it to the service. A publisher that could also
/// read would be a second, unpinned path to the configuration.
/// </remarks>
public interface IServiceConfigurationPublicationSink
{
    ConfigGeneration Publish(SuiteRuntimeConfiguration snapshot);
}

/// <summary>One immutable configuration snapshot together with the generation that identifies it.</summary>
public sealed class ConfigurationPublication
{
    internal ConfigurationPublication(ConfigGeneration generation, SuiteRuntimeConfiguration snapshot)
    {
        Generation = generation;
        Snapshot = snapshot;
    }

    public ConfigGeneration Generation { get; }
    public SuiteRuntimeConfiguration Snapshot { get; }
}

/// <summary>
/// The suite's one configuration publication: an atomic slot holding the immutable snapshot every
/// service evaluates against, and the one generation that identifies it.
/// </summary>
public sealed class ServiceConfigurationPublisher :
    IDisposable,
    IServiceConfigurationPublicationSink
{
    private readonly PublicationLock _sync = new();
    private ConfigurationPublication _latest;
    private bool _disposed;

    internal ServiceConfigurationPublisher(
        SuiteRuntimeConfiguration initialSnapshot,
        ConfigGeneration? initialGeneration = null)
    {
        if (initialSnapshot is null) throw new ArgumentNullException(nameof(initialSnapshot));
        if (initialGeneration.HasValue && !initialGeneration.Value.IsValid)
            throw new ArgumentException(
                "A valid initial configuration generation is required.",
                nameof(initialGeneration));
        _latest = new ConfigurationPublication(
            initialGeneration ?? new ConfigGeneration(1),
            initialSnapshot);
    }

    public ConfigurationPublication ReadLatest()
    {
        ThrowIfDisposed();
        return Volatile.Read(ref _latest) ??
            throw new ObjectDisposedException(nameof(ServiceConfigurationPublisher));
    }

    public ConfigGeneration Publish(SuiteRuntimeConfiguration snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        lock (_sync)
        {
            ThrowIfDisposed();
            var next = new ConfigurationPublication(_latest.Generation.Next(), snapshot);
            Volatile.Write(ref _latest, next);
            return next.Generation;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            Volatile.Write(ref _latest, null!);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed))
            throw new ObjectDisposedException(nameof(ServiceConfigurationPublisher));
    }

    private sealed class PublicationLock { }
}
