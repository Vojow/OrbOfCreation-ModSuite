using System;

namespace OrbModding.Common;

/// <summary>
/// Decides whether an unaudited assembly pair may leave quarantine.
/// </summary>
/// <remarks>
/// Persisted consent is valid only for the exact pair it recorded. During a running session, a new
/// opt-in is explicit consent for the already-observed pair and therefore records that fingerprint.
/// </remarks>
internal static class UnverifiedBuildCompatibilityPolicy
{
    internal static UnverifiedBuildCompatibilityDecision AtStartup(
        bool audited,
        string observedFingerprint,
        bool overrideRequested,
        string acceptedFingerprint)
    {
        if (audited) return UnverifiedBuildCompatibilityDecision.Audited;
        if (!IsFingerprint(observedFingerprint))
            return new(false, resetOverride: overrideRequested, acceptObserved: false, engageEmergencyStop: true);
        if (!overrideRequested)
            return new(false, resetOverride: false, acceptObserved: false, engageEmergencyStop: true);
        if (Matches(observedFingerprint, acceptedFingerprint))
            return new(true, resetOverride: false, acceptObserved: false, engageEmergencyStop: false);
        return new(false, resetOverride: true, acceptObserved: false, engageEmergencyStop: true);
    }

    internal static UnverifiedBuildCompatibilityDecision AfterExplicitChange(
        bool audited,
        string observedFingerprint,
        bool overrideRequested,
        string acceptedFingerprint)
    {
        if (audited) return UnverifiedBuildCompatibilityDecision.Audited;
        if (!overrideRequested || !IsFingerprint(observedFingerprint))
            return new(false, resetOverride: false, acceptObserved: false, engageEmergencyStop: true);
        return new(
            runtimeAllowed: true,
            resetOverride: false,
            acceptObserved: !Matches(observedFingerprint, acceptedFingerprint),
            engageEmergencyStop: false);
    }

    private static bool Matches(string observed, string accepted) =>
        string.Equals(observed, accepted, StringComparison.OrdinalIgnoreCase);

    private static bool IsFingerprint(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 129 || value[64] != ':') return false;
        for (var index = 0; index < value.Length; index++)
        {
            if (index == 64) continue;
            var character = value[index];
            if (!((character >= '0' && character <= '9') ||
                  (character >= 'A' && character <= 'F') ||
                  (character >= 'a' && character <= 'f')))
                return false;
        }
        return true;
    }
}

internal readonly struct UnverifiedBuildCompatibilityDecision
{
    internal static UnverifiedBuildCompatibilityDecision Audited { get; } =
        new(true, resetOverride: false, acceptObserved: false, engageEmergencyStop: false);

    internal UnverifiedBuildCompatibilityDecision(
        bool runtimeAllowed,
        bool resetOverride,
        bool acceptObserved,
        bool engageEmergencyStop)
    {
        RuntimeAllowed = runtimeAllowed;
        ResetOverride = resetOverride;
        AcceptObserved = acceptObserved;
        EngageEmergencyStop = engageEmergencyStop;
    }

    internal bool RuntimeAllowed { get; }
    internal bool ResetOverride { get; }
    internal bool AcceptObserved { get; }
    internal bool EngageEmergencyStop { get; }
}
