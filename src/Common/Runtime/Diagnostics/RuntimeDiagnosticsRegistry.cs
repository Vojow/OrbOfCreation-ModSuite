using System;
using System.Collections.Generic;
using System.Threading;

namespace OrbModding.Common.Runtime;

public sealed class RuntimeDiagnosticsRegistry : IRuntimeDiagnosticsSource
{
    private readonly int _ownerThreadId;
    private readonly Dictionary<FeatureStatusKey, RuntimeServiceDiagnosticsSnapshot> _snapshots = new();
    private long _revision;

    public RuntimeDiagnosticsRegistry() => _ownerThreadId = Thread.CurrentThread.ManagedThreadId;

    public static RuntimeDiagnosticsRegistry Shared { get; } = new();

    public event Action<RuntimeDiagnosticsTransition>? Transitioned;

    public RuntimeDiagnosticsRegistration Register(RuntimeServiceDiagnosticsSnapshot initialSnapshot)
    {
        AssertOwnerThread();
        if (initialSnapshot is null) throw new ArgumentNullException(nameof(initialSnapshot));
        if (_snapshots.ContainsKey(initialSnapshot.Key))
            throw new InvalidOperationException("A runtime diagnostics publisher already owns " + initialSnapshot.Key + ".");
        _snapshots.Add(initialSnapshot.Key, initialSnapshot);
        Publish(new RuntimeDiagnosticsTransition(
            RuntimeDiagnosticsTransitionKind.Added,
            null,
            initialSnapshot,
            checked(++_revision)));
        return new RuntimeDiagnosticsRegistration(this, initialSnapshot.Key);
    }

    public IReadOnlyList<RuntimeServiceDiagnosticsSnapshot> GetSnapshot()
    {
        AssertOwnerThread();
        var result = new List<RuntimeServiceDiagnosticsSnapshot>(_snapshots.Values);
        result.Sort(SnapshotComparer.Instance);
        return result;
    }

    internal bool Update(FeatureStatusKey key, RuntimeServiceDiagnosticsSnapshot snapshot)
    {
        AssertOwnerThread();
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (!key.Equals(snapshot.Key))
            throw new ArgumentException("A registration cannot change its diagnostics key.", nameof(snapshot));
        if (!_snapshots.TryGetValue(key, out var previous))
            throw new ObjectDisposedException(nameof(RuntimeDiagnosticsRegistration));
        if (previous.Equals(snapshot)) return false;
        _snapshots[key] = snapshot;
        Publish(new RuntimeDiagnosticsTransition(
            RuntimeDiagnosticsTransitionKind.Changed,
            previous,
            snapshot,
            checked(++_revision)));
        return true;
    }

    internal void Remove(FeatureStatusKey key)
    {
        AssertOwnerThread();
        if (!_snapshots.Remove(key, out var previous)) return;
        Publish(new RuntimeDiagnosticsTransition(
            RuntimeDiagnosticsTransitionKind.Removed,
            previous,
            null,
            checked(++_revision)));
    }

    private void Publish(RuntimeDiagnosticsTransition transition)
    {
        var handlers = Transitioned;
        if (handlers is null) return;
        foreach (Action<RuntimeDiagnosticsTransition> handler in handlers.GetInvocationList())
        {
            try { handler(transition); }
            catch { }
        }
    }

    private void AssertOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Runtime diagnostics registry access must remain on its owning main thread.");
    }

    private sealed class SnapshotComparer : IComparer<RuntimeServiceDiagnosticsSnapshot>
    {
        public static readonly SnapshotComparer Instance = new();

        public int Compare(RuntimeServiceDiagnosticsSnapshot? left, RuntimeServiceDiagnosticsSnapshot? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var plugin = string.Compare(left.Key.PluginId, right.Key.PluginId, StringComparison.Ordinal);
            return plugin != 0
                ? plugin
                : string.Compare(left.Key.FeatureId, right.Key.FeatureId, StringComparison.Ordinal);
        }
    }
}
