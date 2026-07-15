using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;

namespace OrbMentor;

internal enum MentorDomain { Spells, Artifacts, Alchemy }

internal sealed class MentorRuntime
{
    private sealed class DomainState
    {
        public readonly MentorEngine Engine = new();
        public MentorAmount FrameXp;
        public long NextDistributionTimestamp;
    }

    private const BindingFlags AllFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly MentorConfig _config;
    private readonly ManualLogSource _log;
    private readonly Dictionary<MentorDomain, DomainState> _domains = Enum.GetValues(typeof(MentorDomain)).Cast<MentorDomain>().ToDictionary(d => d, _ => new DomainState());
    private readonly Dictionary<MentorDomain, MentorRecipe[]> _catalogCache = new();
    private static readonly long ContinuousDistributionTicks = Math.Max(1, Stopwatch.Frequency / 4);
    private bool _guarded;
    private int _nextDomain;
    private string? _blockedReason;
    private object? _activeArtifact;

    public MentorRuntime(MentorConfig config, ManualLogSource log) { _config = config; _log = log; }
    public string? BlockedReason => _blockedReason;
    public bool IsBlocked => _blockedReason is not null;

    public string StatusText()
    {
        var parts = new List<string> { $"Spells {_config.SharePercent.Value:0.##}%" };
        parts.Add(_config.ArtifactsEnabled.Value ? $"Artifacts {_config.ArtifactSharePercent.Value:0.##}%" : "Artifacts off");
        parts.Add(_config.AlchemyEnabled.Value ? $"Alchemy {_config.AlchemySharePercent.Value:0.##}%" : "Alchemy off");
        var warning = _config.EconomyMode.Value == MentorEconomyMode.PerRecipient ? " Warning: total bonus scales with recipient count." : string.Empty;
        return $"{_config.EconomyMode.Value}. {string.Join("; ", parts)}.{warning}";
    }

    public void Observe(SpellRecipeSO source, BigDouble xp) => ObserveDomain(MentorDomain.Spells, source, xp);
    public void ObserveAlchemy(object source, BigDouble xp)
    {
        if (_config.AlchemyEnabled.Value) ObserveDomain(MentorDomain.Alchemy, source, xp);
    }

    public void BeginArtifactTick(object source) => _activeArtifact = _guarded ? null : source;
    public void EndArtifactTick() => _activeArtifact = null;
    public void ObserveExperienceContainer(object container, BigDouble xp)
    {
        if (!_config.ArtifactsEnabled.Value || _activeArtifact is null || _guarded) return;
        var owned = Invoke(_activeArtifact, "GetExperienceElement");
        if (owned is not null && ReferenceEquals(owned, container)) ObserveDomain(MentorDomain.Artifacts, _activeArtifact, xp);
    }

    private void ObserveDomain(MentorDomain domain, object source, BigDouble xp)
    {
        if (_guarded || !_config.Active || !TryAmount(xp, out var amount)) return;
        if (!TryCatalog(domain, out var catalog)) return;
        var sourceId = StableId(source);
        if (sourceId is null) { Block($"{domain} source has no stable UUID"); return; }
        if (_domains[domain].Engine.EligibleRecipients(sourceId, catalog).Count == 0) return;
        _domains[domain].FrameXp = _domains[domain].FrameXp.Add(amount);
#if DEBUG
        if (_config.DevelopmentProbeEnabled) _log.LogInfo($"Mentor {domain} probe: source={sourceId} xp={amount.Mantissa}e{amount.Exponent}");
#endif
    }

    public void LateTick()
    {
        if (!_config.Active || IsBlocked) { Cancel(); return; }
        PlanDomain(MentorDomain.Spells, _config.SharePercent.Value, continuous: false);
        if (_config.ArtifactsEnabled.Value) PlanDomain(MentorDomain.Artifacts, _config.ArtifactSharePercent.Value, continuous: true); else CancelDomain(MentorDomain.Artifacts);
        if (_config.AlchemyEnabled.Value) PlanDomain(MentorDomain.Alchemy, _config.AlchemySharePercent.Value, continuous: true); else CancelDomain(MentorDomain.Alchemy);

        var timer = Stopwatch.StartNew();
        var domains = new[] { MentorDomain.Spells, MentorDomain.Artifacts, MentorDomain.Alchemy };
        var operation = 0;
        var emptyChecks = 0;
        while (operation < _config.OperationsPerFrame.Value && timer.Elapsed.TotalMilliseconds < _config.CpuBudgetMilliseconds.Value && emptyChecks < domains.Length)
        {
            var domain = domains[_nextDomain++ % domains.Length];
            var grants = _domains[domain].Engine.Take(1);
            if (grants.Count == 0)
            {
                emptyChecks++;
                continue;
            }
            emptyChecks = 0;
            operation++;
            Grant(domain, grants[0]);
        }
        _catalogCache.Clear();
    }

    private void PlanDomain(MentorDomain domain, double percent, bool continuous)
    {
        var state = _domains[domain];
        if (!state.FrameXp.IsValidPositive) return;
        var now = Stopwatch.GetTimestamp();
        if (continuous && !DistributionDue(now, ref state.NextDistributionTimestamp, ContinuousDistributionTicks)) return;
        if (TryCatalog(domain, out var catalog))
        {
            var highest = catalog.Where(r => r.IsDiscovered).Select(r => r.MasteryLevel).DefaultIfEmpty().Max();
            var recipients = catalog.Where(r => r.IsDiscovered && r.MasteryLevel < highest).OrderBy(r => r.Uuid, StringComparer.Ordinal).ToArray();
            var grants = state.Engine.Plan(state.FrameXp, percent, _config.EconomyMode.Value, recipients);
            state.Engine.Consolidate(grants);
            if (_config.DetailedLogging.Value && grants.Count > 0)
            {
                var mentors = catalog.Where(r => r.IsDiscovered && r.MasteryLevel == highest)
                    .Select(r => Resolve(domain, r.Uuid)).Where(r => r is not null).Select(r => SafeName(r!)).Take(6);
                _log.LogInfo($"Mentor {domain} batch: catalog={catalog.Length}, available={catalog.Count(r => r.IsDiscovered)}, highest={highest}, mentors={string.Join(", ", mentors)}, recipients={grants.Count}, share={percent:0.##}%");
            }
        }
        state.FrameXp = default;
    }

    private void Grant(MentorDomain domain, MentorGrant grant)
    {
        var recipient = Resolve(domain, grant.Uuid);
        if (recipient is null || !IsDiscovered(domain, recipient)) return;
        if (!TryCatalog(domain, out var catalog)) return;
        var highest = catalog.Where(r => r.IsDiscovered).Select(r => r.MasteryLevel).DefaultIfEmpty().Max();
        if (ReadInt(recipient, MasteryField(domain)) >= highest) return;
        try
        {
            _guarded = true;
            var value = new BigDouble(grant.Amount.Mantissa, grant.Amount.Exponent);
            if (domain == MentorDomain.Spells) ((SpellRecipeSO)recipient).GainMasteryExp(value);
            else if (domain == MentorDomain.Alchemy) InvokeRequired(recipient, "GainMasteryXp", value);
            else GrantArtifact(recipient, value);
            if (_config.DetailedLogging.Value) _log.LogInfo($"Mentor {domain} grant: recipient={SafeName(recipient)} ({grant.Uuid}), mastery={ReadInt(recipient, MasteryField(domain))}, amount={grant.Amount.Mantissa}e{grant.Amount.Exponent}");
        }
        catch (Exception ex) { Block($"{domain} native mastery grant failed: {ex.GetBaseException().Message}"); }
        finally { _guarded = false; }
    }

    internal static void GrantArtifact(object equipment, BigDouble xp)
    {
        var container = Invoke(equipment, "GetExperienceElement") ?? throw new MissingMemberException("artifact experience container unavailable");
        InvokeRequired(container, "GainExperience", xp);
        var gained = Convert.ToInt32(InvokeRequired(container, "GetGainedLevels"));
        if (gained > 0) InvokeRequired(equipment, "GainMasteryLevels", gained);
        var current = InvokeRequired(container, "GetExperience");
        var savedXp = FindField(equipment.GetType(), "masteryXp") ?? throw new MissingMemberException("artifact saved mastery XP field unavailable");
        savedXp.SetValue(equipment, current);
    }

    public void Cancel() { foreach (var domain in _domains.Keys.ToArray()) CancelDomain(domain); _activeArtifact = null; _catalogCache.Clear(); }
    private void CancelDomain(MentorDomain domain) { _domains[domain].FrameXp = default; _domains[domain].NextDistributionTimestamp = 0; _domains[domain].Engine.Cancel(); _catalogCache.Remove(domain); }
    public void ClearBlock() => _blockedReason = null;
    private void Block(string reason) { if (_blockedReason == reason) return; _blockedReason = reason; Cancel(); _log.LogError($"Orb Mentor blocked: {reason}"); }

    private bool TryCatalog(MentorDomain domain, out MentorRecipe[] catalog)
    {
        if (_catalogCache.TryGetValue(domain, out catalog!)) return true;
        var typeName = domain switch { MentorDomain.Spells => "SpellRecipeSO", MentorDomain.Artifacts => "EquipmentSO", _ => "AlchemyRecipeSO" };
        var type = Type.GetType(typeName + ", Assembly-CSharp", false);
        var list = type is null ? null : FindField(type, "All")?.GetValue(null) as IEnumerable;
        if (list is null) { catalog = Array.Empty<MentorRecipe>(); Block($"{typeName}.All is unavailable"); return false; }
        var result = new List<MentorRecipe>();
        foreach (var item in list.Cast<object>().Where(x => x is not null))
        {
            var id = StableId(item);
            if (id is null) { catalog = Array.Empty<MentorRecipe>(); Block($"registered {domain} item has no stable UUID"); return false; }
            result.Add(new MentorRecipe(id, ReadInt(item, MasteryField(domain)), IsDiscovered(domain, item)));
        }
        catalog = result.ToArray();
        _catalogCache[domain] = catalog;
        return true;
    }

    private static object? Resolve(MentorDomain domain, string id)
    {
        var typeName = domain switch { MentorDomain.Spells => "SpellRecipeSO", MentorDomain.Artifacts => "EquipmentSO", _ => "AlchemyRecipeSO" };
        var type = Type.GetType(typeName + ", Assembly-CSharp", false);
        var list = type is null ? null : FindField(type, "All")?.GetValue(null) as IEnumerable;
        return list?.Cast<object>().FirstOrDefault(item => string.Equals(StableId(item), id, StringComparison.Ordinal));
    }

    private static string MasteryField(MentorDomain domain) => domain == MentorDomain.Spells ? "masteryLevel" : "masteryLevel";
    private static bool IsDiscovered(MentorDomain domain, object item) => Convert.ToBoolean(Invoke(item, AvailabilityMethod(domain)) ?? false);
    internal static string AvailabilityMethod(MentorDomain domain) => domain switch
    {
        MentorDomain.Artifacts => "IsCreated",
        MentorDomain.Alchemy => "IsAvailable",
        _ => "IsDiscovered",
    };
    private static int ReadInt(object item, string name) => Convert.ToInt32(FindField(item.GetType(), name)?.GetValue(item) ?? 0);
    private static string SafeName(object item)
    {
        try { return Invoke(item, "GetName")?.ToString() ?? "<unnamed>"; }
        catch { return "<unavailable>"; }
    }
    private static object? Invoke(object instance, string name, params object[] args) => FindMethod(instance.GetType(), name, args.Length)?.Invoke(instance, args);
    private static object InvokeRequired(object instance, string name, params object[] args) => Invoke(instance, name, args) ?? (FindMethod(instance.GetType(), name, args.Length)?.ReturnType == typeof(void) ? new object() : throw new MissingMemberException(name));
    private static MethodInfo? FindMethod(Type type, string name, int count) { for (var t = type; t is not null; t = t.BaseType) { var m = t.GetMethods(AllFlags | BindingFlags.DeclaredOnly).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == count); if (m is not null) return m; } return null; }
    private static FieldInfo? FindField(Type type, string name) { for (var t = type; t is not null; t = t.BaseType) { var f = t.GetField(name, AllFlags | BindingFlags.DeclaredOnly); if (f is not null) return f; } return null; }

    private static bool TryAmount(BigDouble value, out MentorAmount amount)
    {
        var boxed = (object)value; var type = boxed.GetType();
        var mantissa = type.GetField("mantissa", AllFlags)?.GetValue(boxed); var exponent = type.GetField("exponent", AllFlags)?.GetValue(boxed);
        if (mantissa is not double m || exponent is not long e) { amount = default; return false; }
        amount = new MentorAmount(m, e); return amount.IsValidPositive;
    }

    internal static string? StableId(object instance)
    {
        for (var type = instance.GetType(); type is not null; type = type.BaseType)
        {
            foreach (var name in new[] { "uuid", "UUID", "Uuid", "guid", "Guid", "id", "ID" }) { var value = type.GetField(name, AllFlags | BindingFlags.DeclaredOnly)?.GetValue(instance); if (!string.IsNullOrWhiteSpace(value?.ToString())) return value!.ToString(); }
            foreach (var name in new[] { "GetUuid", "GetUUID", "GetGuid", "GetId" }) { var value = type.GetMethod(name, AllFlags | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null)?.Invoke(instance, Array.Empty<object>()); if (!string.IsNullOrWhiteSpace(value?.ToString())) return value!.ToString(); }
        }
        return null;
    }

    internal static bool DistributionDue(long now, ref long next, long interval)
    {
        if (now < next) return false;
        next = now + Math.Max(1, interval);
        return true;
    }
}
