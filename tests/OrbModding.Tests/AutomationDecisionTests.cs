using System;
using System.Collections.Generic;
using System.Linq;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomationDecisionTests
{
    [Fact]
    public void PublicCodesHaveFrozenUniqueNumericValues()
    {
        var expected = new (string Name, int Value)[]
        {
            ("None", 0),
            ("Eligible", 100), ("ConfigurationDisabled", 101), ("PolicyExcluded", 102),
            ("Locked", 103), ("Unavailable", 104), ("AlreadyActive", 105), ("DuplicateScheduled", 106),
            ("ContractUnresolved", 200), ("RegistryNotReady", 201), ("IdentityUnavailable", 202),
            ("IdentityChanged", 203), ("WrongNativeType", 204), ("NativeStateUnavailable", 205),
            ("NativeAdmissionRejected", 206), ("MutationQuarantined", 207),
            ("NativeMutationFailed", 208), ("PostconditionFailed", 209), ("ActionFamilyConflict", 210),
            ("CostUnavailable", 300), ("InvalidConfiguration", 301), ("InvalidResourceState", 302),
            ("InsufficientResource", 303), ("ReserveFloor", 304), ("AffordabilityThreshold", 305),
            ("DrainUnsafe", 306), ("ResourceStartThreshold", 307),
            ("QueueUnavailable", 400), ("QueueFull", 401), ("QueuePolicyLimit", 402),
            ("QueueBatchLimit", 403), ("TargetUnavailable", 410), ("TargetInvalid", 411),
            ("TargetingInProgress", 412),
            ("BudgetDeferred", 500), ("WaitingForTurn", 501), ("ScanLimitDeferred", 502),
            ("LifecycleChanged", 503), ("ManualPause", 504), ("NativeBusy", 505),
            ("SourceIneligible", 600), ("NoEligibleTargets", 601), ("ZeroEffect", 602),
            ("CapacityOverflow", 603),
        };
        var actual = Enum.GetValues<AutomationDecisionCode>()
            .Select(value => (value.ToString(), (int)value))
            .ToArray();

        Assert.Equal(expected, actual);
        Assert.Equal(actual.Length, actual.Select(value => value.Item2).Distinct().Count());
    }

    [Theory]
    [InlineData(12.5, 3, 1.25, 4)]
    [InlineData(0.125, 3, 1.25, 2)]
    [InlineData(-25.0, -2, -2.5, -1)]
    [InlineData(-0.0, 99, 0.0, 0)]
    public void ScientificValuesNormalize(double mantissa, long exponent, double expectedMantissa, long expectedExponent)
    {
        var value = new AutomationScientificValue(mantissa, exponent);
        Assert.Equal(expectedMantissa, value.Mantissa, 12);
        Assert.Equal(expectedExponent, value.Exponent);
    }

    [Fact]
    public void ScientificValueRejectsNonFiniteInputAndOrdersSignedValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomationScientificValue(double.NaN, 0));
        Assert.True(new AutomationScientificValue(-2, 5).CompareTo(new AutomationScientificValue(-1, 5)) < 0);
        Assert.True(default(AutomationScientificValue).CompareTo(new AutomationScientificValue(1, -5)) < 0);
        Assert.True(new AutomationScientificValue(1, 5).CompareTo(default) > 0);
        Assert.True(new AutomationScientificValue(-1, -5).CompareTo(default) < 0);
        Assert.Equal("1e5", new AutomationScientificValue(1, 5).ToString());
    }

    [Fact]
    public void NativeUuidIdentityRequiresExpectedType()
    {
        Assert.Throws<ArgumentException>(() => new AutomationEntityIdentity(
            "structures",
            "a39a2748-2bc4-4ad0-9872-2a29f5c88c90"));
        Assert.Throws<ArgumentException>(() => new AutomationEntityIdentity(
            "structures",
            "{a39a2748-2bc4-4ad0-9872-2a29f5c88c90}",
            "StructureSO"));
        Assert.Throws<ArgumentException>(() => new AutomationEntityIdentity("structures", "   "));
        Assert.Throws<ArgumentException>(() => new AutomationEntityIdentity("structures", "candidate", "   "));

        var identity = new AutomationEntityIdentity(
            "structures",
            "A39A2748-2BC4-4AD0-9872-2A29F5C88C90",
            "StructureSO",
            "Display only");
        Assert.Equal("StructureSO", identity.ExpectedNativeType);
        Assert.Equal("a39a2748-2bc4-4ad0-9872-2a29f5c88c90", identity.StableId);
    }

    [Fact]
    public void DecisionRejectsInvalidDispositionCodeCombinations()
    {
        Assert.Throws<ArgumentException>(() => Decision(
            AutomationDecisionCode.Eligible,
            AutomationDecisionDisposition.Rejected));
        Assert.Throws<ArgumentException>(() => Decision(
            AutomationDecisionCode.Locked,
            AutomationDecisionDisposition.Accepted));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomationDecision(
            "automata.auto-buy",
            "evaluate",
            AutomationDecisionDisposition.Rejected,
            AutomationDecisionCode.None));
    }

    [Fact]
    public void ConditionKeyIgnoresPresentationObservedValuesCountsAndLifecycle()
    {
        var first = Decision(
            AutomationDecisionCode.ReserveFloor,
            AutomationDecisionDisposition.Rejected,
            lifecycle: 2,
            displayName: "First name",
            observed: new AutomationScientificValue(5, 0),
            queue: Queue(200, 160, 180, 5, 1),
            detail: "first native wording",
            affectedCount: 1);
        var second = Decision(
            AutomationDecisionCode.ReserveFloor,
            AutomationDecisionDisposition.Rejected,
            lifecycle: 3,
            displayName: "Renamed",
            observed: new AutomationScientificValue(9, 0),
            queue: Queue(200, 100, 180, 5, 1),
            detail: "different wording",
            affectedCount: 50);

        Assert.Equal(first.ConditionKey, second.ConditionKey);
        Assert.Equal(first.ConditionKey.GetHashCode(), second.ConditionKey.GetHashCode());
        Assert.NotEqual(first.InstanceKey, second.InstanceKey);
    }

    [Fact]
    public void ResourceOrderDoesNotChangeConditionKey()
    {
        var mana = Constraint("mana", AutomationResourceConstraintKind.ReserveFloor, 10);
        var knowledge = Constraint("knowledge", AutomationResourceConstraintKind.ReserveFloor, 20);
        var first = DecisionWithConstraints(new[] { mana, knowledge });
        var second = DecisionWithConstraints(new[] { knowledge, mana });

        Assert.Equal(first.ConditionKey, second.ConditionKey);
        Assert.Equal(first.ConditionKey.GetHashCode(), second.ConditionKey.GetHashCode());
        Assert.Equal("knowledge", first.ResourceConstraints[0].Resource.StableId);
    }

    [Fact]
    public void ResourceConstraintsAreDefensivelyCopiedAndValidated()
    {
        var original = new[] { Constraint("mana", AutomationResourceConstraintKind.ReserveFloor, 10) };
        var decision = DecisionWithConstraints(original);
        original[0] = Constraint("knowledge", AutomationResourceConstraintKind.ReserveFloor, 20);

        Assert.Equal("mana", decision.ResourceConstraints[0].Resource.StableId);
        Assert.Throws<ArgumentException>(() => DecisionWithConstraints(new[] { default(AutomationResourceConstraint) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomationResourceConstraint(
            (AutomationResourceConstraintKind)999,
            new AutomationEntityIdentity("resources", "mana"),
            default,
            default,
            default));

        var duplicate = Constraint("mana", AutomationResourceConstraintKind.ReserveFloor, 10);
        Assert.Equal(duplicate, decision.ResourceConstraints.Single());
        Assert.Equal(duplicate.GetHashCode(), decision.ResourceConstraints.Single().GetHashCode());
        Assert.Single((System.Collections.IEnumerable)decision.ResourceConstraints);
    }

    [Fact]
    public void ConstraintCanonicalizationIncludesEveryStableField()
    {
        var constraints = new[]
        {
            new AutomationResourceConstraint(
                AutomationResourceConstraintKind.ReserveFloor,
                new AutomationEntityIdentity("resources", "same", "TypeB"),
                new AutomationScientificValue(2, 0), default, new AutomationScientificValue(3, 0), true),
            new AutomationResourceConstraint(
                AutomationResourceConstraintKind.ReserveFloor,
                new AutomationEntityIdentity("resources", "same", "TypeA"),
                new AutomationScientificValue(2, 0), default, new AutomationScientificValue(3, 0), true),
            Constraint("alpha", AutomationResourceConstraintKind.AffordabilityThreshold, 4),
            Constraint("zeta", AutomationResourceConstraintKind.ReserveFloor, 10),
        };

        var forward = DecisionWithConstraints(constraints);
        var reversed = DecisionWithConstraints(constraints.Reverse().ToArray());

        Assert.Equal(forward.ConditionKey, reversed.ConditionKey);
        Assert.Equal("alpha", forward.ResourceConstraints[0].Resource.StableId);
        Assert.Equal("TypeA", forward.ResourceConstraints[1].Resource.ExpectedNativeType);
        Assert.Equal("TypeB", forward.ResourceConstraints[2].Resource.ExpectedNativeType);
    }

    [Fact]
    public void StableConditionChangesProduceDifferentKeys()
    {
        var baseline = DecisionWithConstraints(new[] { Constraint("mana", AutomationResourceConstraintKind.ReserveFloor, 10) });
        var thresholdChanged = DecisionWithConstraints(new[] { Constraint("mana", AutomationResourceConstraintKind.ReserveFloor, 11) });
        var codeChanged = Decision(
            AutomationDecisionCode.AffordabilityThreshold,
            AutomationDecisionDisposition.Rejected);
        var queuePolicyChanged = Decision(
            AutomationDecisionCode.ReserveFloor,
            AutomationDecisionDisposition.Rejected,
            queue: Queue(200, 160, 179, 5, 1));

        Assert.NotEqual(baseline.ConditionKey, thresholdChanged.ConditionKey);
        Assert.NotEqual(baseline.ConditionKey, codeChanged.ConditionKey);
        Assert.NotEqual(
            Decision(
                AutomationDecisionCode.ReserveFloor,
                AutomationDecisionDisposition.Rejected,
                queue: Queue(200, 160, 180, 5, 1)).ConditionKey,
            queuePolicyChanged.ConditionKey);
    }

    [Fact]
    public void NativeStateUsesStableCodesAndQueueFactsMustBeValidated()
    {
        var baseline = Decision(AutomationDecisionCode.NativeBusy, AutomationDecisionDisposition.Deferred);
        var busy = Decision(
            AutomationDecisionCode.NativeBusy,
            AutomationDecisionDisposition.Deferred,
            native: new AutomationNativeDetail(stateCode: AutomationNativeStateCode.Busy));

        Assert.NotEqual(baseline.ConditionKey, busy.ConditionKey);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomationNativeDetail(
            stateCode: (AutomationNativeStateCode)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomationNativeDetail(
            registryStatus: (TypedRegistryResolutionStatus)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomationNativeDetail(
            mutationOutcome: (NativeMutationOutcome)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutomationQueueDetail.Invalid(QueueCapacityInvalidReason.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutomationQueueDetail.FromSnapshot(default, -1));

        var firstQueue = Queue(200, 160, 180, 5, 1);
        var sameQueue = Queue(200, 160, 180, 5, 1);
        Assert.Equal(firstQueue, sameQueue);
        Assert.Equal(firstQueue.GetHashCode(), sameQueue.GetHashCode());
        Assert.NotEqual(firstQueue, Queue(200, 159, 180, 5, 1));

        var invalid = AutomationQueueDetail.Invalid(QueueCapacityInvalidReason.NegativeNativeCapacity, 2);
        Assert.Equal(QueueCapacityInvalidReason.NegativeNativeCapacity, invalid.InvalidReason);
        Assert.Equal(2, invalid.RequestedCount);

        var firstNative = new AutomationNativeDetail("Contract", AutomationNativeStateCode.Busy);
        var sameNative = new AutomationNativeDetail("Contract", AutomationNativeStateCode.Busy);
        Assert.Equal(firstNative, sameNative);
        Assert.Equal(firstNative.GetHashCode(), sameNative.GetHashCode());
        Assert.False(firstNative.IsEmpty);
        Assert.True(default(AutomationNativeDetail).IsEmpty);
    }

    [Fact]
    public void PresenterCoversEveryStableDecisionCodeWithoutStringDrivenClassification()
    {
        foreach (var code in Enum.GetValues<AutomationDecisionCode>().Where(code => code != AutomationDecisionCode.None))
        {
            Assert.DoesNotContain("Unknown automation decision", AutomationDecisionPresenter.Label(code), StringComparison.Ordinal);
        }

        var decision = Decision(
            AutomationDecisionCode.ReserveFloor,
            AutomationDecisionDisposition.Rejected,
            observed: new AutomationScientificValue(5, 0),
            detail: "technical evidence");
        var presentation = AutomationDecisionPresenter.Format(decision);
        Assert.Contains("resources=Mana", presentation, StringComparison.Ordinal);
        Assert.Contains("technical evidence", presentation, StringComparison.Ordinal);
        var expanded = AutomationDecisionPresenter.FormatExpanded(decision);
        Assert.Contains("\nRequired: 2e1", expanded, StringComparison.Ordinal);
        Assert.Contains("\nAvailable: 5e0", expanded, StringComparison.Ordinal);
        Assert.Contains("\nCost: 1e1", expanded, StringComparison.Ordinal);
        Assert.Contains("\nReserved: 1e1", expanded, StringComparison.Ordinal);
        Assert.Contains("\nShortfall: 1.5e1", expanded, StringComparison.Ordinal);
        Assert.Equal("Unknown automation decision", AutomationDecisionPresenter.Label((AutomationDecisionCode)999));
    }

    [Fact]
    public void PresenterAndCommonOnlySinkConsumeTheSameDto()
    {
        var decision = Decision(
            AutomationDecisionCode.Locked,
            AutomationDecisionDisposition.Rejected,
            detail: "native prerequisite is unavailable");
        IAutomationDecisionSink sink = new CapturingSink();

        sink.Observe(in decision);

        var captured = Assert.IsType<CapturingSink>(sink).Captured;
        Assert.Equal(AutomationDecisionCode.Locked, captured.Code);
        Assert.Equal(
            AutomationDecisionPresenter.Format(decision),
            AutomationDecisionPresenter.Format(captured));
        Assert.StartsWith("Locked", AutomationDecisionPresenter.Format(decision), StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandedPresenterBoundsLongNamesDetailsAndResourceGroups()
    {
        var longName = new string('R', 200);
        var constraints = Enumerable.Range(0, 6)
            .Select(index => new AutomationResourceConstraint(
                AutomationResourceConstraintKind.ReserveFloor,
                new AutomationEntityIdentity("resources", $"resource-{index}", displayName: longName + index),
                new AutomationScientificValue(1, 150),
                new AutomationScientificValue(1, 149),
                new AutomationScientificValue(2, 150)))
            .ToArray();
        var decision = new AutomationDecision(
            "automata.auto-buy",
            "evaluate",
            AutomationDecisionDisposition.Rejected,
            AutomationDecisionCode.ReserveFloor,
            new AutomationEntityIdentity("structures", "candidate", displayName: longName),
            resourceConstraints: constraints,
            technicalDetail: new string('D', 500));

        var expanded = AutomationDecisionPresenter.FormatExpanded(decision, maximumResourceGroups: 2);
        var lines = AutomationDecisionPresenter.FormatExpandedLines(decision, maximumResourceGroups: 2);

        Assert.Contains("...", expanded, StringComparison.Ordinal);
        Assert.Contains("+4 more resource constraint(s)", expanded, StringComparison.Ordinal);
        Assert.Equal(2, expanded.Split("\nRequired:").Length - 1);
        Assert.True(expanded.Length < 1000);
        Assert.All(lines, line =>
        {
            Assert.DoesNotContain('\n', line);
            Assert.DoesNotContain('\r', line);
        });
    }

    [Fact]
    public void PublisherDeliversDecisionsAndIsolatesSubscriberFailures()
    {
        var throwing = new ThrowingSink();
        var capturing = new CapturingSink();
        using var throwingSubscription = AutomationDecisionPublisher.Subscribe(throwing);
        using var capturingSubscription = AutomationDecisionPublisher.Subscribe(capturing);
        var duplicateSubscription = AutomationDecisionPublisher.Subscribe(capturing);
        var decision = Decision(AutomationDecisionCode.Locked, AutomationDecisionDisposition.Rejected);

        AutomationDecisionPublisher.Publish(in decision);
        duplicateSubscription.Dispose();
        AutomationDecisionPublisher.Publish(in decision);

        Assert.Equal(AutomationDecisionCode.Locked, capturing.Captured.Code);
        Assert.Equal(3, capturing.ObservationCount);
        Assert.Throws<ArgumentException>(() => AutomationDecisionPublisher.Publish(default));
    }

    [Fact]
    public void PublicDtoSurfaceContainsOnlyCommonAndBclTypes()
    {
        var forbidden = typeof(AutomationDecision).Assembly.GetTypes()
            .Where(type => type == typeof(AutomationDecision) ||
                           type == typeof(AutomationEntityIdentity) ||
                           type == typeof(AutomationResourceConstraint) ||
                           type == typeof(AutomationResourceConstraintCollection) ||
                           type == typeof(AutomationQueueDetail) ||
                           type == typeof(AutomationNativeDetail))
            .SelectMany(type => type.GetProperties())
            // Keyed on the property type's namespace rather than its declaring assembly: after the
            // one-DLL merge every suite type answers to one assembly name, and an assembly filter
            // would keep passing only because it had stopped selecting anything. The namespace still
            // draws the line this test cares about, between feature-owned types and Common or BCL.
            .Where(property => (property.PropertyType.Namespace ?? string.Empty)
                .StartsWith("OrbAutomata", StringComparison.Ordinal))
            .Select(property => $"{property.DeclaringType?.Name}.{property.Name}")
            .ToArray();

        Assert.Empty(forbidden);
    }

    private static AutomationDecision Decision(
        AutomationDecisionCode code,
        AutomationDecisionDisposition disposition,
        long lifecycle = 0,
        string displayName = "Candidate",
        AutomationScientificValue observed = default,
        AutomationQueueDetail? queue = null,
        string detail = "",
        int affectedCount = 1,
        AutomationNativeDetail native = default)
    {
        IReadOnlyList<AutomationResourceConstraint>? constraints = code == AutomationDecisionCode.ReserveFloor
            ? new[]
            {
                new AutomationResourceConstraint(
                    AutomationResourceConstraintKind.ReserveFloor,
                    new AutomationEntityIdentity("resources", "mana", displayName: "Mana"),
                    new AutomationScientificValue(1, 1),
                    observed,
                    new AutomationScientificValue(2, 1)),
            }
            : null;
        return new AutomationDecision(
            "automata.auto-buy",
            "evaluate",
            disposition,
            code,
            new AutomationEntityIdentity("structures", "candidate", displayName: displayName),
            lifecycleGeneration: lifecycle,
            retryTriggers: AutomationRetryTrigger.ResourceQuantity,
            resourceConstraints: constraints,
            queue: queue,
            native: native,
            affectedCount: affectedCount,
            technicalDetail: detail);
    }

    private static AutomationDecision DecisionWithConstraints(IReadOnlyList<AutomationResourceConstraint> constraints) =>
        new(
            "automata.auto-buy",
            "evaluate",
            AutomationDecisionDisposition.Rejected,
            AutomationDecisionCode.ReserveFloor,
            new AutomationEntityIdentity("structures", "candidate"),
            retryTriggers: AutomationRetryTrigger.ResourceQuantity,
            resourceConstraints: constraints);

    private static AutomationResourceConstraint Constraint(
        string id,
        AutomationResourceConstraintKind kind,
        double required) =>
        new(
            kind,
            new AutomationEntityIdentity("resources", id),
            new AutomationScientificValue(1, 0),
            new AutomationScientificValue(1, 0),
            new AutomationScientificValue(required, 0));

    private static AutomationQueueDetail Queue(
        int nativeCapacity,
        int nativeRemainingRoom,
        int automationLimit,
        int manualReservation,
        int requestedCount)
    {
        Assert.True(QueueCapacitySnapshot.TryCreate(
            nativeCapacity,
            nativeRemainingRoom,
            automationLimit,
            manualReservation,
            out var snapshot,
            out var invalidReason), invalidReason.ToString());
        return AutomationQueueDetail.FromSnapshot(snapshot, requestedCount);
    }

    private sealed class CapturingSink : IAutomationDecisionSink
    {
        public AutomationDecision Captured { get; private set; }
        public int ObservationCount { get; private set; }
        public void Observe(in AutomationDecision decision)
        {
            Captured = decision;
            ObservationCount++;
        }
    }


    private sealed class ThrowingSink : IAutomationDecisionSink
    {
        public void Observe(in AutomationDecision decision) => throw new InvalidOperationException("simulated sink failure");
    }
}
