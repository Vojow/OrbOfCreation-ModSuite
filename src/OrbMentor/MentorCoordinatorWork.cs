using System;
using OrbModding.Common;

namespace OrbMentor;

internal sealed class MentorCoordinatorWork : IDisposable
{
    private readonly SuitePerformanceCoordinator _coordinator;
    private readonly Func<long> _readFrameIdentity;
    private readonly SuiteWorkRegistration _cooperativeWork;
    private readonly SuiteWorkRegistration _mutationWork;

    public MentorCoordinatorWork(SuitePerformanceCoordinator coordinator, Func<long> readFrameIdentity)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _readFrameIdentity = readFrameIdentity ?? throw new ArgumentNullException(nameof(readFrameIdentity));
        _cooperativeWork = coordinator.Register(
            "OrbMentor",
            "Reconcile, resolve, and plan XP",
            SuiteBudgetClass.SoftLimited,
            SuiteWorkExecutionKind.Cooperative);
        _mutationWork = coordinator.Register(
            "OrbMentor",
            "Grant one mastery XP mutation",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
    }

    internal bool CooperativePending => _cooperativeWork.IsPending;
    internal bool MutationPending => _mutationWork.IsPending;

    public void SetState(bool enabled, bool cooperativePending, bool mutationPending)
    {
        SetEnabled(_cooperativeWork, enabled);
        SetEnabled(_mutationWork, enabled);
        if (!enabled) return;
        if (_cooperativeWork.IsPending != cooperativePending)
            _cooperativeWork.SetPending(cooperativePending);
        if (_mutationWork.IsPending != mutationPending)
            _mutationWork.SetPending(mutationPending);
    }

    public bool TryRunCooperative(Func<int> run)
    {
        if (!_cooperativeWork.IsPending ||
            _coordinator.RequestWork(_cooperativeWork, _readFrameIdentity(), out var lease) != SuiteWorkAdmission.Granted)
            return false;
        using (lease)
        {
            var operations = run();
            lease.Complete(operations);
            return true;
        }
    }

    public bool TryRunMutation(Func<int> run)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));
        return TryRunMutation(() => new SuiteWorkCompletion(run()));
    }

    public bool TryRunMutation(Func<SuiteWorkCompletion> run)
    {
        return TryRunMutation(run, readFailureCompletion: null);
    }

    public bool TryRunMutation(
        Func<SuiteWorkCompletion> run,
        Func<SuiteWorkCompletion>? readFailureCompletion)
    {
        if (!_mutationWork.IsPending ||
            _coordinator.RequestWork(_mutationWork, _readFrameIdentity(), out var lease) != SuiteWorkAdmission.Granted)
            return false;
        using (lease)
        {
            try
            {
                lease.Complete(run());
                return true;
            }
            catch
            {
                lease.Fail(readFailureCompletion?.Invoke() ?? new SuiteWorkCompletion(1));
                throw;
            }
        }
    }

    public void Dispose()
    {
        SetState(false, false, false);
        _cooperativeWork.Dispose();
        _mutationWork.Dispose();
    }

    private static void SetEnabled(SuiteWorkRegistration registration, bool enabled)
    {
        if (registration.IsEnabled != enabled) registration.SetEnabled(enabled);
    }
}
