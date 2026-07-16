using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OrbAutomata;

internal sealed class ReflectionAutoCastCatalog : IAutoCastCatalog
{
    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public IReadOnlyList<IAutoCastCandidate> DiscoverActiveLoadout()
    {
        var manager = GetSpellManager();
        var activeSpells = manager is null ? null : ReflectionUtil.ReadMember(manager, "activeSpells");
        if (activeSpells is not IEnumerable enumerable)
        {
            return Array.Empty<IAutoCastCandidate>();
        }

        var result = new List<IAutoCastCandidate>();
        var slot = 0;
        foreach (var spell in enumerable)
        {
            if (spell is not null)
            {
                result.Add(new ReflectionAutoCastCandidate(this, spell, slot));
            }

            slot++;
        }

        return result;
    }

    public bool IsNativeCastBusy()
    {
        var type = ReflectionUtil.FindLoadedType("SpellManager");
        var method = type?.GetMethod("CanCastASpell", StaticFlags, null, Type.EmptyTypes, null);
        try
        {
            return method?.Invoke(null, Array.Empty<object>()) is not true;
        }
        catch (TargetInvocationException)
        {
            return true;
        }
    }

    public bool IsTargeting() => InvokeStaticBool("TargetingManager", "IsTargeting", fallback: true);

    public void Dispose()
    {
    }

    internal bool FireSlotAndResolveTargets(int slotIndex, out string reason)
    {
        if (IsTargeting())
        {
            reason = "a target request was already active";
            return false;
        }

        var manager = GetSpellManager();
        var fire = manager?.GetType().GetMethod(
            "FireSpellIndex",
            ReflectionUtil.InstanceFlags,
            null,
            new[] { typeof(int) },
            null);
        if (manager is null || fire is null)
        {
            reason = "SpellManager.FireSpellIndex(int) unavailable";
            return false;
        }

        try
        {
            using (AutoCastManualSignal.EnterAutomatedFire())
            {
                fire.Invoke(manager, new object[] { slotIndex });
            }

            for (var request = 0; request < 16 && IsTargeting(); request++)
            {
                var targetingType = ReflectionUtil.FindLoadedType("TargetingManager");
                var link = targetingType?.GetMethod("GetTargetingLink", StaticFlags)?.Invoke(null, Array.Empty<object>());
                var target = link is null ? null : ReflectionUtil.InvokeNoArgs(link, "GetRandom");
                var submit = targetingType?.GetMethods(StaticFlags)
                    .FirstOrDefault(method => method.Name == "SubmitTarget" && method.GetParameters().Length == 1);
                if (target is null || submit is null)
                {
                    reason = "native target selector returned no valid target";
                    return false;
                }

                submit.Invoke(null, new[] { target });
            }

            if (IsTargeting())
            {
                reason = "target request limit exceeded";
                return false;
            }

            reason = "cast submitted";
            return true;
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            reason = ex.InnerException?.Message ?? ex.Message;
            return false;
        }
    }

    private static object? GetSpellManager()
    {
        return ReflectionUtil.FindLoadedType("SpellManager")?.GetField("instance", StaticFlags)?.GetValue(null);
    }

    private static bool InvokeStaticBool(string typeName, string methodName, bool fallback)
    {
        try
        {
            return ReflectionUtil.FindLoadedType(typeName)?.GetMethod(methodName, StaticFlags)?.Invoke(null, Array.Empty<object>()) as bool? ?? fallback;
        }
        catch (TargetInvocationException)
        {
            return fallback;
        }
    }
}

internal sealed class ReflectionAutoCastCandidate : IAutoCastCandidate
{
    private const string AutoCastChargeInput = "OrbAutomata.AutoCast.FullCharge";
    private readonly ReflectionAutoCastCatalog _catalog;
    private readonly object _spell;

    public ReflectionAutoCastCandidate(ReflectionAutoCastCatalog catalog, object spell, int slotIndex)
    {
        _catalog = catalog;
        _spell = spell;
        SlotIndex = slotIndex;
    }

    public int SlotIndex { get; }

    public string DisplayName => ReflectionUtil.ReadDisplayName(_spell) ?? $"Spell slot {SlotIndex + 1}";

    public AutoCastSpellKind Kind
    {
        get
        {
            if (ReadBool("IsChanneled", fallback: false))
            {
                return AutoCastSpellKind.Channel;
            }

            return ReadBool("IsToggledSpell", fallback: false)
                ? AutoCastSpellKind.Aura
                : AutoCastSpellKind.Instant;
        }
    }

    public bool IsEmpty => ReadBool("IsEmpty", fallback: true);

    public bool IsCharged => ReadBool("CanCharge", fallback: true);

    public bool IsCasting => ReadBool("IsCasting", fallback: false);

    public bool IsReadyingCast => ReadBool("IsReadyingCast", fallback: false);

    public bool CanCast(out string reason)
    {
        if (!ReflectionUtil.TryInvokeBool(_spell, out var canCast, "CanCast"))
        {
            reason = "Spell.CanCast unavailable";
            return false;
        }

        if (canCast)
        {
            reason = "native readiness passed";
            return true;
        }

        if (ReadBool("IsAttuning", fallback: false))
        {
            reason = "attuning after a previous cast";
            return false;
        }

        if (ReflectionUtil.TryInvokeBool(_spell, out var chargeAvailable, "IsChargeAvailable") && !chargeAvailable)
        {
            var currentCharges = ReadInt("GetCurrSpellCharges");
            var maximumCharges = ReadInt("GetMaxSpellCharges");
            var cooldown = ReflectionUtil.InvokeNoArgs(_spell, "GetCooldownTimeRemaining");
            var cooldownText = BigAmount.TryRead(cooldown, out var remaining) ? remaining.ToString() : "unknown";
            reason = $"recharging: charges={currentCharges}/{maximumCharges}, cooldownRemaining={cooldownText}";
            return false;
        }

        if (ReflectionUtil.TryInvokeBool(_spell, out var enoughResources, "HasEnoughResources") && !enoughResources)
        {
            reason = "native resource availability rejected";
            return false;
        }

        reason = "native CanCast rejected for an unclassified state";
        return false;
    }

    public bool TryGetImmediateCosts(out IReadOnlyList<ResourceAdmissionCost> costs) => TryGetCosts("GetCost", out costs);

    public bool TryGetDrainCosts(out IReadOnlyList<ResourceAdmissionCost> costs) => TryGetCosts("GetDrainCost", out costs);

    public bool HasValidTargets(out string reason)
    {
        var reference = ReflectionUtil.ReadMember(_spell, "reference");
        var scaling = ReflectionUtil.InvokeNoArgs(_spell, "GetScalingInfo");
        if (reference is null || scaling is null)
        {
            reason = "spell recipe or scaling unavailable";
            return false;
        }

        foreach (var request in FindTargetRequests(reference))
        {
            var options = ReflectionUtil.ReadMember(request, "targetOptions");
            var method = options?.GetType().GetMethods(ReflectionUtil.InstanceFlags)
                .FirstOrDefault(candidate => candidate.Name == "HasValidTargetsLeft" && candidate.GetParameters().Length == 1);
            if (options is null || method is null)
            {
                reason = "target preflight contract unavailable";
                return false;
            }

            try
            {
                if (method.Invoke(options, new[] { scaling }) is not true)
                {
                    reason = "native target selector has no valid target";
                    return false;
                }
            }
            catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException)
            {
                reason = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        reason = "target preflight passed";
        return true;
    }

    public bool TryFireAndResolveTargets(out string reason) => _catalog.FireSlotAndResolveTargets(SlotIndex, out reason);

    public bool TryGetIdentity(out AutoCastCandidateIdentity identity, out string reason)
    {
        var reference = ReflectionUtil.ReadMember(_spell, "reference");
        var uuid = reference is null ? null : ReflectionUtil.ReadStableId(reference);
        if (string.IsNullOrWhiteSpace(uuid))
        {
            identity = default;
            reason = "stable spell recipe UUID unavailable";
            return false;
        }

        identity = new AutoCastCandidateIdentity(uuid, _spell, _spell.GetType(), SlotIndex);
        reason = string.Empty;
        return true;
    }

    public bool TrySetChargeHold(bool isHolding, out string reason)
    {
        var method = _spell.GetType().GetMethod(
            "SetChargeInput",
            ReflectionUtil.InstanceFlags,
            null,
            new[] { typeof(string), typeof(bool) },
            null);
        if (method is null)
        {
            reason = "Spell.SetChargeInput(string, bool) unavailable";
            return false;
        }

        try
        {
            method.Invoke(_spell, new object[] { AutoCastChargeInput, isHolding });
            reason = isHolding ? "full-charge hold started" : "full-charge hold released";
            return true;
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            reason = ex.InnerException?.Message ?? ex.Message;
            return false;
        }
    }

    private bool TryGetCosts(string methodName, out IReadOnlyList<ResourceAdmissionCost> costs)
    {
        var container = ReflectionUtil.InvokeNoArgs(_spell, methodName);
        if (container is null)
        {
            costs = Array.Empty<ResourceAdmissionCost>();
            return false;
        }

        costs = ReflectionCostReader.Read(container);
        return true;
    }

    private bool ReadBool(string methodName, bool fallback)
    {
        return ReflectionUtil.TryInvokeBool(_spell, out var value, methodName) ? value : fallback;
    }

    private int ReadInt(string methodName)
    {
        var value = ReflectionUtil.InvokeNoArgs(_spell, methodName);
        try
        {
            return value is null ? -1 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException)
        {
            return -1;
        }
    }

    private static IEnumerable<object> FindTargetRequests(object root)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return Traverse(root, 0, visited).Where(value => value.GetType().Name == "RequestTargetEffectScript");
    }

    private static IEnumerable<object> Traverse(object value, int depth, ISet<object> visited)
    {
        if (depth > 7 || value is string || !visited.Add(value))
        {
            yield break;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is null)
                {
                    continue;
                }

                foreach (var nested in Traverse(item, depth + 1, visited))
                {
                    yield return nested;
                }
            }

            yield break;
        }

        yield return value;
        foreach (var memberValue in ReadEffectMembers(value))
        {
            foreach (var nested in Traverse(memberValue, depth + 1, visited))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<object> ReadEffectMembers(object value)
    {
        var type = value.GetType();
        foreach (var field in type.GetFields(ReflectionUtil.InstanceFlags))
        {
            if (IsEffectMember(field.Name) && field.GetValue(value) is object memberValue)
            {
                yield return memberValue;
            }
        }

        foreach (var property in type.GetProperties(ReflectionUtil.InstanceFlags))
        {
            if (!IsEffectMember(property.Name) || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? memberValue = null;
            try
            {
                memberValue = property.GetValue(value);
            }
            catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
            {
            }

            if (memberValue is not null)
            {
                yield return memberValue;
            }
        }
    }

    private static bool IsEffectMember(string name)
    {
        return name.Contains("effect", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("script", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("block", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
