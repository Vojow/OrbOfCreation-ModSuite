using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

/// <summary>
/// Pure predicates over the immutable configuration that gate whether the Spell Leveling service
/// should run. Spell Leveling owns no settings of its own: it rides Auto Buy's
/// <c>AutoLevelSpells</c> switch and Auto Buy's active gate.
/// </summary>
internal static class SpellLevelConfigurationPolicy
{
    internal static bool IsOperational(SuiteRuntimeConfiguration configuration) =>
        configuration.General.Enabled &&
        configuration.CanStartAutoBuyActively &&
        configuration.AutoBuy.AutoLevelSpells;
}
