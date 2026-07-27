using System;
using OrbModding.Common;

namespace OrbModConfig;

internal sealed class ModConfigCoordinatorWork : IDisposable
{
    private readonly SuitePerformanceCoordinator _coordinator;
    private readonly Func<long> _readFrameIdentity;
    private readonly SuiteWorkRegistration _work;

    public ModConfigCoordinatorWork(SuitePerformanceCoordinator coordinator, Func<long> readFrameIdentity)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _readFrameIdentity = readFrameIdentity ?? throw new ArgumentNullException(nameof(readFrameIdentity));
        var identity = SuitePerformanceWorkIdentities.ModConfigWork;
        _work = coordinator.Register(
            identity.Subsystem,
            identity.WorkName,
            identity.BudgetClass,
            identity.ExecutionKind);
    }

    internal bool IsPending => _work.IsPending;

    public void SetState(bool enabled, bool pending)
    {
        if (_work.IsEnabled != enabled) _work.SetEnabled(enabled);
        if (!enabled) return;
        if (_work.IsPending != pending) _work.SetPending(pending);
    }

    public bool TryRun(bool enabled, bool pending, Action run)
    {
        SetState(enabled, pending);
        if (!pending ||
            _coordinator.RequestWork(_work, _readFrameIdentity(), out var lease) != SuiteWorkAdmission.Granted)
            return false;
        using (lease)
        {
            run();
            lease.Complete();
            return true;
        }
    }

    public void Clear()
    {
        if (_work.IsEnabled) _work.SetEnabled(false);
    }

    public void Dispose()
    {
        Clear();
        _work.Dispose();
    }
}
