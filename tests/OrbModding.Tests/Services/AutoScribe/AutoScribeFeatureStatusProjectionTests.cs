using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoScribeFeatureStatusProjectionTests
{
    [Fact]
    public void QueueFullActionHealthOutranksAnOlderEvidenceDecision()
    {
        var health = new AutoScribeActionHealth();
        var receipt = default(AutoScribeMutationReceipt);
        var submission = new AutoScribeSubmission(
            AutoScribePreflight.QueueFull,
            AutoScribeNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed,
            default,
            in receipt,
            "ActiveScribeInstances.HasEmptySpot() refused the craft.");
        health.Observe(in submission);

        var status = AutoScribeServiceCycleDiagnosticsBridge.ProjectObservedCycle(
            health,
            AutoScribeDecisionKind.EvidenceBlocked,
            blockedRole: 0,
            AutoScribeEvidenceReason.None,
            AutoScribeIdentityCatalog.Audited);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.TemporarySafetyBlock, status.Reason);
        Assert.Contains("HasEmptySpot", status.Summary);
    }

    [Fact]
    public void EvidenceBlockedWithoutAFailureReasonIsAnInvariantViolation()
    {
        var status = AutoScribeServiceCycleDiagnosticsBridge.ProjectObservedCycle(
            new AutoScribeActionHealth(),
            AutoScribeDecisionKind.EvidenceBlocked,
            blockedRole: 0,
            AutoScribeEvidenceReason.None,
            AutoScribeIdentityCatalog.Audited);

        Assert.Equal(FeatureStatusState.ContractUnavailable, status.State);
        Assert.Equal(FeatureStatusReasonCode.InvariantViolation, status.Reason);
        Assert.DoesNotContain("complete evidence", status.Summary);
    }
}
