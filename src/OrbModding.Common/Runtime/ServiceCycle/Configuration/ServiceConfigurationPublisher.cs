using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Configuration;

public enum ConfigurationSaveDisposition
{
    Draft = 0,
    Succeeded = 1,
    ValidationFailed = 2,
    PersistenceFailed = 3,
    Abandoned = 4,
}

public readonly struct ConfigurationSaveResult<TConfig>
    where TConfig : notnull
{
    private readonly TConfig _snapshot;

    private ConfigurationSaveResult(ConfigurationSaveDisposition disposition, TConfig snapshot)
    {
        Disposition = disposition;
        _snapshot = snapshot;
    }

    public ConfigurationSaveDisposition Disposition { get; }
    public bool WasSaved => Disposition == ConfigurationSaveDisposition.Succeeded;
    internal TConfig Snapshot => WasSaved
        ? _snapshot
        : throw new InvalidOperationException("An unsuccessful save has no publishable snapshot.");

    public static ConfigurationSaveResult<TConfig> Saved(TConfig snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        return new ConfigurationSaveResult<TConfig>(ConfigurationSaveDisposition.Succeeded, snapshot);
    }

    public static ConfigurationSaveResult<TConfig> NotSaved(ConfigurationSaveDisposition disposition)
    {
        if (disposition is < ConfigurationSaveDisposition.Draft or > ConfigurationSaveDisposition.Abandoned ||
            disposition == ConfigurationSaveDisposition.Succeeded)
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }

        return new ConfigurationSaveResult<TConfig>(disposition, default!);
    }
}

public sealed class ConfigurationPublication<TConfig>
    where TConfig : notnull
{
    internal ConfigurationPublication(ConfigGeneration generation, TConfig snapshot)
    {
        Generation = generation;
        Snapshot = snapshot;
    }

    public ConfigGeneration Generation { get; }
    public TConfig Snapshot { get; }
}

/// <summary>
/// Publishes complete immutable configuration snapshots only after their Save transaction succeeds.
/// Draft, validation, persistence, and abandonment outcomes cannot advance the runtime generation.
/// </summary>
public sealed class ServiceConfigurationPublisher<TConfig> : IDisposable
    where TConfig : notnull
{
    private readonly PublicationLock _sync = new();
    private ConfigurationPublication<TConfig> _latest;
    private bool _disposed;

    internal ServiceConfigurationPublisher(TConfig initialSnapshot)
    {
        if (initialSnapshot is null) throw new ArgumentNullException(nameof(initialSnapshot));
        _latest = new ConfigurationPublication<TConfig>(new ConfigGeneration(1), initialSnapshot);
    }

    public ConfigurationPublication<TConfig> ReadLatest()
    {
        ThrowIfDisposed();
        return Volatile.Read(ref _latest) ??
            throw new ObjectDisposedException(nameof(ServiceConfigurationPublisher<TConfig>));
    }

    public bool CompleteSave(in ConfigurationSaveResult<TConfig> result)
    {
        if (!result.WasSaved)
        {
            ThrowIfDisposed();
            return false;
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            var next = new ConfigurationPublication<TConfig>(_latest.Generation.Next(), result.Snapshot);
            Volatile.Write(ref _latest, next);
            return true;
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
            throw new ObjectDisposedException(nameof(ServiceConfigurationPublisher<TConfig>));
    }

    private sealed class PublicationLock { }
}
