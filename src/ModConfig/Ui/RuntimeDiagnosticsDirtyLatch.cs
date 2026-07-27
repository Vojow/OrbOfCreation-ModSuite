using System.Threading;
using OrbModding.Common.Runtime;

namespace OrbModConfig;

internal sealed class RuntimeDiagnosticsDirtyLatch
{
    private int _dirty;

    public RuntimeDiagnosticsDirtyLatch(bool initiallyDirty = false)
    {
        _dirty = initiallyDirty ? 1 : 0;
    }

    public bool IsDirty => Volatile.Read(ref _dirty) != 0;

    public void MarkDirty() => Interlocked.Exchange(ref _dirty, 1);

    public bool TryConsume() => Interlocked.Exchange(ref _dirty, 0) != 0;
}

internal sealed class RuntimeDiagnosticsTransitionQueue
{
    private readonly RuntimeDiagnosticsTransition[] _items;
    private int _head;
    private int _count;
    private bool _overflowed;

    public RuntimeDiagnosticsTransitionQueue(int capacity = 32)
    {
        if (capacity <= 0) throw new System.ArgumentOutOfRangeException(nameof(capacity));
        _items = new RuntimeDiagnosticsTransition[capacity];
    }

    public bool Overflowed => _overflowed;
    public int Count => _count;

    public void Enqueue(in RuntimeDiagnosticsTransition transition)
    {
        if (_count == _items.Length)
        {
            _overflowed = true;
            return;
        }
        _items[(_head + _count) % _items.Length] = transition;
        _count++;
    }

    public bool TryDequeue(out RuntimeDiagnosticsTransition transition)
    {
        if (_count == 0)
        {
            transition = default;
            return false;
        }
        transition = _items[_head];
        _items[_head] = default;
        _head = (_head + 1) % _items.Length;
        _count--;
        return true;
    }

    public bool ConsumeOverflow()
    {
        var overflowed = _overflowed;
        _overflowed = false;
        return overflowed;
    }
}
