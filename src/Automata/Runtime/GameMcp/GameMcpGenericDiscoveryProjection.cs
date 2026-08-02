#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbAutomata.GameMcp;

internal static class GameMcpGenericDiscoveryProjection
{
    internal static GameMcpValue Project(in GenericDiscoverySubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();

        var result = new GameMcpObjectBuilder();
        if (submission.Receipt.EvidenceAvailable)
        {
            result["nativeStage"] = submission.Stage.ToString();
            result["outcome"] = submission.Outcome.ToString();
        }
        if (submission.Receipt.EvidenceAvailable)
            result["receipt"] = Receipt(submission.Receipt);
        return result.Freeze();
    }

    private static GameMcpObjectBuilder Receipt(GenericDiscoveryMutationReceipt receipt)
    {
        var result = new GameMcpObjectBuilder
        {
            ["paymentInvoked"] = receipt.PaymentInvoked,
            ["resourcesCharged"] = receipt.ResourcesCharged,
            ["postconditionMatched"] = receipt.PostconditionMatched,
            ["before"] = State(receipt.Before),
            ["after"] = State(receipt.After),
        };
        if (receipt.Costs.Length > 0)
        {
            var costs = new GameMcpArrayBuilder();
            for (var index = 0; index < receipt.Costs.Length; index++)
            {
                var cost = receipt.Costs[index];
                costs.Add(new GameMcpObjectBuilder
                {
                    ["resourceId"] = cost.ResourceId,
                    ["cost"] = new GameMcpDomainValue(cost.Expected),
                    ["amountBefore"] = new GameMcpDomainValue(cost.Before),
                    ["amountAfter"] = new GameMcpDomainValue(cost.After),
                    ["affordable"] = cost.Before.CompareTo(cost.Expected) >= 0,
                    ["observedDelta"] = new GameMcpDomainValue(cost.ObservedDelta),
                });
            }
            result["costs"] = costs;
        }
        return result;
    }

    private static GameMcpObjectBuilder State(GenericDiscoveryState state) =>
        new()
        {
            ["nativeType"] = state.NativeType,
            ["visible"] = state.Visible,
            ["canDiscover"] = state.CanDiscover,
            ["discovered"] = state.Discovered,
            ["requiredDiscovery"] = state.Required,
        };
}
#endif
