using System;
using OrbModding.Common;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

internal sealed class AutoHarvestMutationAdapter : IAutoHarvestMutationPort
{
    private readonly IAutoHarvestStatePort _stateReader;
#if SERVICE_CYCLE_PROFILE
    private readonly AutoHarvestProfileOperations _profileOperations;
#endif

    public AutoHarvestMutationAdapter(
        IAutoHarvestStatePort stateReader
#if SERVICE_CYCLE_PROFILE
        , AutoHarvestProfileOperations profileOperations
#endif
        )
    {
        _stateReader = stateReader ?? throw new ArgumentNullException(nameof(stateReader));
#if SERVICE_CYCLE_PROFILE
        _profileOperations = profileOperations ??
            throw new ArgumentNullException(nameof(profileOperations));
#endif
    }

    public AutoHarvestSubmissionResult Submit(
        in ResolvedAutoHarvestPair resolved
#if SERVICE_CYCLE_PROFILE
        , in ServiceActionContext context
#endif
        )
    {
        var current = resolved;
        AutoHarvestSubmissionState before;
#if SERVICE_CYCLE_PROFILE
        var beforeSnapshot = _profileOperations.Begin(
            AutoHarvestServiceCycleProfileStageCodes.ActionBeforeSnapshot,
            in context,
            ServiceCycleProfileTemperature.Warm);
#endif
        try
        {
            before = _stateReader.CaptureSubmissionState(current);
        }
        catch (Exception ex) when (AutoHarvestReflectionAccess.IsExpectedFailure(ex))
        {
            return new AutoHarvestSubmissionResult(
                NativeMutationOutcome.BeforeCaptureFailed,
                default);
        }
#if SERVICE_CYCLE_PROFILE
        finally { beforeSnapshot.Complete(); }
#endif

        object? prototype;
#if SERVICE_CYCLE_PROFILE
        var revalidation = _profileOperations.Begin(
            AutoHarvestServiceCycleProfileStageCodes.ActionFactRevalidation,
            in context,
            ServiceCycleProfileTemperature.Warm);
#endif
        try
        {
            _stateReader.ReadFacts(
                current,
                before,
                out var facts,
                out prototype);
            var decision = AutoHarvestPolicy.EvaluatePair(
                current.Target.Pair,
                selected: true,
                facts);
            if (!decision.ShouldSubmit || prototype is null)
            {
                return new AutoHarvestSubmissionResult(
                    AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected);
            }
        }
        catch (Exception ex) when (AutoHarvestReflectionAccess.IsExpectedFailure(ex))
        {
            return new AutoHarvestSubmissionResult(
                AutoHarvestSubmissionFailureCode.RuntimeReadFailed);
        }
#if SERVICE_CYCLE_PROFILE
        finally { revalidation.Complete(); }
        var actionContext = context;
#endif

        return SubmitAdmitted(
            current.Target.ActionUuid,
            before,
            () =>
            {
#if SERVICE_CYCLE_PROFILE
                return Measure(
                    () => _stateReader.CaptureSubmissionState(current),
                    AutoHarvestServiceCycleProfileStageCodes.ActionAfterSnapshot,
                    actionContext);
#else
                return _stateReader.CaptureSubmissionState(current);
#endif
            },
            () =>
            {
#if SERVICE_CYCLE_PROFILE
                Measure(
                    () =>
                    {
                        _profileOperations.AddReflectedMethodCall();
                        _profileOperations.AddInvocationArgumentArray();
                        current.Contract.ActiveAddInstance.Invoke(
                            current.Shared.ActiveActions,
                            new[] { prototype!, (object)1 });
                    },
                    AutoHarvestServiceCycleProfileStageCodes.ActionNativeSubmission,
                    actionContext);
#else
                current.Contract.ActiveAddInstance.Invoke(
                    current.Shared.ActiveActions,
                    new[] { prototype!, (object)1 });
#endif
            }
#if SERVICE_CYCLE_PROFILE
            , (capturedBefore, capturedAfter) => Measure(
                () => VerifyAdmittedTransition(capturedBefore, capturedAfter),
                AutoHarvestServiceCycleProfileStageCodes.ActionPostconditionVerification,
                actionContext)
#endif
            );
    }

    internal static AutoHarvestSubmissionResult SubmitCaptured(
        string actionUuid,
        in AutoHarvestSubmissionState before,
        Func<AutoHarvestSubmissionState> captureAfter,
        Action execute,
        Func<AutoHarvestSubmissionState, AutoHarvestSubmissionState, bool>? verify = null)
    {
        if (!CanSubmit(before))
        {
            return new AutoHarvestSubmissionResult(
                before.IsValid
                    ? AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected
                    : AutoHarvestSubmissionFailureCode.RuntimeReadFailed);
        }

        return SubmitAdmitted(
            actionUuid,
            before,
            captureAfter,
            execute,
            verify ?? VerifyAdmittedTransition);
    }

    private static AutoHarvestSubmissionResult SubmitAdmitted(
        string actionUuid,
        in AutoHarvestSubmissionState before,
        Func<AutoHarvestSubmissionState> captureAfter,
        Action execute,
        Func<AutoHarvestSubmissionState, AutoHarvestSubmissionState, bool>? verify = null)
    {
        var evidence = NativeMutationVerifier.ExecuteAfterCapture(
            "Auto Harvest",
            actionUuid,
            "one exact native plot action is engaged using one available action entry",
            before,
            captureAfter,
            execute,
            verify ?? VerifyAdmittedTransition);
        return new AutoHarvestSubmissionResult(
            evidence.Outcome,
            NativeMutationCallOutcome.FromEvidence(evidence));
    }

#if SERVICE_CYCLE_PROFILE
    private void Measure(
        Action action,
        int stageCode,
        ServiceActionContext context)
    {
        var stage = _profileOperations.Begin(
            stageCode,
            in context,
            ServiceCycleProfileTemperature.Warm);
        try { action(); }
        finally { stage.Complete(); }
    }

    private T Measure<T>(
        Func<T> action,
        int stageCode,
        ServiceActionContext context)
    {
        var stage = _profileOperations.Begin(
            stageCode,
            in context,
            ServiceCycleProfileTemperature.Warm);
        try { return action(); }
        finally { stage.Complete(); }
    }
#endif

    internal static bool CanSubmit(in AutoHarvestSubmissionState state) =>
        state.IsValid &&
        state.NativeHasEmptyEntry &&
        state.EmptyEntryCount >= 1 &&
        state.SupportedCollectCount == 0 &&
        state.PairMatchCount == 0;

    internal static bool VerifyTransition(
        AutoHarvestSubmissionState before,
        AutoHarvestSubmissionState after) =>
        CanSubmit(before) &&
        VerifyAdmittedTransition(before, after);

    private static bool VerifyAdmittedTransition(
        AutoHarvestSubmissionState before,
        AutoHarvestSubmissionState after) =>
        after.IsValid &&
        after.UsedEntryCount == before.UsedEntryCount + 1 &&
        after.EmptyEntryCount == before.EmptyEntryCount - 1 &&
        after.NativeHasEmptyEntry == (after.EmptyEntryCount > 0) &&
        after.SupportedCollectCount == 1 &&
        after.PairMatchCount == 1 &&
        after.PairQuantity == 1 &&
        after.PairEngaged;
}
