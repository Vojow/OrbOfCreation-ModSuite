using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OrbModding.Common.Runtime.Catalog;

/// <summary>
/// A dense, suite-local handle into a lifecycle definition catalog for hot-path lookup. The dense index
/// is stored biased by one so that <c>default(DefinitionHandle)</c> is invalid rather than silently
/// pointing at entry 0. The handle also carries the scope of the catalog build it was minted by, so a
/// handle from one catalog build cannot silently index a different (e.g. rebuilt) catalog.
/// </summary>
public readonly struct DefinitionHandle : IEquatable<DefinitionHandle>
{
    // _slot is the dense index + 1, so 0 (the default) means "no entry" and is invalid.
    private readonly int _slot;
    private readonly int _scope;

    internal DefinitionHandle(int index, int scope)
    {
        _slot = index + 1;
        _scope = scope;
    }

    /// <summary>The dense zero-based index this handle refers to; -1 for an invalid/default handle.</summary>
    public int Value => _slot - 1;

    /// <summary>True only for a handle minted from a real catalog entry; false for <c>default</c>.</summary>
    public bool IsValid => _slot > 0;

    /// <summary>The catalog-build scope this handle was minted under; 0 for an invalid/default handle.</summary>
    internal int Scope => _scope;

    public bool Equals(DefinitionHandle other) => _slot == other._slot && _scope == other._scope;
    public override bool Equals(object? obj) => obj is DefinitionHandle other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_slot, _scope);
    public static bool operator ==(DefinitionHandle left, DefinitionHandle right) => left.Equals(right);
    public static bool operator !=(DefinitionHandle left, DefinitionHandle right) => !left.Equals(right);
}

/// <summary>One catalog entry: stable identity, a dense handle, and immutable static relationships.</summary>
public sealed class DefinitionCatalogEntry
{
    internal DefinitionCatalogEntry(
        DefinitionHandle handle, string uuid, string expectedNativeType, string diagnosticName, IReadOnlyList<DefinitionHandle> relations)
    {
        Handle = handle;
        Uuid = uuid;
        ExpectedNativeType = expectedNativeType;
        DiagnosticName = diagnosticName;
        // Wrap in a genuinely read-only view so a consumer cannot downcast the exposed IReadOnlyList back to
        // the backing DefinitionHandle[] and mutate the "immutable" relationships in place. ReadOnlyCollection
        // holds the array privately and exposes no path to it.
        Relations = new ReadOnlyCollection<DefinitionHandle>(
            relations as IList<DefinitionHandle> ?? new List<DefinitionHandle>(relations));
    }

    public DefinitionHandle Handle { get; }

    /// <summary>Stable UUID identity (authoritative with <see cref="ExpectedNativeType"/>).</summary>
    public string Uuid { get; }

    /// <summary>Expected native type; part of the authoritative identity. Names are diagnostics only.</summary>
    public string ExpectedNativeType { get; }

    public string DiagnosticName { get; }

    /// <summary>Immutable static relationships to other definitions, stored once per lifecycle.</summary>
    public IReadOnlyList<DefinitionHandle> Relations { get; }
}

/// <summary>
/// A lifecycle-scoped, immutable catalog of the finite definition universe. It assigns a dense handle
/// per entry for hot-path indexing while keeping stable UUID plus expected native type as the
/// authoritative identity, records a schema version, and stores static relationships once so routine
/// captures reuse them without rediscovery. It is frozen at build time and shared across many captures.
/// </summary>
public sealed class LifecycleDefinitionCatalog
{
    private readonly DefinitionCatalogEntry[] _entries;
    private readonly Dictionary<(string Uuid, string Type), int> _byIdentity;
    private readonly int _scope;

    internal LifecycleDefinitionCatalog(
        int schemaVersion,
        long lifecycleGeneration,
        int scope,
        DefinitionCatalogEntry[] entries,
        Dictionary<(string, string), int> byIdentity)
    {
        SchemaVersion = schemaVersion;
        LifecycleGeneration = lifecycleGeneration;
        _scope = scope;
        _entries = entries;
        _byIdentity = byIdentity;
    }

    public int SchemaVersion { get; }
    public long LifecycleGeneration { get; }
    public int Count => _entries.Length;

    public DefinitionCatalogEntry this[DefinitionHandle handle]
    {
        get
        {
            ValidateHandle(handle);
            return _entries[handle.Value];
        }
    }

    /// <summary>Boundary resolution by authoritative identity (UUID plus expected native type).</summary>
    public bool TryResolve(string uuid, string expectedNativeType, out DefinitionHandle handle)
    {
        if (uuid is not null && expectedNativeType is not null &&
            _byIdentity.TryGetValue((uuid, expectedNativeType), out var index))
        {
            handle = new DefinitionHandle(index, _scope);
            return true;
        }

        handle = default;
        return false;
    }

    public IReadOnlyList<DefinitionHandle> RelationsOf(DefinitionHandle handle)
    {
        ValidateHandle(handle);
        return _entries[handle.Value].Relations;
    }

    private void ValidateHandle(DefinitionHandle handle)
    {
        if (!handle.IsValid)
            throw new ArgumentException("A default or invalid definition handle cannot index a catalog.", nameof(handle));
        if (handle.Scope != _scope)
            throw new ArgumentException(
                "The definition handle was minted by a different catalog build and cannot index this catalog.",
                nameof(handle));
        if (handle.Value < 0 || handle.Value >= _entries.Length)
            throw new ArgumentOutOfRangeException(nameof(handle), "The definition handle is out of range for this catalog.");
    }
}

/// <summary>
/// Builds a <see cref="LifecycleDefinitionCatalog"/> once. Handles are assigned densely in insertion
/// order. A cross-type UUID collision or duplicate identity is rejected. The builder cannot mutate a
/// catalog it has already produced.
/// </summary>
public sealed class DefinitionCatalogBuilder
{
    // A process-wide monotonic source of catalog-build scope ids. Each builder (and therefore each built
    // catalog) gets a distinct scope, so a handle minted by one build is rejected by any other build even
    // when their lifecycle generations coincide. Starts at 1 so the default handle's scope of 0 is never
    // a real catalog scope.
    private static int _nextScope;

    private readonly List<PendingEntry> _entries = new();
    private readonly Dictionary<(string, string), int> _byIdentity = new();
    private readonly HashSet<string> _uuids = new(StringComparer.Ordinal);
    private readonly int _scope = System.Threading.Interlocked.Increment(ref _nextScope);
    private bool _built;

    public DefinitionHandle Add(string uuid, string expectedNativeType, string diagnosticName)
    {
        if (_built) throw new InvalidOperationException("The catalog has already been built.");
        if (string.IsNullOrWhiteSpace(uuid)) throw new ArgumentException("A UUID is required.", nameof(uuid));
        if (string.IsNullOrWhiteSpace(expectedNativeType))
            throw new ArgumentException("An expected native type is required.", nameof(expectedNativeType));
        if (!_uuids.Add(uuid))
            throw new ArgumentException($"UUID '{uuid}' is already present; UUIDs are globally unique.", nameof(uuid));

        var handle = new DefinitionHandle(_entries.Count, _scope);
        _byIdentity.Add((uuid, expectedNativeType), handle.Value);
        _entries.Add(new PendingEntry(uuid, expectedNativeType, diagnosticName ?? string.Empty));
        return handle;
    }

    public void Relate(DefinitionHandle from, DefinitionHandle to)
    {
        if (_built) throw new InvalidOperationException("The catalog has already been built.");
        RequireHandle(from);
        RequireHandle(to);
        _entries[from.Value].Relations.Add(to);
    }

    public LifecycleDefinitionCatalog Build(int schemaVersion, long lifecycleGeneration)
    {
        if (_built) throw new InvalidOperationException("The catalog has already been built.");
        if (schemaVersion <= 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        _built = true;

        var entries = new DefinitionCatalogEntry[_entries.Count];
        for (var index = 0; index < _entries.Count; index++)
        {
            var pending = _entries[index];
            entries[index] = new DefinitionCatalogEntry(
                new DefinitionHandle(index, _scope),
                pending.Uuid,
                pending.ExpectedNativeType,
                pending.DiagnosticName,
                pending.Relations.ToArray());
        }

        return new LifecycleDefinitionCatalog(schemaVersion, lifecycleGeneration, _scope, entries, _byIdentity);
    }

    private void RequireHandle(DefinitionHandle handle)
    {
        if (!handle.IsValid || handle.Scope != _scope || handle.Value < 0 || handle.Value >= _entries.Count)
            throw new ArgumentOutOfRangeException(nameof(handle), "The handle is not part of this builder.");
    }

    private sealed class PendingEntry
    {
        public PendingEntry(string uuid, string expectedNativeType, string diagnosticName)
        {
            Uuid = uuid;
            ExpectedNativeType = expectedNativeType;
            DiagnosticName = diagnosticName;
        }

        public string Uuid { get; }
        public string ExpectedNativeType { get; }
        public string DiagnosticName { get; }
        public List<DefinitionHandle> Relations { get; } = new();
    }
}
