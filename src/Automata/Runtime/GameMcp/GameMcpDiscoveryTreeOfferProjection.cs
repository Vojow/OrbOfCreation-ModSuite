#if SERVICE_CYCLE_PROFILE
using System;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpDiscoveryTreeOfferProjection
{
    internal static GameMcpValue Project(
        DiscoveryTreeOfferActionKind kind,
        in DiscoveryTreeOfferSubmission submission)
    {
        var receipt = submission.Receipt;
        // A committed mutation is followed by the newer published tree row. That post-state is the
        // complete success receipt; replaying action-local counters here would only duplicate it.
        if (submission.Verified) return new JObject().Freeze();

        var result = new JObject();
        if (receipt.EvidenceAvailable)
        {
            result["nativeStage"] = submission.Stage.ToString();
            result["outcome"] = submission.Outcome.ToString();
        }
        if (receipt.EvidenceAvailable)
        {
            var before = receipt.Before;
            var after = receipt.After;
            var failureReceipt = new JObject
            {
                ["paymentInvoked"] = receipt.PaymentInvoked,
                ["resourcesCharged"] = receipt.ResourcesCharged,
                ["postconditionMatched"] = receipt.PostconditionMatched,
                ["offersPendingNativeIncrement"] = receipt.OffersPendingNativeIncrement,
                ["before"] = State(in before),
                ["after"] = State(in after),
            };
            var costs = Costs(receipt.Costs);
            if (costs.Count > 0) failureReceipt["costs"] = costs;
            result["receipt"] = failureReceipt;
        }
        return result.Freeze();
    }

    private static JObject State(in DiscoveryTreeOfferState state)
    {
        var result = new JObject
        {
            ["mode"] = state.Mode switch
            {
                0 => "idle",
                1 => "crafting",
                2 => "choice",
                _ => "unknown",
            },
            ["actionTime"] = new GameMcpDomainValue(state.ActionTime),
            ["rerollsLeft"] = state.Rerolls,
            ["rerollsMaximum"] = state.MaximumRerolls,
            ["rerollUsed"] = state.UsedRerollsLastDiscover,
            ["discoveredCount"] = state.TotalDiscovered,
            ["poolDiscoveredCount"] = state.PoolDiscovered,
            ["targetResolved"] = state.TargetResolved,
            ["targetDiscovered"] = state.TargetDiscovered,
            ["targetRequired"] = state.TargetRequired,
        };
        var current = Guids(state.CurrentChoices);
        if (current.Count > 0) result["currentOffers"] = current;
        var exclusions = Guids(state.NextExclusions);
        if (exclusions.Count > 0) result["excludedOffers"] = exclusions;
        if (state.SelectedChoice != Guid.Empty)
            result["selectedOffer"] = state.SelectedChoice.ToString("D");
        return result;
    }

    private static JArray Costs(DiscoveryTreeCostReceipt[]? values)
    {
        var result = new JArray();
        if (values is null) return result;
        for (var index = 0; index < values.Length; index++)
        {
            result.Add(new JObject
            {
                ["resourceUuid"] = values[index].ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(values[index].Expected),
                ["balanceBefore"] = new GameMcpDomainValue(values[index].Before),
                ["balanceAfter"] = new GameMcpDomainValue(values[index].After),
                ["affordable"] = values[index].Before.CompareTo(values[index].Expected) >= 0,
                ["observedDelta"] = new GameMcpDomainValue(values[index].Charged),
            });
        }
        return result;
    }

    private static JArray Guids(Guid[]? values)
    {
        var result = new JArray();
        if (values is null) return result;
        for (var index = 0; index < values.Length; index++) result.Add(values[index].ToString("D"));
        return result;
    }
}
#endif
