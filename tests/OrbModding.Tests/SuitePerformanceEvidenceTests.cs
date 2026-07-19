using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using OrbModding.Common;
using OrbModding.PerformanceEvidence;
using Xunit;

namespace OrbModding.Tests;

public sealed class SuitePerformanceEvidenceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void FixedClockCapture_ExportsAllRegistrationsAndEvaluatesObservationalProfile()
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 30);

        Assert.EndsWith("\n", captured.Json, StringComparison.Ordinal);
        Assert.Equal(captured.Json, PerformanceEvidencePipeline.CanonicalizeEvidence(captured.Json));
        Assert.Equal(12, captured.Evidence.Start.Work.Count);
        Assert.Equal(12, captured.Evidence.End.Work.Count);
        Assert.Equal(SuitePerformanceEvidence.ProfileSha256, captured.Profile.SourceSha256);
        Assert.DoesNotContain(captured.Json, "registrationId", StringComparison.Ordinal);
        Assert.DoesNotContain(captured.Json, "user", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(captured.Json, "host", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(captured.Json, "save", StringComparison.OrdinalIgnoreCase);

        var evaluation = PerformanceEvidencePipeline.Evaluate(captured.Profile, captured.Evidence);
        Assert.DoesNotContain(evaluation.Results, result => result.Classification == "exceeded");
        Assert.Contains(evaluation.Results, result => result.Classification == "within-target");
        Assert.Contains(evaluation.Results, result => result.Classification == "observe-only");

        var output = Environment.GetEnvironmentVariable("OOC_SUITE_PERFORMANCE_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(output))
        {
            var directory = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(output, captured.Json, new UTF8Encoding(false));
        }
    }

    [Fact]
    public void Canonicalizer_SortsPermutationsAndRoundTrips()
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 1);
        captured.Evidence.Start.Work.Reverse();
        captured.Evidence.End.Work.Reverse();
        var permuted = JsonSerializer.Serialize(captured.Evidence, JsonOptions);

        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.ParseEvidence(permuted));
        var canonical = PerformanceEvidencePipeline.CanonicalizeEvidence(permuted);
        Assert.Equal(canonical, PerformanceEvidencePipeline.CanonicalizeEvidence(canonical));
        _ = PerformanceEvidencePipeline.ParseEvidence(canonical);
    }

    [Theory]
    [InlineData("{\"unknown\":0,", "unknown property")]
    [InlineData("{\"schemaId\":\"duplicate\",", "duplicate property")]
    public void Parser_RejectsUnknownDuplicateAndCaseCollisionProperties(string prefix, string expected)
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 1);
        var malformed = prefix + captured.Json[1..];
        var error = Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.ParseEvidence(malformed));
        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parser_RejectsCaseCollisionProperties()
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 1);
        var malformed = captured.Json.Replace(
            "\"schemaId\":\"orb-modsuite-suite-performance-evidence\",",
            "\"schemaId\":\"orb-modsuite-suite-performance-evidence\",\"SchemaId\":\"collision\",",
            StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.ParseEvidence(malformed));
        Assert.Contains("case-collision", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/*comment*/")]
    [InlineData("\uFEFF")]
    public void Parser_RejectsCommentsAndBom(string prefix)
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 1);
        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.ParseEvidence(prefix + captured.Json));
    }

    [Fact]
    public void Parser_RejectsTrailingDataDepthAndSizeBounds()
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 1);
        var withoutFinalLf = captured.Json.TrimEnd();
        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.ParseEvidence(withoutFinalLf[..^1] + ",}"));
        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.ParseEvidence(
            new string('[', 17) + "0" + new string(']', 17)));
        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.ParseEvidence(
            new string(' ', PerformanceEvidencePipeline.MaximumBytes + 1)));
        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.ParseEvidence(
            captured.Json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal)));
    }

    [Fact]
    public void Parser_RejectsUnknownEnumsNegativeAndContradictoryCounts()
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 1);
        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.ParseEvidence(
            captured.Json.Replace("\"synthetic\"", "\"unknown-platform\"", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.ParseEvidence(
            captured.Json.Replace("\"durationFrames\":13", "\"durationFrames\":-1", StringComparison.Ordinal)));

        var work = captured.Evidence.End.Work[0];
        var invalid = work with { NativeMutationAttempts = work.NativeCallsAttempted + 1 };
        var document = ReplaceEndWork(captured.Evidence, 0, invalid);
        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.ParseEvidence(
            JsonSerializer.Serialize(document, JsonOptions)));
    }

    [Fact]
    public void Evaluator_AttributesSameSubsystemMetricsByCompositeIdentity()
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 30);
        var index = captured.Evidence.End.Work.FindIndex(item => item.WorkName == "Evaluate candidates");
        var work = captured.Evidence.End.Work[index];
        var timing = work.WorkItemTiming with { P95Milliseconds = 0.6, P99Milliseconds = 0.8, MaximumMilliseconds = 1.1 };
        var changed = ReplaceEndWork(captured.Evidence, index, work with { WorkItemTiming = timing });

        var evaluation = PerformanceEvidencePipeline.Evaluate(captured.Profile, changed);
        var exceeded = Assert.Single(evaluation.Results, result =>
            result.Metric == "workItemTiming.p95Milliseconds" && result.Classification == "exceeded");
        Assert.Contains("OrbAutomata.AutoBuy", exceeded.WorkIdentity, StringComparison.Ordinal);
        Assert.Contains("Evaluate candidates", exceeded.WorkIdentity, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluator_NativeTimingRemainsObserveOnlyEvenWhenSlow()
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 30);
        var index = captured.Evidence.End.Work.FindIndex(item => item.WorkName == "Submit one purchase");
        var work = captured.Evidence.End.Work[index];
        var slow = work.WorkItemTiming with { AverageMilliseconds = 2, P95Milliseconds = 3, P99Milliseconds = 4, MaximumMilliseconds = 5 };
        var changed = ReplaceEndWork(captured.Evidence, index, work with { WorkItemTiming = slow });

        var evaluation = PerformanceEvidencePipeline.Evaluate(captured.Profile, changed);
        Assert.Contains(evaluation.Results, result =>
            result.WorkIdentity.Contains("Submit one purchase", StringComparison.Ordinal) &&
            result.Metric == "workItemTiming.p99Milliseconds" &&
            result.Classification == "observe-only");
    }

    [Fact]
    public void Evaluator_GatesStackedCoordinatorActiveFrameTiming()
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 30);
        var timing = captured.Evidence.End.Coordinator.FrameTiming with
        {
            AverageMilliseconds = 0.7,
            P95Milliseconds = 0.8,
            P99Milliseconds = 1.1,
            MaximumMilliseconds = 1.2,
        };
        var changed = captured.Evidence with
        {
            End = captured.Evidence.End with
            {
                Coordinator = captured.Evidence.End.Coordinator with { FrameTiming = timing },
            },
        };

        var result = PerformanceEvidencePipeline.Evaluate(captured.Profile, changed);
        Assert.Contains(result.Results, item =>
            item.WorkIdentity == "coordinator" &&
            item.Metric == "activeFrameTiming.p95Milliseconds" &&
            item.Classification == "exceeded");
        Assert.Contains(result.Results, item =>
            item.WorkIdentity == "coordinator" &&
            item.Metric == "activeFrameTiming.p99Milliseconds" &&
            item.Classification == "exceeded");
    }

    [Fact]
    public void Evaluator_CoordinatorTimingRejectsContaminationAndCapacityDrift()
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 30);
        var startTiming = captured.Evidence.Start.Coordinator.FrameTiming with
        {
            SampleCount = 300,
            TotalSamples = 359,
            Operations = 300,
            TotalOperations = 359,
            AverageMilliseconds = 0.1,
            P95Milliseconds = 0.1,
            P99Milliseconds = 0.1,
            MaximumMilliseconds = 0.1,
        };
        var contaminated = captured.Evidence with
        {
            Start = captured.Evidence.Start with
            {
                Coordinator = captured.Evidence.Start.Coordinator with { FrameTiming = startTiming },
            },
        };
        var result = PerformanceEvidencePipeline.Evaluate(captured.Profile, contaminated);
        Assert.Contains(result.Results, item =>
            item.WorkIdentity == "coordinator" &&
            item.Metric == "activeFrameTiming" &&
            item.Classification == "insufficient-window");

        var endTiming = captured.Evidence.End.Coordinator.FrameTiming;
        var changedCapacity = captured.Evidence with
        {
            End = captured.Evidence.End with
            {
                Coordinator = captured.Evidence.End.Coordinator with
                {
                    FrameTiming = endTiming with { Capacity = 301, SampleCount = 301 },
                },
            },
        };
        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.Evaluate(
            captured.Profile,
            changedCapacity));
    }

    [Fact]
    public void Evaluator_NativeTimingRejectsContaminatedWindowBeforeObserveOnlyFacts()
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 30);
        var index = captured.Evidence.Start.Work.FindIndex(item => item.WorkName == "Submit one purchase");
        var startWork = captured.Evidence.Start.Work[index];
        var contaminated = startWork with
        {
            WorkItemTiming = startWork.WorkItemTiming with { SampleCount = 1, TotalSamples = 1 },
            FrameTiming = startWork.FrameTiming with { SampleCount = 1, TotalSamples = 1 },
        };
        var changed = ReplaceStartWork(captured.Evidence, index, contaminated);

        var result = PerformanceEvidencePipeline.Evaluate(captured.Profile, changed);
        Assert.Contains(result.Results, item =>
            item.WorkIdentity.Contains("Submit one purchase", StringComparison.Ordinal) &&
            item.Metric == "workItemTiming" &&
            item.Classification == "insufficient-window");
        Assert.DoesNotContain(result.Results, item =>
            item.WorkIdentity.Contains("Submit one purchase", StringComparison.Ordinal) &&
            item.Metric.StartsWith("workItemTiming.", StringComparison.Ordinal) &&
            item.Classification == "observe-only");
    }

    [Fact]
    public void Evaluator_DistinguishesInsufficientSamplesAndContaminatedWindow()
    {
        var sparse = CaptureSupportedSuite(samplesPerWork: 5);
        var sparseResult = PerformanceEvidencePipeline.Evaluate(sparse.Profile, sparse.Evidence);
        Assert.Contains(sparseResult.Results, result => result.Classification == "insufficient-samples");

        var full = CaptureSupportedSuite(samplesPerWork: 30);
        var startWork = full.Evidence.Start.Work[0];
        var endWork = full.Evidence.End.Work[0];
        var contaminatedStart = startWork with
        {
            WorkItemTiming = startWork.WorkItemTiming with { SampleCount = 1, TotalSamples = 1 },
            FrameTiming = startWork.FrameTiming with { SampleCount = 1, TotalSamples = 1 },
        };
        var contaminatedEnd = endWork with
        {
            WorkItemTiming = endWork.WorkItemTiming with { TotalSamples = 30 },
            FrameTiming = endWork.FrameTiming with { TotalSamples = 30 },
        };
        var changed = ReplaceWork(full.Evidence, 0, contaminatedStart, contaminatedEnd);
        var result = PerformanceEvidencePipeline.Evaluate(full.Profile, changed);
        Assert.Contains(result.Results, item => item.Classification == "insufficient-window");
    }

    [Fact]
    public void Evaluator_RejectsMissingUnknownWorkPolicyAndProfileMismatch()
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 1);
        var missingStart = captured.Evidence.Start with { Work = captured.Evidence.Start.Work.Skip(1).ToList() };
        var missing = captured.Evidence with { Start = missingStart };
        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.Evaluate(captured.Profile, missing));

        var unknown = captured.Evidence.End.Work[0] with { WorkName = "Unknown work" };
        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.Evaluate(
            captured.Profile,
            ReplaceEndWork(captured.Evidence, 0, unknown)));
        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.Evaluate(
            captured.Profile,
            captured.Evidence with { Policy = captured.Evidence.Policy with { SoftBudgetMilliseconds = 0.5 } }));
        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.Evaluate(
            captured.Profile,
            captured.Evidence with { ProfileSha256 = new string('0', 64) }));
    }

    [Fact]
    public void Evaluator_DoesNotAttributePreexistingLifetimeMaximumToCapture()
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 30);
        var start = captured.Evidence.Start.Work[0] with { MaximumPendingWaitFrames = 20 };
        var end = captured.Evidence.End.Work[0] with { MaximumPendingWaitFrames = 20 };
        var changed = ReplaceWork(captured.Evidence, 0, start, end);
        var result = PerformanceEvidencePipeline.Evaluate(captured.Profile, changed);

        Assert.Contains(result.Results, item =>
            item.Metric == "maximumPendingWaitFrames" && item.Classification == "insufficient-window");
    }

    [Fact]
    public void Evaluator_RejectsBackwardLifetimeMaximumDeferralsAndTimingCapacity()
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 30);
        var start = captured.Evidence.Start.Work[0];
        var end = captured.Evidence.End.Work[0];

        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.Evaluate(
            captured.Profile,
            ReplaceWork(
                captured.Evidence,
                0,
                start with { MaximumPendingWaitFrames = 20 },
                end with { MaximumPendingWaitFrames = 10 })));

        var endDeferrals = end.DeferralsByReason with { WaitingForTurn = 1 };
        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.Evaluate(
            captured.Profile,
            ReplaceWork(
                captured.Evidence,
                0,
                start with { DeferralsByReason = start.DeferralsByReason with { WaitingForTurn = 2 }, DeferredAttempts = 2 },
                end with { DeferralsByReason = endDeferrals, DeferredAttempts = Math.Max(2, end.DeferredAttempts) })));

        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.Evaluate(
            captured.Profile,
            ReplaceEndWork(
                captured.Evidence,
                0,
                end with { WorkItemTiming = end.WorkItemTiming with { Capacity = end.WorkItemTiming.Capacity + 1 } })));
    }

    [Fact]
    public void Parser_RequiresExactRollingWindowSampleCount()
    {
        var captured = CaptureSupportedSuite(samplesPerWork: 30);
        var work = captured.Evidence.End.Work[0];
        var invalid = work with
        {
            WorkItemTiming = work.WorkItemTiming with { SampleCount = work.WorkItemTiming.SampleCount - 1 },
        };
        var json = JsonSerializer.Serialize(ReplaceEndWork(captured.Evidence, 0, invalid), JsonOptions);
        Assert.Throws<InvalidDataException>(() => PerformanceEvidencePipeline.ParseEvidence(json));
    }

    [Fact]
    public void CheckedProfileMatchesProductionRegistrationIdentityCatalog()
    {
        var profile = PerformanceEvidencePipeline.ReadProfile(
            Path.Combine(AppContext.BaseDirectory, "data", "suite-performance-profile-v1.json"));
        Assert.Equal(SuitePerformanceWorkIdentities.SupportedSuiteV1Count, profile.Rules.Count);
        for (var index = 0; index < profile.Rules.Count; index++)
        {
            var expected = SuitePerformanceWorkIdentities.GetSupportedSuiteV1(index);
            var rule = profile.Rules[index];
            Assert.Equal(expected.Subsystem, rule.Subsystem);
            Assert.Equal(expected.WorkName, rule.WorkName);
            Assert.Equal(BudgetName(expected.BudgetClass), rule.BudgetClass);
            Assert.Equal(ExecutionName(expected.ExecutionKind), rule.ExecutionKind);
        }
    }

    [Fact]
    public void Capture_RejectsPolicyChangesAndMoreThan64Registrations()
    {
        var clock = new ManualClock();
        var coordinator = new SuitePerformanceCoordinator(clock);
        using var work = coordinator.Register("test", "work");
        var start = SuitePerformanceEvidence.StartCapture(coordinator);
        coordinator.SetBudgets(0.5, 1);
        var metadata = Metadata(1);
        Assert.Throws<InvalidOperationException>(() => SuitePerformanceEvidence.Capture(coordinator, metadata, start));

        var foreign = new SuitePerformanceCoordinator(clock);
        Assert.Throws<InvalidOperationException>(() => SuitePerformanceEvidence.Capture(
            foreign,
            metadata,
            SuitePerformanceEvidence.StartCapture(coordinator)));

        var oversized = new SuitePerformanceCoordinator(clock);
        var registrations = Enumerable.Range(0, 65).Select(index => oversized.Register("test", index.ToString("D2"))).ToList();
        try
        {
            Assert.Throws<InvalidOperationException>(() => SuitePerformanceEvidence.StartCapture(oversized));
        }
        finally
        {
            foreach (var registration in registrations) registration.Dispose();
        }
    }

    [Fact]
    public void Capture_RejectsRegistrationChurnAndUnboundedIdentityText()
    {
        var clock = new ManualClock();
        var coordinator = new SuitePerformanceCoordinator(clock);
        var registration = coordinator.Register("test", "stable");
        var start = SuitePerformanceEvidence.StartCapture(coordinator);
        registration.Dispose();
        using var replacement = coordinator.Register("test", "stable");
        Assert.Throws<InvalidOperationException>(() => SuitePerformanceEvidence.Capture(
            coordinator,
            Metadata(1),
            start));

        var invalid = new SuitePerformanceCoordinator(clock);
        using var oversized = invalid.Register(new string('x', 129), "work");
        Assert.Throws<InvalidOperationException>(() => SuitePerformanceEvidence.StartCapture(invalid));
    }

    [Fact]
    public void MarkdownWriterEscapesMetadataAndTableContent()
    {
        const string payload = "`tick` <img src=x onerror=1> [link](https://evil) ![image](https://evil) | \\ \r\nnext";
        var evaluation = new PerformanceEvaluation(
            "profile " + payload,
            1,
            new string('0', 64),
            "capture " + payload,
            "commit " + payload,
            "scenario " + payload,
            new List<MetricEvaluation>
            {
                new("work " + payload, "metric " + payload, "classification " + payload, 1, 2, null),
            });

        var markdown = PerformanceEvidencePipeline.WriteEvaluationMarkdown(evaluation);
        Assert.DoesNotContain('`', markdown);
        Assert.DoesNotContain("<img", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[link](", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("![image](", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://evil", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\\', markdown);
        Assert.Contains("&#x60;tick&#x60;", markdown, StringComparison.Ordinal);
        Assert.Contains("&#x3C;img", markdown, StringComparison.Ordinal);
        Assert.Contains("&#x5B;link&#x5D;&#x28;https&#x3A;&#x2F;&#x2F;evil&#x29;", markdown, StringComparison.Ordinal);
        Assert.Contains("&#x21;&#x5B;image&#x5D;", markdown, StringComparison.Ordinal);
        Assert.Contains("&#x7C;", markdown, StringComparison.Ordinal);
        Assert.Contains("&#x5C;", markdown, StringComparison.Ordinal);
        Assert.Contains("next", markdown, StringComparison.Ordinal);
    }

    private static CapturedEvidence CaptureSupportedSuite(int samplesPerWork)
    {
        var profilePath = Path.Combine(AppContext.BaseDirectory, "data", "suite-performance-profile-v1.json");
        var profile = PerformanceEvidencePipeline.ReadProfile(profilePath);
        var clock = new ManualClock();
        var coordinator = new SuitePerformanceCoordinator(clock);
        var registrations = Enumerable.Range(0, SuitePerformanceWorkIdentities.SupportedSuiteV1Count)
            .Select(index => SuitePerformanceWorkIdentities.GetSupportedSuiteV1(index))
            .Select(identity => coordinator.Register(
                identity.Subsystem,
                identity.WorkName,
                identity.BudgetClass,
                identity.ExecutionKind))
            .ToList();
        try
        {
            var start = SuitePerformanceEvidence.StartCapture(coordinator);
            long frame = 0;
            for (var sample = 0; sample < samplesPerWork; sample++)
            {
                for (var index = 0; index < registrations.Count; index++)
                {
                    var registration = registrations[index];
                    registration.SetPending(true);
                    var admission = coordinator.RequestWork(registration, ++frame, out var lease);
                    Assert.Equal(SuiteWorkAdmission.Granted, admission);
                    clock.AdvanceMicroseconds(100);
                    if (registration.ExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNativeMutation)
                    {
                        lease.Complete(SuiteWorkCompletion.NativeMutation(1, 1));
                    }
                    else if (registration.ExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNative)
                    {
                        lease.Complete(new SuiteWorkCompletion(1, nativeCallsAttempted: 1));
                    }
                    else
                    {
                        lease.Complete(1);
                    }

                    registration.SetPending(false);
                }
            }

            coordinator.BeginFrame(++frame);
            var evidence = SuitePerformanceEvidence.Capture(coordinator, Metadata(frame), start);
            var json = evidence.ToCanonicalJson();
            return new CapturedEvidence(profile, PerformanceEvidencePipeline.ParseEvidence(json), json);
        }
        finally
        {
            foreach (var registration in registrations) registration.Dispose();
        }
    }

    private static SuitePerformanceCaptureMetadata Metadata(long frames) => new(
        SuitePerformanceCaptureKind.Synthetic,
        "0123456789abcdef",
        "0.3.3-beta",
        "fixture",
        "fixed-clock-all-registration",
        frames,
        new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero));

    private static PerformanceEvidenceDocument ReplaceEndWork(PerformanceEvidenceDocument document, int index, EvidenceWork replacement)
    {
        var end = document.End.Work.ToList();
        end[index] = replacement;
        return document with { End = document.End with { Work = end } };
    }

    private static PerformanceEvidenceDocument ReplaceStartWork(PerformanceEvidenceDocument document, int index, EvidenceWork replacement)
    {
        var start = document.Start.Work.ToList();
        start[index] = replacement;
        return document with { Start = document.Start with { Work = start } };
    }

    private static string BudgetName(SuiteBudgetClass value) => value == SuiteBudgetClass.SoftLimited
        ? "soft-limited"
        : "hard-limited";

    private static string ExecutionName(SuiteWorkExecutionKind value) => value switch
    {
        SuiteWorkExecutionKind.Cooperative => "cooperative",
        SuiteWorkExecutionKind.NonPreemptibleNative => "non-preemptible-native",
        SuiteWorkExecutionKind.NonPreemptibleNativeMutation => "non-preemptible-native-mutation",
        _ => throw new InvalidOperationException(),
    };

    private static PerformanceEvidenceDocument ReplaceWork(PerformanceEvidenceDocument document, int index, EvidenceWork startReplacement, EvidenceWork endReplacement)
    {
        var start = document.Start.Work.ToList();
        var end = document.End.Work.ToList();
        start[index] = startReplacement;
        end[index] = endReplacement;
        return document with
        {
            Start = document.Start with { Work = start },
            End = document.End with { Work = end },
        };
    }

    private sealed class ManualClock : IPerformanceClock
    {
        private long _microseconds;
        public long GetTimestamp() => _microseconds;
        public double GetElapsedMilliseconds(long startTimestamp, long endTimestamp) => (endTimestamp - startTimestamp) / 1000d;
        public void AdvanceMicroseconds(long microseconds) => _microseconds += microseconds;
    }

    private sealed record CapturedEvidence(PerformanceProfile Profile, PerformanceEvidenceDocument Evidence, string Json);
}
