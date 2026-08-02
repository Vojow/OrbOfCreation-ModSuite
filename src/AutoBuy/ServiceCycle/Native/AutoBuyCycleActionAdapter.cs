using System;
using System.Collections.Generic;
using System.Reflection;
using OrbModding;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

/// <summary>
/// The native execution boundary for Auto Buy. The pure worker plans a ranked batch of
/// <see cref="AutoBuyCycleAction"/> values; this adapter revalidates each decision against the live
/// game on the main thread. Both kinds may submit several levels at once — an upgrade under a pinned
/// multi-buy multiplier, a structure by repeating its one-level purchase.
/// </summary>
/// <remarks>
/// Three guards run before any mutation. First the configuration is re-checked: a service that was
/// disabled or dropped this candidate kind between planning and execution rejects without a
/// penalty. Then the action-family lease for this candidate kind is re-read, because another plugin
/// can take it mid-cycle and a purchase submitted without it is this suite acting on content it has
/// stood down from. Then the native world epoch is re-read and compared to the epoch the snapshot this
/// purchase was planned from was collected under, which the action carries by value. This closes the
/// game-reload race before the submission re-resolves candidate identity and reads current queue,
/// view-availability, affordability, and configuration facts against that lifecycle's static route
/// snapshot.
/// A mismatch is a penalty-free <see cref="CommonActionResultCodes.LifecycleReplaced"/> rejection.
/// The submission re-resolves the candidate by UUID and submits through the audited
/// <see cref="AutoBuyNativePurchaseAdapter"/>. A verified mutation commits, while an attempted
/// call with a zero queued-level delta skips so the remaining batch can still be considered.
/// </remarks>
internal sealed class AutoBuyCycleActionAdapter : IAutoBuyCycleActionPort
{
    private readonly IAutoBuyNativePurchasePort _purchases;
    private readonly IAutoBuyQueueRoomPort _queueRoom;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<AutoBuyCandidateKinds> _ownershipMask;
    private readonly IAutoBuyRefusalResponsePort _refusals;
    private readonly IServiceWorldGenerationSource? _worldGenerations;
    private readonly List<AutoBuyEarlierPurchase> _batchPurchases = new(16);
    private ulong _journalBatch;
#if SERVICE_CYCLE_PROFILE
    private long _diagnosedTopologyEpoch;
    private readonly AutomataProfileOperations _profileOperations;
    private readonly Func<AutoBuyCandidateKind, bool> _gameMcpOwnership;
#endif

    public AutoBuyCycleActionAdapter(
        IAutoBuyNativePurchasePort purchases,
        IAutoBuyQueueRoomPort queueRoom,
        Func<long> readLifecycleEpoch,
        Func<AutoBuyCandidateKinds> ownershipMask
#if SERVICE_CYCLE_PROFILE
        , AutomataProfileOperations profileOperations
#endif
        , IAutoBuyRefusalResponsePort refusals
        , IServiceWorldGenerationSource? worldGenerations = null
#if SERVICE_CYCLE_PROFILE
        , Func<AutoBuyCandidateKind, bool>? gameMcpOwnership = null
#endif
        )
    {
        _purchases = purchases ?? throw new ArgumentNullException(nameof(purchases));
        _queueRoom = queueRoom ?? throw new ArgumentNullException(nameof(queueRoom));
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _ownershipMask = ownershipMask ?? throw new ArgumentNullException(nameof(ownershipMask));
        _refusals = refusals ?? throw new ArgumentNullException(nameof(refusals));
        _worldGenerations = worldGenerations;
#if SERVICE_CYCLE_PROFILE
        _profileOperations = profileOperations ??
            throw new ArgumentNullException(nameof(profileOperations));
        _gameMcpOwnership = gameMcpOwnership ?? (_ => false);
#endif
    }

    internal void InvalidateTopology()
    {
#if SERVICE_CYCLE_PROFILE
        _diagnosedTopologyEpoch = 0;
#endif
        if (_purchases is IAutoBuyPurchaseTopologyPort topology) topology.InvalidateTopology();
    }

#if SERVICE_CYCLE_PROFILE
    internal void EmitRouteDiagnostic(long lifecycleEpoch)
    {
        if (lifecycleEpoch <= 0 || _diagnosedTopologyEpoch == lifecycleEpoch ||
            _purchases is not IAutoBuyPurchaseTopologyPort topology)
            return;
        if (topology.EmitRouteDiagnostic(lifecycleEpoch))
            _diagnosedTopologyEpoch = lifecycleEpoch;
    }
#endif

    public ServiceActionResult TryExecute(
        in AutoBuyCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
        => TryExecuteCore(
            in action,
            in config,
            in context,
            requireAutomationPolicy: true,
            manualOwnershipProven: false);

#if SERVICE_CYCLE_PROFILE
    /// <summary>
    /// Executes one explicit strategist request through the same live native boundary as Auto Buy.
    /// The request is not an automation decision, so only the worker's enable/selection policy is
    /// omitted; ownership, lifecycle, queue reserve, identity, affordability, and mutation proof
    /// remain mandatory below.
    /// </summary>
    internal ServiceActionResult TryExecuteGameMcp(
        in AutoBuyCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
    {
        if (!_gameMcpOwnership(action.Kind))
            return ServiceActionResult.Rejected(
                AutoBuyActionResultCodes.ActionFamilyUnavailable);
        return TryExecuteCore(
            in action,
            in config,
            in context,
            requireAutomationPolicy: false,
            manualOwnershipProven: true);
    }
#endif

    private ServiceActionResult TryExecuteCore(
        in AutoBuyCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context,
        bool requireAutomationPolicy,
        bool manualOwnershipProven)
    {
        BeginBatch(context.Batch.Value);

        if (requireAutomationPolicy &&
            (!AutoBuyConfigurationPolicy.IsOperational(config) ||
             !AutoBuyConfigurationPolicy.IsSelected(config, action.Kind)))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);

        if (!manualOwnershipProven && !Owns(action.Kind))
            return ServiceActionResult.Rejected(AutoBuyActionResultCodes.ActionFamilyUnavailable);

        if (!NativeEpochMatches(action.CollectedAtEpoch))
            return ServiceActionResult.Rejected(CommonActionResultCodes.LifecycleReplaced);

        // Re-check the LIVE queue room against the operator's reserve before every submission. The
        // worker does not bound the plan by the queue at all (W39), and both services consume the
        // slots they compete for, so this is the authority that keeps LeaveQueueSlots free — a
        // collected reading may shape a plan and may never admit an action (W53). Exhausting the
        // reserve rejects (penalty-free) and, being the first non-commit, cascade-terminates the
        // rest of the batch — correct, since nothing more fits. An unreadable room cannot prove the
        // reserve is honoured.
        var reservedSlots = Math.Max(0, config.AutoBuy.LeaveQueueSlots);
        bool queueRoomReadable;
        int remainingRoom;
#if SERVICE_CYCLE_PROFILE
        var queueRoomStage = _profileOperations.Begin(
            ServiceCycleProfileSpan.AutoBuyActionQueueRoomRead,
            in context,
            ServiceCycleProfileTemperature.Warm);
        try
        {
#endif
        queueRoomReadable = _queueRoom.TryReadRemainingRoom(out remainingRoom);
#if SERVICE_CYCLE_PROFILE
        }
        finally { queueRoomStage.Complete(); }
#endif
        if (!queueRoomReadable)
        {
            Plugin.Log?.LogAutomataWarning(
                AutoBuyPurchaseNarration.QueueRoomUnavailable(action.Kind, action.Uuid).Message);
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        }

        if (remainingRoom <= reservedSlots)
        {
            Plugin.Log?.LogAutomataInfo(
                AutoBuyPurchaseNarration.QueueReserveReached(action.Kind, action.Uuid, remainingRoom, reservedSlots).Message);
            return ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
        }

        // One action can take several slots: an upgrade multi-buy queues one stack per level, and the
        // game's own Purchase() loop never consults the queue room. So "there is at least one slot
        // above the reserve" does not mean this submission fits above it. Clamp the request to the
        // room that is actually free above the reserve; the loop stops early on its own if fewer
        // levels are affordable.
        var levels = Math.Min(action.Count, remainingRoom - reservedSlots);

        AutoBuyPurchaseSubmission submission;
        try
        {
            submission = _purchases.Submit(
                action.Kind,
                action.Uuid,
                levels,
                action.CollectedAtEpoch
#if SERVICE_CYCLE_PROFILE
                , in context
#endif
                );
        }
        catch (Exception ex) when (
            ex is TargetInvocationException || ex is ArgumentException ||
            ex is InvalidOperationException || ex is TargetException || ex is MemberAccessException)
        {
            Plugin.Log?.LogAutomataWarning(
                $"Auto Buy failed to purchase {action.Kind} {EntityIdentityFormatter.Format(action.Uuid)}: adapter fault ({ex.GetBaseException().Message}).");
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        }

        Narrate(action.Kind, action.Uuid, submission, action.Belief);
        if (submission.Preflight == AutoBuyPurchasePreflight.NotAdmissible)
            ReportRefusal(in action, levels, in submission, in context);
        var result = Map(submission);
        if (result.Disposition == ServiceActionDisposition.Committed)
        {
            var liveCosts = submission.LiveCosts;
            _batchPurchases.Add(new AutoBuyEarlierPurchase(
                action.Kind,
                action.Uuid,
                context.ActionIndex,
                submission.CommittedLevels,
                in liveCosts));
        }
        return result;
    }

    /// <summary>
    /// Hands a refused plan to whatever the suite does about one, with both halves of the story: the
    /// beliefs the action carried from the worker, and the live terms the boundary just read.
    /// </summary>
    /// <remarks>
    /// A refusal is only interpretable against the readings the cycle was pinned to, so the report
    /// names them — the world generation and the epoch it was collected under, the configuration the
    /// plan obeyed, and the cycle that produced it.
    /// </remarks>
    private void ReportRefusal(
        in AutoBuyCycleAction action,
        int requestedLevels,
        in AutoBuyPurchaseSubmission submission,
        in ServiceActionContext context)
    {
        var latest = default(WorldGeneration);
        var latestReadable = _worldGenerations is not null &&
            _worldGenerations.TryGetLatestGeneration(out latest);
        var liveCosts = submission.Diagnosis.LiveCosts;
        var earlier = RelatedPurchases(in liveCosts);
        _refusals.ObserveRefusal(new AutoBuyRefusalReport(
            action.Kind,
            action.Uuid,
            requestedLevels,
            action.Belief,
            submission.Diagnosis,
            context.Cycle.World.Value,
            action.CollectedAtEpoch,
            context.Cycle.Config.Value,
            context.Cycle.Lifecycle.Value,
            context.Cycle.Cycle.Value,
            context.Batch.Value,
            context.ActionIndex,
            action.WorldCollectedAt,
            context.AttemptedAt,
            latestReadable,
            latestReadable ? latest.Value : 0,
            earlier));
    }

    private void BeginBatch(ulong batch)
    {
        if (_journalBatch == batch) return;
        _journalBatch = batch;
        _batchPurchases.Clear();
    }

    private AutoBuyEarlierPurchase[] RelatedPurchases(in AutoBuyLiveCostSnapshot refusedCosts)
    {
        if (_batchPurchases.Count == 0) return Array.Empty<AutoBuyEarlierPurchase>();
        var related = new List<AutoBuyEarlierPurchase>();
        for (var index = 0; index < _batchPurchases.Count; index++)
        {
            var purchase = _batchPurchases[index];
            if (!refusedCosts.IsComplete || !purchase.HasCompleteCosts ||
                SharesResource(in purchase, in refusedCosts))
            {
                related.Add(purchase);
            }
        }

        return related.ToArray();
    }

    private static bool SharesResource(
        in AutoBuyEarlierPurchase purchase,
        in AutoBuyLiveCostSnapshot refusedCosts)
    {
        var earlier = purchase.Costs;
        var refused = refusedCosts.Rows;
        for (var i = 0; i < earlier.Length; i++)
        {
            for (var j = 0; j < refused.Length; j++)
            {
                if (earlier[i].ResourceId == refused[j].ResourceId) return true;
            }
        }
        return false;
    }

    // Always-on human-readable decision line ("purchased X of Y" / "failed to purchase") so buying
    // behaviour — including the accepted Pillar-A cascade where a later candidate becomes
    // unaffordable — can be analysed from the log. The structured trace carries only the generic
    // native call-outcome today; surfacing the level counts there is a tracked follow-up.
    private static void Narrate(
        AutoBuyCandidateKind kind,
        Guid uuid,
        in AutoBuyPurchaseSubmission submission,
        in AutoBuyPlanBelief belief)
    {
        var narration = AutoBuyPurchaseNarration.Describe(kind, uuid, in submission, in belief);
        switch (narration.Level)
        {
            case AutoBuyPurchaseNarrationLevel.Warning:
                Plugin.Log?.LogAutomataWarning(narration.Message);
                break;
            default:
                Plugin.Log?.LogAutomataInfo(narration.Message);
                break;
        }
    }

    private bool Owns(AutoBuyCandidateKind kind)
    {
        var mask = _ownershipMask();
        return kind == AutoBuyCandidateKind.Structure
            ? (mask & AutoBuyCandidateKinds.Structures) != 0
            : (mask & AutoBuyCandidateKinds.Upgrades) != 0;
    }

    /// <summary>
    /// Whether the game is still the run this purchase was planned for.
    /// </summary>
    /// <remarks>
    /// The live half stays live: this is the only thing that re-reads the game's epoch after a plan is
    /// made, and reading it here rather than trusting a captured copy is what closes the reload race.
    /// What changed is the other half. It used to be the runner's own lifecycle, frozen when the
    /// runner was built, so a world already collected under a new epoch was refused merely because the
    /// host had not been told to replace the runner yet. It is now the epoch the snapshot this plan
    /// was made from was collected under, which is what the plan is actually about. Zero matches
    /// nothing on either side, so an unstamped world and an unreadable game both refuse.
    /// </remarks>
    private bool NativeEpochMatches(long plannedEpoch)
    {
        long epoch;
        try
        {
            epoch = _readLifecycleEpoch();
        }
        catch (Exception ex) when (
            ex is TargetInvocationException || ex is ArgumentException ||
            ex is InvalidOperationException || ex is TargetException || ex is MemberAccessException)
        {
            return false;
        }

        return epoch > 0 && epoch == plannedEpoch;
    }

    private static ServiceActionResult Map(in AutoBuyPurchaseSubmission submission)
    {
        switch (submission.Preflight)
        {
            case AutoBuyPurchasePreflight.CandidateUnavailable:
                return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
            case AutoBuyPurchasePreflight.NotAdmissible:
                return submission.Diagnosis.Classification ==
                    AutoBuyRefusalClassification.AffordabilityChanged
                    ? ServiceActionResult.Skipped(CommonActionResultCodes.Skipped)
                    : ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
            case AutoBuyPurchasePreflight.SingleBuyUnavailable:
                return ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
            case AutoBuyPurchasePreflight.OwningViewUnavailable:
                return ServiceActionResult.Rejected(AutoBuyActionResultCodes.OwningViewUnavailable);
            case AutoBuyPurchasePreflight.OwningViewRelationMissing:
                return ServiceActionResult.Rejected(AutoBuyActionResultCodes.OwningViewRelationMissing);
            case AutoBuyPurchasePreflight.OwningViewRelationUnreadable:
                return ServiceActionResult.Rejected(AutoBuyActionResultCodes.OwningViewRelationUnreadable);
            case AutoBuyPurchasePreflight.OwningViewRelationContradictory:
                return ServiceActionResult.Rejected(AutoBuyActionResultCodes.OwningViewRelationContradictory);
            case AutoBuyPurchasePreflight.StructureUnavailable:
                return ServiceActionResult.Rejected(AutoBuyActionResultCodes.StructureUnavailable);
            case AutoBuyPurchasePreflight.DestinationCapacityFull:
                return ServiceActionResult.Rejected(AutoBuyActionResultCodes.DestinationCapacityFull);
            case AutoBuyPurchasePreflight.DestinationCapacityContractUnavailable:
                return ServiceActionResult.Rejected(
                    AutoBuyActionResultCodes.DestinationCapacityContractUnavailable);
            case AutoBuyPurchasePreflight.DestinationCapacityIdentityMismatch:
                return ServiceActionResult.Rejected(
                    AutoBuyActionResultCodes.DestinationCapacityIdentityMismatch);
        }

        var evidence = ServiceNativeMutationEvidence.Observed(submission.Outcome, submission.CallOutcome);
        if (submission.Verified)
            return ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence);
        if (submission.Outcome == NativeMutationOutcome.PostconditionFailed &&
            submission.CommittedLevels == 0)
        {
            return ServiceActionResult.Skipped(CommonActionResultCodes.Skipped, evidence);
        }
        return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault, evidence);
    }
}
