using System;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

public sealed class AutoBuyPurchaseNarrationTests
{
    private static readonly Guid CandidateId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid BindingResourceId = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");

    [Fact]
    public void Describe_FullUpgradePurchase_IsInfoWithMatchingCounts()
    {
        var submission = Attempted(kind: AutoBuyCandidateKind.Upgrade, before: 4, delta: 3, requested: 3);

        var narration = AutoBuyPurchaseNarration.Describe(AutoBuyCandidateKind.Upgrade, CandidateId, in submission);

        Assert.Equal(AutoBuyPurchaseNarrationLevel.Info, narration.Level);
        Assert.Equal(
            $"Auto Buy purchased 3 of 3 levels for Upgrade {CandidateId:D}.",
            narration.Message);
    }

    [Fact]
    public void Describe_PartialUpgradePurchase_ReportsXofY()
    {
        var submission = Attempted(kind: AutoBuyCandidateKind.Upgrade, before: 0, delta: 2, requested: 3);

        var narration = AutoBuyPurchaseNarration.Describe(AutoBuyCandidateKind.Upgrade, CandidateId, in submission);

        Assert.Equal(AutoBuyPurchaseNarrationLevel.Info, narration.Level);
        Assert.Equal(
            $"Auto Buy purchased 2 of 3 levels for Upgrade {CandidateId:D}.",
            narration.Message);
    }

    [Fact]
    public void Describe_StructurePurchase_ReportsOneOfOne()
    {
        var submission = Attempted(kind: AutoBuyCandidateKind.Structure, before: 2, delta: 1, requested: 1);

        var narration = AutoBuyPurchaseNarration.Describe(AutoBuyCandidateKind.Structure, CandidateId, in submission);

        Assert.Equal(AutoBuyPurchaseNarrationLevel.Info, narration.Level);
        Assert.Equal(
            $"Auto Buy purchased 1 of 1 levels for Structure {CandidateId:D}.",
            narration.Message);
    }

    /// <summary>
    /// A refusal names the term that refused, and reads as the anomaly it is.
    /// </summary>
    /// <remarks>
    /// This line used to say "no longer affordable" for every refusal there is. A live session spent
    /// itself logging that about an upgrade the game was refusing on its queued-level cap, which sent
    /// everyone reading the log to the prices.
    /// </remarks>
    [Fact]
    public void Describe_NotAdmissible_NamesTheTermThatRefused()
    {
        var submission = AutoBuyPurchaseSubmission.Rejected(
            AutoBuyPurchasePreflight.NotAdmissible,
            new AutoBuyAdmissionDiagnosis(
                AutoBuyAdmissionTerm.Passed,
                AutoBuyAdmissionTerm.Passed,
                AutoBuyAdmissionTerm.Refused,
                AutoBuyAdmissionTerm.Passed));

        var narration = AutoBuyPurchaseNarration.Describe(AutoBuyCandidateKind.Upgrade, CandidateId, in submission);

        Assert.Equal(AutoBuyPurchaseNarrationLevel.Warning, narration.Level);
        Assert.Equal(
            $"Auto Buy failed to purchase Upgrade {CandidateId:D}: refused by IsMaxQueuedLevel().",
            narration.Message);
    }

    /// <summary>Every readable term passed, so what refused is the one that cannot be read.</summary>
    [Fact]
    public void Describe_NotAdmissible_AttributesAnUnexplainedRefusalByElimination()
    {
        var submission = AutoBuyPurchaseSubmission.Rejected(
            AutoBuyPurchasePreflight.NotAdmissible,
            new AutoBuyAdmissionDiagnosis(
                AutoBuyAdmissionTerm.Passed,
                AutoBuyAdmissionTerm.Unread,
                AutoBuyAdmissionTerm.Passed,
                AutoBuyAdmissionTerm.Passed));

        var narration = AutoBuyPurchaseNarration.Describe(AutoBuyCandidateKind.Structure, CandidateId, in submission);

        Assert.Equal(AutoBuyPurchaseNarrationLevel.Warning, narration.Level);
        Assert.Equal(
            $"Auto Buy failed to purchase Structure {CandidateId:D}: " +
            "refused by an unreadable admission term (per-level prerequisites by elimination, " +
            "which the planner modelled as met).",
            narration.Message);
    }

    /// <summary>Nothing could be read at all, so the line claims nothing.</summary>
    [Fact]
    public void Describe_NotAdmissible_WithoutAnyReadableTerm_ClaimsNothing()
    {
        var submission = AutoBuyPurchaseSubmission.Rejected(AutoBuyPurchasePreflight.NotAdmissible);

        var narration = AutoBuyPurchaseNarration.Describe(AutoBuyCandidateKind.Upgrade, CandidateId, in submission);

        Assert.Equal(AutoBuyPurchaseNarrationLevel.Warning, narration.Level);
        Assert.Equal(
            $"Auto Buy failed to purchase Upgrade {CandidateId:D}: native admission refused.",
            narration.Message);
    }

    [Fact]
    public void Describe_SingleBuyUnavailable_IsInfoMultiplierUnavailable()
    {
        var submission = AutoBuyPurchaseSubmission.Rejected(AutoBuyPurchasePreflight.SingleBuyUnavailable);

        var narration = AutoBuyPurchaseNarration.Describe(AutoBuyCandidateKind.Upgrade, CandidateId, in submission);

        Assert.Equal(AutoBuyPurchaseNarrationLevel.Info, narration.Level);
        Assert.Equal(
            $"Auto Buy failed to purchase Upgrade {CandidateId:D}: multi-buy multiplier unavailable this cycle.",
            narration.Message);
    }

    [Fact]
    public void Describe_CandidateUnavailable_IsWarning()
    {
        var submission = AutoBuyPurchaseSubmission.Rejected(AutoBuyPurchasePreflight.CandidateUnavailable);

        var narration = AutoBuyPurchaseNarration.Describe(AutoBuyCandidateKind.Structure, CandidateId, in submission);

        Assert.Equal(AutoBuyPurchaseNarrationLevel.Warning, narration.Level);
        Assert.Equal(
            $"Auto Buy failed to purchase Structure {CandidateId:D}: candidate could not be resolved.",
            narration.Message);
    }

    /// <summary>
    /// The game took the call and queued nothing. Without the plan's own numbers this line is
    /// unfalsifiable — it says the game disagreed and nothing about what it disagreed with — so the
    /// binding resource, the price the plan compared, and the holdings it compared against ride
    /// along, at full magnitude.
    /// </summary>
    [Fact]
    public void Describe_AttemptedButNotApplied_PrintsWhatThePlanBelievedAboutThePrice()
    {
        var submission = Attempted(kind: AutoBuyCandidateKind.Upgrade, before: 5, delta: 0, requested: 3);
        var belief = Belief(
            costResourceCount: 2,
            pricedResourceCount: 1,
            cost: new BigDouble(1d, 120L),
            available: new BigDouble(4d, 120L),
            floor: new BigDouble(5d));

        var narration = AutoBuyPurchaseNarration.Describe(
            AutoBuyCandidateKind.Upgrade, CandidateId, in submission, in belief);

        Assert.Equal(AutoBuyPurchaseNarrationLevel.Info, narration.Level);
        Assert.Equal(
            $"Auto Buy skipped 3 levels for Upgrade {CandidateId:D}: native call committed no queued " +
            $"levels. Planned against 1 of 2 cost resource(s); binding {BindingResourceId:D} " +
            "cost 1e120, available 4e120, reserve floor 5e0.",
            narration.Message);
    }

    /// <summary>
    /// A plan that priced nothing says so rather than printing a row of zeroes, because "every cost
    /// row read as nought" and "priced, and the game refused anyway" are different defects and must
    /// not read alike.
    /// </summary>
    [Fact]
    public void Describe_AttemptedButNotApplied_WithoutABindingResource_SaysNothingWasPriced()
    {
        var submission = Attempted(kind: AutoBuyCandidateKind.Structure, before: 0, delta: 0, requested: 1);

        var narration = AutoBuyPurchaseNarration.Describe(
            AutoBuyCandidateKind.Structure, CandidateId, in submission);

        Assert.Equal(AutoBuyPurchaseNarrationLevel.Info, narration.Level);
        Assert.Equal(
            $"Auto Buy skipped 1 levels for Structure {CandidateId:D}: native call committed no " +
            "queued levels. The plan carried no cost evidence for this candidate.",
            narration.Message);
    }

    [Fact]
    public void QueueReserveReached_IsInfoWithSlotCounts()
    {
        var narration = AutoBuyPurchaseNarration.QueueReserveReached(
            AutoBuyCandidateKind.Upgrade, CandidateId, freeSlots: 1, reservedSlots: 1);

        Assert.Equal(AutoBuyPurchaseNarrationLevel.Info, narration.Level);
        Assert.Equal(
            $"Auto Buy failed to purchase Upgrade {CandidateId:D}: queue reserve reached (1 slots free, reserving 1).",
            narration.Message);
    }

    [Fact]
    public void QueueRoomUnavailable_IsWarning()
    {
        var narration = AutoBuyPurchaseNarration.QueueRoomUnavailable(AutoBuyCandidateKind.Structure, CandidateId);

        Assert.Equal(AutoBuyPurchaseNarrationLevel.Warning, narration.Level);
        Assert.Equal(
            $"Auto Buy failed to purchase Structure {CandidateId:D}: queue room unavailable.",
            narration.Message);
    }

    private static AutoBuyPlanBelief Belief(
        int costResourceCount,
        int pricedResourceCount,
        BigDouble cost,
        BigDouble available,
        BigDouble floor) =>
        new(
            isAvailable: true,
            hasFiniteLevels: false,
            isMaxLevel: false,
            isMaxQueuedLevel: false,
            currentLevel: 0,
            queuedLevels: 0,
            costResourceCount,
            pricedResourceCount,
            costRatio: 0.25,
            BindingResourceId,
            bindingIsBandwidth: false,
            cost,
            available,
            floor);

    // Builds a real submission through the audited verifier so the committed/requested counts and
    // Verified classification come from production code, not a test-only constructor.
    private static AutoBuyPurchaseSubmission Attempted(
        AutoBuyCandidateKind kind,
        int before,
        int delta,
        int requested)
    {
        var queued = before;
        var evidence = NativeMutationVerifier.Execute(
            kind == AutoBuyCandidateKind.Structure ? "Auto Buy Structure" : "Auto Buy Upgrade",
            CandidateId.ToString(),
            $"delta in [1, {requested}]",
            () => queued,
            () => queued += delta,
            (start, end) => end > start && end <= start + requested);
        return AutoBuyPurchaseSubmission.Attempted(evidence, requested);
    }
}
