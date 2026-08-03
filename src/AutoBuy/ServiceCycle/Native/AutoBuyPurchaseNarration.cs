using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Actionable Auto Buy refusal and failure lines for the general BepInEx log.</summary>
internal static class AutoBuyPurchaseNarration
{
    /// <summary>
    /// The live queue room could not be read, so the reserve cannot be honoured and no purchase is
    /// submitted. A missing native surface is an actionable anomaly.
    /// </summary>
    public static string QueueRoomUnavailable(AutoBuyCandidateKind kind, Guid uuid) =>
        $"Auto Buy failed to purchase {kind} {uuid:D}: queue room unavailable.";

    /// <summary>
    /// Returns one warning for a refusal that requires attention, or null for successful and ordinary
    /// no-op outcomes already represented by the compact action journal.
    /// </summary>
    public static string? DescribeWarning(
        AutoBuyCandidateKind kind,
        Guid uuid,
        in AutoBuyPurchaseSubmission submission)
    {
        if (submission.Verified ||
            submission.Preflight == AutoBuyPurchasePreflight.NotAdmissible ||
            submission.Preflight == AutoBuyPurchasePreflight.SingleBuyUnavailable)
        {
            return null;
        }

        var candidate = $"{kind} {uuid:D}";
        return submission.Preflight switch
        {
            AutoBuyPurchasePreflight.CandidateUnavailable =>
                $"Auto Buy failed to purchase {candidate}: candidate could not be resolved.",
            AutoBuyPurchasePreflight.AffordabilityUnavailable =>
                $"Auto Buy failed to purchase {candidate}: live affordability could not be read.",
            AutoBuyPurchasePreflight.OwningViewUnavailable =>
                Refusal(candidate, "owning view unavailable"),
            AutoBuyPurchasePreflight.OwningViewRelationMissing =>
                Refusal(candidate, "owning view relation missing"),
            AutoBuyPurchasePreflight.OwningViewRelationUnreadable =>
                Refusal(candidate, "owning view relation unreadable"),
            AutoBuyPurchasePreflight.OwningViewRelationContradictory =>
                Refusal(candidate, "owning view relation contradictory"),
            AutoBuyPurchasePreflight.StructureUnavailable =>
                Refusal(candidate, "structure unavailable"),
            AutoBuyPurchasePreflight.DestinationCapacityFull =>
                Refusal(candidate, "destination capacity full"),
            AutoBuyPurchasePreflight.DestinationCapacityContractUnavailable =>
                Refusal(candidate, "destination capacity contract unavailable"),
            AutoBuyPurchasePreflight.DestinationCapacityIdentityMismatch =>
                Refusal(candidate, "destination capacity identity mismatch"),
            _ =>
                $"Auto Buy failed to purchase {submission.RequestedLevels} levels for {candidate}: native mutation did not apply.",
        };
    }

    private static string Refusal(string candidate, string reason) =>
        $"Auto Buy failed to purchase {candidate}: {reason}.";
}
