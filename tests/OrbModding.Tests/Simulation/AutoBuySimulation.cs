using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;

namespace OrbModding.Tests.Simulation;

internal sealed class AutoBuySimulation : IDisposable
{
    private readonly SimulationPerformanceClock _performanceClock = new SimulationPerformanceClock();
    private readonly DeterministicStopwatchCost _readCost;
    private readonly DeterministicStopwatchCost _purchaseCost;
    private readonly SuitePerformanceCoordinator _coordinator;
    private readonly AutoBuyEngine _engine;
    private long _frame;
    private bool _disposed;

    public AutoBuySimulation(
        int queueCapacity,
        IEnumerable<SimulatedCandidateSpec> candidateSpecs,
        double initialResourceQuantity = 1_000_000_000.0,
        double readObservationCostMilliseconds = 0.05,
        double purchaseObservationCostMilliseconds = 1.1)
    {
        World = new SimulatedAutoBuyWorld(queueCapacity, initialResourceQuantity);
        foreach (var spec in candidateSpecs)
        {
            World.AddCandidate(spec);
        }

        Config = CreateConfig();
        Catalog = new SimulatedAutoBuyCatalog(World);
        _readCost = new DeterministicStopwatchCost(readObservationCostMilliseconds);
        _purchaseCost = new DeterministicStopwatchCost(purchaseObservationCostMilliseconds);
        _coordinator = new SuitePerformanceCoordinator(_performanceClock, 1.0, 2.0, 64);
        _engine = new AutoBuyEngine(
            Config,
            Catalog,
            new ReservePolicy(Config),
            new ManualLogSource(),
            _readCost.Observe,
            _purchaseCost.Observe,
            _coordinator,
            () => _frame);
    }

    public AutomataConfig Config { get; }

    public SimulatedAutoBuyWorld World { get; }

    public SimulatedAutoBuyCatalog Catalog { get; }

    public SimulationMetrics Metrics { get; } = new SimulationMetrics();

    public void Step(
        int completionsBeforeTick = 0,
        float deltaSeconds = 1.0f / 60.0f,
        Action<SimulatedAutoBuyWorld>? afterCompletions = null)
    {
        ThrowIfDisposed();
        _frame++;

        var completed = World.Complete(completionsBeforeTick);
        for (var i = 0; i < completed; i++)
        {
            _engine.NotifyNativeCompletion();
        }

        afterCompletions?.Invoke(World);

        var purchasesBefore = World.TotalSubmitted;
        var evaluationsBefore = World.TotalCandidateEvaluations;
        _engine.Tick(deltaSeconds);
        var purchasesThisFrame = World.TotalSubmitted - purchasesBefore;
        var evaluationsThisFrame = World.TotalCandidateEvaluations - evaluationsBefore;

        Metrics.RecordFrame(
            World,
            purchasesThisFrame,
            evaluationsThisFrame,
            Config.LeaveQueueSlots.Value);
    }

    public void RunFrames(int frameCount, int completionsPerFrame = 0)
    {
        for (var i = 0; i < frameCount; i++)
        {
            Step(completionsPerFrame);
        }
    }

    public bool RunUntil(
        Func<SimulatedAutoBuyWorld, bool> condition,
        int maximumFrames,
        int completionsPerFrame = 0)
    {
        for (var i = 0; i < maximumFrames; i++)
        {
            if (condition(World))
            {
                return true;
            }

            Step(completionsPerFrame);
        }

        return condition(World);
    }

    public void ReloadLifecycle(bool clearQueue = true, bool replaceNativeIdentities = true)
    {
        ThrowIfDisposed();
        if (clearQueue)
        {
            World.ClearQueueForReload();
        }

        if (replaceNativeIdentities)
        {
            World.ReplaceNativeIdentities();
        }

        _engine.InvalidateLifecycle();
    }

    public void NotifyManualQueueChange(SimulatedAutoBuyCandidate candidate)
    {
        ThrowIfDisposed();
        if (candidate.Kind == AutoBuyCandidateKind.Structure)
        {
            _engine.NotifyStructureQueueChanged(candidate.NativeIdentity);
        }
        else
        {
            _engine.NotifyUpgradeQueueChanged(candidate.NativeIdentity);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _engine.Dispose();
    }

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

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AutoBuySimulation));
        }
    }
}

internal sealed class SimulatedAutoBuyWorld
{
    private readonly Queue<SimulatedAutoBuyCandidate?> _queue = new Queue<SimulatedAutoBuyCandidate?>();
    private readonly List<SimulatedAutoBuyCandidate> _candidates = new List<SimulatedAutoBuyCandidate>();
    private readonly List<string> _submissionOrder = new List<string>();

    public SimulatedAutoBuyWorld(int queueCapacity, double initialResourceQuantity)
    {
        if (queueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        }

        QueueCapacity = queueCapacity;
        ResourceQuantity = initialResourceQuantity;
    }

    public int QueueCapacity { get; }

    public int QueueCount => _queue.Count;

    public int RemainingQueueRoom => Math.Max(0, QueueCapacity - QueueCount);

    public int QueueHighWater { get; private set; }

    public int TotalSubmitted { get; private set; }

    public int TotalCompleted { get; private set; }

    public int TotalCandidateEvaluations => _candidates.Sum(candidate => candidate.CanPurchaseCalls);

    public double ResourceQuantity { get; set; }

    public IReadOnlyList<SimulatedAutoBuyCandidate> Candidates => _candidates;

    public IReadOnlyList<string> SubmissionOrder => _submissionOrder;

    public SimulatedAutoBuyCandidate AddCandidate(SimulatedCandidateSpec spec)
    {
        var candidate = new SimulatedAutoBuyCandidate(this, spec);
        _candidates.Add(candidate);
        return candidate;
    }

    public int Complete(int maximumCompletions)
    {
        var completed = 0;
        while (completed < maximumCompletions && _queue.Count > 0)
        {
            var candidate = _queue.Dequeue();
            candidate?.CompleteOne();
            TotalCompleted++;
            completed++;
        }

        return completed;
    }

    public void EnqueueManualAction()
    {
        if (RemainingQueueRoom <= 0)
        {
            throw new InvalidOperationException("The simulated native queue is full.");
        }

        _queue.Enqueue(null);
        QueueHighWater = Math.Max(QueueHighWater, QueueCount);
    }

    public void ClearQueueForReload()
    {
        _queue.Clear();
        foreach (var candidate in _candidates)
        {
            candidate.ClearQueuedLevels();
        }
    }

    public void ReplaceNativeIdentities()
    {
        foreach (var candidate in _candidates)
        {
            candidate.ReplaceNativeIdentity();
        }
    }

    public bool HasPurchasableCandidate(int reservedQueueSlots)
    {
        return RemainingQueueRoom > Math.Max(0, reservedQueueSlots) &&
               _candidates.Any(candidate => candidate.CouldPurchaseNow());
    }

    internal bool TrySubmit(SimulatedAutoBuyCandidate candidate)
    {
        if (!candidate.CouldPurchaseNow())
        {
            return false;
        }

        ResourceQuantity -= candidate.CurrentCost;
        candidate.QueueOne();
        _queue.Enqueue(candidate);
        _submissionOrder.Add(candidate.Uuid);
        TotalSubmitted++;
        QueueHighWater = Math.Max(QueueHighWater, QueueCount);
        return true;
    }
}

internal sealed class SimulatedAutoBuyCandidate :
    IAutoBuyCandidate,
    IAutoBuyNativeIdentity,
    IAutoBuyLifecycleCandidate,
    IAutoBuyDirtyCandidate,
    IAutoBuyPriorityCandidate
{
    private readonly SimulatedAutoBuyWorld _world;
    private readonly AutoBuyCandidateSnapshot _snapshot;
    private object _nativeIdentity = new object();
    private double _observedCost;
    private bool _costDirty = true;
    private long _completionRefreshGeneration = -1;

    public SimulatedAutoBuyCandidate(SimulatedAutoBuyWorld world, SimulatedCandidateSpec spec)
    {
        _world = world;
        Uuid = spec.Uuid;
        Kind = spec.Kind;
        BaseCost = spec.BaseCost;
        CostScaling = spec.CostScaling;
        Available = spec.Available;
        MaximumLevel = spec.MaximumLevel;
        FailureMode = spec.FailureMode;
        EconomicPriority = spec.EconomicPriority;
        _snapshot = new AutoBuyCandidateSnapshot(this, Uuid, Uuid, Kind, nameof(SimulatedAutoBuyCandidate));
    }

    public string Uuid { get; }

    public AutoBuyCandidateKind Kind { get; }

    public double BaseCost { get; }

    public double CostScaling { get; }

    public bool Available { get; set; }

    public int? MaximumLevel { get; }

    public SimulatedPurchaseFailureMode FailureMode { get; set; }

    public int CurrentLevel { get; private set; }

    public int QueuedLevels { get; private set; }

    public int PurchaseCalls { get; private set; }

    public int CanPurchaseCalls { get; private set; }

    public int CostReads { get; private set; }

    public int LifecycleReads { get; private set; }

    public int DirtyMarks { get; private set; }

    public double CostMultiplier { get; set; } = 1.0;

    public double CurrentCost =>
        BaseCost * CostMultiplier * Math.Pow(CostScaling, CurrentLevel + QueuedLevels);

    public object NativeIdentity => _nativeIdentity;

    public IReadOnlyList<string> ResourceDependencies { get; } = new[] { "resource" };

    public bool HasResolvedCosts => true;

    public AutoBuyEconomicPriority EconomicPriority { get; }

    public AutoBuyCandidateSnapshot Snapshot() => _snapshot;

    public bool IsAvailable() => Available;

    public bool CanPurchase(out string reason)
    {
        CanPurchaseCalls++;
        var result = CouldPurchaseNow();
        reason = result ? string.Empty : "simulated native CanPurchase returned false";
        return result;
    }

    public IReadOnlyList<ResourceAdmissionCost> GetCosts()
    {
        CostReads++;
        if (_costDirty)
        {
            _observedCost = CurrentCost;
            _costDirty = false;
        }

        return new[]
        {
            new ResourceAdmissionCost(
                "resource",
                "Resource",
                new BigAmount(_observedCost, 0),
                new BigAmount(_world.ResourceQuantity, 0)),
        };
    }

    public bool TryPurchaseOne(out string reason)
    {
        PurchaseCalls++;
        if (FailureMode == SimulatedPurchaseFailureMode.RejectBeforeMutation)
        {
            reason = "simulated native rejection";
            return false;
        }

        var submitted = _world.TrySubmit(this);
        if (!submitted)
        {
            reason = "simulated queue or resource rejection";
            return false;
        }

        if (FailureMode == SimulatedPurchaseFailureMode.MutateThenReportFailure)
        {
            reason = "simulated ambiguous post-mutation failure";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool TryGetLifecycleEvidence(out AutoBuyLifecycleEvidence evidence, out string reason)
    {
        LifecycleReads++;
        var finite = MaximumLevel.HasValue;
        var maxLevel = finite && CurrentLevel >= MaximumLevel!.Value;
        var maxQueued = finite && CurrentLevel + QueuedLevels >= MaximumLevel!.Value;
        evidence = new AutoBuyLifecycleEvidence(
            Available,
            CurrentLevel,
            QueuedLevels,
            finite,
            maxLevel,
            maxQueued);
        reason = string.Empty;
        return true;
    }

    public void MarkDirty(AutoBuyDirtyReason reasons)
    {
        if (reasons != AutoBuyDirtyReason.None)
        {
            DirtyMarks++;
        }

        if ((reasons & AutoBuyDirtyReason.CostDirty) != 0)
        {
            _costDirty = true;
        }
    }

    public void SetLifecycleEvidence(AutoBuyLifecycleEvidence evidence)
    {
    }

    internal bool TryRefreshAfterCompletion(long completionGeneration, out string reason)
    {
        if (_completionRefreshGeneration != completionGeneration)
        {
            _completionRefreshGeneration = completionGeneration;
            _costDirty = true;
        }

        reason = string.Empty;
        return true;
    }

    internal bool CouldPurchaseNow()
    {
        return Available &&
               _world.RemainingQueueRoom > 0 &&
               _world.ResourceQuantity >= CurrentCost &&
               (!MaximumLevel.HasValue || CurrentLevel + QueuedLevels < MaximumLevel.Value);
    }

    internal void QueueOne() => QueuedLevels++;

    internal void CompleteOne()
    {
        if (QueuedLevels <= 0)
        {
            throw new InvalidOperationException($"Candidate {Uuid} completed without a queued level.");
        }

        QueuedLevels--;
        CurrentLevel++;
    }

    internal void ClearQueuedLevels() => QueuedLevels = 0;

    internal void ReplaceNativeIdentity() => _nativeIdentity = new object();
}

internal sealed class SimulatedAutoBuyCatalog :
    IAutoBuyCatalog,
    IAutoBuyIncrementalCatalog,
    IAutoBuyCompletionRevalidationCatalog
{
    private readonly SimulatedAutoBuyWorld _world;
    private readonly HashSet<string> _deferredResourceInvalidations =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly AutoBuyCompletionSettlementGate _completionSettlement =
        new AutoBuyCompletionSettlementGate();
    private bool _mutationGroupActive;

    public SimulatedAutoBuyCatalog(SimulatedAutoBuyWorld world)
    {
        _world = world;
        RebuildIndex();
    }

    public AutoBuyCandidateIndex Index { get; private set; } = new AutoBuyCandidateIndex();

    public int EvaluationBatches { get; private set; }

    public int CompletedCandidateEvaluations { get; private set; }

    public int CompletionSignals { get; private set; }

    public bool QueueReadSucceeds { get; set; } = true;

    public IEnumerable<IAutoBuyCandidate> Discover() => _world.Candidates;

    public AutoBuyEvaluationBatch BeginEvaluation(AutoBuyEvaluationRequest request)
    {
        EvaluationBatches++;
        CompleteMutationGroup();
        FlushDeferredInvalidations();
        if (_completionSettlement.TryBegin(Index.SettlementValidationPending))
        {
            Index.InvalidateCompletionEffects();
        }

        var batch = Index.PrepareEvaluation(
            request,
            lifecycleWorkLimit: 32,
            activeRefreshCount: 0,
            slowRefreshCount: 0);
        return new AutoBuyEvaluationBatch(
            batch.ActiveCandidates,
            batch.DirtyCandidates,
            batch.FirstExcludedCandidate,
            Index.EpochValidationPending || Index.SettlementValidationPending);
    }

    public void CompleteCandidateEvaluation(
        IAutoBuyCandidate candidate,
        bool suppressResourceTracking,
        bool policyExcluded)
    {
        CompletedCandidateEvaluations++;
        Index.CompleteCandidateEvaluation(candidate, suppressResourceTracking, policyExcluded);
    }

    public void InvalidatePolicy()
    {
        _deferredResourceInvalidations.Clear();
        Index.InvalidatePolicy();
    }

    public void BeginMutationEvaluation()
    {
    }

    public void NotifyPurchaseAttempted(IAutoBuyCandidate candidate)
    {
        _mutationGroupActive = true;
        if (candidate is IAutoBuyDirtyCandidate dirty)
        {
            foreach (var resourceId in dirty.ResourceDependencies)
            {
                _deferredResourceInvalidations.Add(resourceId);
            }

            Index.MarkPurchaseAttempted(candidate, invalidateResourceDependents: false);
            return;
        }

        Index.MarkPurchaseAttempted(candidate);
    }

    public void CompleteMutationGroup() => _mutationGroupActive = false;

    public void NotifyStructureQueueChanged(object nativeIdentity)
    {
        CompleteMutationGroup();
        Index.InvalidateQueue(nativeIdentity, AutoBuyCandidateKind.Structure);
    }

    public void NotifyUpgradeQueueChanged(object nativeIdentity)
    {
        CompleteMutationGroup();
        Index.InvalidateQueue(nativeIdentity, AutoBuyCandidateKind.Upgrade);
    }

    public void NotifyNativeCompletion()
    {
        CompletionSignals++;
        _completionSettlement.Notify();
    }

    public bool TryRefreshCandidateAfterCompletion(
        IAutoBuyCandidate candidate,
        long completionGeneration,
        out string reason)
    {
        if (candidate is SimulatedAutoBuyCandidate simulatedCandidate)
        {
            return simulatedCandidate.TryRefreshAfterCompletion(completionGeneration, out reason);
        }

        reason = "candidate is not part of the simulated native world";
        return false;
    }

    public void InvalidateLifecycle()
    {
        _mutationGroupActive = false;
        _completionSettlement.Clear();
        _deferredResourceInvalidations.Clear();
        RebuildIndex();
    }

    public bool TryGetRemainingQueueRoom(out int remainingRoom)
    {
        remainingRoom = _world.RemainingQueueRoom;
        return QueueReadSucceeds;
    }

    public bool TryGetBulkDevelopment(out int levels)
    {
        levels = 1;
        return true;
    }

    public bool TryGetActionMultiplier(out int multiplier)
    {
        multiplier = 1;
        return true;
    }

    public void Dispose()
    {
    }

    private void FlushDeferredInvalidations()
    {
        if (_mutationGroupActive)
        {
            return;
        }

        foreach (var resourceId in _deferredResourceInvalidations)
        {
            Index.InvalidateResource(resourceId, AutoBuyResourceChange.Quantity);
        }

        _deferredResourceInvalidations.Clear();
    }

    private void RebuildIndex()
    {
        Index.Clear();
        Index.Reconcile(_world.Candidates);
    }
}

internal sealed class SimulationMetrics
{
    private bool _saturated;

    public int Frames { get; private set; }

    public int IdleFramesWithPurchasableWork { get; private set; }

    public int MaximumEvaluationsInFrame { get; private set; }

    public int MinimumQueueAfterSaturation { get; private set; } = int.MaxValue;

    public int? FramesToNinetyPercentQueue { get; private set; }

    public void RecordFrame(
        SimulatedAutoBuyWorld world,
        int purchasesThisFrame,
        int evaluationsThisFrame,
        int reservedQueueSlots)
    {
        Frames++;
        MaximumEvaluationsInFrame = Math.Max(MaximumEvaluationsInFrame, evaluationsThisFrame);
        var usableCapacity = Math.Max(0, world.QueueCapacity - Math.Max(0, reservedQueueSlots));
        var ninetyPercent = (int)Math.Ceiling(usableCapacity * 0.9);
        if (!FramesToNinetyPercentQueue.HasValue && world.QueueCount >= ninetyPercent)
        {
            FramesToNinetyPercentQueue = Frames;
            _saturated = true;
        }

        if (_saturated)
        {
            MinimumQueueAfterSaturation = Math.Min(MinimumQueueAfterSaturation, world.QueueCount);
        }

        if (purchasesThisFrame == 0 && world.HasPurchasableCandidate(reservedQueueSlots))
        {
            IdleFramesWithPurchasableWork++;
        }
    }
}

internal sealed class DeterministicStopwatchCost
{
    private readonly ConditionalWeakTable<Stopwatch, Counter> _observations =
        new ConditionalWeakTable<Stopwatch, Counter>();
    private readonly double _costPerObservationMilliseconds;

    public DeterministicStopwatchCost(double costPerObservationMilliseconds)
    {
        _costPerObservationMilliseconds = costPerObservationMilliseconds;
    }

    public double Observe(Stopwatch stopwatch)
    {
        var counter = _observations.GetOrCreateValue(stopwatch);
        counter.Value += _costPerObservationMilliseconds;
        return counter.Value;
    }

    private sealed class Counter
    {
        public double Value { get; set; }
    }
}

internal sealed class SimulationPerformanceClock : IPerformanceClock
{
    private long _microseconds;

    public long GetTimestamp() => _microseconds;

    public double GetElapsedMilliseconds(long startTimestamp, long endTimestamp) =>
        (endTimestamp - startTimestamp) / 1000.0;

    public void Advance(double milliseconds) =>
        _microseconds += (long)(milliseconds * 1000.0);
}

internal readonly struct SimulatedCandidateSpec
{
    public SimulatedCandidateSpec(
        string uuid,
        AutoBuyCandidateKind kind,
        double baseCost = 1.0,
        double costScaling = 1.0,
        bool available = true,
        int? maximumLevel = null,
        SimulatedPurchaseFailureMode failureMode = SimulatedPurchaseFailureMode.None,
        AutoBuyEconomicPriority economicPriority = AutoBuyEconomicPriority.None)
    {
        Uuid = uuid;
        Kind = kind;
        BaseCost = baseCost;
        CostScaling = costScaling;
        Available = available;
        MaximumLevel = maximumLevel;
        FailureMode = failureMode;
        EconomicPriority = economicPriority;
    }

    public string Uuid { get; }

    public AutoBuyCandidateKind Kind { get; }

    public double BaseCost { get; }

    public double CostScaling { get; }

    public bool Available { get; }

    public int? MaximumLevel { get; }

    public SimulatedPurchaseFailureMode FailureMode { get; }

    public AutoBuyEconomicPriority EconomicPriority { get; }
}

internal enum SimulatedPurchaseFailureMode
{
    None,
    RejectBeforeMutation,
    MutateThenReportFailure,
}
