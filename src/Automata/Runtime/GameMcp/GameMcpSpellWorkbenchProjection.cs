#if SERVICE_CYCLE_PROFILE
using System;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellWorkbenchProjection
{
    internal static GameMcpValue Project(in SpellWorkbenchSubmission submission)
    {
        if (submission.Verified) return new JObject().Freeze();
        var result = new JObject
        {
            ["preflight"] = GameMcpEntityWireNormalizer.Snake(submission.Preflight.ToString()),
        };
        if (submission.Evidence.Available)
        {
            result["nativeStage"] = GameMcpEntityWireNormalizer.Snake(submission.Stage.ToString());
            result["outcome"] = GameMcpEntityWireNormalizer.Snake(submission.Outcome.ToString());
            var before = submission.Evidence.Before;
            var after = submission.Evidence.After;
            result["before"] = State(in before);
            result["after"] = State(in after);
        }
        if (submission.Preflight is SpellWorkbenchPreflight.Quarantined or
            SpellWorkbenchPreflight.PostCommitFault or SpellWorkbenchPreflight.VerificationFailed)
            result["quarantined"] = true;
        return result.Freeze();
    }

    private static JObject State(in SpellWorkbenchState state)
    {
        var result = new JObject
        {
            ["targetDiscovered"] = state.TargetDiscovered,
        };
        if (state.ResolvedRecipeId != Guid.Empty)
            result["resolvedRecipeId"] = state.ResolvedRecipeId.ToString("D");
        Add(result, "coreGlyphs", state.CoreGlyphIds);
        Add(result, "augmentGlyphs", state.AugmentGlyphIds);
        Add(result, "spellInstances", state.TargetSpellInstanceIds);
        return result;
    }

    private static void Add(JObject target, string name, Guid[] values)
    {
        if (values.Length == 0) return;
        var array = new JArray();
        for (var index = 0; index < values.Length; index++)
            if (values[index] != Guid.Empty) array.Add(values[index].ToString("D"));
        if (array.Count > 0) target[name] = array;
    }
}
#endif
