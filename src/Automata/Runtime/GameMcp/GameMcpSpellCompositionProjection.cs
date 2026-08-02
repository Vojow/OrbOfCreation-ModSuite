#if SERVICE_CYCLE_PROFILE
using System;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellCompositionProjection
{
    internal static GameMcpValue Project(in SpellCompositionSubmission submission)
    {
        if (submission.Verified) return new JObject().Freeze();
        var result = new JObject();
        if (submission.Evidence.Available)
        {
            result["nativeStage"] = submission.Stage.ToString();
            result["outcome"] = submission.Outcome.ToString();
            var before = submission.Evidence.Before;
            var after = submission.Evidence.After;
            result["before"] = State(in before);
            result["after"] = State(in after);
        }
        return result.Freeze();
    }

    private static JObject State(in SpellCompositionState state)
    {
        var result = new JObject
        {
            ["outputLevel"] = state.OutputLevel,
            ["maximumOutputLevel"] = state.MaximumOutputLevel,
        };
        if (state.SpellInstanceId != Guid.Empty)
            result["spellInstanceId"] = state.SpellInstanceId.ToString("D");
        if (state.SpellRecipeId != Guid.Empty)
            result["spellRecipeId"] = state.SpellRecipeId.ToString("D");
        if (state.AugmentGlyphs.Length > 0)
        {
            var values = new JArray();
            for (var index = 0; index < state.AugmentGlyphs.Length; index++)
            {
                var value = state.AugmentGlyphs[index];
                values.Add(new JObject
                {
                    ["glyphId"] = value.GlyphId.ToString("D"),
                    ["count"] = value.Count,
                });
            }
            result["augmentGlyphs"] = values;
        }
        return result;
    }
}
#endif
