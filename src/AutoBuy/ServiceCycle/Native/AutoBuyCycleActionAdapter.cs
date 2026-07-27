using System;
using System.Reflection;
using OrbModding;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.Configuration;
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
/// purchase was planned from was collected under, which the action carries by value — nothing
/// re-checks live native state after capture, so this adapter is the only place that closes the
/// game-reload race (a stale <c>StructureSO.All</c>/<c>UpgradeSO.All</c> rebuilt under a new epoch).
/// A mismatch is a penalty-free <see cref="CommonActionResultCodes.LifecycleReplaced"/> rejection.
/// The submission itself re-resolves the candidate by UUID and submits through the audited
/// <see cref="AutoBuyNativePurchaseAdapter"/>. A verified mutation commits, while an attempted
/// call with a zero queued-level delta skips so the remaining batch can still be considered.
/// </remarks>
internal sealed class AutoBuyCycleActionAdapter : IAutoBuyCycleActionPort
{
    private readonly IAutoBuyNativePurchasePort _purchases;
    private readonly IAutoBuyQueueRoomPort _queueRoom;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<AutoBuyCandidateKinds> _ownershipMask;
    private readonly IAutoBuyRefusalResponsePort? _refusals;
#if SERVICE_CYCLE_PROFILE
    private readonly AutomataProfileOperations _profileOperations;
#endif

    public AutoBuyCycleActionAdapter(
        IAutoBuyNativePurchasePort purchases,
        IAutoBuyQueueRoomPort queueRoom,
        Func<long> readLifecycleEpoch,
        Func<AutoBuyCandidateKinds> ownershipMask
#if SERVICE_CYCLE_PROFILE
        , AutomataProfileOperations profileOperations
#endif
        , IAutoBuyRefusalResponsePort? refusals = null)
    {
        _purchases = purchases ?? throw new ArgumentNullException(nameof(purchases));
        _queueRoom = queueRoom ?? throw new ArgumentNullException(nameof(queueRoom));
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _ownershipMask = ownershipMask ?? throw new ArgumentNullException(nameof(ownershipMask));
        _refusals = refusals;
#if SERVICE_CYCLE_PROFILE
        _profileOperations = profileOperations ??
            throw new ArgumentNullException(nameof(profileOperations));
#endif
    }

    public ServiceActionResult TryExecute(
        in AutoBuyCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
    {
        if (!AutoBuyConfigurationPolicy.IsOperational(config) ||
            !AutoBuyConfigurationPolicy.IsSelected(config, action.Kind))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);

        if (!Owns(action.Kind))
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
                levels
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
                $"Auto Buy failed to purchase {action.Kind} {action.Uuid:D}: adapter fault ({ex.GetBaseException().Message}).");
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        }

        Narrate(action.Kind, action.Uuid, submission, action.Belief);
        if (submission.Preflight == AutoBuyPurchasePreflight.NotAdmissible)
            ReportRefusal(in action, levels, in submission, in context);
        return Map(submission);
    }

    /// <summary>
    /// Hands a refused plan to whatever the suite does about one, with both halves of the story: the
    /// beliefs the action carried from the worker, and the live terms the boundary just read.
    /// </summary>
    /// <remarks>
    /// A refusal is only interpretable against the readings the cycle was pinned to, so the report
    /// names them — the world generation and the epoch it was collected under, the configuration the
    /// plan obeyed, and the cycle that produced it. The responder is optional: a composition without
    /// one still narrates the refusal and still rejects, it just does not stand the service down.
    /// </remarks>
    private void ReportRefusal(
        in AutoBuyCycleAction action,
        int requestedLevels,
        in AutoBuyPurchaseSubmission submission,
        in ServiceActionContext context)
    {
        if (_refusals is null) return;
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
            context.Cycle.Cycle.Value));
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
            case AutoBuyPurchasePreflight.SingleBuyUnavailable:
                return ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
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
