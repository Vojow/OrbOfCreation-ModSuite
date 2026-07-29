using System;
using OrbModding;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal sealed class AutoItemsCycleActionAdapter : IAutoItemsCycleActionPort
{
    private readonly AutoItemsNativeAdapter _native;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _ownsActionFamily;

    internal AutoItemsCycleActionAdapter(
        AutoItemsNativeAdapter native,
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _readLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _ownsActionFamily = ownsActionFamily ??
            throw new ArgumentNullException(nameof(ownsActionFamily));
    }

    public ServiceActionResult TryExecute(
        in AutoItemsCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
    {
        if (!AutoItemsConfigurationPolicy.IsOperational(config) ||
            !AutoItemsConfigurationPolicy.Allows(
                config.AutoItems,
                action.Family,
                action.ItemId))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);
        if (!Owns())
            return ServiceActionResult.Rejected(AutoItemsActionResultCodes.ActionFamilyUnavailable);
        if (!NativeEpochMatches(action.CollectedAtEpoch))
            return ServiceActionResult.Rejected(CommonActionResultCodes.LifecycleReplaced);

        AutoItemsSubmission submission;
        try
        {
            submission = _native.Submit(in action);
        }
        catch (Exception ex) when (AutoItemsReflectionAccess.IsExpectedFailure(ex))
        {
            Plugin.Log?.LogAutomataWarning(
                $"Auto Items failed at the native boundary: {ex.GetBaseException().Message}.");
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        }

        if (!submission.Verified && submission.Preflight == AutoItemsPreflight.Proceeded)
            Plugin.Log?.LogAutomataWarning(
                $"Auto Items could not verify {action.Family} {action.ItemId:D}: {submission.Reason}");
        return Map(in submission);
    }

    private static ServiceActionResult Map(in AutoItemsSubmission submission)
    {
        var code = submission.Preflight switch
        {
            AutoItemsPreflight.ItemUnavailable => AutoItemsActionResultCodes.ItemUnavailable,
            AutoItemsPreflight.FamilyChanged => AutoItemsActionResultCodes.FamilyChanged,
            AutoItemsPreflight.NativeBusy => AutoItemsActionResultCodes.NativeBusy,
            AutoItemsPreflight.NotAdmissible => AutoItemsActionResultCodes.NotAdmissible,
            AutoItemsPreflight.RandomizationUnavailable =>
                AutoItemsActionResultCodes.RandomizationUnavailable,
            AutoItemsPreflight.TemporaryEffectPresent =>
                AutoItemsActionResultCodes.TemporaryEffectPresent,
            AutoItemsPreflight.TemporaryCostChanged =>
                AutoItemsActionResultCodes.TemporaryCostChanged,
            AutoItemsPreflight.MutationPermitUnavailable =>
                AutoItemsActionResultCodes.MutationPermitUnavailable,
            AutoItemsPreflight.ContractUnavailable or
            AutoItemsPreflight.MultiBuyUnavailable or
            AutoItemsPreflight.Quarantined =>
                CommonActionResultCodes.AdapterFault,
            AutoItemsPreflight.Proceeded => CommonActionResultCodes.Committed,
            _ => CommonActionResultCodes.AdapterFault,
        };
        if (submission.Preflight != AutoItemsPreflight.Proceeded)
            return IsExpectedRejection(submission.Preflight)
                ? ServiceActionResult.Rejected(code)
                : ServiceActionResult.Faulted(code);

        var evidence = ServiceNativeMutationEvidence.Observed(
            submission.Outcome, submission.CallOutcome);
        return submission.Verified
            ? ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence)
            : ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault, evidence);
    }

    private static bool IsExpectedRejection(AutoItemsPreflight preflight) =>
        preflight is AutoItemsPreflight.ItemUnavailable or
            AutoItemsPreflight.FamilyChanged or
            AutoItemsPreflight.NativeBusy or
            AutoItemsPreflight.NotAdmissible or
            AutoItemsPreflight.RandomizationUnavailable or
            AutoItemsPreflight.TemporaryEffectPresent or
            AutoItemsPreflight.MutationPermitUnavailable or
            AutoItemsPreflight.TemporaryCostChanged;

    private bool Owns()
    {
        try { return _ownsActionFamily(); }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            return false;
        }
    }

    private bool NativeEpochMatches(long plannedEpoch)
    {
        try
        {
            var current = _readLifecycleEpoch();
            return current > 0 && current == plannedEpoch;
        }
        catch (Exception ex) when (AutoItemsReflectionAccess.IsExpectedFailure(ex))
        {
            return false;
        }
    }
}
