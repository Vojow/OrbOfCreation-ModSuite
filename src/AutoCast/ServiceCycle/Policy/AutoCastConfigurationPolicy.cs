using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Pure predicates over the immutable configuration that gate whether the Auto Cast service runs, how
/// often it wakes, and the two thresholds its admission ladder applies.
/// </summary>
internal static class AutoCastConfigurationPolicy
{
    /// <summary>The interval the legacy engine clamped to, kept so the setting means what it meant.</summary>
    internal const float MinimumEvaluationIntervalSeconds = 0.1f;
    internal const float MaximumEvaluationIntervalSeconds = 10.0f;
    internal const float DefaultEvaluationIntervalSeconds = 0.25f;

    /// <summary>
    /// The ceiling the legacy engine applied to the manual pause on top of the configured range.
    /// </summary>
    internal const float MaximumManualPauseSeconds = 60.0f;

    internal static bool IsOperational(SuiteRuntimeConfiguration configuration) =>
        configuration.General.Enabled &&
        configuration.CanStartAutoCastActively;

    /// <summary>
    /// How long to wait before looking again, clamped exactly as the legacy engine clamped it so an
    /// out-of-range setting cannot make the service spin or stall.
    /// </summary>
    internal static MonotonicDuration EvaluationInterval(SuiteRuntimeConfiguration configuration)
    {
        var seconds = configuration.AutoCast.EvaluationIntervalSeconds;
        if (!(seconds > 0f)) seconds = DefaultEvaluationIntervalSeconds;
        seconds = Math.Max(
            MinimumEvaluationIntervalSeconds,
            Math.Min(MaximumEvaluationIntervalSeconds, seconds));
        return MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(seconds));
    }

    /// <summary>How long a manual cast silences the service for.</summary>
    internal static MonotonicDuration ManualPause(SuiteRuntimeConfiguration configuration)
    {
        var seconds = Math.Max(
            0f, Math.Min(MaximumManualPauseSeconds, configuration.AutoCast.ManualPauseSeconds));
        return MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(seconds));
    }

    /// <summary>
    /// The share of its ceiling a resource must hold before a spell touching it may be cast, as a
    /// fraction. Clamped to <c>[0, 1]</c>, so a nonsense percentage cannot block everything forever.
    /// </summary>
    internal static double StartResourceFraction(SuiteRuntimeConfiguration configuration) =>
        Math.Max(0.0, Math.Min(100.0, configuration.AutoCast.StartResourcePercent)) / 100.0;

    internal static bool HoldsFullCharge(SuiteRuntimeConfiguration configuration) =>
        configuration.AutoCast.FullCharge;
}
