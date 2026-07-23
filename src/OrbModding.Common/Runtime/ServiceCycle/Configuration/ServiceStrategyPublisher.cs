using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using RuntimeStrategyGeneration = OrbModding.Common.Runtime.StrategyGeneration;

namespace OrbModding.Common.Runtime.ServiceCycle.Configuration;

internal interface IServiceStrategyGenerationSource
{
    bool TryGetLatestGeneration(out RuntimeStrategyGeneration generation);
}

public sealed class StrategyPublication<TStrategy>
    where TStrategy : notnull
{
    internal StrategyPublication(RuntimeStrategyGeneration generation, TStrategy bulletin)
    {
        Generation = generation;
        Bulletin = bulletin;
    }

    public RuntimeStrategyGeneration Generation { get; }
    public TStrategy Bulletin { get; }
}

/// <summary>A separately typed, atomic source of immutable strategy bulletins.</summary>
public sealed class ServiceStrategyPublisher<TStrategy> : IDisposable, IServiceStrategyGenerationSource
    where TStrategy : notnull
{
    private readonly PublicationLock _sync = new();
    private StrategyPublication<TStrategy> _latest;
    private bool _disposed;

    public ServiceStrategyPublisher(TStrategy initialBulletin)
    {
        ServiceCycleTypeSafetyValidator.EnsureStrategyType<TStrategy>();
        if (initialBulletin is null) throw new ArgumentNullException(nameof(initialBulletin));
        _latest = new StrategyPublication<TStrategy>(new RuntimeStrategyGeneration(1), initialBulletin);
    }

    public StrategyPublication<TStrategy> ReadLatest()
    {
        ThrowIfDisposed();
        return Volatile.Read(ref _latest) ??
            throw new ObjectDisposedException(nameof(ServiceStrategyPublisher<TStrategy>));
    }

    bool IServiceStrategyGenerationSource.TryGetLatestGeneration(out RuntimeStrategyGeneration generation)
    {
        var publication = Volatile.Read(ref _latest);
        if (Volatile.Read(ref _disposed) || publication is null)
        {
            generation = default;
            return false;
        }
        generation = publication.Generation;
        return true;
    }

    public RuntimeStrategyGeneration Publish(TStrategy bulletin)
    {
        if (bulletin is null) throw new ArgumentNullException(nameof(bulletin));
        lock (_sync)
        {
            ThrowIfDisposed();
            var publication = new StrategyPublication<TStrategy>(_latest.Generation.Next(), bulletin);
            Volatile.Write(ref _latest, publication);
            return publication.Generation;
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
            throw new ObjectDisposedException(nameof(ServiceStrategyPublisher<TStrategy>));
    }

    private sealed class PublicationLock { }
}

/// <summary>
/// Feature-owned typed strategy-to-frame mapping. One atomic publication is read, its immutable facts are
/// copied into the frame, and that same publication's generation is returned to the capture result.
/// </summary>
public abstract class ServiceStrategyCapture<TFrame, TStrategy>
    where TStrategy : notnull
{
    private readonly ServiceStrategyPublisher<TStrategy> _publisher;

    protected ServiceStrategyCapture(ServiceStrategyPublisher<TStrategy> publisher) =>
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));

    public RuntimeStrategyGeneration Capture(ref TFrame frame)
    {
        var publication = _publisher.ReadLatest();
        var bulletin = publication.Bulletin;
        CopyToFrame(in bulletin, ref frame);
        return publication.Generation;
    }

    protected abstract void CopyToFrame(in TStrategy bulletin, ref TFrame frame);
}
