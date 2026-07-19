using System;
using System.Collections.Generic;
using OrbMentor;
using OrbModding.Common;

namespace OrbModding.Tests.Scenarios;

internal sealed class ScenarioMentorFeature : ILifecycleScenarioFeature
{
    private readonly LifecycleScenarioKernel _kernel;
    private readonly MentorCoordinatorWork _work;
    private readonly MentorEngine _engine = new();
    private readonly Dictionary<string, Queue<string>> _requestIds =
        new(StringComparer.Ordinal);
    private int _nextRequest;
    private bool _disposed;

    public ScenarioMentorFeature(LifecycleScenarioKernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _work = new MentorCoordinatorWork(kernel.Coordinator, () => kernel.Frame);
    }

    public string Name => "OrbMentor.Spells";

    public bool Enabled { get; private set; } = true;

    public bool Unlocked { get; private set; }

    public int NativeGrantCalls { get; private set; }

    public bool HasPendingGrant => _engine.TryPeek(out _);

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!enabled) CancelPreparedWork();
        _kernel.PublishInvalidation(
            GameplayInvalidationKind.Configuration,
            GameplayInvalidationDomains.MentorSpells,
            source: enabled ? "scenario Mentor enable" : "scenario Mentor disable");
    }

    public void SetUnlocked(bool unlocked)
    {
        Unlocked = unlocked;
        if (!unlocked) CancelPreparedWork();
        _kernel.PublishInvalidation(
            GameplayInvalidationKind.Progression,
            GameplayInvalidationDomains.MentorSpells,
            source: unlocked ? "scenario Mentor unlock" : "scenario Mentor lock");
    }

    public string QueueGrant(string target, double mantissa = 1.0, int exponent = 0)
    {
        if (!Guid.TryParseExact(target, "D", out _))
            throw new ArgumentException("A canonical target UUID is required.", nameof(target));
        var requestId = $"mentor-grant-{++_nextRequest}";
        _engine.Consolidate(new MentorGrant(target, new MentorAmount(mantissa, exponent)));
        if (!_requestIds.TryGetValue(target, out var requests))
        {
            requests = new Queue<string>();
            _requestIds.Add(target, requests);
        }
        requests.Enqueue(requestId);
        return requestId;
    }

    public void CancelPreparedWork()
    {
        _engine.Cancel();
        _requestIds.Clear();
        _work.SetState(false, cooperativePending: false, mutationPending: false);
    }

    public void OnLifecycleTransition(GameLifecycleTransition transition, object? sceneIdentity) =>
        CancelPreparedWork();

    public void Tick(long frame, TimeSpan delta)
    {
        var active = Enabled &&
                     Unlocked &&
                     _kernel.Lifecycle.Current.IsGameplayReady &&
                     string.Equals(_kernel.Lifecycle.Current.SceneName, "Main", StringComparison.Ordinal);
        var pending = active && _engine.TryPeek(out _);
        _work.SetState(active, cooperativePending: false, mutationPending: pending);
        if (!pending) return;

        _work.TryRunMutation(() =>
        {
            if (!_engine.TryPeek(out var grant)) return 0;
            NativeGrantCalls++;
            var requestId = TakeRequestId(grant.Uuid);
            _engine.Complete(grant.Uuid);
            _kernel.RecordMutation(
                Name,
                "SpellMasteryExperienceGrant",
                grant.Uuid,
                requestId);
            return 1;
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelPreparedWork();
        _work.Dispose();
    }

    private string TakeRequestId(string target)
    {
        if (_requestIds.TryGetValue(target, out var requests) && requests.Count > 0)
        {
            var requestId = requests.Dequeue();
            if (requests.Count == 0) _requestIds.Remove(target);
            return requestId;
        }

        throw new InvalidOperationException($"No scenario request identity exists for Mentor target {target}.");
    }
}
