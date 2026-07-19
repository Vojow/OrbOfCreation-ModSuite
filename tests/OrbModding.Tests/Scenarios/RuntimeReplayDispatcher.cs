using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.RuntimeReplay;
using OrbModding.Tests.Simulation;
using ReplayDocument = OrbModding.RuntimeReplay.RuntimeReplay;

namespace OrbModding.Tests.Scenarios;

internal sealed class RuntimeReplayDispatcher : IDisposable
{
    private readonly Dictionary<string, object> _sceneIdentities = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Uuid, string NativeType), ReplayCandidate> _candidatesByIdentity;
    private readonly List<RuntimeReplayDispatchObservation> _trace = new();
    private bool _disposed;

    public RuntimeReplayDispatcher(ReplayDocument replay)
    {
        Replay = replay ?? throw new ArgumentNullException(nameof(replay));
        _candidatesByIdentity = replay.Setup.Candidates.ToDictionary(
            candidate => (candidate.Identity.Uuid, candidate.Identity.ExpectedNativeType));
        Kernel = new LifecycleScenarioKernel();
        AutoBuy = Kernel.AddFeature(new ScenarioAutoBuyFeature(
            Kernel,
            replay.Setup.Candidates.Select(candidate => ToCandidateSpec(candidate, replay.Setup.PrimaryResource.Identity)),
            replay.Setup.QueueCapacity,
            initialResourceQuantity: 0.0));
        AutoBuy.World.SetResourceQuantity(
            replay.Setup.PrimaryResource.Identity.Uuid,
            new BigAmount(decimal.ToDouble(replay.Setup.PrimaryResource.InitialQuantity), 0));
    }

    public ReplayDocument Replay { get; }

    public LifecycleScenarioKernel Kernel { get; }

    public ScenarioAutoBuyFeature AutoBuy { get; }

    public IReadOnlyList<RuntimeReplayDispatchObservation> Trace => _trace;

    public RuntimeReplayResult Run(int settleFrames = 4)
    {
        ThrowIfDisposed();
        if (settleFrames < 0 || settleFrames > 1000) throw new ArgumentOutOfRangeException(nameof(settleFrames));
        foreach (var replayEvent in Replay.Events)
        {
            AdvanceTo(replayEvent);
            Dispatch(replayEvent);
            _trace.Add(new RuntimeReplayDispatchObservation(
                replayEvent.Sequence,
                replayEvent.Kind,
                replayEvent.AtFrame,
                replayEvent.AtMicroseconds,
                Kernel.Frame,
                Kernel.Clock.GetTimestamp(),
                Kernel.Lifecycle.Current.Generation));
        }

        if (settleFrames > 0) Kernel.Step(settleFrames, secondsPerFrame: 1.0 / 60.0);
        return new RuntimeReplayResult(
            AutoBuy.World.TotalSubmitted,
            AutoBuy.World.TotalCompleted,
            AutoBuy.World.QueueCount,
            AutoBuy.World.QueueHighWater,
            AutoBuy.World.SubmissionOrder.ToArray(),
            Kernel.Mutations.ToArray(),
            Kernel.InvalidationTrace.Select(value => value.Kinds).ToArray(),
            _trace.ToArray());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Kernel.Dispose();
    }

    private void AdvanceTo(ReplayEvent replayEvent)
    {
        var targetFrame = replayEvent.AtFrame;
        if (Kernel.Frame > targetFrame)
            throw new InvalidOperationException($"Replay event {replayEvent.Sequence} targets frame {replayEvent.AtFrame}, behind kernel frame {Kernel.Frame}.");

        var steps = targetFrame - Kernel.Frame;
        var elapsedMicroseconds = Kernel.Clock.GetTimestamp();
        var deltaMicroseconds = replayEvent.AtMicroseconds - elapsedMicroseconds;
        if (deltaMicroseconds < 0)
            throw new InvalidOperationException($"Replay event {replayEvent.Sequence} targets time before the deterministic clock.");
        if (steps == 0)
        {
            if (deltaMicroseconds != 0)
                throw new InvalidOperationException($"Replay event {replayEvent.Sequence} advances time without advancing a frame.");
            return;
        }

        if (deltaMicroseconds % steps != 0)
            throw new InvalidOperationException($"Replay event {replayEvent.Sequence} microseconds are not exactly distributable across its frame gap.");
        Kernel.Step(checked((int)steps), deltaMicroseconds / (double)steps / 1_000_000.0);
        if (Kernel.Clock.GetTimestamp() != replayEvent.AtMicroseconds)
            throw new InvalidOperationException($"Replay event {replayEvent.Sequence} could not reproduce its integer-microsecond timestamp exactly.");
    }

    private void Dispatch(ReplayEvent replayEvent)
    {
        switch (replayEvent)
        {
            case LifecycleReplayEvent value:
                DispatchLifecycle(value);
                break;
            case ResourceReplayEvent value:
                RequirePrimaryResourceIdentity(value.Identity);
                AutoBuy.SetResourceQuantity(
                    value.Identity.Uuid,
                    new BigAmount(decimal.ToDouble(value.Quantity), 0));
                break;
            case QueueReplayEvent value:
                if (!AutoBuy.World.TryEnqueueManualActions(value.ManualActions, out var queueReason))
                    throw new InvalidOperationException($"Replay queue observation rejected before mutation: {queueReason}");
                Kernel.PublishInvalidation(GameplayInvalidationKind.Queue, source: "runtime replay queue observation");
                break;
            case ProgressionReplayEvent value:
                RequireCandidateIdentity(value.Identity);
                AutoBuy.SetAvailable(
                    value.Identity.Uuid,
                    value.Identity.ExpectedNativeType == "StructureSO"
                        ? AutoBuyCandidateKind.Structure
                        : AutoBuyCandidateKind.Upgrade,
                    value.Available);
                break;
            case InventoryReplayEvent value:
                PublishIdentity(GameplayInvalidationKind.Inventory, value.Identity, "runtime replay inventory observation");
                break;
            case ConfigurationReplayEvent value:
                AutoBuy.SetEnabled(value.Enabled);
                break;
            case CompletionReplayEvent value:
                RequireCandidateIdentity(value.Identity);
                var kind = value.Identity.ExpectedNativeType == "StructureSO"
                    ? AutoBuyCandidateKind.Structure
                    : AutoBuyCandidateKind.Upgrade;
                if (!AutoBuy.World.TryCompleteExact(value.Identity.Uuid, kind, value.Count, out var completionReason))
                    throw new InvalidOperationException($"Replay completion rejected before mutation: {completionReason}");
                AutoBuy.NotifyNativeCompletion();
                PublishIdentity(GameplayInvalidationKind.Queue | GameplayInvalidationKind.Progression, value.Identity, "runtime replay completion observation");
                break;
            default:
                throw new InvalidOperationException($"Unsupported replay event {replayEvent.GetType().Name}.");
        }
    }

    private void DispatchLifecycle(LifecycleReplayEvent value)
    {
        if (!Enum.TryParse<GameLifecycleTransitionKind>(value.Transition, ignoreCase: false, out var kind))
            throw new InvalidOperationException($"Unsupported lifecycle transition {value.Transition}.");
        if (!_sceneIdentities.TryGetValue(value.NativeIdentityToken, out var identity))
        {
            identity = new object();
            _sceneIdentities.Add(value.NativeIdentityToken, identity);
        }
        Kernel.ObserveAtCurrentFrame(kind, value.SceneName, identity, "runtime replay");
    }

    private void PublishIdentity(GameplayInvalidationKind kinds, ReplayIdentity identity, string source)
    {
        Kernel.PublishInvalidation(kinds, Domain(identity.ExpectedNativeType), identity.Uuid, identity.ExpectedNativeType, source);
    }

    private void RequireCandidateIdentity(ReplayIdentity identity)
    {
        if (!_candidatesByIdentity.ContainsKey((identity.Uuid, identity.ExpectedNativeType)))
            throw new InvalidOperationException($"Replay identity {identity.ExpectedNativeType}:{identity.Uuid} is not present in the reviewed setup.");
    }

    private void RequirePrimaryResourceIdentity(ReplayIdentity identity)
    {
        var expected = Replay.Setup.PrimaryResource.Identity;
        if (identity.Uuid != expected.Uuid || identity.ExpectedNativeType != expected.ExpectedNativeType)
            throw new InvalidOperationException(
                $"Replay resource {identity.ExpectedNativeType}:{identity.Uuid} does not match setup primary resource {expected.ExpectedNativeType}:{expected.Uuid}.");
    }

    private static SimulatedCandidateSpec ToCandidateSpec(ReplayCandidate candidate, ReplayIdentity primaryResource) =>
        new(
            candidate.Identity.Uuid,
            candidate.Identity.ExpectedNativeType == "StructureSO" ? AutoBuyCandidateKind.Structure : AutoBuyCandidateKind.Upgrade,
            decimal.ToDouble(candidate.BaseCost),
            decimal.ToDouble(candidate.CostScaling),
            candidate.Available,
            candidate.MaximumLevel,
            resourceCosts: new[]
            {
                new SimulatedResourceCost(
                    primaryResource.Uuid,
                    "Primary replay resource",
                    new BigAmount(decimal.ToDouble(candidate.BaseCost), 0)),
            });

    private static string Domain(string expectedNativeType) => expectedNativeType switch
    {
        "StructureSO" => GameplayInvalidationDomains.AutomataStructures,
        "UpgradeSO" => GameplayInvalidationDomains.AutomataUpgrades,
        "ResourceSO" => "resources",
        "ArtifactSO" => GameplayInvalidationDomains.MentorArtifacts,
        "SpellSO" => GameplayInvalidationDomains.MentorSpells,
        "AlchemyRecipeSO" => GameplayInvalidationDomains.MentorAlchemy,
        _ => throw new InvalidOperationException($"Unsupported replay native type {expectedNativeType}.")
    };

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RuntimeReplayDispatcher));
    }
}

internal sealed record RuntimeReplayResult(
    int TotalSubmitted,
    int TotalCompleted,
    int QueueCount,
    int QueueHighWater,
    IReadOnlyList<string> SubmissionOrder,
    IReadOnlyList<ScenarioMutationObservation> Mutations,
    IReadOnlyList<GameplayInvalidationKind> Invalidations,
    IReadOnlyList<RuntimeReplayDispatchObservation> DispatchTrace);

internal sealed record RuntimeReplayDispatchObservation(
    int Sequence,
    string Kind,
    long DeclaredFrame,
    long DeclaredMicroseconds,
    long ActualFrame,
    long ActualMicroseconds,
    long LifecycleGeneration);
