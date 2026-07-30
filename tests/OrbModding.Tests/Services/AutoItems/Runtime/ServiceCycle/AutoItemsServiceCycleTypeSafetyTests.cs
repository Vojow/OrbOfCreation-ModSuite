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
    public void WorkerStateUsesOnlyAuditedServiceCycleStorage()
    {
        var violation = ServiceCycleTypeGraphWalker.Validate(
            typeof(AutoItemsCycleState),
            ServiceCycleTypeRole.State,
            "state");

        Assert.False(violation.HasValue, violation?.Message);
    }

    [Fact]
    public void AutoItemsProductionWorkerPassesTheCompleteSeparationAudit()
    {
        Assert.True(new AutoScribeIdentityCatalog().TryGetProfile(
            GameAssemblyAudit.WindowsV1052BaselineId,
            out var profile));

        ServiceCycleWorkerDefinitionValidator.EnsureSeparated(
            new SafetyMain<AutoItemsCycleAction>("test.auto-items"),
            new AutoItemsWorkerDefinition(profile));
    }

    [Fact]
    public void AutoScribeProductionWorkerPassesTheCompleteSeparationAudit()
    {
        Assert.True(new AutoScribeIdentityCatalog().TryGetProfile(
            GameAssemblyAudit.WindowsV1052BaselineId,
            out var profile));

        ServiceCycleWorkerDefinitionValidator.EnsureSeparated(
            new SafetyMain<AutoScribeCycleAction>("test.auto-scribe"),
            new AutoScribeWorker(profile));
    }

    private sealed class SafetyMain<TAction> : IServiceCycleMainThreadDefinition<TAction>
    {
        internal SafetyMain(string serviceId) => ServiceId = new ServiceId(serviceId);

        public ServiceId ServiceId { get; }
        public WakePolicy DefaultWakePolicy => WakePolicy.Immediate;
        public ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
            new(new MonotonicDuration(1), new MonotonicDuration(2));

        public ServiceStartDecision ShouldStart(
            in SuiteRuntimeConfiguration config,
            in ServiceCycleStartContext context) =>
            ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);

        public ServiceActionResult TryExecute(
            in TAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context) =>
            ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);
    }
}
