using System;

namespace OrbAutomata;

internal readonly struct SpellWorkbenchPricePreviewRequest
{
    internal SpellWorkbenchPricePreviewRequest(
        Guid spellRecipeId,
        long lifecycleEpoch,
        SpellWorkbenchGlyphStack[] augmentGlyphs)
    {
        if (spellRecipeId == Guid.Empty)
            throw new ArgumentException("A spell recipe identity is required.", nameof(spellRecipeId));
        if (augmentGlyphs is null) throw new ArgumentNullException(nameof(augmentGlyphs));
        SpellRecipeId = spellRecipeId;
        LifecycleEpoch = lifecycleEpoch;
        AugmentGlyphs = new SpellWorkbenchGlyphStack[augmentGlyphs.Length];
        Array.Copy(augmentGlyphs, AugmentGlyphs, augmentGlyphs.Length);
    }

    internal Guid SpellRecipeId { get; }
    internal long LifecycleEpoch { get; }
    internal SpellWorkbenchGlyphStack[] AugmentGlyphs { get; }
}

internal readonly struct SpellWorkbenchPricePreviewCost
{
    internal SpellWorkbenchPricePreviewCost(Guid resourceId, BigDouble cost)
    {
        if (resourceId == Guid.Empty)
            throw new ArgumentException("A creation-cost resource identity is required.", nameof(resourceId));
        ResourceId = resourceId;
        Cost = cost;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Cost { get; }
}

internal readonly struct SpellWorkbenchPricePreview
{
    private SpellWorkbenchPricePreview(
        SpellWorkbenchPreflight preflight,
        Guid recipeId,
        SpellWorkbenchPricePreviewCost[] costs,
        bool affordable,
        Guid shortResourceId,
        string reason)
    {
        Preflight = preflight;
        RecipeId = recipeId;
        Costs = costs ?? throw new ArgumentNullException(nameof(costs));
        Affordable = affordable;
        ShortResourceId = shortResourceId;
        Reason = reason ?? string.Empty;
    }

    internal SpellWorkbenchPreflight Preflight { get; }
    internal Guid RecipeId { get; }
    internal SpellWorkbenchPricePreviewCost[] Costs { get; }
    internal bool Affordable { get; }
    internal Guid ShortResourceId { get; }
    internal string Reason { get; }
    internal bool Available => Preflight == SpellWorkbenchPreflight.Proceeded;

    internal static SpellWorkbenchPricePreview Priced(
        Guid recipeId,
        SpellWorkbenchPricePreviewCost[] costs,
        bool affordable,
        Guid shortResourceId) =>
        new(
            SpellWorkbenchPreflight.Proceeded,
            recipeId,
            costs,
            affordable,
            shortResourceId,
            string.Empty);

    internal static SpellWorkbenchPricePreview Refused(
        SpellWorkbenchPreflight preflight,
        string reason) =>
        new(preflight, Guid.Empty, Array.Empty<SpellWorkbenchPricePreviewCost>(), false,
            Guid.Empty, reason);
}
