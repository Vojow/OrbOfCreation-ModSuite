namespace OrbModding.ServiceCycleTrace.IO;

internal static class BoundedBinaryFile
{
    internal static byte[] Read(string path, long minimumBytes, long maximumBytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete);
        if (stream.Length < minimumBytes || stream.Length > maximumBytes)
            throw new InvalidDataException("The trace file length is outside its format bounds.");
        var contents = new byte[(int)stream.Length];
        stream.ReadExactly(contents);
        if (stream.ReadByte() != -1)
            throw new InvalidDataException("The trace file changed while it was being read.");
        return contents;
    }
}
