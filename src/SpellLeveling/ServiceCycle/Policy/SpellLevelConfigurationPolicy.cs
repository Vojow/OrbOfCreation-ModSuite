using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Pure predicates over the immutable configuration that gate whether the Spell Leveling service
/// should run and how often it wakes. Spell Leveling owns no settings of its own: it rides Auto Buy's
/// <c>AutoLevelSpells</c> switch and Auto Buy's active gate.
/// </summary>
internal static class SpellLevelConfigurationPolicy
{
    /// <summary>How long the service idles between decisions when nothing configures it (seconds).</summary>
    internal const float DefaultEvaluationIntervalSeconds = 1.0f;

    internal static bool IsOperational(SuiteRuntimeConfiguration configuration) =>
        configuration.General.Enabled &&
        configuration.CanStartAutoBuyActively &&
        configuration.AutoBuy.AutoLevelSpells;

    /// <summary>
    /// How long to wait before looking again. Spell Leveling's natural cadence is "whenever mastery
    /// experience moved", and a new world generation is exactly that, so the interval is a floor on
    /// re-evaluation rather than the thing that drives it. That is why the feature needs no signal
    /// patch telling it a purchase finished: the generation gate already carries that news.
    /// </summary>
    internal static MonotonicDuration EvaluationInterval(SuiteRuntimeConfiguration configuration)
    {
        var seconds = configuration.AutoBuy.EvaluationIntervalSeconds;
        if (!(seconds > 0f)) seconds = DefaultEvaluationIntervalSeconds;
        return MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(seconds));
    }
}
