using System;

namespace OrbAutomata;

internal enum SpellWorkbenchActionKind
{
    Discover = 0,
    CreateWithLayout = 1,
}

internal readonly struct SpellWorkbenchAction
{
    internal SpellWorkbenchAction(
        SpellWorkbenchActionKind kind,
        Guid spellRecipeId,
        long lifecycleEpoch,
        SpellWorkbenchGlyphStack[] coreGlyphs,
        SpellWorkbenchGlyphStack[] augmentGlyphs)
    {
        if (spellRecipeId == Guid.Empty)
            throw new ArgumentException("A spell recipe identity is required.", nameof(spellRecipeId));
        if (coreGlyphs is null) throw new ArgumentNullException(nameof(coreGlyphs));
        if (augmentGlyphs is null) throw new ArgumentNullException(nameof(augmentGlyphs));
        Kind = kind;
        SpellRecipeId = spellRecipeId;
        LifecycleEpoch = lifecycleEpoch;
        CoreGlyphs = Copy(coreGlyphs);
        AugmentGlyphs = Copy(augmentGlyphs);
    }

    internal SpellWorkbenchActionKind Kind { get; }
    internal Guid SpellRecipeId { get; }
    internal long LifecycleEpoch { get; }
    internal SpellWorkbenchGlyphStack[] CoreGlyphs { get; }
    internal SpellWorkbenchGlyphStack[] AugmentGlyphs { get; }

    private static SpellWorkbenchGlyphStack[] Copy(SpellWorkbenchGlyphStack[] source)
    {
        var result = new SpellWorkbenchGlyphStack[source.Length];
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index].GlyphId == Guid.Empty || source[index].Count <= 0)
                throw new ArgumentException("Every glyph stack requires a UUID and positive count.");
            result[index] = source[index];
        }
        return result;
    }
}

internal readonly struct SpellWorkbenchGlyphStack
{
    internal SpellWorkbenchGlyphStack(Guid glyphId, int count)
    {
        if (glyphId == Guid.Empty) throw new ArgumentException("A glyph UUID is required.", nameof(glyphId));
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        GlyphId = glyphId;
        Count = count;
    }

    internal Guid GlyphId { get; }
    internal int Count { get; }
}
