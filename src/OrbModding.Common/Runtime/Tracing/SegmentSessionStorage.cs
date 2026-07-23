using System;
using System.Globalization;

namespace OrbModding.Common.Runtime.Tracing;

internal interface ISegmentSessionStorage
{
    void Initialize();
    void CommitSegment(long ordinal, ReadOnlySpan<byte> bytes);
    void CommitManifest(ReadOnlySpan<byte> bytes);
}

internal sealed class AtomicSegmentSessionStorage : ISegmentSessionStorage
{
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
        EnsureWritable();
        if (ordinal != _nextSegmentOrdinal)
            throw new InvalidOperationException($"Expected segment {_nextSegmentOrdinal}.");
        _directory.CommitFile(
            "segment-" + ordinal.ToString("D8", CultureInfo.InvariantCulture) + _segmentExtension,
            bytes);
        _nextSegmentOrdinal++;
    }

    public void CommitManifest(ReadOnlySpan<byte> bytes)
    {
        EnsureWritable();
        _directory.CommitFile(_manifestFileName, bytes);
        _manifestCommitted = true;
    }

    private void EnsureWritable()
    {
        if (_manifestCommitted) throw new InvalidOperationException("The session manifest is already committed.");
    }
}
