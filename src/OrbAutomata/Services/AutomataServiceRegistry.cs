using System;

namespace OrbAutomata;

/// <summary>
/// Explicit, bounded composition boundary for Automata's runtime services.
/// Registration order is lifecycle order; no assembly scanning or reflection is used.
/// </summary>
internal sealed class AutomataServiceRegistry : IDisposable
{
    internal const int DefaultCapacity = 32;

    private readonly IAutomataService?[] _services;
    private int _count;
    private bool _disposed;

    public AutomataServiceRegistry(int capacity = DefaultCapacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _services = new IAutomataService[capacity];
    }

    public int Count => _count;
    public int Capacity => _services.Length;

    public T Register<T>(T service) where T : class, IAutomataService
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        if (_disposed) throw new ObjectDisposedException(nameof(AutomataServiceRegistry));
        if (_count == _services.Length)
            throw new InvalidOperationException($"Automata supports at most {_services.Length} ordered runtime services.");

        for (var index = 0; index < _count; index++)
        {
            if (ReferenceEquals(_services[index], service))
                throw new InvalidOperationException("The runtime service is already registered.");
        }

        _services[_count++] = service;
        return service;
    }

    public void Tick(float unscaledDeltaTime)
    {
        ThrowIfDisposed();
        for (var index = 0; index < _count; index++)
            _services[index]!.Tick(unscaledDeltaTime);
    }

    public void CancelPreparedWork()
    {
        if (_disposed) return;
        for (var index = 0; index < _count; index++)
            _services[index]!.CancelPreparedWork();
    }

    public void InvalidateLifecycle()
    {
        ThrowIfDisposed();
        for (var index = 0; index < _count; index++)
            _services[index]!.InvalidateLifecycle();
    }

    public void Dispose()
    {
        if (_disposed) return;

        for (var index = 0; index < _count; index++)
            _services[index]!.Dispose();

        Array.Clear(_services, 0, _count);
        _count = 0;
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AutomataServiceRegistry));
    }
}
