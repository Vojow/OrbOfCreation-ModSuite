using System;

using OrbModding.Common;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// Resolves the game types the binders read. Kept beside them so that every native type name the
/// world collector depends on — the <c>TypeName</c> overrides and the resolution that consumes them —
/// lives in audited binder files.
/// </summary>
internal static class WorldNativeTypes
{
    /// <summary>The production resolver: the game's own loaded types, by name.</summary>
    internal static Type? Resolve(string typeName) => ReflectionUtil.FindLoadedType(typeName);
}
