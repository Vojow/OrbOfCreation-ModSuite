using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpCastTests
{
    private static readonly Guid RecipeId =
        Guid.Parse("d8c42ced-12de-4bc7-bf3a-f11a13318e42");
    private static readonly Guid InstanceId =
        Guid.Parse("f9ec2758-ce33-4fcb-883a-5283035254a6");

    [Fact]
    public void ToolOffersTheNativeFireReleaseAndToggleOffButtonPaths()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_cast");

        Assert.Equal(
            new[] { "fire", "release", "toggle_off" },
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!
                .Values<string>()
                .ToArray());
    }

    [Theory]
    [InlineData(true, true, null)]
    [InlineData(false, false, "cancellable_spells_disabled")]
    public void ActiveToggleRowsPublishWhetherThePlayersSettingAllowsTheNextPress(
        bool cancellationEnabled,
        bool available,
        string? reasonCode)
    {
        var world = World(casting: true, cancellationEnabled);

        var row = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectEntityState(
            world,
            "spell-slots",
            world.SpellSlots[0]));

        Assert.Equal(available, (bool)row["toggleOff"]!["available"]!);
        Assert.Equal(reasonCode, (string?)row["toggleOff"]!["reasonCode"]);
    }

    [Fact]
    public void SettledToggleOffRequiresAndReturnsTheObservedActiveToInactiveTransition()
    {
        var completedAt = DateTime.UtcNow.Ticks;
        var before = World(
            casting: true,
            cancellationEnabled: true,
            collectedAtUtcTicks: completedAt - 1);
        var after = World(
            casting: false,
            cancellationEnabled: true,
            collectedAtUtcTicks: completedAt + 1);
        var command = Command(GameMcpTestHarness.Context(before, generation: 41));
        var settled = GameMcpTestHarness.Context(after, generation: 42);

        Assert.True(GameMcpPostStateSettlement.IsReady(
            settled,
            mutationWorld: 41,
            actionCompletedAtUtcTicks: completedAt,
            command));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            settled,
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));
        Assert.Equal(RecipeId.ToString("D"), (string?)delta["uuid"]);
        Assert.Equal(0, (int)delta["slot"]!);
        Assert.True((bool)delta["active"]!["before"]!);
        Assert.False((bool)delta["active"]!["after"]!);

        var unchanged = GameMcpTestHarness.Context(
            World(
                casting: true,
                cancellationEnabled: true,
                collectedAtUtcTicks: completedAt + 1),
            generation: 42);
        Assert.False(GameMcpPostStateSettlement.IsReady(
            unchanged,
            mutationWorld: 41,
            actionCompletedAtUtcTicks: completedAt,
            command));
    }

    private static GameMcpCommand Command(GameMcpFrameContext before) => new(
        1,
        GameMcpCommandKind.Cast,
        9,
        3,
        "toggle_off",
        RecipeId,
        Guid.Empty,
        "SpellRecipeSO",
        1,
        string.Empty,
        string.Empty,
        false,
        false,
        frameContext: before);

    private static GameWorldState World(
        bool casting,
        bool cancellationEnabled,
        long collectedAtUtcTicks = 0) => new()
    {
        CollectedAtEpoch = 9,
        CollectedAtUtcTicks = collectedAtUtcTicks,
        SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[]
        {
            new WorldSpellSlot(
                0,
                InstanceId,
                RecipeId,
                occupied: true,
                casting,
                readyingCast: false,
                attuning: false,
                channeled: false,
                toggled: true,
                chargeable: false,
                castReady: true,
                chargeAvailable: true,
                canRemove: false,
                resourcesCovered: true,
                currentCharges: 1,
                maximumCharges: 1,
                cooldownRemaining: BigDouble.Zero,
                outputLevel: 1,
                effectiveLevel: 1,
                requiredMasteryLevel: 0,
                recipeMasteryLevel: 1,
                durationSpell: true,
                usageRequirementsMet: true,
                augmentGlyphs: PublicationTable<WorldSpellSlotGlyph>.Empty,
                cancellationEnabled: cancellationEnabled),
        }),
    };
}
