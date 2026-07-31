using System;
using System.Reflection;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Execution.Validation;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

public sealed class AutoItemsServiceCycleTypeSafetyTests
{
    [Fact]
    public void WorkerStateAndActionUseOnlyAuditedServiceCycleStorage()
    {
        var stateViolation = ServiceCycleTypeGraphWalker.Validate(
            typeof(AutoItemsCycleState),
            ServiceCycleTypeRole.State,
            "state");
        var actionViolation = ServiceCycleTypeGraphWalker.Validate(
            typeof(AutoItemsCycleAction),
            ServiceCycleTypeRole.Action,
            "action");

        Assert.False(stateViolation.HasValue, stateViolation?.Message);
        Assert.False(actionViolation.HasValue, actionViolation?.Message);
    }

    [Fact]
    public void ProductionWorkerPassesTheCompleteSeparationAudit()
    {
        ServiceCycleWorkerDefinitionValidator.EnsureSeparated(
            new SafetyMain(),
            new AutoItemsWorkerDefinition());
    }

    [Fact]
    public void ActionCarriesIdentityAndFactsButNoConfigurationKey()
    {
        var fields = typeof(AutoItemsCycleAction).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.DoesNotContain(fields, field => field.FieldType == typeof(string));
        Assert.DoesNotContain(
            fields,
            field => field.Name.Contains("Allowlist", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class SafetyMain :
        IServiceCycleMainThreadDefinition<AutoItemsCycleAction>
    {
        public ServiceId ServiceId => new("test.auto-items");
        public WakePolicy DefaultWakePolicy => WakePolicy.OnPublication;
        public ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
            new(new MonotonicDuration(1), new MonotonicDuration(2));

        public ServiceStartDecision ShouldStart(
            in SuiteRuntimeConfiguration config,
            in ServiceCycleStartContext context) =>
            ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);

        public ServiceActionResult TryExecute(
            in AutoItemsCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context) =>
            ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);
    }
}
