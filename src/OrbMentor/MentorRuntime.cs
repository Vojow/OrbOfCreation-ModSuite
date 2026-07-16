using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using UnityEngine;

namespace OrbMentor;

internal enum MentorDomain { Spells, Artifacts, Alchemy }

internal sealed class MentorRuntime
{
    private enum GrantResult { NoWork, Deferred, Dropped, Granted }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();
        public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }

    private sealed class NativeEntry
    {
        public NativeEntry(string uuid, object item, string displayName, object? artifactContainer)
        {
            Uuid = uuid;
            Item = item;
            DisplayName = displayName;
            ArtifactContainer = artifactContainer;
        }

        public string Uuid { get; }
        public object Item { get; }
        public string DisplayName { get; }
        public object? ArtifactContainer { get; set; }
        public int MasteryLevel { get; set; }
        public bool IsDiscovered { get; set; }
    }

    private sealed class DomainCatalog
    {
        public readonly List<NativeEntry> Entries = new();
        public readonly Dictionary<string, NativeEntry> ById = new(StringComparer.Ordinal);
        public readonly Dictionary<object, NativeEntry> ByObject = new(ReferenceComparer.Instance);
        public readonly HashSet<string> MentorIds = new(StringComparer.Ordinal);
        public Type? ExpectedType;
        public FieldInfo? RegistryField;
        public FieldInfo? MasteryField;
        public MethodInfo? AvailabilityMethod;
        public MethodInfo? IdentityMethod;
        public MethodInfo? RegistryLookupMethod;
        public MethodInfo? ArtifactContainerMethod;
        public MentorRecipe[] Recipients = Array.Empty<MentorRecipe>();
        public long NextLiveRefresh;
        public long NextReconcile;
        public int HighestMastery = int.MinValue;
        public bool Initialized;
        public bool RelationshipDirty = true;
        public bool NeedsReconcile = true;
        public long ProgressionEpoch;
        public long RelationshipEpoch;
    }

    private sealed class DomainState
    {
        public readonly MentorEngine Engine = new();
        public readonly MentorCaptureQueue Captures = new();
        public readonly MentorSourceAccumulator Sources = new();
        public readonly HashSet<string> IdentityDeferrals = new(StringComparer.Ordinal);
        public MentorPlan? ActivePlan;
        public long NextDistributionTimestamp;
        public long NextSummaryTimestamp;
        public string MentorSummary = "None";
    }

    private const BindingFlags AllFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const int PlanningOperationsPerFrame = 16;
    private const int GrantValidationChecksPerFrame = 16;
    private static readonly MentorDomain[] DomainOrder = { MentorDomain.Spells, MentorDomain.Artifacts, MentorDomain.Alchemy };
    private static readonly string[] IdentityMethods = { "GetId", "GetGuid", "GetUUID", "GetUuid" };
    private static readonly FieldInfo? BigDoubleMantissa = typeof(BigDouble).GetField("mantissa", AllFlags);
    private static readonly FieldInfo? BigDoubleExponent = typeof(BigDouble).GetField("exponent", AllFlags);
    private static readonly long ContinuousDistributionTicks = Math.Max(1, Stopwatch.Frequency / 4);
    private static readonly long SummaryRefreshTicks = Math.Max(1, Stopwatch.Frequency);
    private static readonly long LiveRefreshTicks = Math.Max(1, Stopwatch.Frequency);
    private static readonly long ReconcileTicks = Math.Max(1, Stopwatch.Frequency * 10);

    private readonly MentorConfig _config;
    private readonly ManualLogSource _log;
    private readonly MentorFailureState _failures = new();
    private readonly MentorLifecycleSignal _lifecycleReset = new();
    private readonly Dictionary<MentorDomain, DomainState> _domains = new();
    private readonly Dictionary<MentorDomain, DomainCatalog> _catalogs = new();
    private bool _guarded;
    private bool _artifactWasEnabled;
    private bool _alchemyWasEnabled;
    private bool _captureFailureLogged;
    private int _nextPlanningDomain;
    private int _nextGrantDomain;
    private object? _activeArtifact;
    private object? _activeArtifactContainer;
    private MentorAmount _activeArtifactXp;

    public MentorRuntime(MentorConfig config, ManualLogSource log)
    {
        _config = config;
        _log = log;
        Diagnostics = new MentorDiagnostics();
        foreach (var domain in DomainOrder)
        {
            _domains.Add(domain, new DomainState());
            _catalogs.Add(domain, new DomainCatalog());
        }
        _artifactWasEnabled = config.ArtifactsEnabled.Value;
        _alchemyWasEnabled = config.AlchemyEnabled.Value;
        if (BigDoubleMantissa is null || BigDoubleExponent is null)
            BlockPermanent("BigDouble mantissa/exponent contract is unavailable");
    }

    internal MentorDiagnostics Diagnostics { get; }
    public string? BlockedReason => _failures.Reason;
    public bool IsBlocked => _failures.IsBlocked;

    public string CurrentMentor(MentorDomain domain)
    {
        var state = _domains[domain];
        if (!_config.Active || !DomainEnabled(domain)) return state.MentorSummary = "Inactive";
        var now = Stopwatch.GetTimestamp();
        if (now < state.NextSummaryTimestamp) return state.MentorSummary;
        state.NextSummaryTimestamp = now + SummaryRefreshTicks;
        var catalog = _catalogs[domain];
        if (!catalog.Initialized || catalog.RelationshipDirty) return state.MentorSummary = "Pending";
        if (catalog.MentorIds.Count == 0) return state.MentorSummary = "None";
        var names = new List<string>(Math.Min(3, catalog.MentorIds.Count));
        foreach (var entry in catalog.Entries)
        {
            if (names.Count >= 3) break;
            if (catalog.MentorIds.Contains(entry.Uuid)) names.Add(entry.DisplayName);
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
        var drops = Diagnostics.DroppedEvents + Diagnostics.DroppedGrants;
        var dropSummary = drops > 0 ? $" Dropped work: {drops}." : string.Empty;
        return $"{_config.EconomyMode.Value}. Spells {_config.SharePercent.Value:0.##}%; {artifact}; {alchemy}.{warning}{dropSummary}";
    }

    public void Observe(SpellRecipeSO source, BigDouble xp)
    {
        if (TryAmount(xp, out var amount)) CaptureDomain(MentorDomain.Spells, source, amount);
    }

    public void ObserveAlchemy(object source, BigDouble xp)
    {
        if (_config.AlchemyEnabled.Value && TryAmount(xp, out var amount)) CaptureDomain(MentorDomain.Alchemy, source, amount);
    }

    public void BeginArtifactTick(object source)
    {
        _activeArtifact = null;
        _activeArtifactContainer = null;
        _activeArtifactXp = default;
        if (_guarded || !_config.Active || !_config.ArtifactsEnabled.Value || IsBlocked) return;
        var catalog = _catalogs[MentorDomain.Artifacts];
        if (!catalog.Initialized || !catalog.ByObject.TryGetValue(source, out var entry))
        {
            Diagnostics.RecordDrop(MentorDropReason.CaptureUnavailable, 1, grant: false);
            return;
        }
        _activeArtifact = source;
        _activeArtifactContainer = entry.ArtifactContainer;
    }

    public void EndArtifactTick(bool nativeSucceeded)
    {
        var source = _activeArtifact;
        var amount = _activeArtifactXp;
        _activeArtifact = null;
        _activeArtifactContainer = null;
        _activeArtifactXp = default;
        if (nativeSucceeded && source is not null && amount.IsValidPositive)
            CaptureDomain(MentorDomain.Artifacts, source, amount);
        else if (!nativeSucceeded && amount.IsValidPositive)
            Diagnostics.RecordDrop(MentorDropReason.ContractFailure, 1, grant: false);
    }

    public void ObserveExperienceContainer(object container, BigDouble xp)
    {
        if (_activeArtifact is null || _activeArtifactContainer is null || _guarded) return;
        if (ReferenceEquals(_activeArtifactContainer, container) && TryAmount(xp, out var amount))
            _activeArtifactXp = _activeArtifactXp.Add(amount);
    }

    private void CaptureDomain(MentorDomain domain, object source, MentorAmount amount)
    {
        if (_guarded || !_config.Active || IsBlocked || !amount.IsValidPositive) return;
        if (_lifecycleReset.IsPending)
        {
            Diagnostics.RecordDrop(MentorDropReason.LifecycleReset, 1, grant: false);
            return;
        }
        var catalog = _catalogs[domain];
        if (!catalog.Initialized || catalog.ExpectedType is null || catalog.MasteryField is null ||
            catalog.AvailabilityMethod is null || catalog.IdentityMethod is null)
        {
            Diagnostics.RecordDrop(MentorDropReason.CaptureUnavailable, 1, grant: false);
            return;
        }
        try
        {
            if (IsDestroyed(source) || !catalog.ExpectedType.IsInstanceOfType(source))
            {
                Diagnostics.RecordDrop(MentorDropReason.SourceIdentityChanged, 1, grant: false);
                return;
            }
            var mastery = Convert.ToInt32(catalog.MasteryField.GetValue(source) ?? 0);
            var discovered = Convert.ToBoolean(catalog.AvailabilityMethod.Invoke(source, null) ?? false);
            var uuid = catalog.ByObject.TryGetValue(source, out var entry) ? entry.Uuid : ReadUuid(catalog.IdentityMethod, source);
            if (string.IsNullOrWhiteSpace(uuid))
            {
                Diagnostics.RecordDrop(MentorDropReason.SourceIdentityChanged, 1, grant: false);
                return;
            }
            if (entry is not null)
            {
                ObserveLiveProgression(
                    catalog,
                    entry,
                    mastery,
                    discovered,
                    epochAlreadyAdvanced: catalog.ProgressionEpoch != catalog.RelationshipEpoch);
            }
            var result = _domains[domain].Captures.Capture(
                new MentorCaptureKey(source, uuid, mastery, discovered, catalog.ProgressionEpoch), amount);
            if (result == MentorCaptureResult.Overflow)
            {
                Diagnostics.RecordDrop(MentorDropReason.CaptureOverflow, 1, grant: false);
                return;
            }
            if (result == MentorCaptureResult.Invalid)
            {
                Diagnostics.RecordDrop(MentorDropReason.CaptureUnavailable, 1, grant: false);
                return;
            }
            Diagnostics.RecordCapture(result == MentorCaptureResult.Coalesced);
            if (entry is null) catalog.NeedsReconcile = true;
#if DEBUG
            if (_config.DevelopmentProbeEnabled)
                _log.LogInfo($"Mentor {domain} capture: source={uuid} mastery={mastery} discovered={discovered} xp={amount.Mantissa}e{amount.Exponent}");
#endif
        }
        catch (Exception ex)
        {
            Diagnostics.RecordDrop(MentorDropReason.CaptureUnavailable, 1, grant: false);
            if (!_captureFailureLogged)
            {
                _captureFailureLogged = true;
                _log.LogWarning($"Mentor {domain} capture failed without blocking other events: {ex.GetBaseException().Message}");
            }
        }
    }

    public void MarkRelationshipDirty(MentorDomain domain)
    {
        var catalog = _catalogs[domain];
        catalog.ProgressionEpoch++;
        catalog.RelationshipDirty = true;
    }

    public void LateTick()
    {
        if (_lifecycleReset.TryConsume()) ResetLifecycle();
        if (!_config.Active || IsBlocked) return;
        var started = Stopwatch.GetTimestamp();
        var cpuBudget = Math.Clamp(_config.CpuBudgetMilliseconds.Value, 0.1, 1.0);
        if (!PrepareDomain(MentorDomain.Spells, started, cpuBudget)) return;
        if (_config.ArtifactsEnabled.Value)
        {
            if (!PrepareDomain(MentorDomain.Artifacts, started, cpuBudget)) return;
        }
        else if (_artifactWasEnabled) CancelDomain(MentorDomain.Artifacts, MentorDropReason.Disabled, clearCatalog: false);
        if (_config.AlchemyEnabled.Value)
        {
            if (!PrepareDomain(MentorDomain.Alchemy, started, cpuBudget)) return;
        }
        else if (_alchemyWasEnabled) CancelDomain(MentorDomain.Alchemy, MentorDropReason.Disabled, clearCatalog: false);
        _artifactWasEnabled = _config.ArtifactsEnabled.Value;
        _alchemyWasEnabled = _config.AlchemyEnabled.Value;

        var planningOperations = 0;
        var planningEmpty = 0;
        while (planningOperations < PlanningOperationsPerFrame && ElapsedMilliseconds(started) < cpuBudget && planningEmpty < DomainOrder.Length)
        {
            var domain = DomainOrder[_nextPlanningDomain++ % DomainOrder.Length];
            if (!DomainEnabled(domain) || !ProcessPlanningStep(domain)) { planningEmpty++; continue; }
            planningOperations++;
            planningEmpty = 0;
        }

        var nativeGrants = 0;
        var validationChecks = 0;
        var grantEmpty = 0;
        var grantLimit = Math.Max(1, _config.OperationsPerFrame.Value);
        while (nativeGrants < grantLimit && validationChecks < GrantValidationChecksPerFrame &&
               ElapsedMilliseconds(started) < cpuBudget && grantEmpty < DomainOrder.Length)
        {
            var domain = DomainOrder[_nextGrantDomain++ % DomainOrder.Length];
            if (!DomainEnabled(domain)) { grantEmpty++; continue; }
            var result = ProcessGrant(domain);
            if (result == GrantResult.NoWork) { grantEmpty++; continue; }
            validationChecks++;
            if (result == GrantResult.Granted) nativeGrants++;
            if (result == GrantResult.Deferred) grantEmpty++;
            else grantEmpty = 0;
        }
    }

    private bool PrepareDomain(MentorDomain domain, long started, double cpuBudget)
    {
        if (ElapsedMilliseconds(started) >= cpuBudget) return false;
        return EnsureCatalog(domain, Stopwatch.GetTimestamp());
    }

    private bool ProcessPlanningStep(MentorDomain domain)
    {
        var state = _domains[domain];
        var catalog = _catalogs[domain];
        if (!catalog.Initialized || catalog.RelationshipDirty || catalog.NeedsReconcile) return false;
        if (state.Captures.TryTake(out var captured))
        {
            if (!TryValidateCapturedSource(catalog, captured))
            {
                Diagnostics.RecordDrop(MentorDropReason.SourceIdentityChanged, captured.EventCount, grant: false);
                return true;
            }
            var qualification = MentorRelationshipQualification.Evaluate(
                captured.Key, catalog.RelationshipEpoch, catalog.HighestMastery, catalog.Recipients.Length);
            if (qualification == MentorQualificationStatus.StaleRelationship)
            {
                Diagnostics.RecordDrop(MentorDropReason.StaleRelationship, captured.EventCount, grant: false);
                return true;
            }
            if (qualification == MentorQualificationStatus.NoRecipients)
            {
                Diagnostics.RecordDrop(MentorDropReason.NoRecipients, captured.EventCount, grant: false);
                return true;
            }
            if (qualification != MentorQualificationStatus.Qualified)
            {
                Diagnostics.RecordDrop(MentorDropReason.SourceIneligible, captured.EventCount, grant: false);
                return true;
            }
            state.Sources.Capture(captured.Key.Uuid, captured.Amount, qualifiesAtEvent: true, captured.EventCount);
            Diagnostics.RecordQualified(captured.EventCount);
            return true;
        }
        if (state.ActivePlan is null && state.Sources.HasPending)
        {
            BeginPlan(domain, SharePercent(domain), domain != MentorDomain.Spells);
            return true;
        }
        if (state.ActivePlan is not null)
        {
            if (state.ActivePlan.TryTake(out var grant)) state.Engine.Consolidate(grant);
            if (state.ActivePlan.RemainingCount == 0) state.ActivePlan = null;
            return true;
        }
        return false;
    }

    private void BeginPlan(MentorDomain domain, double percent, bool continuous)
    {
        var state = _domains[domain];
        var now = Stopwatch.GetTimestamp();
        if (continuous && !DistributionDue(now, ref state.NextDistributionTimestamp, ContinuousDistributionTicks)) return;
        var batch = state.Sources.Drain();
        var recipients = _catalogs[domain].Recipients;
        if (recipients.Length == 0)
        {
            Diagnostics.RecordDrop(MentorDropReason.NoRecipients, batch.EventCount, grant: false);
            return;
        }
        if (!double.IsFinite(percent) || percent <= 0)
        {
            Diagnostics.RecordDrop(MentorDropReason.ZeroShare, batch.EventCount, grant: false);
            return;
        }
        state.ActivePlan = state.Engine.CreatePlan(batch.Amount, percent, _config.EconomyMode.Value, recipients, batch.EventCount);
        if (state.ActivePlan is null)
            Diagnostics.RecordDrop(MentorDropReason.ContractFailure, batch.EventCount, grant: false);
        else if (_config.DetailedLogging.Value)
            _log.LogInfo($"Mentor {domain} batch: sources={batch.SourceCount}, events={batch.EventCount}, recipients={recipients.Length}, share={percent:0.##}%");
    }

    private GrantResult ProcessGrant(MentorDomain domain)
    {
        var state = _domains[domain];
        var catalog = _catalogs[domain];
        if (!state.Engine.TryPeek(out var grant)) return GrantResult.NoWork;
        if (catalog.RelationshipDirty || catalog.NeedsReconcile)
        {
            Diagnostics.RecordDeferredGrant();
            return GrantResult.Deferred;
        }
        if (!catalog.ById.TryGetValue(grant.Uuid, out var entry))
            return DeferOrDropIdentity(domain, grant, state, catalog);
        MentorIdentityStatus identity;
        try { identity = ValidateCurrentIdentity(catalog, entry); }
        catch { identity = MentorIdentityStatus.RegistryMismatch; }
        if (identity != MentorIdentityStatus.Valid)
            return DeferOrDropIdentity(domain, grant, state, catalog);
        state.IdentityDeferrals.Remove(grant.Uuid);
        try
        {
            var discovered = Convert.ToBoolean(catalog.AvailabilityMethod!.Invoke(entry.Item, null) ?? false);
            var mastery = Convert.ToInt32(catalog.MasteryField!.GetValue(entry.Item) ?? 0);
            if (ObserveLiveProgression(catalog, entry, mastery, discovered) || catalog.RelationshipDirty)
            {
                Diagnostics.RecordDeferredGrant();
                return GrantResult.Deferred;
            }
            if (!discovered || mastery >= catalog.HighestMastery)
            {
                state.Engine.Complete(grant.Uuid);
                Diagnostics.RecordDrop(MentorDropReason.RecipientIneligible, 1, grant: true);
                return GrantResult.Dropped;
            }
            _guarded = true;
            var progressionEpochBeforeGrant = catalog.ProgressionEpoch;
            var value = new BigDouble(grant.Amount.Mantissa, grant.Amount.Exponent);
            if (domain == MentorDomain.Spells) ((SpellRecipeSO)entry.Item).GainMasteryExp(value);
            else if (domain == MentorDomain.Alchemy) InvokeRequired(entry.Item, "GainMasteryXp", value);
            else GrantArtifact(entry.Item, value);
            // The native grant may synchronously trigger a domain-specific
            // progression hook. Even when it does not visibly change mastery
            // or discovery, force the relationship cache to settle before a
            // second native grant is admitted.
            MentorProgressionObservation.AfterNativeGrant(ref catalog.RelationshipDirty);
            var masteryAfterGrant = Convert.ToInt32(catalog.MasteryField.GetValue(entry.Item) ?? 0);
            var discoveredAfterGrant = Convert.ToBoolean(catalog.AvailabilityMethod.Invoke(entry.Item, null) ?? false);
            ObserveLiveProgression(
                catalog,
                entry,
                masteryAfterGrant,
                discoveredAfterGrant,
                epochAlreadyAdvanced: catalog.ProgressionEpoch != progressionEpochBeforeGrant);
            state.Engine.Complete(grant.Uuid);
            Diagnostics.RecordGrant();
            if (_config.DetailedLogging.Value)
                _log.LogInfo($"Mentor {domain} grant: recipient={entry.DisplayName} ({grant.Uuid}), amount={grant.Amount.Mantissa}e{grant.Amount.Exponent}");
            return GrantResult.Granted;
        }
        catch (Exception ex)
        {
            BlockTransient($"{domain} native mastery grant failed: {ex.GetBaseException().Message}");
            return GrantResult.Dropped;
        }
        finally { _guarded = false; }
    }

    private static bool ObserveLiveProgression(
        DomainCatalog catalog,
        NativeEntry entry,
        int observedMastery,
        bool observedDiscovered,
        bool epochAlreadyAdvanced = false)
    {
        var cachedMastery = entry.MasteryLevel;
        var cachedDiscovered = entry.IsDiscovered;
        var changed = MentorProgressionObservation.Apply(
            ref catalog.ProgressionEpoch,
            ref catalog.RelationshipDirty,
            ref cachedMastery,
            ref cachedDiscovered,
            observedMastery,
            observedDiscovered,
            epochAlreadyAdvanced);
        if (!changed) return false;
        entry.MasteryLevel = cachedMastery;
        entry.IsDiscovered = cachedDiscovered;
        return true;
    }

    private GrantResult DeferOrDropIdentity(MentorDomain domain, MentorGrant grant, DomainState state, DomainCatalog catalog)
    {
        if (state.IdentityDeferrals.Add(grant.Uuid))
        {
            catalog.NeedsReconcile = true;
            Diagnostics.RecordDeferredGrant();
            return GrantResult.Deferred;
        }
        state.IdentityDeferrals.Remove(grant.Uuid);
        state.Engine.Complete(grant.Uuid);
        Diagnostics.RecordDrop(MentorDropReason.RecipientIdentityChanged, 1, grant: true);
        return GrantResult.Dropped;
    }

    private static bool TryValidateCapturedSource(DomainCatalog catalog, MentorCapturedEvent captured)
    {
        if (!catalog.ById.TryGetValue(captured.Key.Uuid, out var entry)) return false;
        if (!ReferenceEquals(entry.Item, captured.Key.Source) || IsDestroyed(entry.Item)) return false;
        try { return ValidateCurrentIdentity(catalog, entry) == MentorIdentityStatus.Valid; }
        catch { return false; }
    }

    private static MentorIdentityStatus ValidateCurrentIdentity(DomainCatalog catalog, NativeEntry entry)
    {
        var observedUuid = ReadUuid(catalog.IdentityMethod!, entry.Item);
        object? current = null;
        if (Guid.TryParse(entry.Uuid, out var guid))
            current = catalog.RegistryLookupMethod!.Invoke(null, new object[] { guid });
        return MentorIdentityValidation.Validate(
            catalog.ExpectedType!, entry.Uuid, entry.Item, current, observedUuid, IsDestroyed(entry.Item));
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

    public void Cancel(MentorDropReason reason = MentorDropReason.LifecycleReset)
    {
        foreach (var domain in DomainOrder) CancelDomain(domain, reason, clearCatalog: true);
        _activeArtifact = null;
        _activeArtifactContainer = null;
        _activeArtifactXp = default;
    }

    private void CancelDomain(MentorDomain domain, MentorDropReason reason, bool clearCatalog)
    {
        var state = _domains[domain];
        if (state.Captures.EventCount > 0) Diagnostics.RecordDrop(reason, state.Captures.EventCount, grant: false);
        if (state.Sources.EventCount > 0) Diagnostics.RecordDrop(reason, state.Sources.EventCount, grant: false);
        if (state.ActivePlan is not null && state.ActivePlan.RemainingCount > 0)
            Diagnostics.RecordDrop(reason, state.ActivePlan.RemainingCount, grant: true);
        if (state.Engine.PendingCount > 0) Diagnostics.RecordDrop(reason, state.Engine.PendingCount, grant: true);
        state.Captures.Cancel();
        state.Sources.Cancel();
        state.ActivePlan = null;
        state.Engine.Cancel();
        state.IdentityDeferrals.Clear();
        state.NextDistributionTimestamp = 0;
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
        catalog.RelationshipDirty = true;
        catalog.NeedsReconcile = true;
        catalog.ProgressionEpoch = 0;
        catalog.RelationshipEpoch = 0;
    }

    public void ResetLifecycle()
    {
        _lifecycleReset.TryConsume();
        Cancel(MentorDropReason.LifecycleReset);
        _failures.ResetLifecycle();
        _captureFailureLogged = false;
    }

    public void RequestLifecycleReset() => _lifecycleReset.Request();

    public void BlockPermanent(string reason)
    {
        if (_failures.PermanentReason is not null) return;
        _failures.BlockPermanent(reason);
        Cancel(MentorDropReason.ContractFailure);
        _log.LogError($"Orb Mentor permanently blocked: {reason}");
    }

    private void BlockTransient(string reason)
    {
        _failures.BlockTransient(reason);
        Cancel(MentorDropReason.ContractFailure);
        _log.LogError($"Orb Mentor blocked for this lifecycle: {reason}");
    }

    private bool EnsureCatalog(MentorDomain domain, long now)
    {
        try
        {
            var catalog = _catalogs[domain];
            if (!catalog.Initialized || catalog.NeedsReconcile || now >= catalog.NextReconcile)
            {
                if (!Reconcile(domain, catalog, now)) return false;
            }
            if (catalog.RelationshipDirty || now >= catalog.NextLiveRefresh) RefreshLive(catalog, now);
            return true;
        }
        catch (Exception ex)
        {
            BlockPermanent($"{domain} catalog contract failed: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private bool Reconcile(MentorDomain domain, DomainCatalog catalog, long now)
    {
        if (!ResolveSchema(domain, catalog)) return false;
        var registry = catalog.RegistryField!.GetValue(null) as IEnumerable;
        if (registry is null) { BlockPermanent($"{NativeTypeName(domain)}.All is unavailable"); return false; }
        var entries = new List<NativeEntry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in registry)
        {
            if (value is null || IsDestroyed(value)) continue;
            var uuid = ReadUuid(catalog.IdentityMethod!, value);
            if (string.IsNullOrWhiteSpace(uuid) || !ids.Add(uuid))
            {
                BlockPermanent($"registered {domain} UUID is missing or duplicated");
                return false;
            }
            object? artifactContainer = null;
            if (domain == MentorDomain.Artifacts) artifactContainer = catalog.ArtifactContainerMethod!.Invoke(value, null);
            var entry = catalog.ById.TryGetValue(uuid, out var existing) && ReferenceEquals(existing.Item, value)
                ? existing
                : new NativeEntry(uuid, value, SafeName(value), artifactContainer);
            entry.ArtifactContainer = artifactContainer;
            entries.Add(entry);
        }
        entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.Uuid, right.Uuid));
        var identityChanged = catalog.Initialized && !SameIdentity(catalog.Entries, entries);
        if (identityChanged) catalog.ProgressionEpoch++;
        catalog.Entries.Clear();
        catalog.Entries.AddRange(entries);
        catalog.ById.Clear();
        catalog.ByObject.Clear();
        foreach (var entry in entries) { catalog.ById.Add(entry.Uuid, entry); catalog.ByObject.Add(entry.Item, entry); }
        catalog.Initialized = true;
        catalog.NeedsReconcile = false;
        catalog.RelationshipDirty = true;
        catalog.NextReconcile = now + ReconcileTicks;
        return true;
    }

    private static bool SameIdentity(IReadOnlyList<NativeEntry> left, IReadOnlyList<NativeEntry> right)
    {
        if (left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
            if (!string.Equals(left[index].Uuid, right[index].Uuid, StringComparison.Ordinal) ||
                !ReferenceEquals(left[index].Item, right[index].Item)) return false;
        return true;
    }

    private bool ResolveSchema(MentorDomain domain, DomainCatalog catalog)
    {
        if (catalog.ExpectedType is not null) return true;
        var expected = Type.GetType(NativeTypeName(domain) + ", Assembly-CSharp", false);
        var idType = Type.GetType("IdScriptableObject, Assembly-CSharp", false);
        if (expected is null || idType is null) { BlockPermanent($"{domain} native type is unavailable"); return false; }
        catalog.ExpectedType = expected;
        catalog.RegistryField = FindField(expected, "All");
        catalog.MasteryField = FindField(expected, "masteryLevel");
        catalog.AvailabilityMethod = FindMethod(expected, AvailabilityMethod(domain), 0);
        foreach (var name in IdentityMethods)
        {
            catalog.IdentityMethod = FindMethod(expected, name, 0);
            if (catalog.IdentityMethod is not null) break;
        }
        foreach (var method in idType.GetMethods(AllFlags | BindingFlags.DeclaredOnly))
        {
            var parameters = method.GetParameters();
            if (method.Name == "GetInstance" && !method.IsGenericMethodDefinition && parameters.Length == 1 &&
                parameters[0].ParameterType == typeof(Guid))
            {
                catalog.RegistryLookupMethod = method;
                break;
            }
        }
        if (domain == MentorDomain.Artifacts) catalog.ArtifactContainerMethod = FindMethod(expected, "GetExperienceElement", 0);
        if (catalog.RegistryField is null || catalog.MasteryField is null || catalog.AvailabilityMethod is null ||
            catalog.IdentityMethod is null || catalog.RegistryLookupMethod is null ||
            (domain == MentorDomain.Artifacts && catalog.ArtifactContainerMethod is null))
        {
            BlockPermanent($"{domain} native catalog/accessor contract is unavailable");
            return false;
        }
        return true;
    }

    private static void RefreshLive(DomainCatalog catalog, long now)
    {
        var wasDirty = catalog.RelationshipDirty;
        var changed = false;
        var highest = int.MinValue;
        foreach (var entry in catalog.Entries)
        {
            var mastery = Convert.ToInt32(catalog.MasteryField!.GetValue(entry.Item) ?? 0);
            var discovered = Convert.ToBoolean(catalog.AvailabilityMethod!.Invoke(entry.Item, null) ?? false);
            changed |= mastery != entry.MasteryLevel || discovered != entry.IsDiscovered;
            entry.MasteryLevel = mastery;
            entry.IsDiscovered = discovered;
            if (entry.IsDiscovered && entry.MasteryLevel > highest) highest = entry.MasteryLevel;
        }
        MentorProgressionObservation.AdvanceRefreshEpoch(
            ref catalog.ProgressionEpoch,
            catalog.RelationshipEpoch,
            changed);
        if (!wasDirty && !changed)
        {
            catalog.NextLiveRefresh = now + LiveRefreshTicks;
            return;
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
        catalog.RelationshipDirty = false;
        catalog.RelationshipEpoch = catalog.ProgressionEpoch;
        catalog.NextLiveRefresh = now + LiveRefreshTicks;
    }

    private double SharePercent(MentorDomain domain) => domain switch
    {
        MentorDomain.Artifacts => _config.ArtifactSharePercent.Value,
        MentorDomain.Alchemy => _config.AlchemySharePercent.Value,
        _ => _config.SharePercent.Value,
    };

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

    internal static string AvailabilityMethod(MentorDomain domain) => domain switch
    {
        MentorDomain.Artifacts => "IsCreated",
        MentorDomain.Alchemy => "IsAvailable",
        _ => "IsDiscovered",
    };

    private static bool IsDestroyed(object value) => value is UnityEngine.Object unityObject && unityObject == null;

    private static string? ReadUuid(MethodInfo identityMethod, object item)
    {
        var value = identityMethod.Invoke(item, null);
        return value?.ToString();
    }

    private static string SafeName(object item)
    {
        try { return Invoke(item, "GetName")?.ToString() ?? "<unnamed>"; }
        catch { return "<unavailable>"; }
    }

    private static object? Invoke(object instance, string name, params object[] args) => FindMethod(instance.GetType(), name, args.Length)?.Invoke(instance, args);

    private static object InvokeRequired(object instance, string name, params object[] args)
    {
        var method = FindMethod(instance.GetType(), name, args.Length) ?? throw new MissingMemberException(name);
        var result = method.Invoke(instance, args);
        return result ?? (method.ReturnType == typeof(void) ? new object() : throw new MissingMemberException(name));
    }

    private static MethodInfo? FindMethod(Type type, string name, int count)
    {
        for (var current = type; current is not null; current = current.BaseType)
            foreach (var method in current.GetMethods(AllFlags | BindingFlags.DeclaredOnly))
                if (method.Name == name && method.GetParameters().Length == count) return method;
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
        if (BigDoubleMantissa?.GetValue(boxed) is not double mantissa || BigDoubleExponent?.GetValue(boxed) is not long exponent)
        {
            amount = default;
            return false;
        }
        amount = new MentorAmount(mantissa, exponent);
        return amount.IsValidPositive;
    }

    internal static string? StableId(object instance)
    {
        foreach (var name in IdentityMethods)
        {
            var method = FindMethod(instance.GetType(), name, 0);
            var value = method?.Invoke(instance, null);
            if (!string.IsNullOrWhiteSpace(value?.ToString())) return value!.ToString();
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
