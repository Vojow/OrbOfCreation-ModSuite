using System;
using System.Globalization;

namespace OrbModding.Common.Runtime.Tracing;

internal interface ISegmentSessionStorage
{
    void Initialize();
    void CommitSegment(long ordinal, ReadOnlySpan<byte> bytes);
    void CommitManifest(ReadOnlySpan<byte> bytes);
}

/// <summary>
/// A session directory that also accepts whole named files beside its segments.
/// </summary>
/// <remarks>
/// Separate from <see cref="ISegmentSessionStorage"/> rather than folded into it, because only the
/// full trace has anything to put beside its segments; the profile's sink shares the transport and
/// would gain a member it must refuse.
/// </remarks>
internal interface ISessionSideArtifactSink
{
    void CommitSideArtifact(string name, ReadOnlySpan<byte> bytes);
}

/// <summary>
/// A session directory whose commits are serialized.
/// </summary>
/// <remarks>
/// The segments arrive on the writer thread and the side artifacts on the main thread, so the
/// manifest-committed flag they all read was being written on one and read on the other. Commits are
/// rare — one per filled block, one per publication generation, one at the end — so a lock costs
/// nothing measurable and is the whole of the synchronization story here.
/// </remarks>
internal sealed class AtomicSegmentSessionStorage : ISegmentSessionStorage, ISessionSideArtifactSink
{
    private readonly object _commitGate = new();
    private readonly AtomicSessionDirectory _directory;
    private readonly string _segmentExtension;
    private readonly string _manifestFileName;
    private bool _manifestCommitted;
    private long _nextSegmentOrdinal;

    internal AtomicSegmentSessionStorage(
        string rootDirectory,
        string artifactName,
        string segmentExtension,
        string manifestFileName)
    {
        if (string.IsNullOrWhiteSpace(segmentExtension) || segmentExtension[0] != '.')
            throw new ArgumentException("A segment file extension is required.", nameof(segmentExtension));
        if (string.IsNullOrWhiteSpace(manifestFileName))
            throw new ArgumentException("A manifest file name is required.", nameof(manifestFileName));
        ArtifactName = artifactName;
        _segmentExtension = segmentExtension;
        _manifestFileName = manifestFileName;
        _directory = new AtomicSessionDirectory(rootDirectory, artifactName);
    }

    internal string ArtifactName { get; }

    public void Initialize() => _directory.Initialize();

    public void CommitSegment(long ordinal, ReadOnlySpan<byte> bytes)
    {
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
        lock (_commitGate)
        {
            EnsureWritable();
            if (ordinal != _nextSegmentOrdinal)
                throw new InvalidOperationException($"Expected segment {_nextSegmentOrdinal}.");
            _directory.CommitFile(
                "segment-" + ordinal.ToString("D8", CultureInfo.InvariantCulture) + _segmentExtension,
                bytes);
            _nextSegmentOrdinal++;
        }
    }

    public void CommitManifest(ReadOnlySpan<byte> bytes)
    {
        lock (_commitGate)
        {
            EnsureWritable();
            _directory.CommitFile(_manifestFileName, bytes);
            _manifestCommitted = true;
        }
    }

    public void CommitSideArtifact(string name, ReadOnlySpan<byte> bytes)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A side-artifact file name is required.", nameof(name));
        lock (_commitGate)
        {
            EnsureWritable();
            _directory.CommitFile(name, bytes);
        }
    }

    private void EnsureWritable()
    {
        if (_manifestCommitted) throw new InvalidOperationException("The session manifest is already committed.");
    }
}
