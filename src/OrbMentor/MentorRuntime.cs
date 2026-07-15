using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;

namespace OrbMentor;

internal sealed class MentorRuntime
{
    private readonly MentorConfig _config;
    private readonly ManualLogSource _log;
    private readonly MentorEngine _engine = new();
    private MentorAmount _frameXp;
    private bool _guarded;
    private string? _blockedReason;

    public MentorRuntime(MentorConfig config, ManualLogSource log) { _config = config; _log = log; }
    public string? BlockedReason => _blockedReason;
    public bool IsBlocked => _blockedReason is not null;
    public string StatusText()
    {
        if (!TryCatalog(out var catalog)) return _blockedReason ?? "catalog unavailable";
        var discovered = catalog.Where(r => r.IsDiscovered).ToArray();
        var highest = discovered.Select(r => r.MasteryLevel).DefaultIfEmpty().Max();
        var mentorIds = discovered.Where(r => r.MasteryLevel == highest).Select(r => r.Uuid).ToArray();
        var names = SpellRecipeSO.All.Where(r => r is not null && mentorIds.Contains(StableId(r))).Select(SafeName).Take(4).ToArray();
        var recipients = discovered.Count(r => r.MasteryLevel < highest);
        var warning = _config.EconomyMode.Value == MentorEconomyMode.PerRecipient ? " Warning: total bonus scales with recipient count." : string.Empty;
        return $"{_config.EconomyMode.Value}, {_config.SharePercent.Value:0.##}%. Mentors ({mentorIds.Length}): {string.Join(", ", names)}. Eligible recipients: {recipients}.{warning}";
    }

    public void Observe(SpellRecipeSO source, BigDouble xp)
    {
        if (_guarded || !_config.Active || !TryAmount(xp, out var amount)) return;
        if (!TryCatalog(out var catalog)) return;
        var sourceId = StableId(source);
        if (sourceId is null) { Block("source recipe has no stable UUID"); return; }
        var recipients = _engine.EligibleRecipients(sourceId, catalog);
        if (recipients.Count == 0) return;
        _frameXp = _frameXp.Add(amount);
#if DEBUG
        if (_config.DevelopmentProbeEnabled)
            _log.LogInfo($"Mentor probe: source={sourceId} name={SafeName(source)} xp={amount.Mantissa}e{amount.Exponent} mastery={source.masteryLevel} ready={source.IsReadyToLevelMastery()}");
#endif
    }

    public void LateTick()
    {
        if (!_config.Active || IsBlocked) { Cancel(); return; }
        if (_frameXp.IsValidPositive)
        {
            if (TryCatalog(out var catalog))
            {
                var highest = catalog.Where(r => r.IsDiscovered).Select(r => r.MasteryLevel).DefaultIfEmpty().Max();
                var recipients = catalog.Where(r => r.IsDiscovered && r.MasteryLevel < highest).OrderBy(r => r.Uuid, StringComparer.Ordinal).ToArray();
                var grants = _engine.Plan(_frameXp, _config.SharePercent.Value, _config.EconomyMode.Value, recipients);
                _engine.Consolidate(grants);
                if (_config.DetailedLogging.Value && grants.Count > 0) _log.LogInfo($"Mentor batch: recipients={grants.Count}, mode={_config.EconomyMode.Value}, share={_config.SharePercent.Value:0.##}%");
            }
            _frameXp = default;
        }

        var timer = Stopwatch.StartNew();
        for (var operation = 0; operation < _config.OperationsPerFrame.Value; operation++)
        {
            if (timer.Elapsed.TotalMilliseconds >= _config.CpuBudgetMilliseconds.Value) break;
            var grants = _engine.Take(1);
            if (grants.Count == 0) break;
            var grant = grants[0];
            var recipient = Resolve(grant.Uuid);
            if (recipient is null || !recipient.IsDiscovered()) continue;
            var highest = SpellRecipeSO.All.Where(r => r is not null && r.IsDiscovered()).Select(r => r.masteryLevel).DefaultIfEmpty().Max();
            if (recipient.masteryLevel >= highest) continue;
            try
            {
                _guarded = true;
                recipient.GainMasteryExp(new BigDouble(grant.Amount.Mantissa, grant.Amount.Exponent));
                if (_config.DetailedLogging.Value) _log.LogInfo($"Mentor grant: recipient={grant.Uuid} amount={grant.Amount.Mantissa}e{grant.Amount.Exponent}");
            }
            catch (Exception ex) { Block($"native mastery grant failed: {ex.GetBaseException().Message}"); }
            finally { _guarded = false; }
        }
    }

    public void Cancel() { _frameXp = default; _engine.Cancel(); }
    public void ClearBlock() => _blockedReason = null;
    private void Block(string reason) { if (_blockedReason == reason) return; _blockedReason = reason; Cancel(); _log.LogError($"Orb Mentor blocked: {reason}"); }

    private bool TryCatalog(out MentorRecipe[] catalog)
    {
        var list = SpellRecipeSO.All;
        if (list is null) { catalog = Array.Empty<MentorRecipe>(); Block("SpellRecipeSO.All is unavailable"); return false; }
        var result = new List<MentorRecipe>();
        foreach (var recipe in list.Where(r => r is not null))
        {
            var id = StableId(recipe);
            if (id is null) { catalog = Array.Empty<MentorRecipe>(); Block("registered recipe has no stable UUID"); return false; }
            result.Add(new MentorRecipe(id, recipe.masteryLevel, recipe.IsDiscovered()));
        }
        catalog = result.ToArray();
        return true;
    }

    private static SpellRecipeSO? Resolve(string id) => SpellRecipeSO.All?.FirstOrDefault(r => r is not null && string.Equals(StableId(r), id, StringComparison.Ordinal));
    private static bool TryAmount(BigDouble value, out MentorAmount amount)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var boxed = (object)value;
        var type = boxed.GetType();
        var mantissa = type.GetField("mantissa", Flags)?.GetValue(boxed);
        var exponent = type.GetField("exponent", Flags)?.GetValue(boxed);
        if (mantissa is not double m || exponent is not long e) { amount = default; return false; }
        amount = new MentorAmount(m, e);
        return amount.IsValidPositive;
    }
    internal static string? StableId(object instance)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (var type = instance.GetType(); type is not null; type = type.BaseType)
        {
            foreach (var name in new[] { "uuid", "UUID", "Uuid", "guid", "Guid", "id", "ID" })
            {
                var value = type.GetField(name, Flags | BindingFlags.DeclaredOnly)?.GetValue(instance);
                if (!string.IsNullOrWhiteSpace(value?.ToString())) return value!.ToString();
            }
            foreach (var name in new[] { "GetUuid", "GetUUID", "GetGuid", "GetId" })
            {
                var value = type.GetMethod(name, Flags | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null)?.Invoke(instance, Array.Empty<object>());
                if (!string.IsNullOrWhiteSpace(value?.ToString())) return value!.ToString();
            }
        }
        return null;
    }
    private static string SafeName(SpellRecipeSO recipe) { try { return recipe.GetName(); } catch { return "<unavailable>"; } }
}
