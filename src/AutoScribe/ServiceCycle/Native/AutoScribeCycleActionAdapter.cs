using System;
using OrbModding;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal sealed class AutoScribeCycleActionAdapter : IAutoScribeCycleActionPort
{
    private readonly AutoScribeOneShotCraftGameAction _gameAction;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _ownsActionFamily;
    private readonly Func<string> _readOwnershipFailure;
    private readonly AutoScribeActionHealth _health;
    private readonly ConsumableMutationGate _mutationGate;
    private readonly Func<long> _readFrameIdentity;

    internal AutoScribeCycleActionAdapter(
        AutoScribeOneShotCraftGameAction gameAction,
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        Func<string> readOwnershipFailure,
        AutoScribeActionHealth health)
        : this(
            gameAction,
            readLifecycleEpoch,
            ownsActionFamily,
            readOwnershipFailure,
            health,
            new ConsumableMutationGate(),
            static () => 0)
    {
    }

    internal AutoScribeCycleActionAdapter(
        AutoScribeOneShotCraftGameAction gameAction,
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        Func<string> readOwnershipFailure,
        AutoScribeActionHealth health,
        ConsumableMutationGate mutationGate,
        Func<long> readFrameIdentity)
    {
        _gameAction = gameAction ?? throw new ArgumentNullException(nameof(gameAction));
        _readLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _ownsActionFamily = ownsActionFamily ??
            throw new ArgumentNullException(nameof(ownsActionFamily));
        _readOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _mutationGate = mutationGate ?? throw new ArgumentNullException(nameof(mutationGate));
        _readFrameIdentity = readFrameIdentity ??
            throw new ArgumentNullException(nameof(readFrameIdentity));
    }

    public ServiceActionResult TryExecute(
        in AutoScribeCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
    {
        // Roles are intentionally not re-read here. This config is the cycle-pinned operational
        // policy; the worker pinned Roles when the cycle opened.
        if (!AutoScribeConfigurationPolicy.IsOperational(config))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);
        if (!Owns())
        {
            var ownership = ReadOwnershipFailure();
            var rejected = AutoScribeSubmission.Reject(
                AutoScribePreflight.MutationPermitUnavailable,
                ownership);
            _health.Observe(in action, in rejected);
            return ServiceActionResult.Rejected(
                AutoScribeActionResultCodes.MutationPermitUnavailable);
        }
        if (!EpochMatches(action.CollectedAtEpoch))
            return ServiceActionResult.Rejected(CommonActionResultCodes.LifecycleReplaced);
        if (_mutationGate.Blocks(action.CollectedAtEpoch, action.CollectedAtFrame))
            return ServiceActionResult.Rejected(AutoScribeActionResultCodes.PublicationGap);

        var submission = _gameAction.Submit(in action);
        if (submission.CallOutcome.MutationAttempts > 0)
            _mutationGate.ObserveAttempt(action.CollectedAtEpoch, ReadFrameIdentity());
        var newlyObserved = _health.Observe(in action, in submission);
        if (!submission.Verified && newlyObserved)
            Plugin.Log?.LogAutomataWarning(
                $"Auto Scribe {submission.Stage}/{submission.Preflight}; " +
                $"recipe={EntityUuidTranslator.Format(action.RecipeId)}; " +
                $"scroll={EntityUuidTranslator.Format(action.ScrollId)}; " +
                $"level={action.Level}: {submission.Reason}");
        return Map(in submission);
    }

    internal static ServiceActionResult Map(in AutoScribeSubmission submission)
    {
        var code = submission.Preflight switch
        {
            AutoScribePreflight.IdentityUnavailable =>
                AutoScribeActionResultCodes.IdentityUnavailable,
            AutoScribePreflight.RelationshipMismatch =>
                AutoScribeActionResultCodes.RelationshipMismatch,
            AutoScribePreflight.RecipeUnavailable =>
                AutoScribeActionResultCodes.RecipeUnavailable,
            AutoScribePreflight.TargetUnavailable =>
                AutoScribeActionResultCodes.TargetUnavailable,
            AutoScribePreflight.QueueFull => AutoScribeActionResultCodes.QueueFull,
            AutoScribePreflight.CompetingSupply =>
                AutoScribeActionResultCodes.CompetingSupply,
            AutoScribePreflight.Unaffordable => AutoScribeActionResultCodes.Unaffordable,
            AutoScribePreflight.MutationPermitUnavailable =>
                AutoScribeActionResultCodes.MutationPermitUnavailable,
            AutoScribePreflight.ContractUnavailable =>
                AutoScribeActionResultCodes.ContractUnavailable,
            AutoScribePreflight.Quarantined => AutoScribeActionResultCodes.Quarantined,
            AutoScribePreflight.PostPaymentFault =>
                AutoScribeActionResultCodes.PostPaymentFault,
            AutoScribePreflight.VerificationFailed =>
                AutoScribeActionResultCodes.VerificationFailed,
            AutoScribePreflight.Proceeded => CommonActionResultCodes.Committed,
            _ => CommonActionResultCodes.AdapterFault,
        };
        if (submission.CallOutcome.MutationAttempts > 0)
        {
            var evidence = ServiceNativeMutationEvidence.Observed(
                submission.Outcome,
                submission.CallOutcome);
            return submission.Verified
                ? ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence)
                : ServiceActionResult.Faulted(code, evidence);
        }
        return IsExpectedRejection(submission.Preflight)
            ? ServiceActionResult.Rejected(code)
            : ServiceActionResult.Faulted(code);
    }

    private static bool IsExpectedRejection(AutoScribePreflight preflight) =>
        preflight is AutoScribePreflight.IdentityUnavailable or
            AutoScribePreflight.RelationshipMismatch or
            AutoScribePreflight.RecipeUnavailable or
            AutoScribePreflight.TargetUnavailable or
            AutoScribePreflight.QueueFull or
            AutoScribePreflight.CompetingSupply or
            AutoScribePreflight.Unaffordable or
            AutoScribePreflight.MutationPermitUnavailable or
            AutoScribePreflight.Quarantined;

    private bool Owns()
    {
        try { return _ownsActionFamily(); }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            return false;
        }
    }

    private string ReadOwnershipFailure()
    {
        try
        {
            var reason = _readOwnershipFailure();
            return string.IsNullOrWhiteSpace(reason)
                ? "Auto Scribe does not own CraftingQueueSubmission."
                : reason;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            return "Auto Scribe ownership evidence failed: " + ex.GetBaseException().Message;
        }
    }

    private bool EpochMatches(long planned)
    {
        try
        {
            var current = _readLifecycleEpoch();
            return current > 0 && current == planned;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            return false;
        }
    }

    private long ReadFrameIdentity()
    {
        try { return _readFrameIdentity(); }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            return 0;
        }
    }
}
