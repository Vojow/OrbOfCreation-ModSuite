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
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActionRegistrationDependsOnRuntimeAdmissionNotAutomationConfiguration(
        bool automationEnabled)
    {
        Assert.True(GameMcpActionRegistrationPolicy.ShouldCompose(
            runtimeActivationAllowed: true,
            automationEnabled));
        Assert.False(GameMcpActionRegistrationPolicy.ShouldCompose(
            runtimeActivationAllowed: false,
            automationEnabled));
    }

    [Fact]
    public void PostStateSettlementWaitsOneSecondForOneStrictlyNewerWorld()
    {
        Assert.Equal(1f, GameMcpPostStateSettlement.MaximumWaitSeconds);
        Assert.False(GameMcpPostStateSettlement.IsStrictlyNewer(41, 41));
        Assert.False(GameMcpPostStateSettlement.IsStrictlyNewer(40, 41));
        Assert.True(GameMcpPostStateSettlement.IsStrictlyNewer(42, 41));
    }

    [Fact]
    public void ActionProjectionNamesAdmissionWorldAndSuppressesUnknownGenerationMismatch()
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

        Assert.Equal(77UL, (ulong)response["worldGeneration"]!);
        Assert.Equal("world_get", (string?)response["readWith"]!["tool"]);
        Assert.Equal("structures", (string?)response["readWith"]!["category"]);
        Assert.Equal(command.TargetId.ToString("D"), (string?)response["readWith"]!["uuid"]);
        Assert.Null(response["lifecycleGenerationMismatch"]);
        Assert.Null(response["configurationGenerationMismatch"]);
    }
}
