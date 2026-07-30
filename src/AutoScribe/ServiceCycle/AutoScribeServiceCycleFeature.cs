using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal sealed class AutoScribeServiceCycleFeature : IAutomataServiceCycleFeature
{
    private readonly AutoScribeFeatureDependencies _dependencies;
    internal AutoScribeServiceCycleFeature(AutoScribeFeatureDependencies dependencies) =>
        _dependencies = dependencies;

    public IAutomataServiceCycleFeatureRuntime Register(
        in AutomataServiceCycleFeatureContext context)
    {
        var adapter = new AutoScribeNativeAdapter(_dependencies);
        var metadata = new AutomataServiceMetadata(
            ServiceId,
            WakePolicy.AfterDecision(MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1))),
            new ServiceFaultRecoveryPolicy(
                MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250)),
                MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10))));
        var definition = AutomataService.Define<AutoScribeCycleState, AutoScribeCycleAction>(
            in metadata,
            () => new AutoScribeWorker(
                _dependencies.Profile,
                _dependencies.CanConsumeScrolls),
            ShouldStart,
            adapter.TryExecute);
        var registration = context.Registry.Register(
            definition, ServiceActionDispatchPolicy.Bounded(1));
        return new Runtime(
            _dependencies,
            registration,
            adapter,
            context.LifecycleValue,
            context.ConfigurationGeneration);
    }

    internal static ServiceId ServiceId => new("orbautomata.auto-scribe");

    private ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context) =>
        IsOperational(config) && Safe(_dependencies.CanConsumeScrolls)
            ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
            : ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.AfterDecision(Interval(config)));

    internal static bool IsOperational(SuiteRuntimeConfiguration config) =>
        config.General.Enabled &&
        config.CanStartAutoScribeActively &&
        config.CanStartAutoItemsActively &&
        config.AutoItems.UseScrolls;

    internal static MonotonicDuration Interval(SuiteRuntimeConfiguration config) =>
        config.AutoScribe.EvaluationInterval.Ticks > 0
            ? config.AutoScribe.EvaluationInterval
            : MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));

    private static bool Safe(Func<bool> read)
    {
        try { return read(); }
        catch (Exception) { return false; }
    }

    private sealed class Runtime : IAutomataServiceCycleFeatureRuntime
    {
        private readonly ServiceRegistration<AutoScribeCycleState, AutoScribeCycleAction> _registration;
        private readonly AutoScribeFeatureDependencies _dependencies;
        private readonly AutoScribeNativeAdapter _adapter;
        private readonly long _lifecycleValue;
        private readonly ConfigGeneration _initialConfigurationGeneration;
        private AutoScribeServiceCycleDiagnosticsBridge? _diagnostics;
        internal Runtime(
            AutoScribeFeatureDependencies dependencies,
            ServiceRegistration<AutoScribeCycleState, AutoScribeCycleAction> registration,
            AutoScribeNativeAdapter adapter,
            long lifecycleValue,
            ConfigGeneration initialConfigurationGeneration)
        {
            _dependencies = dependencies;
            _registration = registration;
            _adapter = adapter;
            _lifecycleValue = lifecycleValue;
            _initialConfigurationGeneration = initialConfigurationGeneration;
        }
        public void ActivateDiagnostics() =>
            _diagnostics = new AutoScribeServiceCycleDiagnosticsBridge(
                _lifecycleValue,
                _initialConfigurationGeneration,
                Safe(_dependencies.Owns),
                Safe(_dependencies.CanConsumeScrolls),
                _dependencies.FeatureStatus);
        public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report) =>
            _diagnostics?.Observe(
                pump,
                in report,
                Safe(_dependencies.Owns),
                Safe(_dependencies.CanConsumeScrolls),
                _adapter.IsQuarantined);
        public void ObserveConfiguration(ConfigGeneration configurationGeneration) =>
            _diagnostics?.ObserveConfiguration(configurationGeneration);
        public void ObserveLifecycle(long nativeLifecycle, ConfigGeneration configurationGeneration)
        {
            _adapter.InvalidateLifecycle();
            _diagnostics?.ObserveLifecycle(
                nativeLifecycle,
                configurationGeneration,
                Safe(_dependencies.Owns),
                Safe(_dependencies.CanConsumeScrolls));
        }
        public void DisposeDiagnostics() => _diagnostics = null;
        public void DisposeRegistration()
        {
            _registration.Dispose();
            _adapter.InvalidateLifecycle();
        }
    }
}

/// <summary>
/// Keeps an accepted future game baseline from inheriting another build's UUID profile. The rest of
/// Automata can run, but Auto Scribe has no registration and therefore no path to mutation.
/// </summary>
internal sealed class AutoScribeUnavailableServiceCycleFeature : IAutomataServiceCycleFeature
{
    private readonly AutomataFeatureStatusReporter _featureStatus;

    internal AutoScribeUnavailableServiceCycleFeature(
        AutomataFeatureStatusReporter featureStatus) =>
        _featureStatus = featureStatus;

    public IAutomataServiceCycleFeatureRuntime Register(
        in AutomataServiceCycleFeatureContext context) =>
        new Runtime(
            _featureStatus,
            context.LifecycleValue,
            context.ConfigurationGeneration);

    private sealed class Runtime : IAutomataServiceCycleFeatureRuntime
    {
        private readonly AutomataFeatureStatusReporter _featureStatus;
        private readonly long _lifecycle;
        private readonly ConfigGeneration _configuration;

        internal Runtime(
            AutomataFeatureStatusReporter featureStatus,
            long lifecycle,
            ConfigGeneration configuration)
        {
            _featureStatus = featureStatus;
            _lifecycle = lifecycle;
            _configuration = configuration;
        }

        public void ActivateDiagnostics() =>
            _featureStatus.ObserveRuntimeLifecycle(
                FeatureStatusState.ContractUnavailable,
                FeatureStatusReasonCode.IdentityMismatch,
                "No audited Auto Scribe identity profile exists for this game baseline.",
                _lifecycle,
                _configuration);
        public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report) { }
        public void ObserveConfiguration(ConfigGeneration configurationGeneration) { }
        public void ObserveLifecycle(
            long nativeLifecycle,
            ConfigGeneration configurationGeneration) { }
        public void DisposeDiagnostics() { }
        public void DisposeRegistration() { }
    }
}
