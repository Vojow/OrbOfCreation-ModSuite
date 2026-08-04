using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbAutomata;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpSemanticCodeTests
{
    [Fact]
    public void Player_facing_action_codes_are_semantic()
    {
        var cases = new[]
        {
            (GameMcpCommandKind.RitualLifecycle, RitualLifecycleActionResultCodes.LevelOutOfRange, "level_out_of_range"),
            (GameMcpCommandKind.RitualLifecycle, RitualLifecycleActionResultCodes.BattleAlreadyActive, "ritual_battle_active"),
            (GameMcpCommandKind.RitualLifecycle, RitualLifecycleActionResultCodes.NoBattleActive, "no_ritual_battle_active"),
            (GameMcpCommandKind.RitualLifecycle, RitualLifecycleActionResultCodes.WrongActiveRitual, "wrong_active_ritual"),
            (GameMcpCommandKind.HarvestLifecycle, HarvestLifecycleActionResultCodes.ElementUsageUnavailable, "element_capacity_unavailable"),
            (GameMcpCommandKind.HarvestLifecycle, HarvestLifecycleActionResultCodes.ActionUnavailable, "action_not_available"),
            (GameMcpCommandKind.HarvestLifecycle, HarvestLifecycleActionResultCodes.AmountUnavailable, "amount_not_available"),
            (GameMcpCommandKind.Harvest, PlotLifecycleActionResultCodes.ActionUnavailable, "action_not_available"),
            (GameMcpCommandKind.Harvest, PlotLifecycleActionResultCodes.QuantityUnavailable, "amount_not_available"),
        };
        foreach (var (kind, code, expected) in cases)
        {
            var action = ServiceActionResult.Rejected(code);
            var result = GameMcpCommandResult.FromAction(in action, kind, 1, 1);

            Assert.Equal(expected, result.Code);
            Assert.DoesNotContain("feature_", result.Code, System.StringComparison.Ordinal);
        }
    }
}
