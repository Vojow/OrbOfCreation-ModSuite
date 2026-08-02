#if SERVICE_CYCLE_PROFILE
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpEquipmentLoadoutProjection
{
    internal static GameMcpValue Project(in EquipmentLoadoutSubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder
        {
            ["preflight"] = GameMcpEntityWireNormalizer.Snake(submission.Preflight.ToString()),
        };
        if (submission.Receipt.EvidenceAvailable)
        {
            result["nativeStage"] = GameMcpEntityWireNormalizer.Snake(submission.Stage.ToString());
            result["outcome"] = GameMcpEntityWireNormalizer.Snake(submission.Outcome.ToString());
            result["requestedMode"] = GameMcpEntityWireNormalizer.Snake(submission.Receipt.Kind.ToString());
            result["requestedAmount"] = new GameMcpDomainValue(
                new BigDouble(submission.Receipt.RequestedAmount));
            result["before"] = State(submission.Receipt.Before);
            result["after"] = State(submission.Receipt.After);
        }
        return result.Freeze();
    }

    private static GameMcpObjectBuilder State(EquipmentLoadoutState state) => new()
    {
        ["equippedStacks"] = new GameMcpDomainValue(new BigDouble(state.EquippedStacks)),
        ["maximumStacks"] = new GameMcpDomainValue(new BigDouble(state.MaximumStacks)),
        ["multiBuy"] = new GameMcpDomainValue(new BigDouble(state.MultiBuy)),
        ["usedSlots"] = new GameMcpDomainValue(new BigDouble(state.UsedSlots)),
        ["maximumSlots"] = new GameMcpDomainValue(new BigDouble(state.MaximumSlots)),
        ["typeUsedSlots"] = new GameMcpDomainValue(new BigDouble(state.TypeUsedSlots)),
        ["typeMaximumSlots"] = new GameMcpDomainValue(new BigDouble(state.TypeMaximumSlots)),
        ["usageAffordable"] = state.UsageAffordable,
        ["maximumAffordableStacks"] = new GameMcpDomainValue(
            new BigDouble(state.MaximumAffordableStacks)),
    };
}
#endif
