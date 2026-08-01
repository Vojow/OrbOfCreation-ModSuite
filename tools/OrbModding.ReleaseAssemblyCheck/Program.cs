using System;
using System.Buffers.Binary;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: OrbModding.ReleaseAssemblyCheck <refs-built-dll> <game-built-dll>");
    return 2;
}

try
{
    var refsImage = NormalizeDebugIdentity(args[0]);
    var gameImage = NormalizeDebugIdentity(args[1]);
    if (refsImage.Length != gameImage.Length)
    {
        Console.Error.WriteLine(
            $"Faithfulness check failed: normalized sizes differ " +
            $"({refsImage.Length} != {gameImage.Length}).");
        return 1;
    }

    for (var offset = 0; offset < refsImage.Length; offset++)
    {
        if (refsImage[offset] == gameImage[offset])
        {
            continue;
        }

        Console.Error.WriteLine(
            $"Faithfulness check failed: first non-debug-identity difference is at " +
            $"file offset 0x{offset:X} (refs=0x{refsImage[offset]:X2}, " +
            $"game=0x{gameImage[offset]:X2}).");
        return 1;
    }

    var normalizedSha = Convert.ToHexString(SHA256.HashData(refsImage)).ToLowerInvariant();
    Console.WriteLine(
        $"faithfulness=pass normalized-sha256={normalizedSha} " +
        "ignored=coff-timestamp,pe-checksum,module-mvid,debug-directory");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Faithfulness check failed: {exception.Message}");
    return 1;
}

static byte[] NormalizeDebugIdentity(string path)
{
    var image = File.ReadAllBytes(path);
    using var stream = new MemoryStream(image, writable: false);
    using var peReader = new PEReader(stream);
    var headers = peReader.PEHeaders;
    var peHeader = headers.PEHeader ??
        throw new InvalidDataException($"{path} has no PE optional header.");
    var corHeader = headers.CorHeader ??
        throw new InvalidDataException($"{path} is not a managed assembly.");

    EnsureRange(image, 0x3c, 4, "DOS PE-header pointer");
    var peSignatureOffset = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3c, 4));
    EnsureRange(image, peSignatureOffset, 4 + 20 + 68, "PE headers");
    if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(peSignatureOffset, 4)) !=
        0x00004550)
    {
        throw new InvalidDataException($"{path} has an invalid PE signature.");
    }
    Zero(image, checked(peSignatureOffset + 8), 4, "COFF timestamp");
    Zero(image, checked(peSignatureOffset + 4 + 20 + 64), 4, "PE checksum");

    var metadata = peReader.GetMetadataReader();
    var module = metadata.GetModuleDefinition();
    var mvid = metadata.GetGuid(module.Mvid);
    var guidIndex = MetadataTokens.GetHeapOffset(module.Mvid);
    if (guidIndex <= 0)
    {
        throw new InvalidDataException($"{path} has an invalid module MVID handle.");
    }

    var metadataOffset = RvaToFileOffset(
        headers,
        corHeader.MetadataDirectory.RelativeVirtualAddress,
        corHeader.MetadataDirectory.Size,
        path);
    var guidHeapOffset = FindMetadataStreamOffset(image, metadataOffset, "#GUID", path);
    var mvidOffset = checked(metadataOffset + guidHeapOffset + ((guidIndex - 1) * 16));
    EnsureRange(image, mvidOffset, 16, "module MVID");
    if (new Guid(image.AsSpan(mvidOffset, 16)) != mvid)
    {
        throw new InvalidDataException(
            $"{path} module MVID bytes did not match its metadata handle.");
    }
    Zero(image, mvidOffset, 16, "module MVID");

    var debugDirectory = peHeader.DebugTableDirectory;
    if (debugDirectory.Size > 0)
    {
        const int debugEntrySize = 28;
        if (debugDirectory.Size % debugEntrySize != 0)
        {
            throw new InvalidDataException(
                $"{path} has a malformed PE debug-directory size.");
        }

        var debugTableOffset = RvaToFileOffset(
            headers,
            debugDirectory.RelativeVirtualAddress,
            debugDirectory.Size,
            path);
        for (var entryOffset = debugTableOffset;
             entryOffset < debugTableOffset + debugDirectory.Size;
             entryOffset += debugEntrySize)
        {
            EnsureRange(image, entryOffset, debugEntrySize, "debug-directory entry");
            var dataSize = BinaryPrimitives.ReadInt32LittleEndian(
                image.AsSpan(entryOffset + 16, 4));
            var dataPointer = BinaryPrimitives.ReadInt32LittleEndian(
                image.AsSpan(entryOffset + 24, 4));
            if (dataSize < 0 || dataPointer < 0)
            {
                throw new InvalidDataException(
                    $"{path} has a malformed PE debug-directory entry.");
            }
            if (dataSize > 0)
            {
                Zero(image, dataPointer, dataSize, "debug-directory payload");
            }
        }
        Zero(image, debugTableOffset, debugDirectory.Size, "debug directory");
    }

    return image;
}

static int FindMetadataStreamOffset(
    byte[] image,
    int metadataOffset,
    string streamName,
    string path)
{
    EnsureRange(image, metadataOffset, 16, "metadata root");
    const uint metadataSignature = 0x424A5342;
    if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(metadataOffset, 4)) !=
        metadataSignature)
    {
        throw new InvalidDataException($"{path} has an invalid CLR metadata signature.");
    }

    var versionLength = BinaryPrimitives.ReadInt32LittleEndian(
        image.AsSpan(metadataOffset + 12, 4));
    if (versionLength < 0)
    {
        throw new InvalidDataException($"{path} has an invalid CLR metadata version length.");
    }
    var cursor = Align4(checked(metadataOffset + 16 + versionLength));
    EnsureRange(image, cursor, 4, "metadata stream count");
    var streamCount = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(cursor + 2, 2));
    cursor += 4;

    for (var streamIndex = 0; streamIndex < streamCount; streamIndex++)
    {
        EnsureRange(image, cursor, 8, "metadata stream header");
        var streamOffset = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(cursor, 4));
        var streamSize = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(cursor + 4, 4));
        if (streamOffset < 0 || streamSize < 0)
        {
            throw new InvalidDataException($"{path} has an invalid metadata stream range.");
        }
        cursor += 8;

        var nameStart = cursor;
        while (cursor < image.Length && image[cursor] != 0)
        {
            cursor++;
        }
        if (cursor == image.Length)
        {
            throw new InvalidDataException(
                $"{path} has an unterminated metadata stream name.");
        }
        var actualName = Encoding.ASCII.GetString(image, nameStart, cursor - nameStart);
        cursor = Align4(cursor + 1);
        if (actualName == streamName)
        {
            EnsureRange(image, checked(metadataOffset + streamOffset), streamSize, streamName);
            return streamOffset;
        }
    }

    throw new InvalidDataException($"{path} has no {streamName} metadata stream.");
}

static int RvaToFileOffset(PEHeaders headers, int rva, int size, string path)
{
    if (rva < 0 || size < 0)
    {
        throw new InvalidDataException($"{path} has an invalid PE directory range.");
    }
    if (rva < headers.PEHeader!.SizeOfHeaders)
    {
        return rva;
    }

    foreach (var section in headers.SectionHeaders)
    {
        var sectionSize = Math.Max(section.VirtualSize, section.SizeOfRawData);
        if (rva < section.VirtualAddress || rva >= section.VirtualAddress + sectionSize)
        {
            continue;
        }
        var sectionOffset = rva - section.VirtualAddress;
        if (sectionOffset + size > section.SizeOfRawData)
        {
            throw new InvalidDataException(
                $"{path} PE directory extends past section {section.Name} raw data.");
        }
        return checked(section.PointerToRawData + sectionOffset);
    }

    throw new InvalidDataException($"{path} PE RVA 0x{rva:X} is outside every section.");
}

static int Align4(int value) => checked((value + 3) & ~3);

static void Zero(byte[] image, int offset, int size, string field)
{
    EnsureRange(image, offset, size, field);
    image.AsSpan(offset, size).Clear();
}

static void EnsureRange(byte[] image, int offset, int size, string field)
{
    if (offset < 0 || size < 0 || offset > image.Length - size)
    {
        throw new InvalidDataException(
            $"{field} range 0x{offset:X}+0x{size:X} is outside the PE image.");
    }
}
