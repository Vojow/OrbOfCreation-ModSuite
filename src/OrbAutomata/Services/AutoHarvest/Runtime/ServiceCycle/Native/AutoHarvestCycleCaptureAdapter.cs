using System;
using OrbModding.Common.Runtime;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

internal sealed class AutoHarvestCycleCaptureAdapter : IAutoHarvestCycleCapturePort
{
    private readonly IAutoHarvestBindingPort _bindings;
    private readonly IAutoHarvestCaptureStatePort _reader;
    private readonly IAutoHarvestGatePort _gates;
    private readonly IAutoHarvestContractCircuit _contractCircuit;
    private readonly Func<bool> _ownsActionFamily;
#if SERVICE_CYCLE_PROFILE
    private readonly AutoHarvestProfileOperations _profileOperations;
    private readonly IAutoHarvestProfileBindingObservation _profileBindings;
#endif

    public AutoHarvestCycleCaptureAdapter(
        IAutoHarvestBindingPort bindings,
        IAutoHarvestCaptureStatePort reader,
        IAutoHarvestGatePort gates,
        IAutoHarvestContractCircuit contractCircuit,
        Func<bool> ownsActionFamily
#if SERVICE_CYCLE_PROFILE
        , AutoHarvestProfileOperations profileOperations,
        IAutoHarvestProfileBindingObservation profileBindings
#endif
        )
    {
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _gates = gates ?? throw new ArgumentNullException(nameof(gates));
        _contractCircuit = contractCircuit ?? throw new ArgumentNullException(nameof(contractCircuit));
        _ownsActionFamily = ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
#if SERVICE_CYCLE_PROFILE
        _profileOperations = profileOperations ?? throw new ArgumentNullException(nameof(profileOperations));
        _profileBindings = profileBindings ?? throw new ArgumentNullException(nameof(profileBindings));
#endif
    }

    public AutoHarvestCycleCaptureDisposition Capture(
        in AutomataConfiguration config,
        LifecycleGeneration lifecycle,
#if SERVICE_CYCLE_PROFILE
        in ServiceCaptureContext profileContext,
#endif
        out AutoHarvestCycleFrame frame)
    {
        if (!config.AutoHarvest.CollectFruitTrees && !config.AutoHarvest.CollectTreasureTrees)
        {
            frame = AssembleFrame(
                config,
                AutoHarvestPairCapture.NotSelected(AutoHarvestPair.FruitTree),
                AutoHarvestPairCapture.NotSelected(AutoHarvestPair.TreasureTree)
#if SERVICE_CYCLE_PROFILE
                , profileContext,
                _profileBindings.CurrentTemperature
#endif
                );
            return AutoHarvestCycleCaptureDisposition.Captured;
        }

        return CaptureSelected(
            config,
            lifecycle,
#if SERVICE_CYCLE_PROFILE
            profileContext,
#endif
            out frame);
    }

    private AutoHarvestCycleCaptureDisposition CaptureSelected(
        in AutomataConfiguration config,
        LifecycleGeneration lifecycle,
#if SERVICE_CYCLE_PROFILE
        in ServiceCaptureContext profileContext,
#endif
        out AutoHarvestCycleFrame frame)
    {
        var resolved = default(AutoHarvestResolvedPairSet);
#if SERVICE_CYCLE_PROFILE
        var temperature = _profileBindings.PrepareTemperature();
        var bindingStage = _profileOperations.Begin(
            AutoHarvestServiceCycleProfileStageCodes.BindingAndCoherence,
            profileContext,
            temperature);
        try
        {
#endif
        resolved = _bindings.ResolvePairSet();
        if (!AutoHarvestNativeLifecycle.Matches(resolved, lifecycle))
        {
            frame = default;
            return AutoHarvestCycleCaptureDisposition.Unavailable;
        }
#if SERVICE_CYCLE_PROFILE
        if (resolved.Fruit.Succeeded &&
            resolved.Treasure.Succeeded &&
            _profileBindings.TryComplete(temperature))
        {
            bindingStage.Complete();
        }
        }
        finally
        {
            bindingStage.Abandon();
        }
#endif
        _gates.ObserveResolvedPairs(resolved);
        var captureResolution = SelectActiveCaptureResolution(config, resolved);
        var activeCaptureFailure = default(AutoHarvestNativeFailure);
        var activeActions = default(AutoHarvestActiveActionSnapshot);
        if (captureResolution.Succeeded)
        {
#if SERVICE_CYCLE_PROFILE
            var activeStage = _profileOperations.Begin(
                AutoHarvestServiceCycleProfileStageCodes.ActiveActionTraversal,
                profileContext,
                temperature);
#endif
            try
            {
                activeActions = _reader.CaptureActiveActions(captureResolution.Pair);
#if SERVICE_CYCLE_PROFILE
                activeStage.Complete();
#endif
            }
            catch (Exception ex) when (AutoHarvestReflectionAccess.IsExpectedFailure(ex))
            {
                var kind = AutoHarvestReflectionAccess.ClassifyExpectedFailure(ex);
                activeCaptureFailure = AutoHarvestNativeFailure.Create(
                    kind,
                    AutoHarvestRuntimeFailureScope.Feature);
                if (kind == AutoHarvestRuntimeFailureKind.Contract)
                    _contractCircuit.Block(
                        captureResolution.Pair.Target.Pair,
                        AutoHarvestRuntimeFailureScope.Feature);
            }
#if SERVICE_CYCLE_PROFILE
            finally
            {
                activeStage.Abandon();
            }
#endif
        }
        var fruit = CapturePair(
            AutoHarvestPair.FruitTree,
            config.AutoHarvest.CollectFruitTrees,
            resolved.Fruit,
            activeCaptureFailure,
            activeActions
#if SERVICE_CYCLE_PROFILE
            , profileContext,
            temperature
#endif
            );
        var treasure = CapturePair(
            AutoHarvestPair.TreasureTree,
            config.AutoHarvest.CollectTreasureTrees,
            resolved.Treasure,
            activeCaptureFailure,
            activeActions
#if SERVICE_CYCLE_PROFILE
            , profileContext,
            temperature
#endif
            );
        frame = AssembleFrame(
            config,
            fruit,
            treasure
#if SERVICE_CYCLE_PROFILE
            , profileContext,
            temperature
#endif
            );
        return AutoHarvestCycleCaptureDisposition.Captured;
    }

    private AutoHarvestPairCapture CapturePair(
        AutoHarvestPair pair,
        bool selected,
        in AutoHarvestPairResolution resolution,
        in AutoHarvestNativeFailure activeCaptureFailure,
        in AutoHarvestActiveActionSnapshot activeActions
#if SERVICE_CYCLE_PROFILE
        , in ServiceCaptureContext profileContext,
        ServiceCycleProfileTemperature temperature
#endif
        )
    {
        if (!selected) return AutoHarvestPairCapture.NotSelected(pair);
        var contractFailure = _contractCircuit.FailureFor(pair);
        if (contractFailure.IsValid) return Unavailable(pair, contractFailure);
        if (!resolution.Succeeded) return Unavailable(pair, resolution.Failure);
        if (_gates.IsQuarantined(pair))
        {
            return AutoHarvestPairCapture.Unavailable(
                pair,
                AutoHarvestCaptureUnavailableReason.Faulted,
                AutoHarvestCaptureFailureScope.Pair);
        }
        if (activeCaptureFailure.IsValid) return Unavailable(pair, activeCaptureFailure);

#if SERVICE_CYCLE_PROFILE
        var factStage = _profileOperations.Begin(
            pair == AutoHarvestPair.FruitTree
                ? AutoHarvestServiceCycleProfileStageCodes.FruitFactCapture
                : AutoHarvestServiceCycleProfileStageCodes.TreasureFactCapture,
            profileContext,
            temperature);
#endif
        try
        {
            _reader.ReadFacts(
                resolution.Pair,
                activeActions.Project(pair),
                out var facts,
                out _);
            var captured = AutoHarvestPairCapture.Captured(pair, facts);
#if SERVICE_CYCLE_PROFILE
            factStage.Complete();
#endif
            return captured;
        }
        catch (Exception ex) when (AutoHarvestReflectionAccess.IsExpectedFailure(ex))
        {
            var kind = AutoHarvestReflectionAccess.ClassifyExpectedFailure(ex);
            var failure = AutoHarvestNativeFailure.Create(
                kind,
                AutoHarvestRuntimeFailureScope.Pair);
            if (kind == AutoHarvestRuntimeFailureKind.Contract)
                _contractCircuit.Block(pair, AutoHarvestRuntimeFailureScope.Pair);
            return Unavailable(pair, failure);
        }
#if SERVICE_CYCLE_PROFILE
        finally
        {
            factStage.Abandon();
        }
#endif
    }

    private AutoHarvestCycleFrame AssembleFrame(
        in AutomataConfiguration config,
        in AutoHarvestPairCapture fruit,
        in AutoHarvestPairCapture treasure
#if SERVICE_CYCLE_PROFILE
        , in ServiceCaptureContext profileContext,
        ServiceCycleProfileTemperature temperature
#endif
        )
    {
#if SERVICE_CYCLE_PROFILE
        var frameStage = _profileOperations.Begin(
            AutoHarvestServiceCycleProfileStageCodes.FrameAssemblyAndOwnershipProjection,
            profileContext,
            temperature);
        try
        {
            _profileOperations.AddSelectedPairs(
                (uint)((config.AutoHarvest.CollectFruitTrees ? 1 : 0) + (config.AutoHarvest.CollectTreasureTrees ? 1 : 0)));
            _profileOperations.AddReadyPairs(
                (uint)((IsReady(fruit) ? 1 : 0) + (IsReady(treasure) ? 1 : 0)));
#endif
        var frame = new AutoHarvestCycleFrame(fruit, treasure, _ownsActionFamily());
#if SERVICE_CYCLE_PROFILE
        frameStage.Complete();
#endif
        return frame;
#if SERVICE_CYCLE_PROFILE
        }
        finally
        {
            frameStage.Abandon();
        }
#endif
    }

#if SERVICE_CYCLE_PROFILE
    private static bool IsReady(in AutoHarvestPairCapture capture) =>
        capture.Kind == AutoHarvestPairCaptureKind.Captured &&
        capture.Facts.Readiness == AutoHarvestEvidenceState.Verified;
#endif

    private AutoHarvestPairResolution SelectActiveCaptureResolution(
        in AutomataConfiguration config,
        in AutoHarvestResolvedPairSet resolved)
    {
        if (config.AutoHarvest.CollectFruitTrees && resolved.Fruit.Succeeded &&
            !_contractCircuit.FailureFor(AutoHarvestPair.FruitTree).IsValid &&
            !_gates.IsQuarantined(AutoHarvestPair.FruitTree))
        {
            return resolved.Fruit;
        }
        if (config.AutoHarvest.CollectTreasureTrees && resolved.Treasure.Succeeded &&
            !_contractCircuit.FailureFor(AutoHarvestPair.TreasureTree).IsValid &&
            !_gates.IsQuarantined(AutoHarvestPair.TreasureTree))
        {
            return resolved.Treasure;
        }
        return default;
    }

    private static AutoHarvestPairCapture Unavailable(
        AutoHarvestPair pair,
        in AutoHarvestNativeFailure failure) =>
        AutoHarvestPairCapture.Unavailable(
            pair,
            failure.Kind == AutoHarvestRuntimeFailureKind.Contract
                ? AutoHarvestCaptureUnavailableReason.ContractUnavailable
                : AutoHarvestCaptureUnavailableReason.RegistryNotReady,
            failure.Scope == AutoHarvestRuntimeFailureScope.Feature
                ? AutoHarvestCaptureFailureScope.Feature
                : AutoHarvestCaptureFailureScope.Pair);
}
