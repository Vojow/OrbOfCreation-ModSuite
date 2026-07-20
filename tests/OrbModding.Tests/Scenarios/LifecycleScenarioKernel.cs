using System;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbModding.Tests.Scenarios;

internal interface ILifecycleScenarioFeature : IDisposable
{
    string Name { get; }

    void OnLifecycleTransition(GameLifecycleTransition transition, object? sceneIdentity);

    void Tick(long frame, TimeSpan delta);
}

internal sealed class LifecycleScenarioKernel : IDisposable
{
    private readonly List<ILifecycleScenarioFeature> _features = new();
    private readonly List<ScheduledScenarioCallback> _scheduled = new();
    private readonly SuiteWorkRegistration _mutationBlocker;
    private readonly IDisposable _invalidationTraceSubscription;
    private bool _blockNextMutation;
    private bool _disposed;

    public LifecycleScenarioKernel()
    {
        Clock = new ScenarioPerformanceClock();
        Lifecycle = new GameLifecycleMonitor(() => Environment.CurrentManagedThreadId);
        Coordinator = new SuitePerformanceCoordinator(Clock, 10.0, 10.0, 256);
        _mutationBlocker = Coordinator.Register(
            "Scenario",
            "Deliberately occupy native mutation admission",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        Invalidations = new GameplayInvalidationBus(
            Lifecycle,
            capacity: 128,
            readThreadId: () => Environment.CurrentManagedThreadId,
            coordinator: Coordinator,
            coordinatorSliceOperations: 16);
        _invalidationTraceSubscription = Invalidations.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.All),
            change => InvalidationTrace.Add(change),
            "LifecycleScenarioKernel trace");
        Lifecycle.Transitioned += OnLifecycleTransition;
    }

    public long Frame { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public object? SceneIdentity { get; private set; }

    public ScenarioPerformanceClock Clock { get; }

    public GameLifecycleMonitor Lifecycle { get; }

    public GameplayInvalidationBus Invalidations { get; }

    public SuitePerformanceCoordinator Coordinator { get; }

    public List<GameLifecycleTransition> LifecycleTrace { get; } = new();

    public List<GameplayInvalidation> InvalidationTrace { get; } = new();

    public List<ScenarioCallbackObservation> CallbackTrace { get; } = new();

    public List<ScenarioMutationObservation> Mutations { get; } = new();

    public T AddFeature<T>(T feature) where T : ILifecycleScenarioFeature
    {
        ThrowIfDisposed();
        _features.Add(feature ?? throw new ArgumentNullException(nameof(feature)));
        return feature;
    }

    public GameLifecycleTransition Observe(
        GameLifecycleTransitionKind kind,
        string sceneName = "Main",
        object? nativeIdentity = null,
        string source = "scenario")
    {
        ThrowIfDisposed();
        AdvanceFrame(TimeSpan.Zero);
        return ObserveAtCurrentFrame(kind, sceneName, nativeIdentity, source);
    }

    public GameLifecycleTransition ObserveAtCurrentFrame(
        GameLifecycleTransitionKind kind,
        string sceneName = "Main",
        object? nativeIdentity = null,
        string source = "scenario")
    {
        ThrowIfDisposed();
        if (!Lifecycle.TryObserve(
                new GameLifecycleObservation(kind, Frame, sceneName, source, nativeIdentity),
                out var transition,
                out var reason))
        {
            throw new InvalidOperationException($"Scenario lifecycle observation was rejected: {reason}");
        }

        return transition;
    }

    public object EnterScene(string sceneName = "Main", object? sceneIdentity = null)
    {
        SceneIdentity = sceneIdentity ?? new object();
        Observe(GameLifecycleTransitionKind.SceneEntered, sceneName, SceneIdentity, "scenario scene enter");
        return SceneIdentity;
    }

    public object RecreateSceneWithSameName(string sceneName = "Main")
    {
        Observe(GameLifecycleTransitionKind.SceneExited, sceneName, SceneIdentity, "scenario scene exit");
        return EnterScene(sceneName, new object());
    }

    public void PublishInvalidation(
        GameplayInvalidationKind kinds,
        string? domain = null,
        string? entityId = null,
        string? expectedTypeName = null,
        string source = "scenario")
    {
        ThrowIfDisposed();
        if (!Invalidations.Publish(kinds, Frame, domain, entityId, expectedTypeName, source))
            throw new InvalidOperationException("Scenario invalidation was not accepted.");
    }

    public bool TryPublishForGeneration(
        long generation,
        GameplayInvalidationKind kinds,
        string? domain,
        string? entityId,
        string? expectedTypeName,
        out string reason) =>
        Invalidations.TryPublish(
            new GameplayInvalidationRequest(
                kinds,
                generation,
                Frame,
                domain,
                entityId,
                expectedTypeName,
                "scenario delayed callback"),
            out reason);

    public void Schedule(string name, int delayFrames, Action callback)
    {
        ScheduleCore(name, delayFrames, callback, discardWhenLifecycleIsStale: true);
    }

    public void ScheduleUnfiltered(string name, int delayFrames, Action callback)
    {
        ScheduleCore(name, delayFrames, callback, discardWhenLifecycleIsStale: false);
    }

    private void ScheduleCore(
        string name,
        int delayFrames,
        Action callback,
        bool discardWhenLifecycleIsStale)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A callback name is required.", nameof(name));
        if (delayFrames < 1) throw new ArgumentOutOfRangeException(nameof(delayFrames));
        _scheduled.Add(new ScheduledScenarioCallback(
            name,
            checked(Frame + delayFrames),
            Lifecycle.Current.Generation,
            callback ?? throw new ArgumentNullException(nameof(callback)),
            discardWhenLifecycleIsStale));
    }

    public void BlockNativeMutationOnNextStep()
    {
        ThrowIfDisposed();
        _blockNextMutation = true;
    }

    public void Step(int frames = 1, double secondsPerFrame = 1.0 / 60.0)
    {
        ThrowIfDisposed();
        if (frames < 1) throw new ArgumentOutOfRangeException(nameof(frames));
        if (secondsPerFrame < 0 || double.IsNaN(secondsPerFrame) || double.IsInfinity(secondsPerFrame))
            throw new ArgumentOutOfRangeException(nameof(secondsPerFrame));

        var delta = TimeSpan.FromSeconds(secondsPerFrame);
        for (var index = 0; index < frames; index++)
        {
            AdvanceFrame(delta);
            DispatchDueCallbacks();
            AdmitMutationBlockerIfRequested();
            Invalidations.Pump(Frame, GameplayInvalidationBus.DefaultMaxOperationsPerFrame);
            foreach (var feature in _features)
                feature.Tick(Frame, delta);
        }
    }

    public void RecordMutation(string feature, string actionFamily, string target, string requestId)
    {
        Mutations.Add(new ScenarioMutationObservation(
            Frame,
            Lifecycle.Current.Generation,
            feature,
            actionFamily,
            target,
            requestId));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Lifecycle.Transitioned -= OnLifecycleTransition;
        for (var index = _features.Count - 1; index >= 0; index--)
            _features[index].Dispose();
        _features.Clear();
        _invalidationTraceSubscription.Dispose();
        Invalidations.Dispose();
        _mutationBlocker.Dispose();
    }

    private void OnLifecycleTransition(GameLifecycleTransition transition)
    {
        LifecycleTrace.Add(transition);
        foreach (var feature in _features)
            feature.OnLifecycleTransition(transition, SceneIdentity);
    }

    private void AdvanceFrame(TimeSpan delta)
    {
        Frame = checked(Frame + 1);
        Elapsed += delta;
        Clock.Advance(delta);
    }

    private void DispatchDueCallbacks()
    {
        for (var index = 0; index < _scheduled.Count;)
        {
            var scheduled = _scheduled[index];
            if (scheduled.DueFrame > Frame)
            {
                index++;
                continue;
            }

            _scheduled.RemoveAt(index);
            var currentGeneration = Lifecycle.Current.Generation;
            if (scheduled.DiscardWhenLifecycleIsStale && scheduled.Generation != currentGeneration)
            {
                CallbackTrace.Add(new ScenarioCallbackObservation(
                    scheduled.Name,
                    scheduled.Generation,
                    currentGeneration,
                    Frame,
                    executed: false));
                continue;
            }

            scheduled.Callback();
            CallbackTrace.Add(new ScenarioCallbackObservation(
                scheduled.Name,
                scheduled.Generation,
                currentGeneration,
                Frame,
                executed: true));
        }
    }

    private void AdmitMutationBlockerIfRequested()
    {
        if (!_blockNextMutation) return;
        _blockNextMutation = false;
        _mutationBlocker.SetPending(true);
        var admission = Coordinator.RequestWork(_mutationBlocker, Frame, out var lease);
        if (admission != SuiteWorkAdmission.Granted)
            throw new InvalidOperationException($"Scenario mutation blocker was not admitted: {admission}");
        lease.Complete();
        _mutationBlocker.SetPending(false);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LifecycleScenarioKernel));
    }

    private sealed class ScheduledScenarioCallback
    {
        public ScheduledScenarioCallback(
            string name,
            long dueFrame,
            long generation,
            Action callback,
            bool discardWhenLifecycleIsStale)
        {
            Name = name;
            DueFrame = dueFrame;
            Generation = generation;
            Callback = callback;
            DiscardWhenLifecycleIsStale = discardWhenLifecycleIsStale;
        }

        public string Name { get; }
        public long DueFrame { get; }
        public long Generation { get; }
        public Action Callback { get; }
        public bool DiscardWhenLifecycleIsStale { get; }
    }
}

internal sealed class ScenarioPerformanceClock : IPerformanceClock
{
    private long _microseconds;

    public long GetTimestamp() => _microseconds;

    public double GetElapsedMilliseconds(long startTimestamp, long endTimestamp) =>
        (endTimestamp - startTimestamp) / 1000.0;

    public void Advance(TimeSpan delta) =>
        _microseconds = checked(_microseconds + (long)Math.Round(delta.TotalMilliseconds * 1000.0));
}

internal readonly struct ScenarioCallbackObservation
{
    public ScenarioCallbackObservation(
        string name,
        long scheduledGeneration,
        long currentGeneration,
        long frame,
        bool executed)
    {
        Name = name;
        ScheduledGeneration = scheduledGeneration;
        CurrentGeneration = currentGeneration;
        Frame = frame;
        Executed = executed;
    }

    public string Name { get; }
    public long ScheduledGeneration { get; }
    public long CurrentGeneration { get; }
    public long Frame { get; }
    public bool Executed { get; }
}

internal readonly struct ScenarioMutationObservation
{
    public ScenarioMutationObservation(
        long frame,
        long lifecycleGeneration,
        string feature,
        string actionFamily,
        string target,
        string requestId)
    {
        Frame = frame;
        LifecycleGeneration = lifecycleGeneration;
        Feature = feature;
        ActionFamily = actionFamily;
        Target = target;
        RequestId = requestId;
    }

    public long Frame { get; }
    public long LifecycleGeneration { get; }
    public string Feature { get; }
    public string ActionFamily { get; }
    public string Target { get; }
    public string RequestId { get; }
}
