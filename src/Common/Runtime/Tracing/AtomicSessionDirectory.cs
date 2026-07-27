using System;
using System.IO;

namespace OrbModding.Common.Runtime.Tracing;

internal sealed class AtomicSessionDirectory
{
    private readonly string _rootDirectory;
    private readonly string _sessionDirectory;
    private bool _initialized;

    internal AtomicSessionDirectory(string rootDirectory, string artifactName)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("A session root directory is required.", nameof(rootDirectory));
        ValidateLeafName(artifactName, nameof(artifactName));
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _sessionDirectory = Path.Combine(_rootDirectory, artifactName);
    }

    internal void Initialize()
    {
        if (_initialized) throw new InvalidOperationException("The session directory is already initialized.");
        Directory.CreateDirectory(_rootDirectory);
        var temporaryDirectory = _sessionDirectory + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            Directory.Move(temporaryDirectory, _sessionDirectory);
        }
        catch
        {
            Directory.Delete(temporaryDirectory);
            throw;
        }
        _initialized = true;
    }

    internal void CommitFile(string fileName, ReadOnlySpan<byte> bytes)
    {
        if (!_initialized) throw new InvalidOperationException("The session directory is not initialized.");
        ValidateLeafName(fileName, nameof(fileName));
        var finalPath = Path.Combine(_sessionDirectory, fileName);
        var temporaryPath = finalPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, finalPath);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    private static void ValidateLeafName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal))
            throw new ArgumentException("A safe session leaf name is required.", parameterName);
    }
}
