using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Simulation;

namespace OrbModding.Tests.Scenarios;

internal sealed class ScenarioAutoBuyFeature : ILifecycleScenarioFeature
{
    private readonly LifecycleScenarioKernel _kernel;
    private readonly AutoBuyEngine _engine;
    private readonly IDisposable _invalidationSubscription;
    private readonly Dictionary<string, SimulatedAutoBuyCandidate> _candidatesByUuid =
        new(StringComparer.Ordinal);
    private int _recordedSubmissions;
    private bool _disposed;

    public ScenarioAutoBuyFeature(
        LifecycleScenarioKernel kernel,
        IEnumerable<SimulatedCandidateSpec> candidateSpecs,
        int queueCapacity = 4,
        double initialResourceQuantity = 1_000.0)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        World = new SimulatedAutoBuyWorld(queueCapacity, initialResourceQuantity);
        foreach (var spec in candidateSpecs ?? throw new ArgumentNullException(nameof(candidateSpecs)))
        {
            if (!Guid.TryParseExact(spec.Uuid, "D", out _))
                throw new ArgumentException("Scenario candidate identities must be canonical UUIDs.", nameof(candidateSpecs));
            if (_candidatesByUuid.ContainsKey(spec.Uuid))
                throw new ArgumentException($"Scenario candidate UUID {spec.Uuid} is duplicated.", nameof(candidateSpecs));
            var candidate = World.AddCandidate(spec);
            _candidatesByUuid.Add(spec.Uuid, candidate);
        }
        Config = CreateConfig();
        Catalog = new SimulatedAutoBuyCatalog(World);
        _engine = new AutoBuyEngine(
            Config,
            Catalog,
            new ReservePolicy(Config),
            new ManualLogSource(),
            _ => 0.0,
            _ => 0.0,
            kernel.Coordinator,
            () => kernel.Frame);
        _invalidationSubscription = kernel.Invalidations.Subscribe(
            new GameplayInvalidationFilter(
                GameplayInvalidationKind.Progression |
                GameplayInvalidationKind.Registry |
                GameplayInvalidationKind.Queue),
            OnGameplayInvalidation,
            "Scenario Auto Buy production driver");
    }

    public string Name => "OrbAutomata.AutoBuy";

    public AutomataConfig Config { get; }

    public SimulatedAutoBuyWorld World { get; }

    public SimulatedAutoBuyCatalog Catalog { get; }

    public bool Enabled => Config.AutoBuyMode.Value == AutoBuyOperationMode.Active;

    public SimulatedAutoBuyCandidate Candidate(string uuid)
    {
        if (uuid is null) throw new ArgumentNullException(nameof(uuid));
        if (_candidatesByUuid.TryGetValue(uuid, out var candidate)) return candidate;
        throw new KeyNotFoundException($"Scenario candidate UUID {uuid} is not registered.");
    }

    public SimulatedAutoBuyCandidate Candidate(string uuid, AutoBuyCandidateKind expectedKind)
    {
        var candidate = Candidate(uuid);
        if (candidate.Kind != expectedKind)
            throw new InvalidOperationException(
                $"Scenario candidate {uuid} is {candidate.Kind}, not expected {expectedKind}.");
        return candidate;
    }

    public void SetAvailable(string uuid, bool available)
    {
        var candidate = Candidate(uuid);
        SetAvailable(candidate, available);
    }

    public void SetAvailable(string uuid, AutoBuyCandidateKind expectedKind, bool available)
    {
        var candidate = Candidate(uuid, expectedKind);
        SetAvailable(candidate, available);
    }

    private void SetAvailable(SimulatedAutoBuyCandidate candidate, bool available)
    {
        candidate.Available = available;
        _kernel.PublishInvalidation(
            GameplayInvalidationKind.Progression | GameplayInvalidationKind.Registry,
            Domain(candidate.Kind),
            candidate.Uuid,
            ExpectedNativeType(candidate.Kind),
            "scenario progression unlock");
    }

    public void SetEnabled(bool enabled)
    {
        Config.AutoBuyMode.Value = enabled
            ? AutoBuyOperationMode.Active
            : AutoBuyOperationMode.Disabled;
        if (!enabled)
            _engine.CancelPreparedWork();
        _kernel.PublishInvalidation(
            GameplayInvalidationKind.Configuration,
            source: enabled ? "scenario enable" : "scenario disable");
    }

    public void SetResourceQuantity(string resourceId, BigAmount quantity)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
            throw new ArgumentException("A resource UUID is required.", nameof(resourceId));
        var previous = World.GetResourceQuantity(resourceId);
        World.SetResourceQuantity(resourceId, quantity);
        Catalog.NotifyResourceChanged(resourceId, previous, quantity);
        _kernel.PublishInvalidation(
            GameplayInvalidationKind.ResourceQuantity,
            domain: "resources",
            entityId: resourceId,
            expectedTypeName: "ResourceSO",
            source: "scenario resource observation");
    }

    public int CompleteOne()
    {
        var completed = World.Complete(1);
        if (completed > 0)
        {
            _engine.NotifyNativeCompletion();
            _kernel.PublishInvalidation(
                GameplayInvalidationKind.Queue | GameplayInvalidationKind.Progression,
                source: "scenario native completion");
        }
        return completed;
    }

    public void NotifyNativeCompletion() => _engine.NotifyNativeCompletion();

    public void RecreateNativeWrappers()
    {
        World.ReplaceCandidateWrappers();
        RebuildCandidateIndex();
        _engine.InvalidateLifecycle();
    }

    public void OnLifecycleTransition(GameLifecycleTransition transition, object? sceneIdentity)
    {
        if (transition.Current.LastTransition is
            GameLifecycleTransitionKind.SceneExited or
            GameLifecycleTransitionKind.SaveLoadStarted or
            GameLifecycleTransitionKind.ResetStarted or
            GameLifecycleTransitionKind.NewGamePlusStarted)
        {
            World.ClearQueueForReload();
        }

        if (transition.Current.LastTransition is
            GameLifecycleTransitionKind.SceneEntered or
            GameLifecycleTransitionKind.SaveLoaded or
            GameLifecycleTransitionKind.ResetCompleted)
        {
            World.ReplaceCandidateWrappers();
            RebuildCandidateIndex();
        }

        _engine.InvalidateLifecycle();
    }

    public void Tick(long frame, TimeSpan delta)
    {
        if (!Enabled ||
            !_kernel.Lifecycle.Current.IsGameplayReady ||
            !string.Equals(_kernel.Lifecycle.Current.SceneName, "Main", StringComparison.Ordinal))
        {
            _engine.CancelPreparedWork();
            return;
        }

        _engine.Tick((float)delta.TotalSeconds);
        while (_recordedSubmissions < World.SubmissionObservations.Count)
        {
            var submission = World.SubmissionObservations[_recordedSubmissions];
            _recordedSubmissions++;
            _kernel.RecordMutation(
                Name,
                "StructureOrUpgradeQueueSubmission",
                submission.Uuid,
                $"{_kernel.Lifecycle.Current.Generation}:{submission.ExpectedNativeType}:{submission.Uuid}:{submission.IntendedLevel}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _invalidationSubscription.Dispose();
        _engine.Dispose();
    }

    private void OnGameplayInvalidation(GameplayInvalidation invalidation)
    {
        if ((invalidation.Kinds & (GameplayInvalidationKind.Progression | GameplayInvalidationKind.Registry)) != 0)
            Catalog.Index.InvalidateLifecycleIncrementally();
    }

    private void RebuildCandidateIndex()
    {
        _candidatesByUuid.Clear();
        foreach (var candidate in World.Candidates)
        {
            if (!_candidatesByUuid.TryAdd(candidate.Uuid, candidate))
                throw new InvalidOperationException($"Scenario candidate UUID {candidate.Uuid} is duplicated after lifecycle recreation.");
        }
    }

    private static string Domain(AutoBuyCandidateKind kind) =>
        kind == AutoBuyCandidateKind.Structure
            ? GameplayInvalidationDomains.AutomataStructures
            : GameplayInvalidationDomains.AutomataUpgrades;

    private static string ExpectedNativeType(AutoBuyCandidateKind kind) =>
        kind == AutoBuyCandidateKind.Structure ? "StructureSO" : "UpgradeSO";

    private static AutomataConfig CreateConfig()
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.UpgradeAffordability.Value = AutoBuyAffordabilityMode.BuyAll;
        config.AutoBuyStructures.Value = true;
        config.AutoBuyUpgrades.Value = true;
        config.AutoBuyBatchSizing.Value = AutoBuyBatchSizingMode.FillAvailableQueue;
        config.AutoBuyMaxCandidatesPerScan.Value = 1024;
        config.LeaveQueueSlots.Value = 1;
        config.RepeatWhileAffordable.Value = true;
        config.RespectActionMultiplier.Value = false;
        config.CpuBudgetMilliseconds.Value = 1.0f;
        config.AllowedAutoBuyUuids.Value = string.Empty;
        config.BlockedAutoBuyUuids.Value = string.Empty;
        config.EnableOperationalLogging.Value = false;
        config.AutoLevelSpells.Value = false;
        return config;
    }
}
