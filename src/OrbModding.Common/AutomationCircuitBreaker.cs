using System;

namespace OrbModding.Common;

/// <summary>
/// The bounded scheduling state for one automation candidate or domain.
/// Values are append-only because diagnostics and replay fixtures persist them.
/// </summary>
public enum AutomationCircuitState
{
    Healthy = 0,
    RetryAfterTime = 1,
    RetryAfterLifecycle = 2,
    QuarantinedUntilConfigChange = 3,
    ContractFailed = 4,
}

public readonly struct AutomationCircuitSnapshot
{
    internal AutomationCircuitSnapshot(
        AutomationCircuitState state,
        AutomationDecisionCode cause,
        AutomationRetryTrigger wakeTriggers,
        int consecutiveFailures,
        long retryAt,
        long lifecycleGeneration)
    {
        State = state;
        Cause = cause;
        WakeTriggers = wakeTriggers;
        ConsecutiveFailures = consecutiveFailures;
        RetryAtTimestamp = retryAt;
        OpenedLifecycleGeneration = lifecycleGeneration;
    }

    public AutomationCircuitState State { get; }
    public AutomationDecisionCode Cause { get; }
    public AutomationRetryTrigger WakeTriggers { get; }
    public int ConsecutiveFailures { get; }
    public long RetryAtTimestamp { get; }
    public long OpenedLifecycleGeneration { get; }
    public bool IsOpen => State != AutomationCircuitState.Healthy;
    public bool IsAttemptDue(long timestamp) =>
        State == AutomationCircuitState.Healthy ||
        State == AutomationCircuitState.RetryAfterTime && timestamp >= RetryAtTimestamp;
}

/// <summary>
/// Allocation-free circuit state machine. Owners store one instance per existing
/// bounded candidate or fixed domain; this type deliberately owns no registry,
/// worker, timer, logging, or native-game access.
/// </summary>
public sealed class AutomationCircuitBreaker
{
    public const int MaximumConsecutiveFailures = 16;
    public const int MaximumBackoffExponent = 6;

    private readonly long _initialBackoffTicks;
    private readonly long _maximumBackoffTicks;
    private readonly int _maximumFailureCount;
    private AutomationCircuitState _state;
    private AutomationDecisionCode _cause;
    private AutomationRetryTrigger _wakeTriggers;
    private int _consecutiveFailures;
    private long _retryAt;
    private long _lifecycleGeneration;

    public AutomationCircuitBreaker(
        long initialBackoffTicks = 1,
        long maximumBackoffTicks = 64,
        int maximumFailureCount = MaximumConsecutiveFailures)
    {
        if (initialBackoffTicks <= 0) throw new ArgumentOutOfRangeException(nameof(initialBackoffTicks));
        if (maximumBackoffTicks < initialBackoffTicks) throw new ArgumentOutOfRangeException(nameof(maximumBackoffTicks));
        if (maximumFailureCount <= 0 || maximumFailureCount > MaximumConsecutiveFailures)
            throw new ArgumentOutOfRangeException(nameof(maximumFailureCount));
        _initialBackoffTicks = initialBackoffTicks;
        _maximumBackoffTicks = maximumBackoffTicks;
        _maximumFailureCount = maximumFailureCount;
    }

    public AutomationCircuitState State => _state;
    public bool IsOpen => _state != AutomationCircuitState.Healthy;
    public bool CanAttempt => _state == AutomationCircuitState.Healthy;

    public AutomationCircuitSnapshot Snapshot => new(
        _state,
        _cause,
        _wakeTriggers,
        _consecutiveFailures,
        _retryAt,
        _lifecycleGeneration);

    public bool CanAttemptAt(long timestamp)
    {
        if (_state == AutomationCircuitState.Healthy) return true;
        if (_state != AutomationCircuitState.RetryAfterTime || timestamp < _retryAt) return false;
        return Close(resetFailures: false);
    }

    public long RetryAfterTime(
        AutomationDecisionCode cause,
        AutomationRetryTrigger authoritativeWakeTriggers,
        long timestamp,
        long lifecycleGeneration)
    {
        if (_state == AutomationCircuitState.ContractFailed ||
            _state == AutomationCircuitState.RetryAfterLifecycle ||
            _state == AutomationCircuitState.QuarantinedUntilConfigChange)
            return _retryAt;

        _consecutiveFailures = Math.Min(_maximumFailureCount, _consecutiveFailures + 1);
        var exponent = Math.Min(MaximumBackoffExponent, _consecutiveFailures - 1);
        var multiplier = 1L << exponent;
        var delay = _initialBackoffTicks > _maximumBackoffTicks / multiplier
            ? _maximumBackoffTicks
            : Math.Min(_maximumBackoffTicks, _initialBackoffTicks * multiplier);
        _retryAt = timestamp > long.MaxValue - delay ? long.MaxValue : timestamp + delay;
        _state = AutomationCircuitState.RetryAfterTime;
        _cause = cause;
        _wakeTriggers = authoritativeWakeTriggers;
        _lifecycleGeneration = lifecycleGeneration;
        return _retryAt;
    }

    public void RetryAfterLifecycle(AutomationDecisionCode cause, long lifecycleGeneration)
    {
        if (_state == AutomationCircuitState.ContractFailed) return;
        _state = AutomationCircuitState.RetryAfterLifecycle;
        _cause = cause;
        _wakeTriggers = AutomationRetryTrigger.Lifecycle;
        _lifecycleGeneration = lifecycleGeneration;
        _retryAt = 0;
        _consecutiveFailures = Math.Min(_maximumFailureCount, _consecutiveFailures + 1);
    }

    public void QuarantineUntilConfiguration(AutomationDecisionCode cause, long lifecycleGeneration)
    {
        if (_state == AutomationCircuitState.ContractFailed || _state == AutomationCircuitState.RetryAfterLifecycle)
            return;
        _state = AutomationCircuitState.QuarantinedUntilConfigChange;
        _cause = cause;
        _wakeTriggers = AutomationRetryTrigger.Configuration;
        _lifecycleGeneration = lifecycleGeneration;
        _retryAt = 0;
        _consecutiveFailures = Math.Min(_maximumFailureCount, _consecutiveFailures + 1);
    }

    public void FailContract(AutomationDecisionCode cause, long lifecycleGeneration)
    {
        _state = AutomationCircuitState.ContractFailed;
        _cause = cause;
        _wakeTriggers = AutomationRetryTrigger.None;
        _lifecycleGeneration = lifecycleGeneration;
        _retryAt = 0;
        _consecutiveFailures = Math.Min(_maximumFailureCount, _consecutiveFailures + 1);
    }

    public bool Wake(
        AutomationRetryTrigger trigger,
        long timestamp,
        long lifecycleGeneration)
    {
        if (_state == AutomationCircuitState.RetryAfterTime && timestamp >= _retryAt)
            return Close(resetFailures: false);
        if ((_wakeTriggers & trigger) == 0) return false;
        if ((trigger & AutomationRetryTrigger.Lifecycle) != 0 &&
            lifecycleGeneration <= _lifecycleGeneration)
            return false;
        return Close(resetFailures: false);
    }

    public void RecordSuccess()
    {
        if (_state == AutomationCircuitState.ContractFailed) return;
        Close(resetFailures: true);
    }

    private bool Close(bool resetFailures)
    {
        _state = AutomationCircuitState.Healthy;
        _cause = AutomationDecisionCode.None;
        _wakeTriggers = AutomationRetryTrigger.None;
        _retryAt = 0;
        if (resetFailures) _consecutiveFailures = 0;
        return true;
    }
}
