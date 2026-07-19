using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace OrbModding.Common;

public interface IPerformanceClock
{
    long GetTimestamp();

    double GetElapsedMilliseconds(long startTimestamp, long endTimestamp);
}

public sealed class StopwatchPerformanceClock : IPerformanceClock
{
    public static StopwatchPerformanceClock Instance { get; } = new();

    private StopwatchPerformanceClock()
    {
    }

    public long GetTimestamp()
    {
        return Stopwatch.GetTimestamp();
    }

    public double GetElapsedMilliseconds(long startTimestamp, long endTimestamp)
    {
        return (endTimestamp - startTimestamp) * 1000.0 / Stopwatch.Frequency;
    }
}

/// <summary>
/// Defines which shared frame limit controls admission of a resumable work item.
/// Soft-limited work is ordinary background work. Hard-limited work should be
/// reserved for small time-sensitive checks that may continue after the soft limit.
/// </summary>
public enum SuiteBudgetClass
{
    SoftLimited,
    HardLimited,
}

public enum SuiteWorkExecutionKind
{
    Cooperative,
    NonPreemptibleNative,
    NonPreemptibleNativeMutation,
}

public enum SuiteWorkAdmission
{
    Granted,
    Unregistered,
    Disabled,
    NoPendingWork,
    WorkInProgress,
    WaitingForTurn,
    SoftBudgetExhausted,
    HardBudgetExhausted,
    NativeMutationAlreadyAdmitted,
}

/// <summary>
/// Cooperative, main-thread frame budget shared by suite plugins through the
/// Common assembly. Work is admitted in bounded weighted round-robin order. The
/// coordinator does not own gameplay work and never invokes Unity APIs itself.
/// </summary>
public sealed class SuitePerformanceCoordinator
{
    private const int DefaultMetricsWindow = 300;
    private const int DefaultMissedRequestFrames = 2;
    private const int DefaultStarvationThresholdFrames = 120;
    private readonly IPerformanceClock _clock;
    private readonly int _ownerThreadId;
    private readonly List<SuiteWorkRegistration> _registrations = new();
    private readonly Dictionary<string, SubsystemState> _subsystems =
        new(StringComparer.Ordinal);
    private readonly RollingPerformanceMetrics _suiteFrameMetrics;
    private readonly int _metricsWindow;
    private readonly int _missedRequestFrames;
    private readonly int _starvationThresholdFrames;
    private int _nextRegistrationId;
    private int _roundRobinIndex;
    private long _frameIdentity;
    private long _frameEpoch;
    private bool _hasFrame;
    private bool _frameHadWork;
    private double _frameElapsedMilliseconds;
    private long _activeLeaseToken;
    private SuiteWorkRegistration? _activeRegistration;
    private long _activeStartTimestamp;
    private SuiteWorkExecutionKind _activeExecutionKind;
    private bool _nativeMutationAdmitted;

    public SuitePerformanceCoordinator(
        IPerformanceClock clock,
        double softBudgetMilliseconds = 0.75,
        double hardBudgetMilliseconds = 1.0,
        int metricsWindow = DefaultMetricsWindow,
        int missedRequestFrames = DefaultMissedRequestFrames,
        int starvationThresholdFrames = DefaultStarvationThresholdFrames)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ValidateBudgets(softBudgetMilliseconds, hardBudgetMilliseconds);
        if (metricsWindow <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(metricsWindow), "The metrics window must be positive.");
        }

        if (missedRequestFrames <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(missedRequestFrames),
                "The missed-request watchdog must allow at least one frame.");
        }

        if (starvationThresholdFrames <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(starvationThresholdFrames),
                "The starvation threshold must allow at least one frame.");
        }

        _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        SoftBudgetMilliseconds = softBudgetMilliseconds;
        HardBudgetMilliseconds = hardBudgetMilliseconds;
        _metricsWindow = metricsWindow;
        _missedRequestFrames = missedRequestFrames;
        _starvationThresholdFrames = starvationThresholdFrames;
        _suiteFrameMetrics = new RollingPerformanceMetrics(metricsWindow);
    }

    /// <summary>
    /// Shared coordinator used when all supported plugins resolve the same
    /// OrbModding.Common assembly instance in the process.
    /// </summary>
    public static SuitePerformanceCoordinator Shared { get; } =
        new(StopwatchPerformanceClock.Instance);

    public double SoftBudgetMilliseconds { get; private set; }

    public double HardBudgetMilliseconds { get; private set; }

    public long CurrentFrameIdentity => _frameIdentity;

    public bool HasCurrentFrame => _hasFrame;

    public double CurrentFrameElapsedMilliseconds => _frameElapsedMilliseconds;

    public bool IsHardBudgetExceeded =>
        _hasFrame && _frameElapsedMilliseconds >= HardBudgetMilliseconds;

    public bool NativeMutationAdmittedThisFrame => _nativeMutationAdmitted;

    public SuiteWorkRegistration Register(
        string subsystem,
        string workName,
        SuiteBudgetClass budgetClass = SuiteBudgetClass.SoftLimited,
        SuiteWorkExecutionKind executionKind = SuiteWorkExecutionKind.Cooperative)
    {
        return RegisterCore(subsystem, workName, budgetClass, executionKind, schedulingWeight: 1);
    }

    public SuiteWorkRegistration RegisterWeighted(
        string subsystem,
        string workName,
        SuiteBudgetClass budgetClass,
        SuiteWorkExecutionKind executionKind,
        int schedulingWeight)
    {
        return RegisterCore(subsystem, workName, budgetClass, executionKind, schedulingWeight);
    }

    private SuiteWorkRegistration RegisterCore(
        string subsystem,
        string workName,
        SuiteBudgetClass budgetClass,
        SuiteWorkExecutionKind executionKind,
        int schedulingWeight)
    {
        EnsureOwnerThread();
        if (string.IsNullOrWhiteSpace(subsystem))
        {
            throw new ArgumentException("A subsystem name is required.", nameof(subsystem));
        }

        if (string.IsNullOrWhiteSpace(workName))
        {
            throw new ArgumentException("A work item name is required.", nameof(workName));
        }

        ValidateBudgetClass(budgetClass);
        ValidateExecutionKind(executionKind);
        if (schedulingWeight < 1 || schedulingWeight > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schedulingWeight),
                "Scheduling weight must be between 1 and 8.");
        }

        if (!_subsystems.TryGetValue(subsystem, out var subsystemState))
        {
            subsystemState = new SubsystemState(_metricsWindow);
            _subsystems.Add(subsystem, subsystemState);
        }

        var registration = new SuiteWorkRegistration(
            this,
            ++_nextRegistrationId,
            subsystem,
            workName,
            budgetClass,
            executionKind,
            schedulingWeight,
            _frameEpoch,
            subsystemState,
            new RegistrationState(_metricsWindow, _starvationThresholdFrames));
        _registrations.Add(registration);
        return registration;
    }

    public void SetBudgets(double softBudgetMilliseconds, double hardBudgetMilliseconds)
    {
        EnsureOwnerThread();
        ValidateBudgets(softBudgetMilliseconds, hardBudgetMilliseconds);
        SoftBudgetMilliseconds = softBudgetMilliseconds;
        HardBudgetMilliseconds = hardBudgetMilliseconds;
    }

    /// <summary>
    /// Starts a frame when its identity differs from the current frame. Calling
    /// this repeatedly with the same identity does not reset accumulated work.
    /// </summary>
    public void BeginFrame(long frameIdentity)
    {
        EnsureOwnerThread();
        if (_hasFrame && _frameIdentity == frameIdentity)
        {
            return;
        }

        var frameIdentityWentBackward = _hasFrame && frameIdentity < _frameIdentity;

        RecoverAbandonedLease();

        FlushFrameMetrics();
        _frameIdentity = frameIdentity;
        _frameEpoch++;
        _hasFrame = true;
        _frameHadWork = false;
        _frameElapsedMilliseconds = 0.0;
        _nativeMutationAdmitted = false;
        if (frameIdentityWentBackward)
        {
            for (var index = 0; index < _registrations.Count; index++)
            {
                var registration = _registrations[index];
                if (!registration.HasPendingWork)
                {
                    continue;
                }

                registration.PendingSinceFrameIdentity = frameIdentity;
                registration.HasPendingFrameIdentity = true;
                registration.PerformanceState.ResetFrameIdentityDiscontinuity();
            }
        }
    }

    public SuiteWorkAdmission RequestWork(
        SuiteWorkRegistration registration,
        long frameIdentity,
        out SuiteWorkLease lease)
    {
        EnsureOwnerThread();
        lease = default;

        if (!IsRegistered(registration))
        {
            return SuiteWorkAdmission.Unregistered;
        }

        BeginFrame(frameIdentity);
        registration.LastRequestEpoch = _frameEpoch;

        if (!registration.Enabled)
        {
            return SuiteWorkAdmission.Disabled;
        }

        if (!registration.HasPendingWork)
        {
            return SuiteWorkAdmission.NoPendingWork;
        }

        if (_activeRegistration is not null)
        {
            return RecordDeferral(registration, SuiteWorkAdmission.WorkInProgress);
        }

        if (_frameElapsedMilliseconds >= HardBudgetMilliseconds)
        {
            return RecordDeferral(registration, SuiteWorkAdmission.HardBudgetExhausted);
        }

        if (registration.BudgetClass == SuiteBudgetClass.SoftLimited &&
            _frameElapsedMilliseconds >= SoftBudgetMilliseconds)
        {
            return RecordDeferral(registration, SuiteWorkAdmission.SoftBudgetExhausted);
        }

        if (registration.ExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNativeMutation &&
            _nativeMutationAdmitted)
        {
            return RecordDeferral(registration, SuiteWorkAdmission.NativeMutationAlreadyAdmitted);
        }

        var nextIndex = FindNextAdmissibleRegistration(registration);
        if (nextIndex < 0 || !ReferenceEquals(_registrations[nextIndex], registration))
        {
            return RecordDeferral(registration, SuiteWorkAdmission.WaitingForTurn);
        }

        long startTimestamp;
        try
        {
            startTimestamp = _clock.GetTimestamp();
        }
        catch
        {
            registration.SubsystemState.MeasurementFailures++;
            registration.PerformanceState.MeasurementFailures++;
            throw;
        }

        AdvanceWeightedRoundRobin(registration, nextIndex);
        _activeRegistration = registration;
        _activeStartTimestamp = startTimestamp;
        _activeExecutionKind = registration.ExecutionKind;
        _activeLeaseToken++;
        registration.SubsystemState.AdmittedWorkItems++;
        registration.PerformanceState.AdmittedWorkItems++;
        registration.PerformanceState.ClosePendingWait(
            ReadPendingWaitFrames(registration, _frameIdentity));
        registration.PerformanceState.ResetWaitAfterAdmission();
        registration.PendingSinceEpoch = _frameEpoch;
        registration.PendingSinceFrameIdentity = _frameIdentity;
        registration.HasPendingFrameIdentity = true;
        if (registration.ExecutionKind != SuiteWorkExecutionKind.Cooperative)
        {
            registration.SubsystemState.NativeCallsStarted++;
            registration.SubsystemState.NativeLeaseAdmissions++;
            registration.PerformanceState.NativeLeaseAdmissions++;
        }

        if (registration.ExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNativeMutation)
        {
            _nativeMutationAdmitted = true;
            registration.SubsystemState.NativeMutationsStarted++;
            registration.SubsystemState.NativeMutationLeaseAdmissions++;
            registration.PerformanceState.NativeMutationLeaseAdmissions++;
        }

        lease = new SuiteWorkLease(this, _activeLeaseToken);
        return SuiteWorkAdmission.Granted;
    }

    private SuiteWorkAdmission RecordDeferral(
        SuiteWorkRegistration registration,
        SuiteWorkAdmission reason)
    {
        registration.SubsystemState.DeferredAdmissions++;
        registration.PerformanceState.RecordDeferral(
            reason,
            _frameIdentity,
            ReadPendingWaitFrames(registration, _frameIdentity));
        return reason;
    }

    private long ReadPendingWaitFrames(
        SuiteWorkRegistration registration,
        long currentFrameIdentity)
    {
        if (!registration.HasPendingFrameIdentity)
        {
            registration.PendingSinceFrameIdentity = currentFrameIdentity;
            registration.HasPendingFrameIdentity = true;
            return 0;
        }

        if (currentFrameIdentity < 0 ||
            registration.PendingSinceFrameIdentity < 0 ||
            currentFrameIdentity < registration.PendingSinceFrameIdentity)
        {
            registration.PendingSinceFrameIdentity = currentFrameIdentity;
            registration.PerformanceState.ResetFrameIdentityDiscontinuity();
            return 0;
        }

        return currentFrameIdentity - registration.PendingSinceFrameIdentity;
    }

    private void ClosePendingWait(SuiteWorkRegistration registration)
    {
        if (!registration.HasPendingFrameIdentity)
        {
            return;
        }

        var waitFrames = _hasFrame
            ? ReadPendingWaitFrames(registration, _frameIdentity)
            : 0;
        registration.PerformanceState.ClosePendingWait(waitFrames);
        registration.HasPendingFrameIdentity = false;
    }

    public SuiteCoordinatorSnapshot GetSnapshot(double percentile = 0.95)
    {
        EnsureOwnerThread();
        return new SuiteCoordinatorSnapshot(
            _hasFrame,
            _frameIdentity,
            _frameElapsedMilliseconds,
            SoftBudgetMilliseconds,
            HardBudgetMilliseconds,
            _frameElapsedMilliseconds >= HardBudgetMilliseconds,
            _nativeMutationAdmitted,
            _suiteFrameMetrics.GetSnapshot(percentile));
    }

    public bool TryGetSubsystemSnapshot(
        string subsystem,
        out SubsystemPerformanceSnapshot snapshot,
        double percentile = 0.95)
    {
        EnsureOwnerThread();
        if (_subsystems.TryGetValue(subsystem, out var state))
        {
            snapshot = new SubsystemPerformanceSnapshot(
                state.CurrentFrameElapsedMilliseconds,
                state.AdmittedWorkItems,
                state.CompletedWorkItems,
                state.FailedWorkItems,
                state.AbandonedWorkItems,
                state.TotalOperations,
                state.DeferredAdmissions,
                state.MissedRequestExpirations,
                state.NativeCallsStarted,
                state.NativeMutationsStarted,
                state.NativeHardBudgetOverruns,
                state.MeasurementFailures,
                state.WorkItemMetrics.GetSnapshot(percentile),
                state.FrameMetrics.GetSnapshot(percentile),
                state.NativeLeaseAdmissions,
                state.NativeMutationLeaseAdmissions,
                state.NativeCallsAttempted,
                state.NativeMutationAttempts,
                state.NativeMutationsCommitted);
            return true;
        }

        snapshot = default;
        return false;
    }

    public bool TryGetRegistrationSnapshot(
        SuiteWorkRegistration registration,
        out RegistrationPerformanceSnapshot snapshot)
    {
        EnsureOwnerThread();
        if (registration is null || !ReferenceEquals(registration.Coordinator, this))
        {
            snapshot = default;
            return false;
        }

        snapshot = registration.PerformanceState.Freeze(
            registration,
            _hasFrame && registration.HasPendingWork
                ? ReadPendingWaitFrames(registration, _frameIdentity)
                : 0);
        return true;
    }

    /// <summary>
    /// Freezes all currently registered work identities for low-frequency
    /// diagnostics. The returned array is intentionally allocated here, never
    /// while admitting, deferring, or recording work.
    /// </summary>
    public RegistrationPerformanceSnapshot[] GetRegistrationSnapshots()
    {
        EnsureOwnerThread();
        var snapshots = new RegistrationPerformanceSnapshot[_registrations.Count];
        for (var index = 0; index < _registrations.Count; index++)
        {
            var registration = _registrations[index];
            snapshots[index] = registration.PerformanceState.Freeze(
                registration,
                _hasFrame && registration.HasPendingWork
                    ? ReadPendingWaitFrames(registration, _frameIdentity)
                    : 0);
        }

        return snapshots;
    }

    internal void SetEnabled(SuiteWorkRegistration registration, bool enabled)
    {
        EnsureOwnerThread();
        if (!IsRegistered(registration))
        {
            return;
        }

        registration.Enabled = enabled;
        if (!enabled)
        {
            ClosePendingWait(registration);
            registration.HasPendingWork = false;
            registration.PerformanceState.ResetPendingWait();
        }
    }

    internal void SetPending(SuiteWorkRegistration registration, bool pending)
    {
        EnsureOwnerThread();
        if (IsRegistered(registration) && registration.Enabled)
        {
            var becamePending = pending && !registration.HasPendingWork;
            registration.HasPendingWork = pending;
            if (becamePending)
            {
                registration.PendingSinceEpoch = _frameEpoch;
                registration.PendingSinceFrameIdentity = _frameIdentity;
                registration.HasPendingFrameIdentity = _hasFrame;
                registration.PerformanceState.BeginPendingWait();
            }
            else if (!pending)
            {
                ClosePendingWait(registration);
                registration.PerformanceState.ResetPendingWait();
            }
        }
    }

    internal void Unregister(SuiteWorkRegistration registration)
    {
        EnsureOwnerThread();
        var index = _registrations.IndexOf(registration);
        if (index < 0)
        {
            return;
        }

        if (ReferenceEquals(_activeRegistration, registration))
        {
            throw new InvalidOperationException("Cannot unregister work while its lease is active.");
        }
        ClosePendingWait(registration);
        registration.PerformanceState.ResetPendingWait();
        FlushRegistrationFrame(registration.PerformanceState);

        _registrations.RemoveAt(index);
        registration.MarkDisposed();
        if (_registrations.Count == 0)
        {
            _roundRobinIndex = 0;
        }
        else
        {
            if (index < _roundRobinIndex)
            {
                _roundRobinIndex--;
            }

            if (_roundRobinIndex >= _registrations.Count)
            {
                _roundRobinIndex = 0;
            }
        }
    }

    internal void CompleteLease(long token, SuiteWorkCompletion completion)
    {
        EnsureOwnerThread();
        if (_activeRegistration is null || token != _activeLeaseToken)
        {
            return;
        }

        var registration = _activeRegistration;
        try
        {
            if (_activeExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNativeMutation &&
                completion.Operations == 0)
            {
                completion = new SuiteWorkCompletion(
                    1,
                    completion.NativeCallsAttempted,
                    completion.NativeMutationAttempts,
                    completion.NativeMutationsCommitted);
            }
            completion.Validate(_activeExecutionKind);

            double elapsed;
            try
            {
                elapsed = MeasureActiveLease();
            }
            catch
            {
                registration.SubsystemState.MeasurementFailures++;
                registration.PerformanceState.MeasurementFailures++;
                throw;
            }

            AccountActiveLease(registration, elapsed, completion);
            registration.SubsystemState.CompletedWorkItems++;
            registration.PerformanceState.CompletedWorkItems++;
        }
        catch
        {
            registration.SubsystemState.FailedWorkItems++;
            registration.PerformanceState.FailedWorkItems++;
            throw;
        }
        finally
        {
            ClearActiveLease();
        }
    }

    private static void ValidateBudgetClass(SuiteBudgetClass budgetClass)
    {
        if (budgetClass != SuiteBudgetClass.SoftLimited &&
            budgetClass != SuiteBudgetClass.HardLimited)
        {
            throw new ArgumentOutOfRangeException(nameof(budgetClass));
        }
    }

    private static void ValidateExecutionKind(SuiteWorkExecutionKind executionKind)
    {
        if (executionKind != SuiteWorkExecutionKind.Cooperative &&
            executionKind != SuiteWorkExecutionKind.NonPreemptibleNative &&
            executionKind != SuiteWorkExecutionKind.NonPreemptibleNativeMutation)
        {
            throw new ArgumentOutOfRangeException(nameof(executionKind));
        }
    }

    internal void FailLease(long token, SuiteWorkCompletion completion = default)
    {
        EnsureOwnerThread();
        if (_activeRegistration is null || token != _activeLeaseToken)
        {
            return;
        }

        var registration = _activeRegistration;
        try
        {
            if (_activeExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNativeMutation &&
                completion.Operations == 0)
            {
                completion = new SuiteWorkCompletion(
                    1,
                    completion.NativeCallsAttempted,
                    completion.NativeMutationAttempts,
                    completion.NativeMutationsCommitted);
            }
            completion.Validate(_activeExecutionKind);
            try
            {
                var elapsed = MeasureActiveLease();
                AccountActiveLease(registration, elapsed, completion);
            }
            catch
            {
                registration.SubsystemState.MeasurementFailures++;
                registration.PerformanceState.MeasurementFailures++;
            }

            registration.SubsystemState.FailedWorkItems++;
            registration.PerformanceState.FailedWorkItems++;
        }
        finally
        {
            ClearActiveLease();
        }
    }

    private static void ValidateBudgets(double softBudgetMilliseconds, double hardBudgetMilliseconds)
    {
        if (double.IsNaN(softBudgetMilliseconds) ||
            double.IsInfinity(softBudgetMilliseconds) ||
            softBudgetMilliseconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(softBudgetMilliseconds));
        }

        if (double.IsNaN(hardBudgetMilliseconds) ||
            double.IsInfinity(hardBudgetMilliseconds) ||
            hardBudgetMilliseconds <= 0.0 ||
            softBudgetMilliseconds > hardBudgetMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(hardBudgetMilliseconds));
        }
    }

    private bool IsRegistered(SuiteWorkRegistration? registration)
    {
        return registration is not null &&
               ReferenceEquals(registration.Coordinator, this) &&
               !registration.IsDisposed &&
               _registrations.Contains(registration);
    }

    private int FindNextAdmissibleRegistration(SuiteWorkRegistration requester)
    {
        for (var offset = 0; offset < _registrations.Count; offset++)
        {
            var index = (_roundRobinIndex + offset) % _registrations.Count;
            var candidate = _registrations[index];
            if (!candidate.Enabled || !candidate.HasPendingWork)
            {
                continue;
            }

            if (candidate.BudgetClass == SuiteBudgetClass.SoftLimited &&
                _frameElapsedMilliseconds >= SoftBudgetMilliseconds)
            {
                continue;
            }

            if (candidate.ExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNativeMutation &&
                _nativeMutationAdmitted)
            {
                continue;
            }

            if (!ReferenceEquals(candidate, requester) && IsMissedRequestExpired(candidate))
            {
                if (candidate.LastMissedRequestExpiryEpoch != _frameEpoch)
                {
                    candidate.LastMissedRequestExpiryEpoch = _frameEpoch;
                    candidate.SubsystemState.MissedRequestExpirations++;
                }

                continue;
            }

            return index;
        }

        return -1;
    }

    private void AdvanceWeightedRoundRobin(SuiteWorkRegistration registration, int grantedIndex)
    {
        if (registration.SchedulingWeight > 1 &&
            registration.ConsecutiveWeightedGrants + 1 < registration.SchedulingWeight)
        {
            registration.ConsecutiveWeightedGrants++;
            _roundRobinIndex = grantedIndex;
            return;
        }

        registration.ConsecutiveWeightedGrants = 0;
        _roundRobinIndex = (grantedIndex + 1) % _registrations.Count;
    }

    private bool IsMissedRequestExpired(SuiteWorkRegistration registration)
    {
        var mostRecentInterest = Math.Max(
            registration.LastRequestEpoch,
            registration.PendingSinceEpoch);
        return _frameEpoch - mostRecentInterest >= _missedRequestFrames;
    }

    private double MeasureActiveLease()
    {
        var endTimestamp = _clock.GetTimestamp();
        var elapsed = _clock.GetElapsedMilliseconds(_activeStartTimestamp, endTimestamp);
        if (double.IsNaN(elapsed) || double.IsInfinity(elapsed) || elapsed < 0.0)
        {
            throw new InvalidOperationException("The performance clock returned an invalid elapsed time.");
        }

        return elapsed;
    }

    private void AccountActiveLease(
        SuiteWorkRegistration registration,
        double elapsed,
        SuiteWorkCompletion completion)
    {
        var beforeCompletion = _frameElapsedMilliseconds;
        _frameElapsedMilliseconds += elapsed;
        _frameHadWork = true;

        var subsystem = registration.SubsystemState;
        subsystem.CurrentFrameElapsedMilliseconds += elapsed;
        subsystem.FrameTouched = true;
        subsystem.TotalOperations += completion.Operations;
        subsystem.NativeCallsAttempted += completion.NativeCallsAttempted;
        subsystem.NativeMutationAttempts += completion.NativeMutationAttempts;
        subsystem.NativeMutationsCommitted += completion.NativeMutationsCommitted;
        subsystem.WorkItemMetrics.Record(elapsed, completion.Operations);

        var registrationState = registration.PerformanceState;
        registrationState.CurrentFrameElapsedMilliseconds += elapsed;
        registrationState.FrameTouched = true;
        registrationState.TotalOperations += completion.Operations;
        registrationState.NativeCallsAttempted += completion.NativeCallsAttempted;
        registrationState.NativeMutationAttempts += completion.NativeMutationAttempts;
        registrationState.NativeMutationsCommitted += completion.NativeMutationsCommitted;
        registrationState.WorkItemMetrics.Record(elapsed, completion.Operations);

        if (_activeExecutionKind != SuiteWorkExecutionKind.Cooperative &&
            beforeCompletion < HardBudgetMilliseconds &&
            _frameElapsedMilliseconds > HardBudgetMilliseconds)
        {
            subsystem.NativeHardBudgetOverruns++;
            registrationState.NativeHardBudgetOverruns++;
        }
    }

    private void RecoverAbandonedLease()
    {
        if (_activeRegistration is null)
        {
            return;
        }

        var registration = _activeRegistration;
        try
        {
            try
            {
                var elapsed = MeasureActiveLease();
                var completion = _activeExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNativeMutation
                    ? new SuiteWorkCompletion(1)
                    : default;
                AccountActiveLease(registration, elapsed, completion);
            }
            catch
            {
                registration.SubsystemState.MeasurementFailures++;
                registration.PerformanceState.MeasurementFailures++;
            }

            registration.SubsystemState.FailedWorkItems++;
            registration.SubsystemState.AbandonedWorkItems++;
            registration.PerformanceState.FailedWorkItems++;
            registration.PerformanceState.AbandonedWorkItems++;
        }
        finally
        {
            ClearActiveLease();
        }
    }

    private void ClearActiveLease()
    {
        _activeRegistration = null;
        _activeStartTimestamp = 0;
        _activeExecutionKind = SuiteWorkExecutionKind.Cooperative;
    }

    private void EnsureOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "SuitePerformanceCoordinator must be used only from the thread that created it.");
        }
    }

    private void FlushFrameMetrics()
    {
        if (!_hasFrame)
        {
            return;
        }

        if (_frameHadWork)
        {
            _suiteFrameMetrics.Record(_frameElapsedMilliseconds);
        }

        foreach (var subsystem in _subsystems.Values)
        {
            if (subsystem.FrameTouched)
            {
                subsystem.FrameMetrics.Record(subsystem.CurrentFrameElapsedMilliseconds);
            }

            subsystem.CurrentFrameElapsedMilliseconds = 0.0;
            subsystem.FrameTouched = false;
        }

        for (var index = 0; index < _registrations.Count; index++)
        {
            FlushRegistrationFrame(_registrations[index].PerformanceState);
        }
    }

    private static void FlushRegistrationFrame(RegistrationState state)
    {
        if (state.FrameTouched)
        {
            state.FrameMetrics.Record(state.CurrentFrameElapsedMilliseconds);
        }

        state.CurrentFrameElapsedMilliseconds = 0.0;
        state.FrameTouched = false;
    }

    internal sealed class SubsystemState
    {
        public SubsystemState(int metricsWindow)
        {
            WorkItemMetrics = new RollingPerformanceMetrics(metricsWindow);
            FrameMetrics = new RollingPerformanceMetrics(metricsWindow);
        }

        public RollingPerformanceMetrics WorkItemMetrics { get; }

        public RollingPerformanceMetrics FrameMetrics { get; }

        public double CurrentFrameElapsedMilliseconds { get; set; }

        public bool FrameTouched { get; set; }

        public long AdmittedWorkItems { get; set; }

        public long CompletedWorkItems { get; set; }

        public long FailedWorkItems { get; set; }

        public long AbandonedWorkItems { get; set; }

        public long TotalOperations { get; set; }

        public long DeferredAdmissions { get; set; }

        public long MissedRequestExpirations { get; set; }

        public long NativeCallsStarted { get; set; }

        public long NativeMutationsStarted { get; set; }

        public long NativeLeaseAdmissions { get; set; }

        public long NativeMutationLeaseAdmissions { get; set; }

        public long NativeCallsAttempted { get; set; }

        public long NativeMutationAttempts { get; set; }

        public long NativeMutationsCommitted { get; set; }

        public long NativeHardBudgetOverruns { get; set; }
        public long MeasurementFailures { get; set; }
    }

    internal sealed class RegistrationState
    {
        private readonly long[] _deferralsByReason =
            new long[(int)SuiteWorkAdmission.NativeMutationAlreadyAdmitted + 1];
        private long _lastDeferredFrameIdentity = -1;
        private long _lastConsecutiveDeferredFrameIdentity = -1;
        private bool _starvationReported;

        public RegistrationState(int metricsWindow, int starvationThresholdFrames)
        {
            WorkItemMetrics = new RollingPerformanceMetrics(metricsWindow);
            FrameMetrics = new RollingPerformanceMetrics(metricsWindow);
            StarvationThresholdFrames = starvationThresholdFrames;
        }

        public RollingPerformanceMetrics WorkItemMetrics { get; }
        public RollingPerformanceMetrics FrameMetrics { get; }
        public int StarvationThresholdFrames { get; }
        public double CurrentFrameElapsedMilliseconds { get; set; }
        public bool FrameTouched { get; set; }
        public long AdmittedWorkItems { get; set; }
        public long CompletedWorkItems { get; set; }
        public long FailedWorkItems { get; set; }
        public long AbandonedWorkItems { get; set; }
        public long TotalOperations { get; set; }
        public long DeferredAttempts { get; private set; }
        public long DeferredFrames { get; private set; }
        public long ConsecutiveDeferrals { get; private set; }
        public long MaximumConsecutiveDeferrals { get; private set; }
        public long MaximumPendingWaitFrames { get; private set; }
        public long StarvationEvents { get; private set; }
        public long NativeLeaseAdmissions { get; set; }
        public long NativeMutationLeaseAdmissions { get; set; }
        public long NativeCallsAttempted { get; set; }
        public long NativeMutationAttempts { get; set; }
        public long NativeMutationsCommitted { get; set; }
        public long NativeHardBudgetOverruns { get; set; }
        public long MeasurementFailures { get; set; }

        public void BeginPendingWait()
        {
            ConsecutiveDeferrals = 0;
            _lastConsecutiveDeferredFrameIdentity = -1;
            _starvationReported = false;
        }

        public void ResetPendingWait()
        {
            ConsecutiveDeferrals = 0;
            _lastConsecutiveDeferredFrameIdentity = -1;
            _starvationReported = false;
        }

        public void ResetWaitAfterAdmission()
        {
            ConsecutiveDeferrals = 0;
            _lastConsecutiveDeferredFrameIdentity = -1;
            _starvationReported = false;
        }

        public void ClosePendingWait(long waitFrames)
        {
            if (waitFrames > MaximumPendingWaitFrames)
            {
                MaximumPendingWaitFrames = waitFrames;
            }

            if (!_starvationReported && waitFrames >= StarvationThresholdFrames)
            {
                _starvationReported = true;
                StarvationEvents++;
            }
        }

        public void ResetFrameIdentityDiscontinuity()
        {
            ConsecutiveDeferrals = 0;
            _lastDeferredFrameIdentity = -1;
            _lastConsecutiveDeferredFrameIdentity = -1;
            _starvationReported = false;
        }

        public void RecordDeferral(SuiteWorkAdmission reason, long frameIdentity, long waitFrames)
        {
            DeferredAttempts++;
            _deferralsByReason[(int)reason]++;

            if (_lastConsecutiveDeferredFrameIdentity != frameIdentity)
            {
                ConsecutiveDeferrals = _lastConsecutiveDeferredFrameIdentity == frameIdentity - 1
                    ? ConsecutiveDeferrals + 1
                    : 1;
                _lastConsecutiveDeferredFrameIdentity = frameIdentity;
                if (ConsecutiveDeferrals > MaximumConsecutiveDeferrals)
                {
                    MaximumConsecutiveDeferrals = ConsecutiveDeferrals;
                }
            }

            if (_lastDeferredFrameIdentity != frameIdentity)
            {
                _lastDeferredFrameIdentity = frameIdentity;
                DeferredFrames++;
            }

            if (waitFrames > MaximumPendingWaitFrames)
            {
                MaximumPendingWaitFrames = waitFrames;
            }

            if (!_starvationReported && waitFrames >= StarvationThresholdFrames)
            {
                _starvationReported = true;
                StarvationEvents++;
            }
        }

        public RegistrationPerformanceSnapshot Freeze(
            SuiteWorkRegistration registration,
            long currentPendingWaitFrames)
        {
            return new RegistrationPerformanceSnapshot(
                registration.RegistrationId,
                registration.Subsystem,
                registration.WorkName,
                registration.BudgetClass,
                registration.ExecutionKind,
                registration.IsEnabled,
                registration.IsPending,
                registration.IsDisposed,
                currentPendingWaitFrames,
                Math.Max(MaximumPendingWaitFrames, currentPendingWaitFrames),
                StarvationThresholdFrames,
                currentPendingWaitFrames >= StarvationThresholdFrames,
                StarvationEvents,
                AdmittedWorkItems,
                CompletedWorkItems,
                FailedWorkItems,
                AbandonedWorkItems,
                TotalOperations,
                DeferredAttempts,
                DeferredFrames,
                ConsecutiveDeferrals,
                MaximumConsecutiveDeferrals,
                new SuiteWorkAdmissionCountsSnapshot(_deferralsByReason),
                NativeLeaseAdmissions,
                NativeMutationLeaseAdmissions,
                NativeCallsAttempted,
                NativeMutationAttempts,
                NativeMutationsCommitted,
                NativeHardBudgetOverruns,
                MeasurementFailures,
                WorkItemMetrics.GetDistributionSnapshot(),
                FrameMetrics.GetDistributionSnapshot());
        }
    }
}

public sealed class SuiteWorkRegistration : IDisposable
{
    internal SuiteWorkRegistration(
        SuitePerformanceCoordinator coordinator,
        int registrationId,
        string subsystem,
        string workName,
        SuiteBudgetClass budgetClass,
        SuiteWorkExecutionKind executionKind,
        int schedulingWeight,
        long registeredEpoch,
        SuitePerformanceCoordinator.SubsystemState subsystemState,
        SuitePerformanceCoordinator.RegistrationState performanceState)
    {
        Coordinator = coordinator;
        RegistrationId = registrationId;
        Subsystem = subsystem;
        WorkName = workName;
        BudgetClass = budgetClass;
        ExecutionKind = executionKind;
        SchedulingWeight = schedulingWeight;
        PendingSinceEpoch = registeredEpoch;
        LastRequestEpoch = registeredEpoch;
        SubsystemState = subsystemState;
        PerformanceState = performanceState;
        Enabled = true;
    }

    internal SuitePerformanceCoordinator Coordinator { get; }

    internal SuitePerformanceCoordinator.SubsystemState SubsystemState { get; }

    internal SuitePerformanceCoordinator.RegistrationState PerformanceState { get; }

    internal bool Enabled { get; set; }

    internal bool HasPendingWork { get; set; }

    internal bool IsDisposed { get; private set; }

    internal long PendingSinceEpoch { get; set; }

    internal long PendingSinceFrameIdentity { get; set; }

    internal bool HasPendingFrameIdentity { get; set; }

    internal long LastRequestEpoch { get; set; }

    internal long LastMissedRequestExpiryEpoch { get; set; } = -1;

    internal int ConsecutiveWeightedGrants { get; set; }

    public int RegistrationId { get; }

    public string Subsystem { get; }

    public string WorkName { get; }

    public SuiteBudgetClass BudgetClass { get; }

    public SuiteWorkExecutionKind ExecutionKind { get; }

    public int SchedulingWeight { get; }

    public bool IsEnabled => Enabled && !IsDisposed;

    public bool IsPending => HasPendingWork && IsEnabled;

    public void SetEnabled(bool enabled)
    {
        Coordinator.SetEnabled(this, enabled);
    }

    public void SetPending(bool pending)
    {
        Coordinator.SetPending(this, pending);
    }

    public void Dispose()
    {
        Coordinator.Unregister(this);
    }

    internal void MarkDisposed()
    {
        Enabled = false;
        HasPendingWork = false;
        IsDisposed = true;
    }
}

public readonly struct SuiteWorkCompletion
{
    public SuiteWorkCompletion(
        int operations,
        int nativeCallsAttempted = 0,
        int nativeMutationAttempts = 0,
        int nativeMutationsCommitted = 0)
    {
        Operations = operations;
        NativeCallsAttempted = nativeCallsAttempted;
        NativeMutationAttempts = nativeMutationAttempts;
        NativeMutationsCommitted = nativeMutationsCommitted;
    }

    public int Operations { get; }
    public int NativeCallsAttempted { get; }
    public int NativeMutationAttempts { get; }
    public int NativeMutationsCommitted { get; }

    public static SuiteWorkCompletion NativeMutation(
        int attempted,
        int committed,
        int operations = 1) =>
        new(operations, attempted, attempted, committed);

    internal void Validate(SuiteWorkExecutionKind executionKind)
    {
        if (Operations < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Operations), "The operation count cannot be negative.");
        }

        if (NativeCallsAttempted < 0 || NativeMutationAttempts < 0 || NativeMutationsCommitted < 0 ||
            NativeMutationAttempts > NativeCallsAttempted ||
            NativeMutationsCommitted > NativeMutationAttempts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(NativeCallsAttempted),
                "Native outcome counts must be non-negative and committed mutations cannot exceed attempts.");
        }

        if (executionKind == SuiteWorkExecutionKind.Cooperative && NativeCallsAttempted != 0)
        {
            throw new InvalidOperationException("Cooperative work cannot report non-preemptible native calls.");
        }

        if (executionKind != SuiteWorkExecutionKind.NonPreemptibleNativeMutation && NativeMutationAttempts != 0)
        {
            throw new InvalidOperationException("Only a native-mutation lease can report mutation outcomes.");
        }
    }
}

public readonly struct SuiteWorkLease : IDisposable
{
    private readonly SuitePerformanceCoordinator? _coordinator;
    private readonly long _token;

    internal SuiteWorkLease(SuitePerformanceCoordinator coordinator, long token)
    {
        _coordinator = coordinator;
        _token = token;
    }

    public bool IsGranted => _coordinator is not null;

    /// <summary>
    /// Records successful completion. Call this before leaving a using scope;
    /// disposing a still-active lease records failed work instead.
    /// </summary>
    public void Complete(int operations = 1)
    {
        _coordinator?.CompleteLease(_token, new SuiteWorkCompletion(operations));
    }

    public void Complete(SuiteWorkCompletion completion)
    {
        _coordinator?.CompleteLease(_token, completion);
    }

    /// <summary>
    /// Records caller failure and releases the active coordinator slot. Call this
    /// from a catch/finally path when the admitted work did not finish normally.
    /// </summary>
    public void Fail()
    {
        _coordinator?.FailLease(_token);
    }

    public void Fail(SuiteWorkCompletion completion)
    {
        _coordinator?.FailLease(_token, completion);
    }

    public void Dispose()
    {
        Fail();
    }
}

public readonly struct SuiteCoordinatorSnapshot
{
    public SuiteCoordinatorSnapshot(
        bool hasFrame,
        long frameIdentity,
        double frameElapsedMilliseconds,
        double softBudgetMilliseconds,
        double hardBudgetMilliseconds,
        bool hardBudgetExceeded,
        bool nativeMutationAdmitted,
        RollingPerformanceSnapshot frameTiming)
    {
        HasFrame = hasFrame;
        FrameIdentity = frameIdentity;
        FrameElapsedMilliseconds = frameElapsedMilliseconds;
        SoftBudgetMilliseconds = softBudgetMilliseconds;
        HardBudgetMilliseconds = hardBudgetMilliseconds;
        HardBudgetExceeded = hardBudgetExceeded;
        NativeMutationAdmitted = nativeMutationAdmitted;
        FrameTiming = frameTiming;
    }

    public bool HasFrame { get; }

    public long FrameIdentity { get; }

    public double FrameElapsedMilliseconds { get; }

    public double SoftBudgetMilliseconds { get; }

    public double HardBudgetMilliseconds { get; }

    public bool HardBudgetExceeded { get; }

    public bool NativeMutationAdmitted { get; }

    /// <summary>
    /// Rolling timing for frames in which coordinator work was active. Idle
    /// frames are intentionally not inserted as zero-duration samples.
    /// </summary>
    public RollingPerformanceSnapshot FrameTiming { get; }
}

public readonly struct SubsystemPerformanceSnapshot
{
    public SubsystemPerformanceSnapshot(
        double currentFrameElapsedMilliseconds,
        long admittedWorkItems,
        long completedWorkItems,
        long failedWorkItems,
        long abandonedWorkItems,
        long totalOperations,
        long deferredAdmissions,
        long missedRequestExpirations,
        long nativeCallsStarted,
        long nativeMutationsStarted,
        long nativeHardBudgetOverruns,
        long measurementFailures,
        RollingPerformanceSnapshot workItemTiming,
        RollingPerformanceSnapshot frameTiming,
        long nativeLeaseAdmissions = 0,
        long nativeMutationLeaseAdmissions = 0,
        long nativeCallsAttempted = 0,
        long nativeMutationAttempts = 0,
        long nativeMutationsCommitted = 0)
    {
        CurrentFrameElapsedMilliseconds = currentFrameElapsedMilliseconds;
        AdmittedWorkItems = admittedWorkItems;
        CompletedWorkItems = completedWorkItems;
        FailedWorkItems = failedWorkItems;
        AbandonedWorkItems = abandonedWorkItems;
        TotalOperations = totalOperations;
        DeferredAdmissions = deferredAdmissions;
        MissedRequestExpirations = missedRequestExpirations;
        NativeCallsStarted = nativeCallsStarted;
        NativeMutationsStarted = nativeMutationsStarted;
        NativeHardBudgetOverruns = nativeHardBudgetOverruns;
        MeasurementFailures = measurementFailures;
        WorkItemTiming = workItemTiming;
        FrameTiming = frameTiming;
        NativeLeaseAdmissions = nativeLeaseAdmissions;
        NativeMutationLeaseAdmissions = nativeMutationLeaseAdmissions;
        NativeCallsAttempted = nativeCallsAttempted;
        NativeMutationAttempts = nativeMutationAttempts;
        NativeMutationsCommitted = nativeMutationsCommitted;
    }

    public double CurrentFrameElapsedMilliseconds { get; }

    public long AdmittedWorkItems { get; }

    public long CompletedWorkItems { get; }

    public long FailedWorkItems { get; }

    public long AbandonedWorkItems { get; }

    public long TotalOperations { get; }

    public long DeferredAdmissions { get; }

    public long MissedRequestExpirations { get; }

    /// <summary>
    /// Compatibility counter for admitted non-cooperative native leases. Use
    /// <see cref="NativeCallsAttempted"/> for actual audited invocations.
    /// </summary>
    public long NativeCallsStarted { get; }

    /// <summary>
    /// Compatibility counter for admitted mutation leases. Use
    /// <see cref="NativeMutationAttempts"/> for attempts that reached the
    /// audited native boundary.
    /// </summary>
    public long NativeMutationsStarted { get; }

    public long NativeLeaseAdmissions { get; }

    public long NativeMutationLeaseAdmissions { get; }

    public long NativeCallsAttempted { get; }

    public long NativeMutationAttempts { get; }

    public long NativeMutationsCommitted { get; }

    public long NativeHardBudgetOverruns { get; }

    public long MeasurementFailures { get; }

    public RollingPerformanceSnapshot WorkItemTiming { get; }

    /// <summary>
    /// Rolling timing for active-work frames for this subsystem. Idle frames are
    /// intentionally omitted rather than reported as zero-duration work.
    /// </summary>
    public RollingPerformanceSnapshot FrameTiming { get; }
}

public readonly struct SuiteWorkAdmissionCountsSnapshot
{
    internal SuiteWorkAdmissionCountsSnapshot(long[] counts)
    {
        WorkInProgress = counts[(int)SuiteWorkAdmission.WorkInProgress];
        WaitingForTurn = counts[(int)SuiteWorkAdmission.WaitingForTurn];
        SoftBudgetExhausted = counts[(int)SuiteWorkAdmission.SoftBudgetExhausted];
        HardBudgetExhausted = counts[(int)SuiteWorkAdmission.HardBudgetExhausted];
        NativeMutationAlreadyAdmitted = counts[(int)SuiteWorkAdmission.NativeMutationAlreadyAdmitted];
    }

    public long WorkInProgress { get; }
    public long WaitingForTurn { get; }
    public long SoftBudgetExhausted { get; }
    public long HardBudgetExhausted { get; }
    public long NativeMutationAlreadyAdmitted { get; }
}

public readonly struct RegistrationPerformanceSnapshot
{
    public RegistrationPerformanceSnapshot(
        int registrationId,
        string subsystem,
        string workName,
        SuiteBudgetClass budgetClass,
        SuiteWorkExecutionKind executionKind,
        bool isEnabled,
        bool isPending,
        bool isDisposed,
        long currentPendingWaitFrames,
        long maximumPendingWaitFrames,
        int starvationThresholdFrames,
        bool isStarved,
        long starvationEvents,
        long admittedWorkItems,
        long completedWorkItems,
        long failedWorkItems,
        long abandonedWorkItems,
        long totalOperations,
        long deferredAttempts,
        long deferredFrames,
        long consecutiveDeferrals,
        long maximumConsecutiveDeferrals,
        SuiteWorkAdmissionCountsSnapshot deferralsByReason,
        long nativeLeaseAdmissions,
        long nativeMutationLeaseAdmissions,
        long nativeCallsAttempted,
        long nativeMutationAttempts,
        long nativeMutationsCommitted,
        long nativeHardBudgetOverruns,
        long measurementFailures,
        RollingPerformanceDistributionSnapshot workItemTiming,
        RollingPerformanceDistributionSnapshot frameTiming)
    {
        RegistrationId = registrationId;
        Subsystem = subsystem;
        WorkName = workName;
        BudgetClass = budgetClass;
        ExecutionKind = executionKind;
        IsEnabled = isEnabled;
        IsPending = isPending;
        IsDisposed = isDisposed;
        CurrentPendingWaitFrames = currentPendingWaitFrames;
        MaximumPendingWaitFrames = maximumPendingWaitFrames;
        StarvationThresholdFrames = starvationThresholdFrames;
        IsStarved = isStarved;
        StarvationEvents = starvationEvents;
        AdmittedWorkItems = admittedWorkItems;
        CompletedWorkItems = completedWorkItems;
        FailedWorkItems = failedWorkItems;
        AbandonedWorkItems = abandonedWorkItems;
        TotalOperations = totalOperations;
        DeferredAttempts = deferredAttempts;
        DeferredFrames = deferredFrames;
        ConsecutiveDeferrals = consecutiveDeferrals;
        MaximumConsecutiveDeferrals = maximumConsecutiveDeferrals;
        DeferralsByReason = deferralsByReason;
        NativeLeaseAdmissions = nativeLeaseAdmissions;
        NativeMutationLeaseAdmissions = nativeMutationLeaseAdmissions;
        NativeCallsAttempted = nativeCallsAttempted;
        NativeMutationAttempts = nativeMutationAttempts;
        NativeMutationsCommitted = nativeMutationsCommitted;
        NativeHardBudgetOverruns = nativeHardBudgetOverruns;
        MeasurementFailures = measurementFailures;
        WorkItemTiming = workItemTiming;
        FrameTiming = frameTiming;
    }

    public int RegistrationId { get; }
    public string Subsystem { get; }
    public string WorkName { get; }
    public SuiteBudgetClass BudgetClass { get; }
    public SuiteWorkExecutionKind ExecutionKind { get; }
    public bool IsEnabled { get; }
    public bool IsPending { get; }
    public bool IsDisposed { get; }
    public long CurrentPendingWaitFrames { get; }
    public long MaximumPendingWaitFrames { get; }
    public int StarvationThresholdFrames { get; }
    public bool IsStarved { get; }
    public long StarvationEvents { get; }
    public long AdmittedWorkItems { get; }
    public long CompletedWorkItems { get; }
    public long FailedWorkItems { get; }
    public long AbandonedWorkItems { get; }
    public long TotalOperations { get; }
    public long DeferredAttempts { get; }
    public long DeferredFrames { get; }
    public long ConsecutiveDeferrals { get; }
    public long MaximumConsecutiveDeferrals { get; }
    public long ConsecutiveDeferredFrames => ConsecutiveDeferrals;
    public long MaximumConsecutiveDeferredFrames => MaximumConsecutiveDeferrals;
    public SuiteWorkAdmissionCountsSnapshot DeferralsByReason { get; }
    public long NativeLeaseAdmissions { get; }
    public long NativeMutationLeaseAdmissions { get; }
    public long NativeCallsAttempted { get; }
    public long NativeMutationAttempts { get; }
    public long NativeMutationsCommitted { get; }
    public long NativeHardBudgetOverruns { get; }
    public long MeasurementFailures { get; }
    public RollingPerformanceDistributionSnapshot WorkItemTiming { get; }
    public RollingPerformanceDistributionSnapshot FrameTiming { get; }
}
