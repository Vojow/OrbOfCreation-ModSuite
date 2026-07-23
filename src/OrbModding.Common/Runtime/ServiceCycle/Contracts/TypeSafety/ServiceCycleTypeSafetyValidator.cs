using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

internal enum ServiceCycleTypeRole
{
    Frame,
    Configuration,
    State,
    Action,
    Strategy,
}

internal static class ServiceCycleTypeSafetyValidator
{
    internal static void EnsureServiceTypes<TFrame, TConfig, TState, TAction>()
    {
        var violation = ClosedServiceTypes<TFrame, TConfig, TState, TAction>.Violation;
        if (violation.HasValue) throw new InvalidOperationException(violation.Value.Message);
    }

    internal static void EnsureStrategyType<TStrategy>()
    {
        var violation = ClosedStrategyType<TStrategy>.Violation;
        if (violation.HasValue) throw new InvalidOperationException(violation.Value.Message);
    }

    private static ServiceCycleTypeViolation? ValidateServiceTypes(
        Type frame,
        Type configuration,
        Type state,
        Type action)
    {
        var violation = ServiceCycleTypeGraphWalker.Validate(frame, ServiceCycleTypeRole.Frame, frame, "frame");
        if (violation.HasValue) return violation;
        violation = ServiceCycleTypeGraphWalker.Validate(
            configuration, ServiceCycleTypeRole.Configuration, frame, "configuration");
        if (violation.HasValue) return violation;
        violation = ServiceCycleTypeGraphWalker.Validate(state, ServiceCycleTypeRole.State, frame, "state");
        if (violation.HasValue) return violation;
        return ServiceCycleTypeGraphWalker.Validate(action, ServiceCycleTypeRole.Action, frame, "action");
    }

    private static class ClosedServiceTypes<TFrame, TConfig, TState, TAction>
    {
        internal static readonly ServiceCycleTypeViolation? Violation = ValidateServiceTypes(
            typeof(TFrame), typeof(TConfig), typeof(TState), typeof(TAction));
    }

    private static class ClosedStrategyType<TStrategy>
    {
        internal static readonly ServiceCycleTypeViolation? Violation = ServiceCycleTypeGraphWalker.Validate(
            typeof(TStrategy), ServiceCycleTypeRole.Strategy, typeof(void), "strategy");
    }
}
