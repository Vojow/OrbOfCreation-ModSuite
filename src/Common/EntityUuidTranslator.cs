using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace OrbModding.Common;

internal readonly struct UuidDiagnosticIdentity
{
    internal UuidDiagnosticIdentity(string nativeName, string managedType, string displayName)
    {
        NativeName = nativeName ?? string.Empty;
        ManagedType = managedType ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
    }

    internal string NativeName { get; }
    internal string ManagedType { get; }
    internal string DisplayName { get; }
    internal string PreferredName => DisplayName.Length == 0 ? NativeName : DisplayName;
}

/// <summary>Diagnostic-only UUID translation. Gameplay still resolves exact UUID plus type.</summary>
internal static class EntityUuidTranslator
{
    private const string CanonicalResource = "OrbModSuite.EntityMappings.tsv";
    private const string DisplayResource = "OrbModSuite.EntityDisplayNames.tsv";
    private static readonly Lazy<IReadOnlyDictionary<Guid, UuidDiagnosticIdentity>> Catalog =
        new(Load, isThreadSafe: true);

    internal static int Count => Catalog.Value.Count;

    internal static bool TryTranslate(Guid uuid, out UuidDiagnosticIdentity identity) =>
        Catalog.Value.TryGetValue(uuid, out identity);

    internal static string Format(Guid uuid)
    {
        if (!TryTranslate(uuid, out var identity)) return uuid.ToString("D");
        var label = identity.PreferredName;
        return string.Equals(label, identity.NativeName, StringComparison.Ordinal)
            ? $"{label} [{identity.ManagedType}; {uuid:D}]"
            : $"{label} [{identity.NativeName}/{identity.ManagedType}; {uuid:D}]";
    }

    private static IReadOnlyDictionary<Guid, UuidDiagnosticIdentity> Load()
    {
        var result = new Dictionary<Guid, UuidDiagnosticIdentity>();
        Read(CanonicalResource, 3, parts =>
        {
            var id = new Guid(parts[0]);
            result.Add(id, new UuidDiagnosticIdentity(parts[1], parts[2], string.Empty));
        });
        Read(DisplayResource, 4, parts =>
        {
            var id = new Guid(parts[0]);
            if (result.TryGetValue(id, out var existing))
            {
                if (!string.Equals(existing.NativeName, parts[2], StringComparison.Ordinal) ||
                    !string.Equals(existing.ManagedType, parts[1], StringComparison.Ordinal))
                    throw new InvalidDataException($"UUID diagnostic mapping drift for {id:D}.");
                result[id] = new UuidDiagnosticIdentity(parts[2], parts[1], parts[3]);
            }
            else
            {
                result.Add(id, new UuidDiagnosticIdentity(parts[2], parts[1], parts[3]));
            }
        });
        return result;
    }

    private static void Read(string resource, int width, Action<string[]> consume)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource) ??
            throw new InvalidDataException($"Embedded UUID diagnostic resource {resource} is missing.");
        using var reader = new StreamReader(stream);
        _ = reader.ReadLine();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var parts = line.Split('\t');
            if (parts.Length != width)
                throw new InvalidDataException($"Malformed UUID diagnostic row in {resource}.");
            consume(parts);
        }
    }
}
