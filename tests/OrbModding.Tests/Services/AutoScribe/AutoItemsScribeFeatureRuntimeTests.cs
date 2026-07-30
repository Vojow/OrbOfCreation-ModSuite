using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoItemsScribeFeatureRuntimeTests
{
    [Fact]
    public void AutoItemsRuntimePublishesEvaluationEmergencyOwnershipAndLifecycleHealth()
    {
        var owns = true;
        var statusRegistry = new FeatureStatusRegistry();
        using var status = Reporter(
            statusRegistry,
            AutomataFeatureStatuses.AutoItemsFeatureId,
            "Auto Items");
        var dependencies = new AutoItemsFeatureDependencies(
            Resolver(),
            () => 2,
            () => owns,
            () => true,
            status);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1));
        registry.ConfigurationPublication.Publish(Configuration());
        var context = new AutomataServiceCycleFeatureContext(
            registry,
            new ConfigGeneration(1),
            lifecycleValue: 1);
        var runtime = new AutoItemsServiceCycleFeature(dependencies).Register(in context);
        registry.Seal();
        runtime.ActivateDiagnostics();
        TestWorldCollector.CollectedAtActivation(registry);

        using (var pump = new SuiteFramePump(registry))
        {
            PumpUntil(
                pump,
                runtime,
                () => status.Current.State == FeatureStatusState.Operational);
            Assert.Equal(FeatureStatusReasonCode.None, status.Current.Reason.Code);

            pump.SetEmergencyStop(true);
            var report = default(SuiteFramePumpReport);
            runtime.ObserveFrame(pump, in report);
            Assert.Equal(FeatureStatusReasonCode.EmergencyDisabled, status.Current.Reason.Code);

            pump.SetEmergencyStop(false);
            owns = false;
            runtime.ObserveFrame(pump, in report);
            Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, status.Current.Reason.Code);

            owns = true;
            runtime.ObserveLifecycle(2, new ConfigGeneration(2));
            Assert.Equal(FeatureStatusState.NotReady, status.Current.State);
            Assert.Equal(2, status.Current.LifecycleGeneration);
            runtime.ObserveConfiguration(new ConfigGeneration(1));
            Assert.Equal(2, status.Current.LifecycleGeneration);

            runtime.DisposeDiagnostics();
            report = pump.PumpFrame(42);
            runtime.ObserveFrame(pump, in report);
        }

        runtime.DisposeRegistration();
    }

    [Fact]
    public void AutoScribeRuntimeProjectsEvidenceAndEveryRuntimeBlocker()
    {
        var owns = true;
        var canConsume = true;
        var statusRegistry = new FeatureStatusRegistry();
        using var status = Reporter(
            statusRegistry,
            AutomataFeatureStatuses.AutoScribeFeatureId,
            "Auto Scribe");
        Assert.True(new AutoScribeIdentityCatalog().TryGetProfile(
            GameAssemblyAudit.WindowsV1052BaselineId,
            out var profile));
        var dependencies = new AutoScribeFeatureDependencies(
            Resolver(),
            profile,
            () => 2,
            () => owns,
            () => canConsume,
            () => true,
            status);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1));
        registry.ConfigurationPublication.Publish(Configuration());
        var context = new AutomataServiceCycleFeatureContext(
            registry,
            new ConfigGeneration(1),
            lifecycleValue: 1);
        var runtime = new AutoScribeServiceCycleFeature(dependencies).Register(in context);
        registry.Seal();
        runtime.ActivateDiagnostics();
        TestWorldCollector.CollectedAtActivation(registry);

        using (var pump = new SuiteFramePump(registry))
        {
            PumpUntil(
                pump,
                runtime,
                () => status.Current.State == FeatureStatusState.Degraded);
            Assert.Equal(FeatureStatusReasonCode.EvidenceUnavailable, status.Current.Reason.Code);

            pump.SetEmergencyStop(true);
            var report = default(SuiteFramePumpReport);
            runtime.ObserveFrame(pump, in report);
            Assert.Equal(FeatureStatusReasonCode.EmergencyDisabled, status.Current.Reason.Code);

            pump.SetEmergencyStop(false);
            owns = false;
            runtime.ObserveFrame(pump, in report);
            Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, status.Current.Reason.Code);

            owns = true;
            runtime.ObserveLifecycle(2, new ConfigGeneration(2));
            Assert.Equal(
                FeatureStatusReasonCode.GameplayNotReady,
                status.Current.Reason.Code);
            Assert.Equal(2, status.Current.LifecycleGeneration);

            canConsume = false;
            runtime.ObserveFrame(pump, in report);
            Assert.Equal(FeatureStatusReasonCode.ParentFeatureDisabled, status.Current.Reason.Code);

            runtime.DisposeDiagnostics();
            report = pump.PumpFrame(43);
            runtime.ObserveFrame(pump, in report);
        }

        runtime.DisposeRegistration();
    }

    [Fact]
    public void AutoScribeDiagnosticsQuarantineOverridesEvaluatedCoverage()
    {
        var statusRegistry = new FeatureStatusRegistry();
        using var status = Reporter(
            statusRegistry,
            AutomataFeatureStatuses.AutoScribeFeatureId,
            "Auto Scribe");
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1));
        registry.ConfigurationPublication.Publish(Configuration());
        Assert.True(new AutoScribeIdentityCatalog().TryGetProfile(
            GameAssemblyAudit.WindowsV1052BaselineId,
            out var profile));
        var metadata = new AutomataServiceMetadata(
            AutoScribeServiceCycleFeature.ServiceId,
            WakePolicy.AfterDecision(
                MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1))),
            new ServiceFaultRecoveryPolicy(
                MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250)),
                MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10))));
        var definition = AutomataService.Define<AutoScribeCycleState, AutoScribeCycleAction>(
            in metadata,
            () => new AutoScribeWorker(profile),
            AlwaysStart,
            RejectScribeAction);
        using var registration = registry.Register(definition);
        registry.Seal();
        TestWorldCollector.CollectedAtActivation(registry);
        var bridge = new AutoScribeServiceCycleDiagnosticsBridge(
            lifecycle: 1,
            new ConfigGeneration(1),
            owned: true,
            canConsumeScrolls: true,
            status);

        using var pump = new SuiteFramePump(registry);
        PumpUntil(
            pump,
            (framePump, report) =>
                bridge.Observe(
                    framePump,
                    in report,
                    owned: true,
                    canConsumeScrolls: true,
                    quarantined: false),
            () => status.Current.State == FeatureStatusState.Degraded);

        var quiet = pump.PumpFrame(40);
        bridge.Observe(
            pump,
            in quiet,
            owned: true,
            canConsumeScrolls: true,
            quarantined: true);

        Assert.Equal(FeatureStatusState.Faulted, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.PostconditionFailed, status.Current.Reason.Code);
    }

    [Fact]
    public void UnavailableAutoScribeFeaturePublishesAuditedIdentityFailure()
    {
        var statusRegistry = new FeatureStatusRegistry();
        using var status = Reporter(
            statusRegistry,
            AutomataFeatureStatuses.AutoScribeFeatureId,
            "Auto Scribe");
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1));
        var context = new AutomataServiceCycleFeatureContext(
            registry,
            new ConfigGeneration(3),
            lifecycleValue: 7);
        var runtime = new AutoScribeUnavailableServiceCycleFeature(status).Register(in context);

        runtime.ActivateDiagnostics();
        runtime.ObserveConfiguration(new ConfigGeneration(4));
        runtime.ObserveLifecycle(8, new ConfigGeneration(4));
        runtime.DisposeDiagnostics();
        runtime.DisposeRegistration();

        Assert.Equal(FeatureStatusState.ContractUnavailable, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.IdentityMismatch, status.Current.Reason.Code);
        Assert.Equal(7, status.Current.LifecycleGeneration);
    }

    private static void PumpUntil(
        SuiteFramePump pump,
        IAutomataServiceCycleFeatureRuntime runtime,
        Func<bool> complete) =>
        PumpUntil(
            pump,
            (framePump, report) => runtime.ObserveFrame(framePump, in report),
            complete);

    private static void PumpUntil(
        SuiteFramePump pump,
        Action<SuiteFramePump, SuiteFramePumpReport> observe,
        Func<bool> complete)
    {
        long frame = 1;
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                var report = pump.PumpFrame(frame++);
                observe(pump, report);
                return complete();
            },
            TimeSpan.FromSeconds(2)));
    }

    private static TypedRegistryResolver Resolver()
    {
        IDictionary registry = new Dictionary<Guid, object>();
        return new TypedRegistryResolver(
            () => 2,
            () => TypedRegistrySourceSnapshot.Ready(registry),
            value => value is IdScriptableObject entity ? entity.GetGuid() : null);
    }

    private static SuiteRuntimeConfiguration Configuration() =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = AutoItemsOperationMode.Active,
                UseScrolls = true,
                EvaluationInterval =
                    MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(10)),
            },
            AutoScribe = new AutoScribeConfiguration
            {
                Mode = AutoScribeOperationMode.Active,
                EvaluationInterval =
                    MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(10)),
            },
        };

    private static AutomataFeatureStatusReporter Reporter(
        FeatureStatusRegistry registry,
        string featureId,
        string name) =>
        new(
            registry,
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, featureId),
                name,
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.GameplayNotReady,
                    "waiting"),
                lifecycleGeneration: 1));

    private static ServiceStartDecision AlwaysStart(
        in SuiteRuntimeConfiguration configuration,
        in ServiceCycleStartContext context) =>
        ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);

    private static ServiceActionResult RejectScribeAction(
        in AutoScribeCycleAction action,
        in SuiteRuntimeConfiguration configuration,
        in ServiceActionContext context) =>
        ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);
}
