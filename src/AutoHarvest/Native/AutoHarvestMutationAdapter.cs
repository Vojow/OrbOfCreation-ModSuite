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
    private readonly IAutoHarvestSubmissionStatePort _stateReader;
#if SERVICE_CYCLE_PROFILE
    private readonly AutomataProfileOperations _profileOperations;
#endif

    public AutoHarvestMutationAdapter(
        IAutoHarvestSubmissionStatePort stateReader
#if SERVICE_CYCLE_PROFILE
        , AutomataProfileOperations profileOperations
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
        in ResolvedAutoHarvestPair resolved,
        in AutoHarvestPairFacts facts,
        AutoHarvestActionSafetyState safety
#if SERVICE_CYCLE_PROFILE
        , in ServiceActionContext context
#endif
        )
    {
        var current = resolved;
        AutoHarvestSubmissionState before;
#if SERVICE_CYCLE_PROFILE
        var beforeSnapshot = _profileOperations.Begin(
            ServiceCycleProfileSpan.AutoHarvestActionBeforeSnapshot,
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
            ServiceCycleProfileSpan.AutoHarvestActionPrototypeResolution,
            in context,
            ServiceCycleProfileTemperature.Warm);
#endif
        try
        {
            prototype = _stateReader.ReadPrototype(current);
            var decision = AutoHarvestPolicy.EvaluateSubmission(
                current.Target.Pair,
                facts,
                safety,
                before);
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
                    ServiceCycleProfileSpan.AutoHarvestActionAfterSnapshot,
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
                    ServiceCycleProfileSpan.AutoHarvestActionNativeSubmission,
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
                ServiceCycleProfileSpan.AutoHarvestActionPostconditionVerification,
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
        ServiceCycleProfileSpan span,
        ServiceActionContext context)
    {
        var stage = _profileOperations.Begin(
            span,
            in context,
            ServiceCycleProfileTemperature.Warm);
        try { action(); }
        finally { stage.Complete(); }
    }

    private T Measure<T>(
        Func<T> action,
        ServiceCycleProfileSpan span,
        ServiceActionContext context)
    {
        var stage = _profileOperations.Begin(
            span,
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
