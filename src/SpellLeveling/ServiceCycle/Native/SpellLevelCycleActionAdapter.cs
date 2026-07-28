using System;
using System.Reflection;
using OrbModding;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// The native execution boundary for Spell Leveling. The pure worker plans at most one mastery-level
/// purchase; this adapter revalidates that decision against the live game on the main thread and
/// submits it.
/// </summary>
/// <remarks>
/// Three guards run before the native port is asked for anything. The configuration is re-checked, so
/// a feature switched off between planning and execution rejects without a penalty. The action-family
/// lease is re-read, because another plugin can take it mid-cycle and a level bought without it is
/// this suite acting on content it has stood down from. Then the native world epoch is compared with
/// the epoch the snapshot this plan came from was collected under, which the action carries by value —
/// a plan made against another run of the game is refused, penalty-free.
/// <para>
/// After that the port owns the verdict. Spell Leveling's planner sees less than Auto Buy's does by
/// design (W59), so a refusal here is the ordinary case rather than the alarming one, and the result
/// codes distinguish the refusal that means "not unlocked yet" from the one that means "cannot afford
/// it right now" — the first is what the feature status reads as <c>Locked</c>.
/// </para>
/// </remarks>
internal sealed class SpellLevelCycleActionAdapter : ISpellLevelCycleActionPort
{
    private readonly ISpellLevelNativePort _levels;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _ownsActionFamily;
    private readonly Action<AutoSpellLevelCapability>? _observeCapability;

    public SpellLevelCycleActionAdapter(
        ISpellLevelNativePort levels,
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        Action<AutoSpellLevelCapability>? observeCapability = null)
    {
        _levels = levels ?? throw new ArgumentNullException(nameof(levels));
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _ownsActionFamily = ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
        _observeCapability = observeCapability;
    }

    public ServiceActionResult TryExecute(
        in SpellLevelCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
    {
        if (!SpellLevelConfigurationPolicy.IsOperational(config))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);

        if (!Owns())
            return ServiceActionResult.Rejected(SpellLevelActionResultCodes.ActionFamilyUnavailable);

        if (!NativeEpochMatches(action.CollectedAtEpoch))
            return ServiceActionResult.Rejected(CommonActionResultCodes.LifecycleReplaced);

        SpellLevelSubmission submission;
        try
        {
            submission = _levels.Submit(action.Kind, action.Uuid);
        }
        catch (Exception ex) when (
            ex is TargetInvocationException || ex is ArgumentException ||
            ex is InvalidOperationException || ex is TargetException || ex is MemberAccessException)
        {
            Plugin.Log?.LogAutomataWarning(
                $"Spell Leveling failed to buy a mastery level for {action.Uuid:D}: adapter fault ({ex.GetBaseException().Message}).");
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        }

        Narrate(in action, in submission);
        ObserveCapability(in submission);
        return Map(in submission);
    }

    /// <summary>
    /// Tells the feature status what the boundary just learned about the capability, which is the only
    /// place <see cref="AutoSpellLevelCapability.Locked"/> can be observed at all.
    /// </summary>
    private void ObserveCapability(in SpellLevelSubmission submission)
    {
        if (_observeCapability is null) return;
        switch (submission.Preflight)
        {
            case SpellLevelPreflight.ProgressionLocked:
                _observeCapability(AutoSpellLevelCapability.Locked);
                break;
            case SpellLevelPreflight.BatchUnavailable:
                _observeCapability(AutoSpellLevelCapability.Single);
                break;
            case SpellLevelPreflight.Proceeded:
            case SpellLevelPreflight.NotAffordable:
                // Reaching either means a discovered spell's prerequisite passed, which is exactly
                // what separates Single from Locked.
                _observeCapability(AutoSpellLevelCapability.Single);
                break;
        }
    }

    private static void Narrate(in SpellLevelCycleAction action, in SpellLevelSubmission submission)
    {
        var what = action.Kind == SpellLevelActionKind.All
            ? "every ready spell"
            : $"spell {action.Uuid:D}";
        if (submission.Verified)
        {
            Plugin.Log?.LogAutomataInfo($"Spell Leveling bought a mastery level for {what}.");
            return;
        }

        var message =
            $"Spell Leveling did not level {what}: {submission.Reason} " +
            $"(planned at mastery {action.Belief.MasteryLevel} with {action.Belief.ReadySpellCount} ready).";
        if (submission.Preflight == SpellLevelPreflight.Proceeded)
            Plugin.Log?.LogAutomataWarning(message);
        else
            Plugin.Log?.LogAutomataInfo(message);
    }

    private bool Owns()
    {
        try
        {
            return _ownsActionFamily();
        }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            return false;
        }
    }

    /// <summary>Whether the game is still the run this level was planned for.</summary>
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

    private static ServiceActionResult Map(in SpellLevelSubmission submission)
    {
        switch (submission.Preflight)
        {
            case SpellLevelPreflight.ContractUnavailable:
                return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
            case SpellLevelPreflight.ProgressionLocked:
                return ServiceActionResult.Rejected(SpellLevelActionResultCodes.ProgressionLocked);
            case SpellLevelPreflight.NotAffordable:
            case SpellLevelPreflight.BatchUnavailable:
                return ServiceActionResult.Rejected(SpellLevelActionResultCodes.LevelNotAffordable);
        }

        var evidence = ServiceNativeMutationEvidence.Observed(submission.Outcome, submission.CallOutcome);
        if (submission.Verified)
            return ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence);

        // An attempted mutation the verifier could not confirm is a fault, not a skip. The native port
        // has already blocked itself until the next lifecycle, so this is reported once and the
        // service backs off rather than re-probing a contract it no longer understands.
        return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault, evidence);
    }
}
