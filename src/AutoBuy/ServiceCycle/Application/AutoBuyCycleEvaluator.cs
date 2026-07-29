using System;
using System.Collections.Generic;
using System.Globalization;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbAutomata;

/// <summary>
/// The pure Auto Buy worker policy: a stateless per-capture batch planner. Given one native-free
/// <see cref="AutoBuyCycleFrame"/> and the pinned <see cref="SuiteRuntimeConfiguration"/> it plans one
/// purchase per eligible candidate in ranked order — the faithful replacement for the legacy
/// engine's admission → reserve/affordability → ranked-pass → grouping pipeline — and returns an
/// <c>AfterDecision</c> wake anchored on the capture time. It owns no hidden control state: no
/// ranked-pass cursor, no group/batch counter, no retry/backoff. Fairness and pacing come entirely
/// from one-action-per-pump batch execution plus the wake cadence, so it can never re-plan stale
/// facts. All magnitude math stays on the game's own <see cref="BigDouble"/> end to end — no copy,
/// no conversion — exactly as the raw quantities and costs were captured.
/// </summary>
/// <remarks>
/// Pillar A plans ONE action per eligible candidate per cycle: the batch cascade-terminates on the
/// first native rejection, so planning a candidate twice is wasted work (the next cycle re-plans from
/// fresh facts, and an already-queued slot is retained). One action is not one queue slot: a bulk
/// grouping mode raises that action's requested level <see cref="AutoBuyCycleAction.Count"/> to the
/// game's own live count — the MultiBuy multiplier for an upgrade, the bulk-development count for a
/// structure — so one action advances several levels. Nothing here bounds the plan by the queue.
/// The action adapter is
/// the real-time authority that re-validates and submits each call, stopping the cascade the moment
/// one is rejected; pacing is <c>AfterDecision</c>, not per-item backoff. The adapter re-reads the
/// live queue room before every submission, rejects — cascade-terminating the batch — once only
/// <see cref="AutoBuyConfiguration.LeaveQueueSlots"/> remain, and clamps a multi-level request to the
/// room above that reserve, since the game queues one entry per level. So the plan is as long as the
/// ranked list and the runtime stops it at the truth. A cost-curve-aware planner that buys many
/// affordable levels of a candidate in one cycle is deferred to Pillar B.
/// </remarks>
internal static class AutoBuyCycleEvaluator
{
    private static readonly IComparer<Eligible> RankOrder = Comparer<Eligible>.Create(static (left, right) =>
    {
        // PriorityRank descending, then CostRatio ascending, then UUID ascending (OrdinalIgnoreCase)
        // — byte-for-byte the legacy ranked-pass ordering.
        var priority = right.PriorityRank.CompareTo(left.PriorityRank);
        if (priority != 0) return priority;
        var ratio = left.CostRatio.CompareTo(right.CostRatio);
        if (ratio != 0) return ratio;
        return string.Compare(left.UuidText, right.UuidText, StringComparison.OrdinalIgnoreCase);
    });

    public static WakePolicy Evaluate(
        in AutoBuyCycleFrame frame,
        in SuiteRuntimeConfiguration config,
        ServiceActionWriter<AutoBuyCycleAction> actions,
        out AutoBuyDecisionMetrics metrics)
    {
        var wake = WakePolicy.AfterDecision(AutoBuyConfigurationPolicy.EvaluationInterval(config));
        metrics = new AutoBuyDecisionMetrics(
            frame.StructureCount,
            frame.UpgradeCount,
            eligibleCandidates: 0,
            plannedActions: 0,
            requestedLevels: 0);

        // Whole-service gates: an inactive/disabled config or a live emergency plans nothing but
        // still reschedules so the service resumes the moment the operator re-enables it.
        if (!AutoBuyConfigurationPolicy.IsOperational(config))
            return wake;

        // The reserve floor is config-only. An empty/malformed AbsoluteReserve falls back to the
        // config's own default of no reserve (0) — the legacy reject-everything path only ever fired
        // on a malformed config that production never produces.
        var absoluteReserve = ResolveAbsoluteReserve(config.Reserves.AbsoluteReserve);
        var relativeMultiplier = Math.Max(0.0, config.Reserves.RelativeReserveMultiplier);

        var allowed = ParseUuidSet(config.AutoBuy.AllowedUuids);
        var blocked = ParseUuidSet(config.AutoBuy.BlockedUuids);

        var candidates = frame.Candidates;
        var resources = frame.Resources;
        var costs = frame.Costs;

        // The worker holds no state between cycles (the framework's worker-definition validator
        // enforces this), so the ranked-pass scratch is a per-cycle local. The evaluation runs off
        // the main thread at the configured cadence, where this allocation is immaterial.
        var eligible = new List<Eligible>();

        // One counter per exclusion term. Every candidate that does not reach the plan increments
        // exactly one of them, so the histogram and the eligible count always add back up to the
        // captured count — a term that starts silently swallowing candidates cannot hide in a total.
        var excluded = new int[ExclusionTermCount];
        for (var i = 0; i < candidates.Length; i++)
        {
            ref readonly var candidate = ref candidates[i];
            var admission = Admit(in candidate, config, allowed, blocked);
            if (admission != AutoBuyExclusion.None)
            {
                excluded[(int)admission]++;
                continue;
            }

            if (!TryEvaluateAffordability(
                    in candidate,
                    costs,
                    resources,
                    in absoluteReserve,
                    relativeMultiplier,
                    config,
                    out var costRatio,
                    out var belief,
                    out var refusal))
            {
                excluded[(int)refusal]++;
                continue;
            }

            eligible.Add(new Eligible(
                i,
                candidate.Kind,
                candidate.Uuid,
                candidate.Uuid.ToString("D", CultureInfo.InvariantCulture),
                costRatio,
                PriorityRank(in candidate, config),
                in belief));
        }

        if (eligible.Count == 0)
        {
            metrics = new AutoBuyDecisionMetrics(
                frame.StructureCount,
                frame.UpgradeCount,
                eligibleCandidates: 0,
                plannedActions: 0,
                requestedLevels: 0,
                Histogram(excluded));
            return wake;
        }

        eligible.Sort(RankOrder);

        // One action (one queue slot) per eligible candidate, in ranked order. Each action requests
        // `count` levels via a single native call: no per-candidate overcommit, but a bulk grouping
        // mode lets one call buy several levels.
        //
        // Nothing here bounds the plan by the queue. FillAvailableQueue means "keep going until only
        // LeaveQueueSlots remain", and only the action boundary can know when that is — it re-reads
        // the live room before every submission, clamps the request to what fits above the reserve,
        // and the first submission that does not fit cascade-terminates the rest of the batch. So
        // the plan is as long as the ranked list and the runtime stops it at the truth.
        var maximumActions = eligible.Count;
        if (config.AutoBuy.BatchSizing == AutoBuyBatchSizingMode.Fixed)
            maximumActions = Math.Min(maximumActions, Math.Max(1, config.AutoBuy.MaxPurchasesPerBatch));

        // Eligibility asked "can this candidate be afforded on its own", against untouched quantities.
        // A batch spends for real: the game deducts the cost when a purchase is queued, not when it
        // completes, so by the time the fourth action runs the first three have already been paid for.
        // Without a ledger every action in the batch clears the same floor against the same numbers
        // and the batch as a whole spends straight through it.
        var committed = resources.Length == 0 ? Array.Empty<BigDouble>() : new BigDouble[resources.Length];

        var emitted = 0;
        var requestedLevels = 0;
        for (var i = 0; i < eligible.Count && emitted < maximumActions; i++)
        {
            var decision = eligible[i];
            var count = PerCandidateAmount(decision.Kind, config, frame.Global);
            ref readonly var candidate = ref candidates[decision.CandidateIndex];
            if (!TryCommitSpend(
                    in candidate, costs, resources, committed, count, in absoluteReserve, relativeMultiplier))
                continue; // an earlier action in this batch already spent what this one needed

            var action = new AutoBuyCycleAction(
                decision.Kind, decision.Uuid, frame.Global.CollectedAtEpoch, count, decision.Belief);
            actions.Add(in action);
            emitted++;
            requestedLevels = checked(requestedLevels + count);
        }

        metrics = new AutoBuyDecisionMetrics(
            frame.StructureCount,
            frame.UpgradeCount,
            eligible.Count,
            emitted,
            requestedLevels,
            Histogram(excluded));
        return wake;
    }

    /// <summary>One more than the highest <see cref="AutoBuyExclusion"/> member.</summary>
    private const int ExclusionTermCount = (int)AutoBuyExclusion.Unpriceable + 1;

    private static AutoBuyExclusionHistogram Histogram(int[] excluded) =>
        new(
            excluded[(int)AutoBuyExclusion.KindNotSelected],
            excluded[(int)AutoBuyExclusion.Blocklisted],
            excluded[(int)AutoBuyExclusion.NotAllowlisted],
            excluded[(int)AutoBuyExclusion.Unavailable],
            excluded[(int)AutoBuyExclusion.RequirementsUnmet],
            excluded[(int)AutoBuyExclusion.Terminal],
            excluded[(int)AutoBuyExclusion.Unaffordable],
            excluded[(int)AutoBuyExclusion.Unpriceable]);

    /// <summary>
    /// Which admission term excluded this candidate, or <see cref="AutoBuyExclusion.None"/> when
    /// none did.
    /// </summary>
    /// <remarks>
    /// The terms are tested in the same order as before and the first one that refuses is the one
    /// reported, so the histogram attributes a candidate to the earliest reason rather than to all of
    /// them. That matches how an operator reads it: a blocklisted candidate that is also maxed out is
    /// blocklisted, and unblocking it is what would change the answer.
    /// </remarks>
    private static AutoBuyExclusion Admit(
        in AutoBuyCandidateRow candidate,
        in SuiteRuntimeConfiguration config,
        HashSet<Guid>? allowed,
        HashSet<Guid>? blocked)
    {
        // Kind selection — the action adapter revalidates this too, so a config that drops a family
        // between planning and execution can never commit an unwanted purchase.
        if (!AutoBuyConfigurationPolicy.IsSelected(config, candidate.Kind))
            return AutoBuyExclusion.KindNotSelected;

        if (blocked is not null && blocked.Contains(candidate.Uuid))
            return AutoBuyExclusion.Blocklisted;
        if (allowed is not null && !allowed.Contains(candidate.Uuid))
            return AutoBuyExclusion.NotAllowlisted;

        if (!candidate.IsAvailable)
            return AutoBuyExclusion.Unavailable;

        // The conditions on the level this purchase would actually reach. Unlike availability, which
        // latches once for the entity, this one moves with the level — an upgrade six levels in can be
        // available and still be gated on a research entry nobody has finished. Anything short of met,
        // including a condition the suite cannot evaluate, keeps the candidate out of the plan.
        if (!candidate.MeetsNextLevelRequirements)
            return AutoBuyExclusion.RequirementsUnmet;

        // Finite-level structures/upgrades that are done (or fully queued to their cap) are terminal.
        if (candidate.HasFiniteLevels && candidate.IsMaxLevel && candidate.QueuedLevels == 0)
            return AutoBuyExclusion.Terminal;
        if (candidate.HasFiniteLevels && candidate.IsMaxQueuedLevel && candidate.QueuedLevels > 0)
            return AutoBuyExclusion.Terminal;

        return AutoBuyExclusion.None;
    }

    /// <summary>
    /// Whether this row is the first one for its resource. Rows that draw on the same resource are
    /// combined (the stricter-than-native duplicate-resource rule), so each resource is processed
    /// exactly once, at its first row.
    /// </summary>
    private static bool IsFirstRowForItsResource(
        ReadOnlySpan<AutoBuyCostRow> costs,
        int start,
        int row)
    {
        var resourceIndex = costs[row].ResourceRowIndex;
        for (var i = start; i < row; i++)
        {
            if (costs[i].ResourceRowIndex == resourceIndex)
                return false;
        }

        return true;
    }

    private static BigDouble CombinedCost(
        ReadOnlySpan<AutoBuyCostRow> costs,
        int start,
        int end,
        int resourceIndex)
    {
        var cost = default(BigDouble);
        for (var i = start; i < end; i++)
        {
            if (costs[i].ResourceRowIndex == resourceIndex)
                cost += costs[i].Cost;
        }

        return cost;
    }

    /// <summary>How much of a resource a purchase of this cost must leave behind untouched.</summary>
    private static BigDouble RequiredFloor(
        in BigDouble cost,
        in BigDouble absoluteReserve,
        double relativeMultiplier) =>
        BigDouble.Max(absoluteReserve, cost * relativeMultiplier);

    /// <summary>
    /// Charges this purchase to the batch ledger if every resource it draws on still clears the
    /// reserve floor after what the batch has already committed. All-or-nothing: a purchase that
    /// cannot be paid for leaves the ledger untouched, so a candidate behind it that draws on
    /// something else still gets its turn.
    /// </summary>
    /// <remarks>
    /// A multi-level request is charged <c>levels x next-cost</c>. That is a lower bound, because each
    /// level costs more than the last and the snapshot only carries the next one. The game's own
    /// per-level <c>HasEnough()</c> still stops a purchase going negative; what the curve can do is let
    /// a multi-level buy dip into the reserve. Charging it exactly needs the ported cost math, which is
    /// not wired in yet. See W25.
    /// </remarks>
    private static bool TryCommitSpend(
        in AutoBuyCandidateRow candidate,
        ReadOnlySpan<AutoBuyCostRow> costs,
        ReadOnlySpan<AutoBuyResourceRow> resources,
        BigDouble[] committed,
        int levels,
        in BigDouble absoluteReserve,
        double relativeMultiplier)
    {
        var start = candidate.CostRowStart;
        var end = start + candidate.CostRowCount;

        for (var i = start; i < end; i++)
        {
            if (!IsFirstRowForItsResource(costs, start, i))
                continue;

            var resourceIndex = costs[i].ResourceRowIndex;
            var cost = CombinedCost(costs, start, end, resourceIndex) * levels;
            if (IsZero(cost))
                continue;
            if ((uint)resourceIndex >= (uint)committed.Length)
                return false;

            var remaining = resources[resourceIndex].Spendable - committed[resourceIndex];
            var requiredBeforeSpend = cost + RequiredFloor(in cost, in absoluteReserve, relativeMultiplier);
            if (remaining.CompareTo(requiredBeforeSpend) < 0)
                return false;
        }

        for (var i = start; i < end; i++)
        {
            if (!IsFirstRowForItsResource(costs, start, i))
                continue;

            var resourceIndex = costs[i].ResourceRowIndex;
            committed[resourceIndex] += CombinedCost(costs, start, end, resourceIndex) * levels;
        }

        return true;
    }

    private static bool TryEvaluateAffordability(
        in AutoBuyCandidateRow candidate,
        ReadOnlySpan<AutoBuyCostRow> costs,
        ReadOnlySpan<AutoBuyResourceRow> resources,
        in BigDouble absoluteReserve,
        double relativeMultiplier,
        in SuiteRuntimeConfiguration config,
        out double costRatio,
        out AutoBuyPlanBelief belief,
        out AutoBuyExclusion refusal)
    {
        costRatio = 0.0;
        belief = default;
        refusal = AutoBuyExclusion.Unaffordable;
        var start = candidate.CostRowStart;
        var end = start + candidate.CostRowCount;
        var maxRatio = 0.0;
        var costResourceCount = 0;
        var pricedResourceCount = 0;
        var hasBinding = false;
        var bindingResourceId = Guid.Empty;
        var bindingIsBandwidth = false;
        var bindingCost = default(BigDouble);
        var bindingAvailable = default(BigDouble);
        var bindingFloor = default(BigDouble);

        for (var i = start; i < end; i++)
        {
            if (!IsFirstRowForItsResource(costs, start, i))
                continue;

            costResourceCount++;
            var resourceIndex = costs[i].ResourceRowIndex;
            var cost = CombinedCost(costs, start, end, resourceIndex);

            if (IsNegative(cost))
            {
                refusal = AutoBuyExclusion.Unpriceable;
                return false; // invalid resource snapshot
            }

            if (IsZero(cost))
                continue; // free on this resource

            pricedResourceCount++;
            if ((uint)resourceIndex >= (uint)resources.Length)
            {
                refusal = AutoBuyExclusion.Unpriceable;
                return false; // defensive: cost references a resource the frame did not capture
            }


            // Holdings for an ordinary resource, room below the ceiling for a bandwidth one — the
            // game charges bandwidth against the gap, so comparing a cost against a full pool's
            // quantity says "affordable" about a purchase the game will refuse outright.
            var quantity = resources[resourceIndex].Spendable;
            if (IsNegative(quantity))
            {
                refusal = AutoBuyExclusion.Unpriceable;
                return false;
            }

            var floor = RequiredFloor(in cost, in absoluteReserve, relativeMultiplier);
            var requiredBeforeSpend = cost + floor;
            if (quantity.CompareTo(requiredBeforeSpend) < 0)
                return false; // reserve floor blocks the purchase

            // quantity >= requiredBeforeSpend > 0 here, so the ratio never divides by zero.
            var ratio = (cost / quantity).ToDouble();
            if (!hasBinding || ratio > maxRatio)
            {
                hasBinding = true;
                bindingResourceId = resources[resourceIndex].ResourceId;
                bindingIsBandwidth = resources[resourceIndex].IsBandwidth;
                bindingCost = cost;
                bindingAvailable = quantity;
                bindingFloor = floor;
            }
            if (ratio > maxRatio)
                maxRatio = ratio;
        }

        // Nothing was priced, so nothing was tested. A candidate whose every cost row is zero has not
        // been shown to be affordable — it has been shown to be unpriceable, and the skip above
        // stepped over the one comparison that would have said otherwise. Answering true here made
        // such a candidate eligible with no affordability test at all, and that is exactly what
        // happened on the first cold collection after a load: the global structure-cost multiplier
        // read as an uncalculated zero, all 180 structures priced at nothing, all 180 were planned,
        // and the game refused 147 of them.
        //
        // W5 — a value that could not be read is not evidence. A single free row on an otherwise
        // priced candidate still skips, because that one really is free relative to the rows that
        // did price.
        if (pricedResourceCount == 0)
        {
            refusal = AutoBuyExclusion.Unpriceable;
            return false;
        }

        var affordabilityMode = candidate.Kind == AutoBuyCandidateKind.Upgrade
            ? config.AutoBuy.UpgradeAffordability
            : config.AutoBuy.StructureAffordability;
        if (maxRatio > MaximumAllowedCostRatio(affordabilityMode))
            return false;

        costRatio = maxRatio;
        belief = new AutoBuyPlanBelief(
            candidate.IsAvailable,
            candidate.HasFiniteLevels,
            candidate.IsMaxLevel,
            candidate.IsMaxQueuedLevel,
            candidate.CurrentLevel,
            candidate.QueuedLevels,
            costResourceCount,
            pricedResourceCount,
            maxRatio,
            bindingResourceId,
            bindingIsBandwidth,
            bindingCost,
            bindingAvailable,
            bindingFloor);
        return true;
    }

    private static int PriorityRank(in AutoBuyCandidateRow candidate, in SuiteRuntimeConfiguration config) =>
        config.AutoBuy.PrioritizeCostAndQualityStructures &&
        candidate.Kind == AutoBuyCandidateKind.Structure &&
        candidate.EconomicPriority != AutoBuyEconomicPriority.None
            ? 1
            : 0;

    /// <summary>
    /// How many levels one action requests for a candidate, from the operator's grouping mode and
    /// the game's own live counts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two bulk modes name two different native mechanisms and each belongs to one kind.
    /// <c>ActionMultiplier</c> is the MultiBuy value the native upgrade <c>Purchase()</c> honours, so
    /// it raises an upgrade's count and does nothing for a structure, whose purchase consults no
    /// multiplier. <c>BulkDevelopment</c> is the structure count the game's own bulk-build control
    /// sets, so it raises a structure's count and does nothing for an upgrade. Every other mode asks
    /// for one level.
    /// </para>
    /// <para>
    /// The hundred-level ceiling is the legacy engine's and is kept: it bounds one action to
    /// something a person can still read in a log, and the live queue room bounds it again at the
    /// action boundary, which is the only place that knows what actually fits. The frame carries one
    /// when either count is unreadable, which collapses to a single level on its own.
    /// </para>
    /// </remarks>
    private static int PerCandidateAmount(
        AutoBuyCandidateKind kind,
        in SuiteRuntimeConfiguration config,
        in AutoBuyGlobalRow global) =>
        config.AutoBuy.PurchaseGrouping switch
        {
            AutoBuyPurchaseGroupingMode.ActionMultiplier when kind == AutoBuyCandidateKind.Upgrade =>
                Clamp(global.ActionMultiplier),
            AutoBuyPurchaseGroupingMode.BulkDevelopment when kind == AutoBuyCandidateKind.Structure =>
                Clamp(global.BulkDevelopment),
            _ => 1,
        };

    /// <summary>The most levels one action may ask for, whatever the game's count says.</summary>
    private const int MaximumGroupedLevels = 100;

    private static int Clamp(int levels) => Math.Max(1, Math.Min(MaximumGroupedLevels, levels));

    private static double MaximumAllowedCostRatio(AutoBuyAffordabilityMode mode) =>
        mode switch
        {
            AutoBuyAffordabilityMode.BuyAll => double.PositiveInfinity,
            AutoBuyAffordabilityMode.Excess10 => 0.1,
            AutoBuyAffordabilityMode.Excess100 => 0.01,
            AutoBuyAffordabilityMode.Excess1000 => 0.001,
            _ => 0.01,
        };

    // Parse AbsoluteReserve the way legacy did (invariant double, per BigAmount.TryParse →
    // TryFromDouble) and widen to BigDouble by value; but treat empty/malformed/non-finite as the
    // config default of no reserve (0) rather than rejecting every candidate.
    private static BigDouble ResolveAbsoluteReserve(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            !double.IsNaN(parsed) && !double.IsInfinity(parsed))
        {
            return parsed;
        }

        return default;
    }

    // Legacy splits on ',' and trims; a non-empty allowlist restricts, the blocklist excludes, both
    // OrdinalIgnoreCase. Every candidate UUID is a parsed Guid, so matching parsed Guids is the same
    // membership decision (and is robust to brace/case formatting) — non-Guid tokens can never match
    // a Guid candidate and are dropped. A null set means "no filter".
    private static HashSet<Guid>? ParseUuidSet(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return null;

        HashSet<Guid>? set = null;
        var start = 0;
        while (start <= csv.Length)
        {
            var separator = csv.IndexOf(',', start);
            var stop = separator >= 0 ? separator : csv.Length;
            var token = csv.Substring(start, stop - start).Trim();
            if (token.Length > 0 && Guid.TryParse(token, out var uuid))
                (set ??= new HashSet<Guid>()).Add(uuid);
            if (separator < 0)
                break;
            start = separator + 1;
        }

        return set;
    }

    private static bool IsZero(BigDouble value) => value.Mantissa == 0.0;

    private static bool IsNegative(BigDouble value) => value.Mantissa < 0.0;

    private readonly struct Eligible
    {
        public Eligible(
            int candidateIndex,
            AutoBuyCandidateKind kind,
            Guid uuid,
            string uuidText,
            double costRatio,
            int priorityRank,
            in AutoBuyPlanBelief belief)
        {
            CandidateIndex = candidateIndex;
            Kind = kind;
            Uuid = uuid;
            UuidText = uuidText;
            CostRatio = costRatio;
            PriorityRank = priorityRank;
            Belief = belief;
        }

        /// <summary>Where the candidate's rows live, so the batch ledger can re-read its costs.</summary>
        public int CandidateIndex { get; }
        public AutoBuyCandidateKind Kind { get; }
        public Guid Uuid { get; }
        public string UuidText { get; }
        public double CostRatio { get; }
        public int PriorityRank { get; }

        /// <summary>What this candidate was believed to be when it was ranked.</summary>
        public AutoBuyPlanBelief Belief { get; }
    }
}
