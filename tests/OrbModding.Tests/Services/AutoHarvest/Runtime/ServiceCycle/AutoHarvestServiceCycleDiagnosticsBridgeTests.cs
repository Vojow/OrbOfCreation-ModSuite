using System;
using System.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutoHarvestServiceCycleDiagnosticsBridgeTests
{
    [Fact]
    public void ProjectionPublishesServiceCyclePairHealthWithoutLegacyCycleIdentity()
    {
        var configuration = AutoHarvestConfigurationFactory.Create(
            masterEnabled: true,
            emergencyDisabled: false,
            activeMode: true,
            fruitSelected: true,
            treasureSelected: false,
            MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
        var definition = AutoHarvestService.Define(
            new ReadyFruitCapture(),
            new CommittingActions());
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1));
        using var registration = registry.RegisterReplay(
            definition,
            configuration,
            new ServiceCycleReplaySession(
                new ServiceCycleTraceSessionId(81),
                new ServiceCycleReplaySessionOptions(false, 0, 0, 0)));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var runtimeDiagnostics = new RuntimeDiagnosticsRegistry();
        using var bridge = new AutoHarvestServiceCycleDiagnosticsBridge(
            1,
            configuration,
            ownsActionFamily: true,
            runtimeDiagnostics,
            featureStatus: null);

        pump.PumpFrame(1);
        Assert.True(registration.WaitForResponseReady(TimeSpan.FromSeconds(2)));
        var response = pump.PumpFrame(2);
        using (var contention = new HandoffGateContention(registration.Runner))
        {
            contention.Acquire();
            try { bridge.Observe(pump, in response, ownsActionFamily: true); }
            finally { contention.Release(); }
        }

        var waiting = Assert.Single(runtimeDiagnostics.GetSnapshot());
        Assert.NotEqual(
            FeatureStatusState.Operational,
            waiting.Capabilities.Single(value =>
                value.CapabilityId == AutoHarvestRuntimeDiagnosticsPublisher.FruitCapabilityId).State);

        var quiet = pump.PumpFrame(3);
        Assert.Equal(0, quiet.ResponsesAcquired);
        bridge.Observe(pump, in quiet, ownsActionFamily: true);

        var snapshot = Assert.Single(runtimeDiagnostics.GetSnapshot());
        Assert.Equal(AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName, snapshot.Implementation);
        Assert.Equal(
            FeatureStatusState.Operational,
            snapshot.Capabilities.Single(value =>
                value.CapabilityId == AutoHarvestRuntimeDiagnosticsPublisher.FruitCapabilityId).State);
        Assert.Equal(
            FeatureStatusState.ConfigurationDisabled,
            snapshot.Capabilities.Single(value =>
                value.CapabilityId == AutoHarvestRuntimeDiagnosticsPublisher.TreasureCapabilityId).State);
    }

    private sealed class ReadyFruitCapture : IAutoHarvestCycleCapturePort
    {
        public AutoHarvestCycleCaptureDisposition Capture(
            in AutomataConfiguration config,
            LifecycleGeneration lifecycle,
            out AutoHarvestCycleFrame frame)
        {
            var facts = new AutoHarvestPairFacts(
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestActionSafetyState.NativePhaseCyclePreserving,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified);
            frame = new AutoHarvestCycleFrame(
                AutoHarvestPairCapture.Captured(AutoHarvestPair.FruitTree, facts),
                AutoHarvestPairCapture.NotSelected(AutoHarvestPair.TreasureTree),
                ownsActionFamily: true);
            return AutoHarvestCycleCaptureDisposition.Captured;
        }
    }

    private sealed class CommittingActions : IAutoHarvestCycleActionPort
    {
        public ServiceActionResult TryExecute(
            in AutoHarvestCycleAction action,
            in AutomataConfiguration config,
            in ServiceActionContext context) =>
            ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(1, 1, 1)));
    }

    [Fact]
    public void EmergencyAndOwnershipConditionsOverrideProjectedPairHealth()
    {
        var configuration = AutoHarvestConfigurationFactory.Create(
            masterEnabled: true,
            emergencyDisabled: false,
            activeMode: true,
            fruitSelected: true,
            treasureSelected: false,
            MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
        var definition = AutoHarvestService.Define(
            new ReadyFruitCapture(),
            new CommittingActions());
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1));
        using var registration = registry.RegisterReplay(
            definition,
            configuration,
            new ServiceCycleReplaySession(
                new ServiceCycleTraceSessionId(82),
                new ServiceCycleReplaySessionOptions(false, 0, 0, 0)));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var featureRegistry = new FeatureStatusRegistry();
        using var featureStatus = new AutomataFeatureStatusReporter(
            featureRegistry,
            new FeatureStatusSnapshot(
                new FeatureStatusKey(
                    PluginIds.AutomataGuid,
                    AutomataFeatureStatuses.AutoHarvestFeatureId),
                "Auto Harvest",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.RegistryNotReady, "waiting"),
                lifecycleGeneration: 1));
        using var bridge = new AutoHarvestServiceCycleDiagnosticsBridge(
            1,
            configuration,
            ownsActionFamily: true,
            runtimeDiagnostics: null,
            featureStatus);

        pump.PumpFrame(1);
        Assert.True(registration.WaitForResponseReady(TimeSpan.FromSeconds(2)));
        var response = pump.PumpFrame(2);
        bridge.Observe(pump, in response, ownsActionFamily: true);
        Assert.Equal(FeatureStatusState.Operational, featureStatus.Current.State);

        pump.SetEmergencyStop(true);
        bridge.Observe(pump, default, ownsActionFamily: true);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, featureStatus.Current.State);
        Assert.Equal(FeatureStatusReasonCode.EmergencyDisabled, featureStatus.Current.Reason.Code);

        pump.SetEmergencyStop(false);
        bridge.Observe(pump, default, ownsActionFamily: false);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, featureStatus.Current.State);
        Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, featureStatus.Current.Reason.Code);
    }
}
