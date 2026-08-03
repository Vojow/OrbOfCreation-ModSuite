using System;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpCorrectnessCoreTests
{
    [Fact]
    public void ActionRegistrationDependsOnlyOnRuntimeAdmission()
    {
        Assert.True(GameMcpActionRegistrationPolicy.ShouldCompose(
            runtimeActivationAllowed: true));
        Assert.False(GameMcpActionRegistrationPolicy.ShouldCompose(
            runtimeActivationAllowed: false));
    }

    [Fact]
    public void PostStateSettlementRejectsAWorldCapturedBeforeTheActionEvenIfPublishedLater()
    {
        Assert.Equal(1f, GameMcpPostStateSettlement.MaximumWaitSeconds);
        Assert.False(GameMcpPostStateSettlement.IsStrictlyNewer(41, 41));
        Assert.False(GameMcpPostStateSettlement.IsStrictlyNewer(40, 41));
        Assert.True(GameMcpPostStateSettlement.IsStrictlyNewer(42, 41));

        var actionCompleted = DateTime.UtcNow.Ticks;
        var staleCapture = GameMcpTestHarness.Context(
            new GameWorldState { CollectedAtUtcTicks = actionCompleted - 1 },
            generation: 42);
        var settledCapture = GameMcpTestHarness.Context(
            new GameWorldState { CollectedAtUtcTicks = actionCompleted + 1 },
            generation: 42);

        Assert.False(GameMcpPostStateSettlement.HasSettledWorld(
            staleCapture, 41, actionCompleted));
        Assert.True(GameMcpPostStateSettlement.HasSettledWorld(
            settledCapture, 41, actionCompleted));
    }

    [Fact]
    public void TimedOutPostStateCarriesExceptionalEvidenceInsteadOfAnEmptyCommit()
    {
        var value = GameMcpPostStateSettlement.TimedOut(
            GameMcpAcceptanceFixture.NativeCommand(),
            latest: null);

        Assert.Equal(
            "{\"postStateUnavailable\":{\"reasonCode\":\"post_state_timeout\",\"reason\":\"no world captured after the action exposed its committed post-state within one second\"}}",
            GameMcpTestHarness.Json(value).ToString(Newtonsoft.Json.Formatting.None));
    }

    [Fact]
    public void HarvestPostStateReportsTheRequestedActivePairOrItsCompletedMasteryChange()
    {
        var plotId = Guid.Parse("f1000000-0000-0000-0000-000000000001");
        var actionId = Guid.Parse("f1000000-0000-0000-0000-000000000002");
        var before = new GameWorldState
        {
            PlotNodes = PublicationTable<WorldPlotNode>.Create(new[] { Plot(plotId, 4) }),
        };
        var command = new GameMcpCommand(
            1,
            GameMcpCommandKind.Harvest,
            expectedLifecycleGeneration: 9,
            expectedConfigurationGeneration: 3,
            mode: "execute",
            targetId: plotId,
            secondaryId: actionId,
            derivedNativeType: "PlotNodeSO",
            expectedNativeType: string.Empty,
            amount: 1,
            payloadKey: string.Empty,
            payloadValue: string.Empty,
            capture: false,
            saveCapture: false,
            frameContext: GameMcpTestHarness.Context(before));
        var committed = GameMcpCommandResult.Committed("committed", 9, 3);
        var activeWorld = new GameWorldState
        {
            PlotNodes = PublicationTable<WorldPlotNode>.Create(new[] { Plot(plotId, 4) }),
            PlotActionInstances = PublicationTable<WorldPlotActionInstance>.Create(new[]
            {
                new WorldPlotActionInstance(
                    plotId,
                    actionId,
                    ordinal: 0,
                    quantity: 3,
                    engaged: true,
                    empty: false,
                    referenceResolved: true),
            }),
        };

        var active = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(activeWorld), command, committed));
        Assert.Equal("active", (string?)active["state"]);
        Assert.Equal(3, (int)active["amount"]!);
        Assert.Equal(actionId.ToString("D"), (string?)active["action"]!["uuid"]);

        var completedWorld = new GameWorldState
        {
            PlotNodes = PublicationTable<WorldPlotNode>.Create(new[] { Plot(plotId, 5) }),
        };
        var completed = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(completedWorld), command, committed));
        Assert.Equal(4, (int)completed["mastery"]!["before"]!);
        Assert.Equal(5, (int)completed["mastery"]!["after"]!);
    }

    [Fact]
    public void ActionFailureProjectionCarriesOnlyStableCodeAndActionableReason()
    {
        var context = GameMcpTestHarness.Context(
            GameWorldStateDefaults.Empty,
            generation: 77);
        var operation = new GameMcpFrameOperation(
            1,
            new GameMcpOperationRequestBuilder
            {
                ToolName = "game_purchase",
                Classification = GameMcpOperationClass.Gameplay,
            }.Freeze());
        var command = new GameMcpCommand(
            1,
            GameMcpCommandKind.Purchase,
            expectedLifecycleGeneration: 9,
            expectedConfigurationGeneration: 3,
            mode: "purchase",
            targetId: Guid.Parse("01234567-89ab-4cde-8f01-23456789abcd"),
            secondaryId: Guid.Empty,
            derivedNativeType: "StructureSO",
            expectedNativeType: string.Empty,
            amount: 1,
            payloadKey: string.Empty,
            payloadValue: string.Empty,
            capture: false,
            saveCapture: false,
            sourceOperation: operation,
            frameContext: context);

        var response = GameMcpTestHarness.Json(GameMcpCommandResult.Rejected(
            "native_rejected",
            "live native admission refused",
            observedLifecycleGeneration: 0,
            observedConfigurationGeneration: 0).Project(command));

        Assert.Equal("refused", (string?)response["status"]);
        Assert.Equal("native_rejected", (string?)response["code"]);
        Assert.Equal("live native admission refused", (string?)response["reason"]);
        Assert.Equal(3, response.Count);
        Assert.Null(response["worldGeneration"]);
        Assert.Null(response["readWith"]);
        Assert.Null(response["lifecycleGenerationMismatch"]);
        Assert.Null(response["configurationGenerationMismatch"]);
    }

    private static WorldPlotNode Plot(Guid plotId, int masteryLevel) => new(
        new RawPlotNodeSample(
            plotId,
            true,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            masteryLevel,
            false,
            false,
            false,
            false,
            false,
            0,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            0,
            0,
            0),
        0,
        0);
}
