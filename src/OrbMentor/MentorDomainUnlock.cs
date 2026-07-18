using System;
using System.Collections;
using System.Reflection;

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
    internal const string MasteriesEnabledUuid = "07dfae7e-76b9-4b38-bf81-38abc40b9ed7";
    internal const string SpellbookUuid = "ca934900-0253-4f71-93e9-733fb91132b7";
    internal const string ArtifactWorkshopUuid = "668a2a7a-468f-4e0e-b182-979b12a4b0ad";
    internal const string AlchemyScreenUuid = "3ae45ec0-4449-4903-b3d0-b5182e03dca3";

    private static readonly Guid MasteriesEnabledId = new(MasteriesEnabledUuid);
    private static readonly Guid SpellbookId = new(SpellbookUuid);
    private static readonly Guid ArtifactWorkshopId = new(ArtifactWorkshopUuid);
    private static readonly Guid AlchemyScreenId = new(AlchemyScreenUuid);

    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Func<string, Type?> _resolveType;
    private Type? _viewType;
    private FieldInfo? _runtimeLookup;
    private MethodInfo? _isAvailable;
    private string? _schemaFailure;

    public MentorDomainUnlockGate(Func<string, Type?>? resolveType = null)
    {
        _resolveType = resolveType ?? (name => Type.GetType(name + ", Assembly-CSharp", false));
    }

    public MentorDomainUnlockSnapshot Evaluate(MentorDomain domain)
    {
        if (!ResolveSchema()) return Blocked(_schemaFailure!);

        IDictionary? lookup;
        try { lookup = _runtimeLookup!.GetValue(null) as IDictionary; }
        catch (Exception ex) { return Blocked($"native progression registry read failed: {BaseMessage(ex)}"); }
        if (lookup is null) return Waiting("native progression registry is not ready");

        var domainId = DomainId(domain);
        if (!lookup.Contains(MasteriesEnabledId)) return Waiting("mastery progression has not registered yet");
        if (!lookup.Contains(domainId)) return Waiting($"{DomainLabel(domain)} progression has not registered yet");

        var masteryView = lookup[MasteriesEnabledId];
        var domainView = lookup[domainId];
        if (masteryView is null || masteryView.GetType() != _viewType)
            return Blocked("MasteriesEnabled UUID/type contract does not resolve to ViewSO");
        if (domainView is null || domainView.GetType() != _viewType)
            return Blocked($"{DomainAssetLabel(domain)} UUID/type contract does not resolve to ViewSO");

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
        var viewType = _resolveType("ViewSO");
        var idType = _resolveType("IdScriptableObject");
        if (viewType is null || idType is null)
        {
            _schemaFailure = "native ViewSO/IdScriptableObject progression types are unavailable";
            return false;
        }
        var lookup = idType.GetField("RuntimeLookup", StaticFlags);
        var available = viewType.GetMethod("IsAvailable", InstanceFlags, null, Type.EmptyTypes, null);
        if (lookup is null || available is null || available.ReturnType != typeof(bool))
        {
            _schemaFailure = "native ViewSO.IsAvailable/IdScriptableObject.RuntimeLookup contract is unavailable";
            return false;
        }
        _viewType = viewType;
        _runtimeLookup = lookup;
        _isAvailable = available;
        return true;
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
        MentorDomain.Spells => "MagicSpellbook",
        MentorDomain.Artifacts => "WorkshopArtifact",
        _ => "ScreenAlchemy",
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
