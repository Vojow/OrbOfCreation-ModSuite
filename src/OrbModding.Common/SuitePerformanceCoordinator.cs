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
/// Common assembly. Work is admitted in round-robin registration order. The
/// coordinator does not own gameplay work and never invokes Unity APIs itself.
/// </summary>
public sealed class SuitePerformanceCoordinator
{
    private const int DefaultMetricsWindow = 300;
    private const int DefaultMissedRequestFrames = 2;
    private readonly IPerformanceClock _clock;
    private readonly int _ownerThreadId;
    private readonly List<SuiteWorkRegistration> _registrations = new();
    private readonly Dictionary<string, SubsystemState> _subsystems =
        new(StringComparer.Ordinal);
    private readonly RollingPerformanceMetrics _suiteFrameMetrics;
    private readonly int _metricsWindow;
    private readonly int _missedRequestFrames;
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
        int missedRequestFrames = DefaultMissedRequestFrames)
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

        _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        SoftBudgetMilliseconds = softBudgetMilliseconds;
        HardBudgetMilliseconds = hardBudgetMilliseconds;
        _metricsWindow = metricsWindow;
        _missedRequestFrames = missedRequestFrames;
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
            _frameEpoch,
            subsystemState);
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

        RecoverAbandonedLease();

        FlushFrameMetrics();
        _frameIdentity = frameIdentity;
        _frameEpoch++;
        _hasFrame = true;
        _frameHadWork = false;
        _frameElapsedMilliseconds = 0.0;
        _nativeMutationAdmitted = false;
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
            registration.SubsystemState.DeferredAdmissions++;
            return SuiteWorkAdmission.WorkInProgress;
        }

        if (_frameElapsedMilliseconds >= HardBudgetMilliseconds)
        {
            registration.SubsystemState.DeferredAdmissions++;
            return SuiteWorkAdmission.HardBudgetExhausted;
        }

        if (registration.BudgetClass == SuiteBudgetClass.SoftLimited &&
            _frameElapsedMilliseconds >= SoftBudgetMilliseconds)
        {
            registration.SubsystemState.DeferredAdmissions++;
            return SuiteWorkAdmission.SoftBudgetExhausted;
        }

        if (registration.ExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNativeMutation &&
            _nativeMutationAdmitted)
        {
            registration.SubsystemState.DeferredAdmissions++;
            return SuiteWorkAdmission.NativeMutationAlreadyAdmitted;
        }

        var nextIndex = FindNextAdmissibleRegistration(registration);
        if (nextIndex < 0 || !ReferenceEquals(_registrations[nextIndex], registration))
        {
            registration.SubsystemState.DeferredAdmissions++;
            return SuiteWorkAdmission.WaitingForTurn;
        }

        long startTimestamp;
        try
        {
            startTimestamp = _clock.GetTimestamp();
        }
        catch
        {
            registration.SubsystemState.MeasurementFailures++;
            throw;
        }

        _roundRobinIndex = (nextIndex + 1) % _registrations.Count;
        _activeRegistration = registration;
        _activeStartTimestamp = startTimestamp;
        _activeExecutionKind = registration.ExecutionKind;
        _activeLeaseToken++;
        registration.SubsystemState.AdmittedWorkItems++;
        if (registration.ExecutionKind != SuiteWorkExecutionKind.Cooperative)
        {
            registration.SubsystemState.NativeCallsStarted++;
        }

        if (registration.ExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNativeMutation)
        {
            _nativeMutationAdmitted = true;
            registration.SubsystemState.NativeMutationsStarted++;
        }

        lease = new SuiteWorkLease(this, _activeLeaseToken);
        return SuiteWorkAdmission.Granted;
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
                state.FrameMetrics.GetSnapshot(percentile));
            return true;
        }

        snapshot = default;
        return false;
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
            registration.HasPendingWork = false;
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

    internal void CompleteLease(long token, int operations)
    {
        EnsureOwnerThread();
        if (_activeRegistration is null || token != _activeLeaseToken)
        {
            return;
        }

        var registration = _activeRegistration;
        try
        {
            if (operations < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(operations),
                    "The operation count cannot be negative.");
            }

            double elapsed;
            try
            {
                elapsed = MeasureActiveLease();
            }
            catch
            {
                registration.SubsystemState.MeasurementFailures++;
                throw;
            }

            AccountActiveLease(registration, elapsed, operations);
            registration.SubsystemState.CompletedWorkItems++;
        }
        catch
        {
            registration.SubsystemState.FailedWorkItems++;
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

    internal void FailLease(long token)
    {
        EnsureOwnerThread();
        if (_activeRegistration is null || token != _activeLeaseToken)
        {
            return;
        }

        var registration = _activeRegistration;
        try
        {
            try
            {
                var elapsed = MeasureActiveLease();
                AccountActiveLease(registration, elapsed, 0);
            }
            catch
            {
                registration.SubsystemState.MeasurementFailures++;
            }

            registration.SubsystemState.FailedWorkItems++;
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
        int operations)
    {
        var beforeCompletion = _frameElapsedMilliseconds;
        _frameElapsedMilliseconds += elapsed;
        _frameHadWork = true;

        var subsystem = registration.SubsystemState;
        subsystem.CurrentFrameElapsedMilliseconds += elapsed;
        subsystem.FrameTouched = true;
        subsystem.TotalOperations += operations;
        subsystem.WorkItemMetrics.Record(elapsed, operations);

        if (_activeExecutionKind != SuiteWorkExecutionKind.Cooperative &&
            beforeCompletion < HardBudgetMilliseconds &&
            _frameElapsedMilliseconds > HardBudgetMilliseconds)
        {
            subsystem.NativeHardBudgetOverruns++;
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
                AccountActiveLease(registration, elapsed, 0);
            }
            catch
            {
                registration.SubsystemState.MeasurementFailures++;
            }

            registration.SubsystemState.FailedWorkItems++;
            registration.SubsystemState.AbandonedWorkItems++;
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

        public long NativeHardBudgetOverruns { get; set; }

        public long MeasurementFailures { get; set; }
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
        long registeredEpoch,
        SuitePerformanceCoordinator.SubsystemState subsystemState)
    {
        Coordinator = coordinator;
        RegistrationId = registrationId;
        Subsystem = subsystem;
        WorkName = workName;
        BudgetClass = budgetClass;
        ExecutionKind = executionKind;
        PendingSinceEpoch = registeredEpoch;
        LastRequestEpoch = registeredEpoch;
        SubsystemState = subsystemState;
        Enabled = true;
    }

    internal SuitePerformanceCoordinator Coordinator { get; }

    internal SuitePerformanceCoordinator.SubsystemState SubsystemState { get; }

    internal bool Enabled { get; set; }

    internal bool HasPendingWork { get; set; }

    internal bool IsDisposed { get; private set; }

    internal long PendingSinceEpoch { get; set; }

    internal long LastRequestEpoch { get; set; }

    internal long LastMissedRequestExpiryEpoch { get; set; } = -1;

    public int RegistrationId { get; }

    public string Subsystem { get; }

    public string WorkName { get; }

    public SuiteBudgetClass BudgetClass { get; }

    public SuiteWorkExecutionKind ExecutionKind { get; }

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
        _coordinator?.CompleteLease(_token, operations);
    }

    /// <summary>
    /// Records caller failure and releases the active coordinator slot. Call this
    /// from a catch/finally path when the admitted work did not finish normally.
    /// </summary>
    public void Fail()
    {
        _coordinator?.FailLease(_token);
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
        RollingPerformanceSnapshot frameTiming)
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
    }

    public double CurrentFrameElapsedMilliseconds { get; }

    public long AdmittedWorkItems { get; }

    public long CompletedWorkItems { get; }

    public long FailedWorkItems { get; }

    public long AbandonedWorkItems { get; }

    public long TotalOperations { get; }

    public long DeferredAdmissions { get; }

    public long MissedRequestExpirations { get; }

    public long NativeCallsStarted { get; }

    public long NativeMutationsStarted { get; }

    public long NativeHardBudgetOverruns { get; }

    public long MeasurementFailures { get; }

    public RollingPerformanceSnapshot WorkItemTiming { get; }

    /// <summary>
    /// Rolling timing for active-work frames for this subsystem. Idle frames are
    /// intentionally omitted rather than reported as zero-duration work.
    /// </summary>
    public RollingPerformanceSnapshot FrameTiming { get; }
}
