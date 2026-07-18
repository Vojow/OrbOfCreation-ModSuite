using System;
using System.Reflection;
using OrbModding.Common;

namespace OrbMentor;

internal enum MentorDomainUnlockState
{
    Waiting,
    Unlocked,
    ContractBlocked,
}

internal readonly struct MentorDomainUnlockSnapshot
{
    public MentorDomainUnlockSnapshot(MentorDomainUnlockState state, string reason)
    {
        State = state;
        Reason = reason;
    }

    public MentorDomainUnlockState State { get; }
    public string Reason { get; }
    public bool IsUnlocked => State == MentorDomainUnlockState.Unlocked;
    public bool IsContractBlocked => State == MentorDomainUnlockState.ContractBlocked;
}

internal sealed class MentorDomainUnlockGate
{
    internal static readonly string MasteriesEnabledUuid = KnownEntities.MasteriesEnabled.Uuid.ToString("D");
    internal static readonly string SpellbookUuid = KnownEntities.MagicSpellbook.Uuid.ToString("D");
    internal static readonly string ArtifactWorkshopUuid = KnownEntities.WorkshopArtifact.Uuid.ToString("D");
    internal static readonly string AlchemyScreenUuid = KnownEntities.AlchemyScreen.Uuid.ToString("D");

    private static readonly Guid MasteriesEnabledId = KnownEntities.MasteriesEnabled.Uuid;
    private static readonly Guid SpellbookId = KnownEntities.MagicSpellbook.Uuid;
    private static readonly Guid ArtifactWorkshopId = KnownEntities.WorkshopArtifact.Uuid;
    private static readonly Guid AlchemyScreenId = KnownEntities.AlchemyScreen.Uuid;

    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Func<string, Type?> _resolveType;
    private readonly TypedRegistryResolver _registryResolver;
    private Type? _viewType;
    private MethodInfo? _isAvailable;
    private string? _schemaFailure;

    public MentorDomainUnlockGate(
        Func<string, Type?>? resolveType = null,
        TypedRegistryResolver? registryResolver = null)
    {
        _resolveType = resolveType ?? (name => Type.GetType(name + ", Assembly-CSharp", false));
        _registryResolver = registryResolver ?? TypedRegistryResolver.Shared;
    }

    public MentorDomainUnlockSnapshot Evaluate(MentorDomain domain)
    {
        if (!ResolveSchema()) return Blocked(_schemaFailure!);

        var domainId = DomainId(domain);
        var masteryResolution = _registryResolver.Resolve(MasteriesEnabledId, _viewType!);
        if (!masteryResolution.IsResolved)
            return FromResolution(KnownEntities.MasteriesEnabled.DiagnosticName, masteryResolution);
        var domainResolution = _registryResolver.Resolve(domainId, _viewType!);
        if (!domainResolution.IsResolved)
            return FromResolution(DomainAssetLabel(domain), domainResolution);
        var masteryView = masteryResolution.Value!;
        var domainView = domainResolution.Value!;

        try
        {
            var masteryAvailable = _isAvailable!.Invoke(masteryView, null) as bool?;
            var domainAvailable = _isAvailable.Invoke(domainView, null) as bool?;
            if (!masteryAvailable.HasValue || !domainAvailable.HasValue)
                return Blocked("ViewSO.IsAvailable did not return a boolean progression state");
            if (!masteryAvailable.Value) return Waiting("native mastery progression is locked");
            if (!domainAvailable.Value) return Waiting($"native {DomainLabel(domain)} progression is locked");
            return new MentorDomainUnlockSnapshot(MentorDomainUnlockState.Unlocked, string.Empty);
        }
        catch (Exception ex)
        {
            return Blocked($"native {DomainLabel(domain)} progression check failed: {BaseMessage(ex)}");
        }
    }

    private bool ResolveSchema()
    {
        if (_schemaFailure is not null) return false;
        if (_viewType is not null) return true;
        var viewType = _resolveType(KnownEntities.MasteriesEnabled.ManagedTypeName);
        if (viewType is null)
        {
            _schemaFailure = "native ViewSO progression type is unavailable";
            return false;
        }
        var available = viewType.GetMethod("IsAvailable", InstanceFlags, null, Type.EmptyTypes, null);
        if (available is null || available.ReturnType != typeof(bool))
        {
            _schemaFailure = "native ViewSO.IsAvailable contract is unavailable";
            return false;
        }
        _viewType = viewType;
        _isAvailable = available;
        return true;
    }

    private static MentorDomainUnlockSnapshot FromResolution(
        string label,
        TypedRegistryResolution resolution)
    {
        var reason = $"{label} resolution failed. {resolution.Format()}";
        return resolution.IsRetryable ? Waiting(reason) : Blocked(reason);
    }

    private static MentorDomainUnlockSnapshot Waiting(string reason) =>
        new(MentorDomainUnlockState.Waiting, reason);

    private static MentorDomainUnlockSnapshot Blocked(string reason) =>
        new(MentorDomainUnlockState.ContractBlocked, reason);

    private static Guid DomainId(MentorDomain domain) => domain switch
    {
        MentorDomain.Spells => SpellbookId,
        MentorDomain.Artifacts => ArtifactWorkshopId,
        _ => AlchemyScreenId,
    };

    private static string DomainAssetLabel(MentorDomain domain) => domain switch
    {
        MentorDomain.Spells => KnownEntities.MagicSpellbook.DiagnosticName,
        MentorDomain.Artifacts => KnownEntities.WorkshopArtifact.DiagnosticName,
        _ => KnownEntities.AlchemyScreen.DiagnosticName,
    };

    private static string DomainLabel(MentorDomain domain) => domain switch
    {
        MentorDomain.Spells => "spell",
        MentorDomain.Artifacts => "artifact",
        _ => "alchemy",
    };

    private static string BaseMessage(Exception exception) =>
        exception.GetBaseException().Message;
}
