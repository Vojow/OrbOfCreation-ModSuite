using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Execution.Validation;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

internal sealed class ServiceRunnerPreparedResources<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    internal ServiceRunnerPreparedResources(
        IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction> workerDefinition,
        TFrame frame,
        ServiceResourceClaimLedger claims,
        ServiceResourceClaim workerDefinitionClaim,
        ServiceResourceClaim? frameClaim)
    {
        WorkerDefinition = workerDefinition;
        Frame = frame;
        Claims = claims;
        WorkerDefinitionClaim = workerDefinitionClaim;
        FrameClaim = frameClaim;
    }

    internal IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction> WorkerDefinition { get; }
    internal TFrame Frame { get; }
    internal ServiceResourceClaimLedger Claims { get; }
    internal ServiceResourceClaim WorkerDefinitionClaim { get; }
    internal ServiceResourceClaim? FrameClaim { get; }
}

internal static class ServiceRunnerResourcePreparer<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    internal static ServiceResourceClaimResult TryPrepare(
        IServiceCycleDefinition<TFrame, TConfig, TState, TAction> definition,
        ServiceResourceClaimLedger claims,
        out ServiceRunnerPreparedResources<TFrame, TConfig, TState, TAction>? prepared)
    {
        prepared = null;
        IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction>? workerDefinition = null;
        ServiceResourceClaim? workerClaim = null;
        ServiceResourceClaim? frameClaim = null;
        var frame = default(TFrame)!;
        var hasFrame = false;
        try
        {
            var workerAdmission = claims.TryBeginFactory(
                ServiceResourceRole.WorkerDefinition,
                out workerClaim);
            if (workerAdmission != ServiceResourceClaimResult.Claimed) return workerAdmission;
            ServiceResourceClaimResult workerResult;
            try
            {
                workerDefinition = definition.CreateWorkerDefinition() ??
                    throw new InvalidOperationException("The service did not create a worker definition.");
                workerResult = claims.FinalizeFactory(workerClaim, workerDefinition);
            }
            finally { claims.EndFactory(workerClaim); }
            if (workerResult == ServiceResourceClaimResult.Aliased)
                throw new ServiceRunnerResourceAliasingException(
                    "worker definition",
                    suppressFrameRelease: false);
            ServiceCycleWorkerDefinitionValidator.EnsureSeparated(definition, workerDefinition);

            if (!typeof(TFrame).IsValueType)
            {
                var frameAdmission = claims.TryBeginFactory(ServiceResourceRole.Frame, out frameClaim);
                if (frameAdmission != ServiceResourceClaimResult.Claimed)
                {
                    claims.Release(workerClaim);
                    workerClaim = null;
                    return frameAdmission;
                }
            }
            ServiceResourceClaimResult frameResult = ServiceResourceClaimResult.Claimed;
            try
            {
                frame = definition.CreateFrame();
                if (!typeof(TFrame).IsValueType && frame is null)
                    throw new InvalidOperationException("The service did not create a reference frame.");
                hasFrame = true;
                if (!typeof(TFrame).IsValueType)
                    frameResult = claims.FinalizeFactory(frameClaim!, (object)frame!);
            }
            finally
            {
                if (!typeof(TFrame).IsValueType) claims.EndFactory(frameClaim!);
            }
            if (frameResult == ServiceResourceClaimResult.Aliased)
            {
                frameClaim = null;
                throw new ServiceRunnerResourceAliasingException("frame", suppressFrameRelease: true);
            }

            prepared = new ServiceRunnerPreparedResources<TFrame, TConfig, TState, TAction>(
                workerDefinition,
                frame,
                claims,
                workerClaim,
                frameClaim);
            return ServiceResourceClaimResult.Claimed;
        }
        catch (Exception ex)
        {
            var suppressFrameRelease =
                ex is ServiceRunnerResourceClaimException claimFailure && claimFailure.SuppressFrameRelease;
            Exception? cleanupFailure = null;
            if (hasFrame && workerDefinition is not null && !suppressFrameRelease)
            {
                try { workerDefinition.ReleaseFrame(ref frame); }
                catch (Exception exception) { cleanupFailure = exception; }
            }
            claims.Release(frameClaim);
            claims.Release(workerClaim);
            if (workerDefinition is not null &&
                !ServiceCycleFatalExceptionPolicy.MustEscape(workerDefinition, ex) &&
                cleanupFailure is not null &&
                ServiceCycleFatalExceptionPolicy.MustEscape(workerDefinition, cleanupFailure))
                throw cleanupFailure;
            throw;
        }
    }
}
