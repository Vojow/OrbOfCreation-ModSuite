using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;

internal sealed partial class ServiceCycleReplayDefinitionAdapter<
    TFrame,
    TConfig,
    TState,
    TAction,
    TCycleInputRecord,
    TStateRecord,
    TActionRecord>
    where TConfig : notnull
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    public ServiceStartDecision ShouldStart(
        in TConfig config,
        in ServiceCycleStartContext context) => _definition.ShouldStart(in config, in context);

    public ServiceCaptureResult Capture(
        ref TFrame frame,
        in TConfig config,
        in ServiceCaptureContext context)
    {
        var reusableFrame = frame;
        var result = _definition.Capture(ref frame, in config, in context);
        if (!result.IsValid || !result.IsCaptured ||
            !typeof(TFrame).IsValueType && !ReferenceEquals(reusableFrame, frame))
            return result;

        var cycleIdentity = new ServiceCycleIdentity(
            context.Service,
            context.Lifecycle,
            context.Config,
            result.StrategyGeneration,
            context.Capture,
            context.Cycle);
        var traceServiceKey = _traceServiceKey;
        if (traceServiceKey <= 0)
            throw new InvalidOperationException("Replay trace identity was not bound before capture.");
        var cycle = new ServiceCycleReplayCycleKey(traceServiceKey, in cycleIdentity);
        var bridge = FindBridge(context.Lifecycle.Value);
#if SERVICE_CYCLE_PROFILE
        var constructionProfile = BeginProfile(
            ServiceCycleProfileCommonStageCodes.DetachedInputConstruction,
            in context);
        var publicationProfile = default(ServiceCycleProfileStageScope);
#endif
        try
        {
            var record = _definition.CreateCycleInputRecord(
                in frame,
                in config,
                in context,
                in result);
#if SERVICE_CYCLE_PROFILE
            constructionProfile.AddRecordCopies();
            constructionProfile.Complete();
#endif
            if (bridge is null)
                _session.MarkRequiredRecordMissing(
                    in cycle,
                    new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0));
            else
            {
#if SERVICE_CYCLE_PROFILE
                publicationProfile = BeginProfile(
                    ServiceCycleProfileCommonStageCodes.DetachedInputBridgePublication,
                    in context);
#endif
                bridge.Publish(in cycle, in record);
#if SERVICE_CYCLE_PROFILE
                publicationProfile.AddRecordCopies();
                publicationProfile.Complete();
#endif
            }
        }
        catch (Exception exception) when (
            exception is not StackOverflowException &&
            !ServiceCycleFatalExceptionPolicy.MustEscape(this, exception))
        {
            if (bridge is null)
                _session.MarkRequiredRecordMissing(
                    in cycle,
                    new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0));
            else
                bridge.PublishMissing(in cycle);
        }
#if SERVICE_CYCLE_PROFILE
        finally
        {
            publicationProfile.Abandon();
            constructionProfile.Abandon();
        }
#endif
        return result;
    }

#if SERVICE_CYCLE_PROFILE
    private ServiceCycleProfileStageScope BeginProfile(
        int stageCode,
        in ServiceCaptureContext context) =>
        context.ProfileCoordinates.TryCreateContext(
            stageCode,
            context.Lifecycle.Value,
            context.Cycle.Value,
            ServiceCycleProfileTemperature.Warm,
            out var profileContext)
            ? _profileProbe.Begin(in profileContext)
            : default;
#endif

    public ServiceActionResult TryExecute(
        in TAction action,
        in TConfig config,
        in ServiceActionContext context) => _definition.TryExecute(in action, in config, in context);
}
