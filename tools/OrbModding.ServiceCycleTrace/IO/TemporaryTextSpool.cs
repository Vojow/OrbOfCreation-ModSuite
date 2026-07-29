using System.Text;

namespace OrbModding.ServiceCycleTrace.IO;

internal sealed class TemporaryTextSpool : IDisposable
{
    private readonly FileStream _stream;
    private StreamWriter? _writer;
    private bool _disposed;

    internal TemporaryTextSpool()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "orb-service-cycle-report-" + Guid.NewGuid().ToString("N") + ".tmp");
        _stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 16 * 1024,
            FileOptions.DeleteOnClose | FileOptions.SequentialScan);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false), 16 * 1024, leaveOpen: true)
        {
            NewLine = "\n",
        };
    }

    internal TextWriter Writer => _writer ??
        throw new InvalidOperationException("The report spool is sealed.");

    internal void Seal()
    {
        var writer = _writer ?? throw new InvalidOperationException("The report spool is already sealed.");
        writer.Flush();
        writer.Dispose();
        _writer = null;
        _stream.Position = 0;
    }

    internal void CopyTo(TextWriter destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (_writer is not null) throw new InvalidOperationException("The report spool is not sealed.");
        if (_disposed) throw new ObjectDisposedException(nameof(TemporaryTextSpool));
        _stream.Position = 0;
        using var reader = new StreamReader(
            _stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 16 * 1024,
            leaveOpen: true);
        var buffer = new char[16 * 1024];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) != 0)
            destination.Write(buffer, 0, read);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writer?.Dispose();
        _writer = null;
        _stream.Dispose();
    }
}
