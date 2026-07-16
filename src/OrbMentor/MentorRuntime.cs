using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;

namespace OrbMentor;

internal enum MentorDomain { Spells, Artifacts, Alchemy }

internal sealed class MentorRuntime
{
    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();
        public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }

    private sealed class NativeEntry
    {
        public NativeEntry(string uuid, object item, string displayName, FieldInfo masteryField, MethodInfo availabilityMethod)
        {
            Uuid = uuid;
            Item = item;
            DisplayName = displayName;
            MasteryField = masteryField;
            AvailabilityMethod = availabilityMethod;
        }

        public string Uuid { get; }
        public object Item { get; }
        public string DisplayName { get; }
        public FieldInfo MasteryField { get; }
        public MethodInfo AvailabilityMethod { get; }
        public int MasteryLevel { get; set; }
        public bool IsDiscovered { get; set; }
    }

    private sealed class DomainCatalog
    {
        public readonly List<NativeEntry> Entries = new();
        public readonly Dictionary<string, NativeEntry> ById = new(StringComparer.Ordinal);
        public readonly Dictionary<object, NativeEntry> ByObject = new(ReferenceComparer.Instance);
        public readonly HashSet<string> MentorIds = new(StringComparer.Ordinal);
        public MentorRecipe[] Recipients = Array.Empty<MentorRecipe>();
        public long NextLiveRefresh;
        public long NextReconcile;
        public int HighestMastery = int.MinValue;
        public bool Initialized;
    }

    private sealed class DomainState
    {
        public readonly MentorEngine Engine = new();
        public readonly MentorSourceAccumulator Sources = new();
        public MentorPlan? ActivePlan;
        public long NextDistributionTimestamp;
        public long NextSummaryTimestamp;
        public string MentorSummary = "None";
        public bool PreferPlanStep = true;
    }

    private const BindingFlags AllFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly MentorDomain[] DomainOrder = { MentorDomain.Spells, MentorDomain.Artifacts, MentorDomain.Alchemy };
    private static readonly string[] IdentityFields = { "uuid", "UUID", "Uuid", "guid", "Guid", "id", "ID" };
    private static readonly string[] IdentityMethods = { "GetUuid", "GetUUID", "GetGuid", "GetId" };
    private static readonly FieldInfo? BigDoubleMantissa = typeof(BigDouble).GetField("mantissa", AllFlags);
    private static readonly FieldInfo? BigDoubleExponent = typeof(BigDouble).GetField("exponent", AllFlags);
    private static readonly long ContinuousDistributionTicks = Math.Max(1, Stopwatch.Frequency / 4);
    private static readonly long SummaryRefreshTicks = Math.Max(1, Stopwatch.Frequency);
    private static readonly long LiveRefreshTicks = Math.Max(1, Stopwatch.Frequency);
    private static readonly long ReconcileTicks = Math.Max(1, Stopwatch.Frequency * 10);

    private readonly MentorConfig _config;
    private readonly ManualLogSource _log;
    private readonly Dictionary<MentorDomain, DomainState> _domains = new();
    private readonly Dictionary<MentorDomain, DomainCatalog> _catalogs = new();
    private readonly Dictionary<object, object> _artifactContainers = new(ReferenceComparer.Instance);
    private bool _guarded;
    private bool _artifactWasEnabled;
    private bool _alchemyWasEnabled;
    private int _nextDomain;
    private string? _blockedReason;
    private object? _activeArtifact;
    private object? _activeArtifactContainer;

    public MentorRuntime(MentorConfig config, ManualLogSource log)
    {
        _config = config;
        _log = log;
        foreach (var domain in DomainOrder)
        {
            _domains.Add(domain, new DomainState());
            _catalogs.Add(domain, new DomainCatalog());
        }

        _artifactWasEnabled = config.ArtifactsEnabled.Value;
        _alchemyWasEnabled = config.AlchemyEnabled.Value;
        if (BigDoubleMantissa is null || BigDoubleExponent is null)
            Block("BigDouble mantissa/exponent contract is unavailable");
    }

    public string? BlockedReason => _blockedReason;
    public bool IsBlocked => _blockedReason is not null;

    public string CurrentMentor(MentorDomain domain)
    {
        var state = _domains[domain];
        if (!_config.Active || !DomainEnabled(domain)) return state.MentorSummary = "Inactive";
        var now = Stopwatch.GetTimestamp();
        if (now < state.NextSummaryTimestamp) return state.MentorSummary;
        state.NextSummaryTimestamp = now + SummaryRefreshTicks;
        var catalog = _catalogs[domain];
        if (!catalog.Initialized) return state.MentorSummary = "Pending";
        if (catalog.MentorIds.Count == 0) return state.MentorSummary = "None";

        var names = new List<string>(Math.Min(3, catalog.MentorIds.Count));
        foreach (var entry in catalog.Entries)
        {
            if (!catalog.MentorIds.Contains(entry.Uuid)) continue;
            if (names.Count < 3) names.Add(entry.DisplayName);
        }

        return state.MentorSummary = FormatMentorSummary(names, catalog.HighestMastery, catalog.MentorIds.Count);
    }

    internal static string FormatMentorSummary(IReadOnlyList<string> names, int mastery, int total)
    {
        if (names.Count == 0) return "None";
        var shown = Math.Min(3, names.Count);
        var summary = names[0];
        for (var index = 1; index < shown; index++) summary += ", " + names[index];
        var extra = Math.Max(0, total - shown);
        return $"{summary}{(extra > 0 ? $" +{extra}" : string.Empty)} (Lv {mastery})";
    }

    public string StatusText()
    {
        var artifact = _config.ArtifactsEnabled.Value ? $"Artifacts {_config.ArtifactSharePercent.Value:0.##}%" : "Artifacts off";
        var alchemy = _config.AlchemyEnabled.Value ? $"Alchemy {_config.AlchemySharePercent.Value:0.##}%" : "Alchemy off";
        var warning = _config.EconomyMode.Value == MentorEconomyMode.PerRecipient ? " Warning: total bonus scales with recipient count." : string.Empty;
        return $"{_config.EconomyMode.Value}. Spells {_config.SharePercent.Value:0.##}%; {artifact}; {alchemy}.{warning}";
    }

    public void Observe(SpellRecipeSO source, BigDouble xp) => ObserveDomain(MentorDomain.Spells, source, xp);

    public void ObserveAlchemy(object source, BigDouble xp)
    {
        if (_config.AlchemyEnabled.Value) ObserveDomain(MentorDomain.Alchemy, source, xp);
    }

    public void BeginArtifactTick(object source)
    {
        _activeArtifact = null;
        _activeArtifactContainer = null;
        if (_guarded || !_config.Active || !_config.ArtifactsEnabled.Value) return;
        _activeArtifact = source;
        try
        {
            if (!_artifactContainers.TryGetValue(source, out _activeArtifactContainer))
            {
                _activeArtifactContainer = Invoke(source, "GetExperienceElement");
                if (_activeArtifactContainer is not null) _artifactContainers[source] = _activeArtifactContainer;
            }
        }
        catch (Exception ex)
        {
            Block($"Artifacts experience-container lookup failed: {ex.GetBaseException().Message}");
        }
    }

    public void EndArtifactTick()
    {
        _activeArtifact = null;
        _activeArtifactContainer = null;
    }

    public void ObserveExperienceContainer(object container, BigDouble xp)
    {
        if (!_config.ArtifactsEnabled.Value || _activeArtifact is null || _guarded) return;
        if (_activeArtifactContainer is not null && ReferenceEquals(_activeArtifactContainer, container))
            ObserveDomain(MentorDomain.Artifacts, _activeArtifact, xp);
    }

    private void ObserveDomain(MentorDomain domain, object source, BigDouble xp)
    {
        if (_guarded || !_config.Active || !TryAmount(xp, out var amount)) return;
        var now = Stopwatch.GetTimestamp();
        var catalog = _catalogs[domain];
        // LateTick owns periodic live refresh and reconciliation. The capture
        // hook only performs the initial build (or a missing-source recovery),
        // so high-frequency XP never triggers a timed full-catalog scan.
        if (!catalog.Initialized && !EnsureCatalog(domain, now, forceReconcile: false)) return;
        if (!catalog.ByObject.TryGetValue(source, out var entry))
        {
            if (!EnsureCatalog(domain, now, forceReconcile: true) || !catalog.ByObject.TryGetValue(source, out entry))
            {
                Block($"{domain} source is not present in its native registry");
                return;
            }
        }

        // Refreshing only a changed source is unsafe because another recipe may
        // have become the new highest mentor. The common high-frequency path is
        // O(1); mastery/discovery transitions trigger one complete live refresh.
        if (!TryRefreshEntry(domain, catalog, entry, now)) return;
        var qualifiesAtEvent = entry.IsDiscovered &&
                               entry.MasteryLevel == catalog.HighestMastery &&
                               catalog.Recipients.Length > 0;
        _domains[domain].Sources.Capture(entry.Uuid, amount, qualifiesAtEvent);
        if (!qualifiesAtEvent) return;
#if DEBUG
        if (_config.DevelopmentProbeEnabled)
            _log.LogInfo($"Mentor {domain} probe: source={entry.Uuid} xp={amount.Mantissa}e{amount.Exponent}");
#endif
    }

    public void LateTick()
    {
        if (!_config.Active || IsBlocked) return;
        var started = Stopwatch.GetTimestamp();
        var cpuBudget = Math.Clamp(_config.CpuBudgetMilliseconds.Value, 0.1, 1.0);

        if (!EnsureCatalog(MentorDomain.Spells, started, forceReconcile: false)) return;
        if (ElapsedMilliseconds(started) >= cpuBudget) return;
        if (_config.ArtifactsEnabled.Value)
        {
            if (!EnsureCatalog(MentorDomain.Artifacts, Stopwatch.GetTimestamp(), forceReconcile: false)) return;
            if (ElapsedMilliseconds(started) >= cpuBudget) return;
        }
        else if (_artifactWasEnabled)
        {
            CancelDomain(MentorDomain.Artifacts, clearCatalog: false);
        }

        if (_config.AlchemyEnabled.Value)
        {
            if (!EnsureCatalog(MentorDomain.Alchemy, Stopwatch.GetTimestamp(), forceReconcile: false)) return;
            if (ElapsedMilliseconds(started) >= cpuBudget) return;
        }
        else if (_alchemyWasEnabled)
        {
            CancelDomain(MentorDomain.Alchemy, clearCatalog: false);
        }

        _artifactWasEnabled = _config.ArtifactsEnabled.Value;
        _alchemyWasEnabled = _config.AlchemyEnabled.Value;

        BeginPlan(MentorDomain.Spells, _config.SharePercent.Value, continuous: false);
        if (ElapsedMilliseconds(started) >= cpuBudget) return;
        if (_config.ArtifactsEnabled.Value) BeginPlan(MentorDomain.Artifacts, _config.ArtifactSharePercent.Value, continuous: true);
        if (ElapsedMilliseconds(started) >= cpuBudget) return;
        if (_config.AlchemyEnabled.Value) BeginPlan(MentorDomain.Alchemy, _config.AlchemySharePercent.Value, continuous: true);

        var operations = 0;
        var emptyChecks = 0;
        var operationLimit = Math.Max(1, _config.OperationsPerFrame.Value);
        while (operations < operationLimit && ElapsedMilliseconds(started) < cpuBudget && emptyChecks < DomainOrder.Length)
        {
            var domain = DomainOrder[_nextDomain++ % DomainOrder.Length];
            if (!DomainEnabled(domain) || !StepDomain(domain))
            {
                emptyChecks++;
                continue;
            }

            operations++;
            emptyChecks = 0;
        }
    }

    private void BeginPlan(MentorDomain domain, double percent, bool continuous)
    {
        var state = _domains[domain];
        if (state.ActivePlan is not null || !state.Sources.HasPending) return;
        var now = Stopwatch.GetTimestamp();
        if (continuous && !DistributionDue(now, ref state.NextDistributionTimestamp, ContinuousDistributionTicks)) return;

        var sourceCount = state.Sources.SourceCount;
        var total = state.Sources.Drain();
        var recipients = _catalogs[domain].Recipients;
        var plan = state.Engine.CreatePlan(total, percent, _config.EconomyMode.Value, recipients);
        state.ActivePlan = plan;
        state.PreferPlanStep = true;
        if (_config.DetailedLogging.Value && plan is not null)
        {
            _log.LogInfo($"Mentor {domain} batch: sources={sourceCount}, recipients={recipients.Length}, share={percent:0.##}%");
        }
    }

    private bool StepDomain(MentorDomain domain)
    {
        var state = _domains[domain];
        if (state.ActivePlan is not null && (state.PreferPlanStep || state.Engine.PendingCount == 0))
        {
            if (state.ActivePlan.TryTake(out var planned))
            {
                state.Engine.Consolidate(planned);
                state.PreferPlanStep = false;
                if (state.ActivePlan.RemainingCount == 0) state.ActivePlan = null;
                return true;
            }

            state.ActivePlan = null;
        }

        if (state.Engine.TryTake(out var grant))
        {
            state.PreferPlanStep = true;
            Grant(domain, grant);
            return true;
        }

        if (state.ActivePlan is not null && state.ActivePlan.TryTake(out var fallback))
        {
            state.Engine.Consolidate(fallback);
            if (state.ActivePlan.RemainingCount == 0) state.ActivePlan = null;
            return true;
        }

        return false;
    }

    private void Grant(MentorDomain domain, MentorGrant grant)
    {
        var now = Stopwatch.GetTimestamp();
        if (!EnsureCatalog(domain, now, forceReconcile: false)) return;
        var catalog = _catalogs[domain];
        if (!catalog.ById.TryGetValue(grant.Uuid, out var entry))
        {
            if (!EnsureCatalog(domain, now, forceReconcile: true) || !catalog.ById.TryGetValue(grant.Uuid, out entry))
            {
                Block($"{domain} recipient {grant.Uuid} is no longer present in its native registry");
                return;
            }
        }

        if (!TryRefreshEntry(domain, catalog, entry, now)) return;
        if (!entry.IsDiscovered || entry.MasteryLevel >= catalog.HighestMastery)
        {
            if (_config.DetailedLogging.Value)
                _log.LogInfo($"Mentor {domain} discarded an ineligible delayed recipient: {grant.Uuid}.");
            return;
        }

        try
        {
            _guarded = true;
            var value = new BigDouble(grant.Amount.Mantissa, grant.Amount.Exponent);
            if (domain == MentorDomain.Spells) ((SpellRecipeSO)entry.Item).GainMasteryExp(value);
            else if (domain == MentorDomain.Alchemy) InvokeRequired(entry.Item, "GainMasteryXp", value);
            else GrantArtifact(entry.Item, value);
            if (_config.DetailedLogging.Value)
                _log.LogInfo($"Mentor {domain} grant: recipient={SafeName(entry.Item)} ({grant.Uuid}), mastery={entry.MasteryLevel}, amount={grant.Amount.Mantissa}e{grant.Amount.Exponent}");
        }
        catch (Exception ex)
        {
            Block($"{domain} native mastery grant failed: {ex.GetBaseException().Message}");
        }
        finally
        {
            _guarded = false;
        }
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

    public void Cancel()
    {
        foreach (var domain in DomainOrder) CancelDomain(domain, clearCatalog: true);
        _activeArtifact = null;
        _activeArtifactContainer = null;
        _artifactContainers.Clear();
    }

    private void CancelDomain(MentorDomain domain, bool clearCatalog)
    {
        var state = _domains[domain];
        state.Sources.Cancel();
        state.ActivePlan = null;
        state.NextDistributionTimestamp = 0;
        state.Engine.Cancel();
        state.PreferPlanStep = true;
        if (!clearCatalog) return;
        var catalog = _catalogs[domain];
        catalog.Entries.Clear();
        catalog.ById.Clear();
        catalog.ByObject.Clear();
        catalog.MentorIds.Clear();
        catalog.Recipients = Array.Empty<MentorRecipe>();
        catalog.HighestMastery = int.MinValue;
        catalog.NextLiveRefresh = 0;
        catalog.NextReconcile = 0;
        catalog.Initialized = false;
    }

    public void ClearBlock() => _blockedReason = null;

    private void Block(string reason)
    {
        if (_blockedReason == reason) return;
        _blockedReason = reason;
        Cancel();
        _log.LogError($"Orb Mentor blocked: {reason}");
    }

    private bool EnsureCatalog(MentorDomain domain, long now, bool forceReconcile)
    {
        try
        {
            var catalog = _catalogs[domain];
            if (forceReconcile || !catalog.Initialized || now >= catalog.NextReconcile)
            {
                if (!Reconcile(domain, catalog, now)) return false;
            }
            else if (now >= catalog.NextLiveRefresh)
            {
                RefreshLive(catalog, now, forceRelationship: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            Block($"{domain} catalog refresh failed: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private bool TryRefreshEntry(MentorDomain domain, DomainCatalog catalog, NativeEntry entry, long now)
    {
        try
        {
            if (RefreshEntry(entry)) RefreshLive(catalog, now, forceRelationship: true);
            return true;
        }
        catch (Exception ex)
        {
            Block($"{domain} live state refresh failed: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private bool Reconcile(MentorDomain domain, DomainCatalog catalog, long now)
    {
        var typeName = NativeTypeName(domain);
        var type = Type.GetType(typeName + ", Assembly-CSharp", false);
        var registry = type is null ? null : FindField(type, "All")?.GetValue(null) as IEnumerable;
        if (registry is null)
        {
            Block($"{typeName}.All is unavailable");
            return false;
        }

        var entries = new List<NativeEntry>();
        var byId = new Dictionary<string, NativeEntry>(StringComparer.Ordinal);
        foreach (var value in registry)
        {
            if (value is null) continue;
            var id = StableId(value);
            if (id is null)
            {
                Block($"registered {domain} item has no stable UUID");
                return false;
            }

            if (byId.ContainsKey(id))
            {
                Block($"registered {domain} UUID is duplicated: {id}");
                return false;
            }

            NativeEntry entry;
            if (catalog.ById.TryGetValue(id, out var existing) && ReferenceEquals(existing.Item, value))
            {
                entry = existing;
            }
            else
            {
                var mastery = FindField(value.GetType(), MasteryField(domain));
                var availability = FindMethod(value.GetType(), AvailabilityMethod(domain), 0);
                if (mastery is null || availability is null)
                {
                    Block($"{domain} mastery or availability contract is unavailable");
                    return false;
                }

                entry = new NativeEntry(id, value, SafeName(value), mastery, availability);
            }

            entries.Add(entry);
            byId.Add(id, entry);
        }

        entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.Uuid, right.Uuid));
        catalog.Entries.Clear();
        catalog.Entries.AddRange(entries);
        catalog.ById.Clear();
        catalog.ByObject.Clear();
        foreach (var entry in entries)
        {
            catalog.ById.Add(entry.Uuid, entry);
            catalog.ByObject.Add(entry.Item, entry);
        }

        catalog.Initialized = true;
        catalog.NextReconcile = now + ReconcileTicks;
        RefreshLive(catalog, now, forceRelationship: true);
        return true;
    }

    private static void RefreshLive(DomainCatalog catalog, long now, bool forceRelationship)
    {
        var changed = false;
        foreach (var entry in catalog.Entries) changed |= RefreshEntry(entry);
        if (forceRelationship || changed) RebuildRelationship(catalog);
        catalog.NextLiveRefresh = now + LiveRefreshTicks;
    }

    private static bool RefreshEntry(NativeEntry entry)
    {
        var mastery = Convert.ToInt32(entry.MasteryField.GetValue(entry.Item) ?? 0);
        var discovered = Convert.ToBoolean(entry.AvailabilityMethod.Invoke(entry.Item, null) ?? false);
        if (entry.MasteryLevel == mastery && entry.IsDiscovered == discovered) return false;
        entry.MasteryLevel = mastery;
        entry.IsDiscovered = discovered;
        return true;
    }

    private static void RebuildRelationship(DomainCatalog catalog)
    {
        var highest = int.MinValue;
        foreach (var entry in catalog.Entries)
        {
            if (entry.IsDiscovered && entry.MasteryLevel > highest) highest = entry.MasteryLevel;
        }

        catalog.HighestMastery = highest;
        catalog.MentorIds.Clear();
        var recipients = new List<MentorRecipe>();
        foreach (var entry in catalog.Entries)
        {
            if (!entry.IsDiscovered) continue;
            if (entry.MasteryLevel == highest) catalog.MentorIds.Add(entry.Uuid);
            else if (entry.MasteryLevel < highest) recipients.Add(new MentorRecipe(entry.Uuid, entry.MasteryLevel, true));
        }

        catalog.Recipients = recipients.ToArray();
    }

    private bool DomainEnabled(MentorDomain domain) => domain switch
    {
        MentorDomain.Artifacts => _config.ArtifactsEnabled.Value,
        MentorDomain.Alchemy => _config.AlchemyEnabled.Value,
        _ => true,
    };

    private static string NativeTypeName(MentorDomain domain) => domain switch
    {
        MentorDomain.Spells => "SpellRecipeSO",
        MentorDomain.Artifacts => "EquipmentSO",
        _ => "AlchemyRecipeSO",
    };

    private static string MasteryField(MentorDomain domain) => "masteryLevel";

    internal static string AvailabilityMethod(MentorDomain domain) => domain switch
    {
        MentorDomain.Artifacts => "IsCreated",
        MentorDomain.Alchemy => "IsAvailable",
        _ => "IsDiscovered",
    };

    private static string SafeName(object item)
    {
        try { return Invoke(item, "GetName")?.ToString() ?? "<unnamed>"; }
        catch { return "<unavailable>"; }
    }

    private static object? Invoke(object instance, string name, params object[] args) => FindMethod(instance.GetType(), name, args.Length)?.Invoke(instance, args);

    private static object InvokeRequired(object instance, string name, params object[] args)
    {
        var method = FindMethod(instance.GetType(), name, args.Length);
        if (method is null) throw new MissingMemberException(name);
        var result = method.Invoke(instance, args);
        return result ?? (method.ReturnType == typeof(void) ? new object() : throw new MissingMemberException(name));
    }

    private static MethodInfo? FindMethod(Type type, string name, int count)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMethods(AllFlags | BindingFlags.DeclaredOnly))
            {
                if (method.Name == name && method.GetParameters().Length == count) return method;
            }
        }

        return null;
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, AllFlags | BindingFlags.DeclaredOnly);
            if (field is not null) return field;
        }

        return null;
    }

    private static bool TryAmount(BigDouble value, out MentorAmount amount)
    {
        var boxed = (object)value;
        if (BigDoubleMantissa?.GetValue(boxed) is not double mantissa ||
            BigDoubleExponent?.GetValue(boxed) is not long exponent)
        {
            amount = default;
            return false;
        }

        amount = new MentorAmount(mantissa, exponent);
        return amount.IsValidPositive;
    }

    internal static string? StableId(object instance)
    {
        for (var type = instance.GetType(); type is not null; type = type.BaseType)
        {
            foreach (var name in IdentityFields)
            {
                var value = type.GetField(name, AllFlags | BindingFlags.DeclaredOnly)?.GetValue(instance);
                if (!string.IsNullOrWhiteSpace(value?.ToString())) return value!.ToString();
            }

            foreach (var name in IdentityMethods)
            {
                var value = type.GetMethod(name, AllFlags | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null)?.Invoke(instance, null);
                if (!string.IsNullOrWhiteSpace(value?.ToString())) return value!.ToString();
            }
        }

        return null;
    }

    internal static bool DistributionDue(long now, ref long next, long interval)
    {
        if (now < next) return false;
        next = now + Math.Max(1, interval);
        return true;
    }

    private static double ElapsedMilliseconds(long started) =>
        (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
}
