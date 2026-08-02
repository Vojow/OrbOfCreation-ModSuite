using System;
using System.Globalization;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// How prominent an Auto Buy purchase decision line is in the always-on log. An expected outcome —
/// a committed purchase, a queue reserve reached, or a zero-delta native no-op — is
/// <see cref="Info"/>; a genuine anomaly is <see cref="Warning"/>. A native admission refusal is an
/// anomaly: the worker planned from published rows and the game disagreed, which is a planner bug
/// however ordinary it looks in a log.
/// </summary>
internal enum AutoBuyPurchaseNarrationLevel
{
    Info = 0,
    Warning = 1,
}

/// <summary>One always-on decision line describing the outcome of a single purchase submission.</summary>
internal readonly struct AutoBuyPurchaseNarration
{
    public AutoBuyPurchaseNarration(AutoBuyPurchaseNarrationLevel level, string message)
    {
        Level = level;
        Message = message;
    }

    public AutoBuyPurchaseNarrationLevel Level { get; }
    public string Message { get; }

    /// <summary>
    /// The candidate was skipped because committing it would eat into the operator's reserved queue
    /// slots (<c>LeaveQueueSlots</c>). Expected once the queue fills, so it is <see cref="Info"/>.
    /// </summary>
    public static AutoBuyPurchaseNarration QueueReserveReached(
        AutoBuyCandidateKind kind,
        Guid uuid,
        int freeSlots,
        int reservedSlots) =>
        new(
            AutoBuyPurchaseNarrationLevel.Info,
            $"Auto Buy failed to purchase {kind} {EntityIdentityFormatter.Format(uuid)}: queue reserve reached ({freeSlots} slots free, reserving {reservedSlots}).");

    /// <summary>
    /// The live queue room could not be read, so the reserve cannot be honoured and no purchase is
    /// submitted. A missing native surface is an anomaly, so it is <see cref="Warning"/>.
    /// </summary>
    public static AutoBuyPurchaseNarration QueueRoomUnavailable(AutoBuyCandidateKind kind, Guid uuid) =>
        new(
            AutoBuyPurchaseNarrationLevel.Warning,
            $"Auto Buy failed to purchase {kind} {EntityIdentityFormatter.Format(uuid)}: queue room unavailable.");

    /// <summary>
    /// Renders the human-readable "purchased X of Y" / "failed to purchase" line for a completed
    /// submission. Pure and deterministic so the phrasing and success/anomaly classification can be
    /// asserted without a live logger; the adapter emits the result through <c>Plugin.Log</c>.
    /// </summary>
    public static AutoBuyPurchaseNarration Describe(
        AutoBuyCandidateKind kind,
        Guid uuid,
        in AutoBuyPurchaseSubmission submission,
        in AutoBuyPlanBelief belief = default)
    {
        var candidate = $"{kind} {EntityIdentityFormatter.Format(uuid)}";
        if (submission.Verified)
        {
            return new AutoBuyPurchaseNarration(
                AutoBuyPurchaseNarrationLevel.Info,
                $"Auto Buy purchased {submission.CommittedLevels} of {submission.RequestedLevels} levels for {candidate}.");
        }
        if (submission.Preflight == AutoBuyPurchasePreflight.Proceeded &&
            submission.Outcome == NativeMutationOutcome.PostconditionFailed &&
            submission.CommittedLevels == 0)
        {
            // The game accepted the call and queued nothing, which almost always means it disagreed
            // about the price. Printing the plan's own arithmetic turns that from an unfalsifiable
            // "the game said no" into a comparison anyone can check against the game's UI: if the
            // cost we planned on is not the cost on screen, the capture is wrong; if it matches and
            // the holdings do not, the resource read is wrong.
            return new AutoBuyPurchaseNarration(
                AutoBuyPurchaseNarrationLevel.Info,
                $"Auto Buy skipped {submission.RequestedLevels} levels for {candidate}: " +
                $"native call committed no queued levels. {Evidence(in belief)}");
        }

        return submission.Preflight switch
        {
            // The reason is the boundary's own split reads, not a guess. This line used to say "no
            // longer affordable" for every refusal there is, which sent a session's worth of logs
            // pointing at the price while the game was refusing on a level cap.
            AutoBuyPurchasePreflight.NotAdmissible => new AutoBuyPurchaseNarration(
                AutoBuyPurchaseNarrationLevel.Warning,
                $"Auto Buy failed to purchase {candidate}: {submission.Diagnosis.Describe()}."),
            AutoBuyPurchasePreflight.SingleBuyUnavailable => new AutoBuyPurchaseNarration(
                AutoBuyPurchaseNarrationLevel.Info,
                $"Auto Buy failed to purchase {candidate}: multi-buy multiplier unavailable this cycle."),
            AutoBuyPurchasePreflight.CandidateUnavailable => new AutoBuyPurchaseNarration(
                AutoBuyPurchaseNarrationLevel.Warning,
                $"Auto Buy failed to purchase {candidate}: candidate could not be resolved."),
            AutoBuyPurchasePreflight.OwningViewUnavailable => Refusal(
                candidate, "owning view unavailable"),
            AutoBuyPurchasePreflight.OwningViewRelationMissing => Refusal(
                candidate, "owning view relation missing"),
            AutoBuyPurchasePreflight.OwningViewRelationUnreadable => Refusal(
                candidate, "owning view relation unreadable"),
            AutoBuyPurchasePreflight.OwningViewRelationContradictory => Refusal(
                candidate, "owning view relation contradictory"),
            AutoBuyPurchasePreflight.StructureUnavailable => Refusal(
                candidate, "structure unavailable"),
            AutoBuyPurchasePreflight.DestinationCapacityFull => Refusal(
                candidate, "destination capacity full"),
            AutoBuyPurchasePreflight.DestinationCapacityContractUnavailable => Refusal(
                candidate, "destination capacity contract unavailable"),
            AutoBuyPurchasePreflight.DestinationCapacityIdentityMismatch => Refusal(
                candidate, "destination capacity identity mismatch"),
            // Proceeded but not verified with a nonzero incoherent delta.
            _ => new AutoBuyPurchaseNarration(
                AutoBuyPurchaseNarrationLevel.Warning,
                $"Auto Buy failed to purchase {submission.RequestedLevels} levels for {candidate}: native mutation did not apply."),
        };
    }

    private static AutoBuyPurchaseNarration Refusal(string candidate, string reason) =>
        new(
            AutoBuyPurchaseNarrationLevel.Warning,
            $"Auto Buy failed to purchase {candidate}: {reason}.");

    /// <summary>
    /// What the plan believed about the price, in the terms it actually compared.
    /// </summary>
    /// <remarks>
    /// A belief with no binding resource is not silently rendered as zeros — it says so, because a
    /// candidate whose every cost row priced at nought is a different defect from one that was
    /// priced and refused anyway, and the two must not read alike in a log.
    /// </remarks>
    private static string Evidence(in AutoBuyPlanBelief belief)
    {
        if (belief.BindingResourceId == Guid.Empty)
        {
            return belief.CostResourceCount == 0
                ? "The plan carried no cost evidence for this candidate."
                : $"The plan priced none of its {belief.CostResourceCount} cost resource(s) above nought.";
        }

        return $"Planned against {belief.PricedResourceCount} of {belief.CostResourceCount} " +
            $"cost resource(s); binding {EntityIdentityFormatter.Format(belief.BindingResourceId)} cost {Magnitude(belief.BindingCost)}, " +
            $"available {Magnitude(belief.BindingAvailable)}, reserve floor {Magnitude(belief.BindingReserveFloor)}.";
    }

    /// <summary>
    /// A magnitude written as the game holds it. The game's own formatting abbreviates and rounds,
    /// and a line that exists to expose a number the plan got wrong is exactly where rounding must
    /// not happen.
    /// </summary>
    private static string Magnitude(BigDouble value) =>
        value.Mantissa.ToString("R", CultureInfo.InvariantCulture) + "e" +
        value.Exponent.ToString(CultureInfo.InvariantCulture);
}
