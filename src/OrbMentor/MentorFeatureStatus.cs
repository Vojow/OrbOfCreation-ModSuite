using System;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbMentor;

internal enum MentorFeatureFailureKind
{
    None,
    Permanent,
    Transient,
}

internal readonly struct MentorDomainFeatureStatusInput
{
    public MentorDomainFeatureStatusInput(
        bool parentConfigured,
        bool domainConfigured,
        bool emergencyDisabled,
        MentorFeatureFailureKind globalFailure,
        string? globalFailureReason,
        MentorFeatureFailureKind domainFailure,
        string? domainFailureReason,
        MentorDomainUnlockSnapshot unlock,
        bool catalogInitialized,
        long lifecycleGeneration)
    {
        ParentConfigured = parentConfigured;
        DomainConfigured = domainConfigured;
        EmergencyDisabled = emergencyDisabled;
        GlobalFailure = globalFailure;
        GlobalFailureReason = globalFailureReason;
        DomainFailure = domainFailure;
        DomainFailureReason = domainFailureReason;
        Unlock = unlock;
        CatalogInitialized = catalogInitialized;
        LifecycleGeneration = lifecycleGeneration;
    }

    public bool ParentConfigured { get; }
    public bool DomainConfigured { get; }
    public bool EmergencyDisabled { get; }
    public MentorFeatureFailureKind GlobalFailure { get; }
    public string? GlobalFailureReason { get; }
    public MentorFeatureFailureKind DomainFailure { get; }
    public string? DomainFailureReason { get; }
    public MentorDomainUnlockSnapshot Unlock { get; }
    public bool CatalogInitialized { get; }
    public long LifecycleGeneration { get; }
}

internal static class MentorFeatureStatus
{
    internal const string RootFeatureId = "Mentor";
    internal const string SpellsFeatureId = "Spells";
    internal const string ArtifactsFeatureId = "Artifacts";
    internal const string AlchemyFeatureId = "Alchemy";

    public static FeatureStatusSnapshot ProjectDomain(
        MentorDomain domain,
        in MentorDomainFeatureStatusInput input)
    {
        var key = Key(domain);
        var name = DisplayName(domain);
        if (!input.DomainConfigured)
            return Disabled(key, name, FeatureStatusReasonCode.ConfigurationDisabled,
                $"{DomainLabel(domain)} sharing is disabled in configuration", input.LifecycleGeneration);
        if (!input.ParentConfigured)
            return Blocked(key, name, FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ParentFeatureDisabled,
                "Orb Mentor is disabled in configuration", input.LifecycleGeneration);
        if (input.EmergencyDisabled)
            return Blocked(key, name, FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled, "emergency disable is active", input.LifecycleGeneration);
        if (input.GlobalFailure != MentorFeatureFailureKind.None)
            return Failure(key, name, input.GlobalFailure, input.GlobalFailureReason, input.LifecycleGeneration);
        if (input.DomainFailure != MentorFeatureFailureKind.None)
            return Failure(key, name, input.DomainFailure, input.DomainFailureReason, input.LifecycleGeneration);
        if (!input.Unlock.IsUnlocked)
        {
            var reasonCode = input.Unlock.StatusReasonCode == FeatureStatusReasonCode.None
                ? FeatureStatusReasonCode.Initializing
                : input.Unlock.StatusReasonCode;
            var state = reasonCode == FeatureStatusReasonCode.ProgressionLocked
                ? FeatureStatusState.Locked
                : input.Unlock.IsContractBlocked
                    ? FeatureStatusState.ContractUnavailable
                    : FeatureStatusState.NotReady;
            return Blocked(key, name, state, reasonCode, input.Unlock.Reason, input.LifecycleGeneration);
        }
        if (!input.CatalogInitialized)
            return Blocked(key, name, FeatureStatusState.NotReady,
                FeatureStatusReasonCode.Initializing, $"{DomainLabel(domain)} catalog is initializing", input.LifecycleGeneration);
        return Operational(key, name, input.LifecycleGeneration);
    }

    public static FeatureStatusSnapshot ProjectRoot(
        bool configured,
        bool emergencyDisabled,
        MentorFeatureFailureKind globalFailure,
        string? globalFailureReason,
        IReadOnlyList<FeatureStatusSnapshot> domains,
        long lifecycleGeneration)
    {
        var key = new FeatureStatusKey(PluginIds.MentorGuid, RootFeatureId);
        if (!configured)
            return Disabled(key, "Orb Mentor", FeatureStatusReasonCode.ConfigurationDisabled,
                "Mentor mode is disabled in configuration", lifecycleGeneration);
        if (emergencyDisabled)
            return Blocked(key, "Orb Mentor", FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled, "emergency disable is active", lifecycleGeneration);
        if (globalFailure != MentorFeatureFailureKind.None)
            return Failure(key, "Orb Mentor", globalFailure, globalFailureReason, lifecycleGeneration);

        FeatureStatusSnapshot? firstContractFailure = null;
        FeatureStatusSnapshot? firstWaiting = null;
        var operational = 0;
        for (var index = 0; index < domains.Count; index++)
        {
            var domain = domains[index];
            if (!domain.ConfiguredEnabled) continue;
            if (domain.State == FeatureStatusState.Operational) operational++;
            else if (domain.State is FeatureStatusState.ContractUnavailable or FeatureStatusState.Faulted)
                firstContractFailure ??= domain;
            else
                firstWaiting ??= domain;
        }
        if (firstContractFailure is null && firstWaiting is null)
            return Operational(key, "Orb Mentor", lifecycleGeneration);
        if (operational > 0 && firstContractFailure is not null)
            return Blocked(key, "Orb Mentor", FeatureStatusState.Degraded,
                FeatureStatusReasonCode.PartialCapabilityUnavailable,
                $"{firstContractFailure.Value.DisplayName} is {FeatureStatusPresenter.Label(firstContractFailure.Value.State).ToLowerInvariant()}",
                lifecycleGeneration);
        if (operational > 0) return Operational(key, "Orb Mentor", lifecycleGeneration);

        if (firstContractFailure is null)
            return Blocked(key, "Orb Mentor", FeatureStatusState.NotReady,
                firstWaiting!.Value.Reason.Code, firstWaiting.Value.Reason.Summary, lifecycleGeneration);
        var unavailable = firstContractFailure.Value;
        return Blocked(key, "Orb Mentor", unavailable.State, unavailable.Reason.Code,
            unavailable.Reason.Summary, lifecycleGeneration);
    }

    private static FeatureStatusSnapshot Failure(
        FeatureStatusKey key,
        string name,
        MentorFeatureFailureKind kind,
        string? reason,
        long generation) => kind == MentorFeatureFailureKind.Permanent
        ? Blocked(key, name, FeatureStatusState.ContractUnavailable,
            FeatureStatusReasonCode.ContractUnavailable, reason ?? "required native contract is unavailable", generation)
        : Blocked(key, name, FeatureStatusState.Faulted,
            FeatureStatusReasonCode.NativeMutationFailed, reason ?? "native runtime operation failed", generation);

    private static FeatureStatusSnapshot Operational(FeatureStatusKey key, string name, long generation) =>
        new(key, name, true, FeatureStatusState.Operational, lifecycleGeneration: generation);

    private static FeatureStatusSnapshot Disabled(
        FeatureStatusKey key,
        string name,
        FeatureStatusReasonCode code,
        string summary,
        long generation) =>
        new(key, name, false, FeatureStatusState.ConfigurationDisabled,
            new FeatureStatusReason(code, summary), generation);

    private static FeatureStatusSnapshot Blocked(
        FeatureStatusKey key,
        string name,
        FeatureStatusState state,
        FeatureStatusReasonCode code,
        string summary,
        long generation) =>
        new(key, name, true, state, new FeatureStatusReason(code, summary), generation);

    private static FeatureStatusKey Key(MentorDomain domain) =>
        new(PluginIds.MentorGuid, domain switch
        {
            MentorDomain.Spells => SpellsFeatureId,
            MentorDomain.Artifacts => ArtifactsFeatureId,
            _ => AlchemyFeatureId,
        });

    private static string DisplayName(MentorDomain domain) => "Mentor " + DomainLabel(domain);

    private static string DomainLabel(MentorDomain domain) => domain switch
    {
        MentorDomain.Spells => "spells",
        MentorDomain.Artifacts => "artifacts",
        _ => "alchemy",
    };
}
