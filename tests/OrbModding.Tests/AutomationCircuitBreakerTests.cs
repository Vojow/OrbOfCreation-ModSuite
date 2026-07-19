using System;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomationCircuitBreakerTests
{
    [Fact]
    public void StableEnumValues_AreAppendOnly()
    {
        Assert.Equal(0, (int)AutomationCircuitState.Healthy);
        Assert.Equal(1, (int)AutomationCircuitState.RetryAfterTime);
        Assert.Equal(2, (int)AutomationCircuitState.RetryAfterLifecycle);
        Assert.Equal(3, (int)AutomationCircuitState.QuarantinedUntilConfigChange);
        Assert.Equal(4, (int)AutomationCircuitState.ContractFailed);
    }

    [Fact]
    public void TransientFailure_UsesBoundedBackoffAndTimeWake()
    {
        var circuit = new AutomationCircuitBreaker(2, 32);
        long retryAt = 0;

        for (var failure = 1; failure <= 100; failure++)
        {
            retryAt = circuit.RetryAfterTime(
                AutomationDecisionCode.NativeStateUnavailable,
                AutomationRetryTrigger.None,
                100,
                0);
        }

        Assert.Equal(132, retryAt);
        Assert.Equal(AutomationCircuitState.RetryAfterTime, circuit.State);
        Assert.Equal(AutomationCircuitBreaker.MaximumConsecutiveFailures, circuit.Snapshot.ConsecutiveFailures);
        Assert.False(circuit.CanAttemptAt(131));
        Assert.True(circuit.CanAttemptAt(132));
        Assert.True(circuit.CanAttempt);
        Assert.Equal(AutomationCircuitBreaker.MaximumConsecutiveFailures, circuit.Snapshot.ConsecutiveFailures);
        circuit.RecordSuccess();
        Assert.Equal(0, circuit.Snapshot.ConsecutiveFailures);
    }

    [Fact]
    public void TransientFailure_CanWakeFromAuthoritativeEvent()
    {
        var circuit = new AutomationCircuitBreaker();
        circuit.RetryAfterTime(
            AutomationDecisionCode.RegistryNotReady,
            AutomationRetryTrigger.Registry,
            10,
            0);

        Assert.True(circuit.Wake(AutomationRetryTrigger.Registry, 10, 0));
        Assert.True(circuit.CanAttempt);
    }

    [Fact]
    public void EarlyWakeFollowedByFailure_IncreasesBackoffUntilSuccess()
    {
        var circuit = new AutomationCircuitBreaker(2, 32);
        Assert.Equal(12, circuit.RetryAfterTime(
            AutomationDecisionCode.RegistryNotReady,
            AutomationRetryTrigger.Registry,
            10,
            0));
        Assert.True(circuit.Wake(AutomationRetryTrigger.Registry, 10, 0));
        Assert.Equal(14, circuit.RetryAfterTime(
            AutomationDecisionCode.RegistryNotReady,
            AutomationRetryTrigger.Registry,
            10,
            0));
        Assert.Equal(2, circuit.Snapshot.ConsecutiveFailures);
    }

    [Fact]
    public void MutationPostcondition_WakesOnlyFromNewerLifecycle()
    {
        var circuit = new AutomationCircuitBreaker();
        circuit.RetryAfterLifecycle(AutomationDecisionCode.PostconditionFailed, 7);

        Assert.False(circuit.CanAttemptAt(long.MaxValue));
        Assert.False(circuit.Wake(AutomationRetryTrigger.Registry, long.MaxValue, 8));
        Assert.False(circuit.Wake(AutomationRetryTrigger.Configuration, long.MaxValue, 8));
        Assert.False(circuit.Wake(AutomationRetryTrigger.Lifecycle, long.MaxValue, 7));
        Assert.True(circuit.Wake(AutomationRetryTrigger.Lifecycle, long.MaxValue, 8));
    }

    [Fact]
    public void ConfigurationQuarantine_WakesOnlyFromNewerConfiguration()
    {
        var circuit = new AutomationCircuitBreaker();
        circuit.QuarantineUntilConfiguration(AutomationDecisionCode.InvalidConfiguration, 3);

        Assert.False(circuit.Wake(AutomationRetryTrigger.Lifecycle, 0, 4));
        Assert.True(circuit.Wake(AutomationRetryTrigger.Configuration, 0, 3));
    }

    [Fact]
    public void ContractFailure_IsProcessLifetime()
    {
        var circuit = new AutomationCircuitBreaker();
        circuit.FailContract(AutomationDecisionCode.ContractUnresolved, 1);

        Assert.False(circuit.CanAttemptAt(long.MaxValue));
        Assert.False(circuit.Wake(AutomationRetryTrigger.Registry, long.MaxValue, 2));
        Assert.False(circuit.Wake(AutomationRetryTrigger.Lifecycle, long.MaxValue, 2));
        Assert.False(circuit.Wake(AutomationRetryTrigger.Configuration, long.MaxValue, 2));
        Assert.Equal(AutomationRetryTrigger.None, circuit.Snapshot.WakeTriggers);
        Assert.True(circuit.IsOpen);
    }

    [Fact]
    public void StrongCircuitState_CannotBeDowngradedByTimedFailure()
    {
        var circuit = new AutomationCircuitBreaker();
        circuit.RetryAfterLifecycle(AutomationDecisionCode.PostconditionFailed, 5);
        circuit.RetryAfterTime(
            AutomationDecisionCode.NativeStateUnavailable,
            AutomationRetryTrigger.Registry,
            10,
            5);

        Assert.Equal(AutomationCircuitState.RetryAfterLifecycle, circuit.State);
        Assert.Equal(AutomationDecisionCode.PostconditionFailed, circuit.Snapshot.Cause);

        circuit.FailContract(AutomationDecisionCode.ContractUnresolved, 5);
        circuit.RetryAfterLifecycle(AutomationDecisionCode.PostconditionFailed, 5);
        Assert.Equal(AutomationCircuitState.ContractFailed, circuit.State);
    }

    [Fact]
    public void RetryDeadline_SaturatesWithoutOverflow()
    {
        var circuit = new AutomationCircuitBreaker(8, 64);
        var retryAt = circuit.RetryAfterTime(
            AutomationDecisionCode.NativeStateUnavailable,
            AutomationRetryTrigger.None,
            long.MaxValue - 1,
            0);
        Assert.Equal(long.MaxValue, retryAt);
    }

    [Fact]
    public void InvalidBackoffBounds_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomationCircuitBreaker(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomationCircuitBreaker(2, 1));
    }
}
