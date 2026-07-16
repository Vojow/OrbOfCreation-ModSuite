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
        public List<NativeEntry> Entries = new();
        public Dictionary<string, NativeEntry> ById = new(StringComparer.Ordinal);
        public Dictionary<object, NativeEntry> ByObject = new(ReferenceComparer.Instance);
        public HashSet<string> MentorIds = new(StringComparer.Ordinal);
        public Type? ExpectedType;
        public FieldInfo? RegistryField;
        public FieldInfo? MasteryField;
        public MethodInfo? AvailabilityMethod;
        public MethodInfo? IdentityMethod;
        public MethodInfo? RegistryLookupMethod;
        public readonly object?[] RegistryLookupArguments = new object?[1];
        public MethodInfo? ArtifactContainerMethod;
        public List<MentorRecipe> Recipients = new();
        public MentorRelationshipSnapshot? Relationship;
        public MentorRelationshipEvidence? Evidence;
        public long NextLiveRefresh;
        public long NextReconcile;
        public int HighestMastery = int.MinValue;
        public bool Initialized;
        public bool RelationshipDirty = true;
        public bool NeedsReconcile = true;
        public long ProgressionEpoch;
        public long RelationshipEpoch;
        public readonly MentorWorkGeneration ReconcileRequests = new();
        public readonly MentorWorkGeneration RelationshipRequests = new();
        public ReconcileWork? Reconcile;
        public RefreshWork? Refresh;
    }

    private sealed class DomainState : MentorPendingWork
    {
        public readonly HashSet<string> IdentityDeferrals = new(StringComparer.Ordinal);
        public long NextDistributionTimestamp;
        public long NextSummaryTimestamp;
        public string MentorSummary = "None";
    }

    private sealed class ReconcileWork : IDisposable
    {
        public ReconcileWork(
            IEnumerator enumerator,
            int initialCapacity,
            bool catalogWasInitialized,
            long requestGeneration)
        {
            Enumerator = enumerator;
            Entries = new List<NativeEntry>(Math.Max(0, initialCapacity));
            ById = new Dictionary<string, NativeEntry>(Math.Max(0, initialCapacity), StringComparer.Ordinal);
            ByObject = new Dictionary<object, NativeEntry>(Math.Max(0, initialCapacity), ReferenceComparer.Instance);
            CatalogWasInitialized = catalogWasInitialized;
            RequestGeneration = requestGeneration;
        }

        public IEnumerator Enumerator { get; }
        public List<NativeEntry> Entries { get; }
        public Dictionary<string, NativeEntry> ById { get; }
        public Dictionary<object, NativeEntry> ByObject { get; }
        public MentorIncrementalOrder<NativeEntry> Order { get; } = new();
        public bool CatalogWasInitialized { get; }
        public long RequestGeneration { get; }
        public bool IdentityChanged { get; set; }
        public bool EnumerationComplete { get; set; }
        public bool OrderingComplete { get; set; }
        public void Dispose()
        {
            (Enumerator as IDisposable)?.Dispose();
            Order.Dispose();
        }
    }

    private sealed class RefreshWork
    {
        public RefreshWork(long progressionEpoch, long requestGeneration, bool wasDirty, int capacity)
        {
            ProgressionEpoch = progressionEpoch;
            RequestGeneration = requestGeneration;
            WasDirty = wasDirty;
            MentorIds = new HashSet<string>(Math.Max(0, capacity), StringComparer.Ordinal);
            DiscoveredIds = new HashSet<string>(Math.Max(0, capacity), StringComparer.Ordinal);
            RecipientIds = new HashSet<string>(Math.Max(0, capacity), StringComparer.Ordinal);
            Recipients = new List<MentorRecipe>(Math.Max(0, capacity));
            Discovered = new List<MentorRecipe>(Math.Max(0, capacity));
        }

        public long ProgressionEpoch { get; set; }
        public long RequestGeneration { get; }
        public bool WasDirty { get; }
        public int ReadIndex { get; set; }
        public int BuildIndex { get; set; }
        public int HighestMastery { get; set; } = int.MinValue;
        public bool Changed { get; set; }
        public bool ReadComplete { get; set; }
        public HashSet<string> MentorIds { get; }
        public HashSet<string> DiscoveredIds { get; }
        public HashSet<string> RecipientIds { get; }
        public List<MentorRecipe> Recipients { get; }
        public List<MentorRecipe> Discovered { get; }
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
    private readonly MentorFailureRegistry _failures = new();
    private readonly MentorLifecycleSignal _lifecycleReset = new();
    private readonly Dictionary<MentorDomain, DomainState> _domains = new();
    private readonly Dictionary<MentorDomain, DomainCatalog> _catalogs = new();
    private readonly bool[] _domainResetPending = new bool[DomainOrder.Length];
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
    public string? BlockedReason => _failures.Global.Reason;
    public bool IsBlocked => _failures.Global.IsBlocked;

    public string CurrentMentor(MentorDomain domain)
    {
        var state = _domains[domain];
        if (!_config.Active || !DomainConfigured(domain)) return state.MentorSummary = "Inactive";
        if (DomainBlocked(domain)) return state.MentorSummary = "Blocked";
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
        var artifact = DomainStatus(MentorDomain.Artifacts, _config.ArtifactSharePercent.Value);
        var alchemy = DomainStatus(MentorDomain.Alchemy, _config.AlchemySharePercent.Value);
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
        if (_guarded || !_config.Active || !DomainEnabled(MentorDomain.Artifacts) || IsBlocked) return;
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
        if (_guarded || !_config.Active || IsBlocked || !DomainEnabled(domain) || !amount.IsValidPositive) return;
        if (_lifecycleReset.IsPending)
        {
            Diagnostics.RecordDrop(MentorDropReason.LifecycleReset, 1, grant: false);
            return;
        }
        var catalog = _catalogs[domain];
        if (catalog.Reconcile?.IdentityChanged == true)
        {
            RequestReconcile(catalog);
            Diagnostics.RecordDrop(MentorDropReason.CatalogIdentityChanged, 1, grant: false);
            return;
        }
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
                var sourceChanged = entry.MasteryLevel != mastery || entry.IsDiscovered != discovered;
                ObserveLiveProgression(
                    catalog,
                    entry,
                    mastery,
                    discovered,
                    epochAlreadyAdvanced: catalog.ProgressionEpoch != catalog.RelationshipEpoch);
                if (sourceChanged) AppendRelationshipEvidence(catalog, uuid, mastery, discovered);
            }
            var relationship = !catalog.RelationshipDirty && !catalog.NeedsReconcile &&
                catalog.Reconcile is null && catalog.Refresh is null
                ? catalog.Relationship
                : null;
            var evidence = relationship is null ? catalog.Evidence : null;
            if (relationship is null && evidence is null)
            {
                Diagnostics.RecordDrop(MentorDropReason.CaptureUnavailable, 1, grant: false);
                return;
            }
            var result = _domains[domain].Captures.Capture(
                new MentorCaptureKey(
                    source, uuid, mastery, discovered, catalog.ProgressionEpoch, relationship, evidence), amount);
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
            if (entry is null) RequestReconcile(catalog);
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

    public void MarkRelationshipDirty(MentorDomain domain, object? changedSource = null)
    {
        if (DomainBlocked(domain)) return;
        var catalog = _catalogs[domain];
        RequestRelationshipRefresh(catalog, advanceProgressionEpoch: true);
        if (changedSource is null || catalog.ExpectedType is null || catalog.MasteryField is null ||
            catalog.AvailabilityMethod is null || catalog.IdentityMethod is null ||
            IsDestroyed(changedSource) || !catalog.ExpectedType.IsInstanceOfType(changedSource)) return;
        try
        {
            var uuid = catalog.ByObject.TryGetValue(changedSource, out var entry)
                ? entry.Uuid
                : ReadUuid(catalog.IdentityMethod, changedSource);
            if (string.IsNullOrWhiteSpace(uuid)) return;
            var mastery = Convert.ToInt32(catalog.MasteryField.GetValue(changedSource) ?? 0);
            var discovered = Convert.ToBoolean(catalog.AvailabilityMethod.Invoke(changedSource, null) ?? false);
            if (entry is null || entry.MasteryLevel != mastery || entry.IsDiscovered != discovered)
                AppendRelationshipEvidence(catalog, uuid, mastery, discovered);
            if (entry is not null)
            {
                entry.MasteryLevel = mastery;
                entry.IsDiscovered = discovered;
            }
        }
        catch { }
    }

    public void LateTick()
    {
        if (_lifecycleReset.TryConsume()) ResetLifecycle();
        foreach (var domain in DomainOrder)
        {
            var index = (int)domain;
            if (!_domainResetPending[index]) continue;
            _domainResetPending[index] = false;
            CancelDomain(domain, MentorDropReason.LifecycleReset, clearCatalog: true);
            _failures.For(domain).ResetLifecycle();
        }
        if (!_config.Active || IsBlocked) return;
        var started = Stopwatch.GetTimestamp();
        var cpuBudget = Math.Clamp(_config.CpuBudgetMilliseconds.Value, 0.1, 1.0);
        if (!_config.ArtifactsEnabled.Value && _artifactWasEnabled)
            CancelDomain(MentorDomain.Artifacts, MentorDropReason.Disabled, clearCatalog: false);
        if (!_config.AlchemyEnabled.Value && _alchemyWasEnabled)
            CancelDomain(MentorDomain.Alchemy, MentorDropReason.Disabled, clearCatalog: false);
        _artifactWasEnabled = _config.ArtifactsEnabled.Value;
        _alchemyWasEnabled = _config.AlchemyEnabled.Value;

        var planningOperations = 0;
        var planningEmpty = 0;
        while (planningOperations < PlanningOperationsPerFrame && ElapsedMilliseconds(started) < cpuBudget && planningEmpty < DomainOrder.Length)
        {
            var domain = DomainOrder[_nextPlanningDomain++ % DomainOrder.Length];
            if (!DomainEnabled(domain) || !ProcessDomainStep(domain, Stopwatch.GetTimestamp())) { planningEmpty++; continue; }
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

    private bool ProcessDomainStep(MentorDomain domain, long now)
    {
        var catalog = _catalogs[domain];
        if (!catalog.Initialized || catalog.NeedsReconcile || catalog.Reconcile is not null || now >= catalog.NextReconcile)
            return ProcessReconcileStep(domain, catalog, now);
        if (catalog.RelationshipDirty || catalog.Refresh is not null || now >= catalog.NextLiveRefresh)
            return ProcessRefreshStep(domain, catalog, now);
        return ProcessPlanningStep(domain);
    }

    private bool ProcessPlanningStep(MentorDomain domain)
    {
        var state = _domains[domain];
        var catalog = _catalogs[domain];
        if (!catalog.Initialized || catalog.RelationshipDirty || catalog.NeedsReconcile) return false;
        if (state.RelationshipResolution is not null)
        {
            state.RelationshipResolution.Step();
            if (!state.RelationshipResolution.IsComplete) return true;
            var resolvedCapture = state.ResolvingCapture!;
            var relationship = state.RelationshipResolution.Result!;
            state.RelationshipResolution.Dispose();
            state.RelationshipResolution = null;
            state.ResolvingCapture = null;
            QualifyCaptured(state, catalog, resolvedCapture, relationship);
            return true;
        }
        if (state.Captures.TryTake(out var captured))
        {
            if (!TryValidateCapturedSource(catalog, captured))
            {
                Diagnostics.RecordDrop(MentorDropReason.SourceIdentityChanged, captured.EventCount, grant: false);
                return true;
            }
            if (captured.Key.Relationship is not null)
                QualifyCaptured(state, catalog, captured, captured.Key.Relationship);
            else if (captured.Key.Evidence?.Resolved is not null)
                QualifyCaptured(state, catalog, captured, captured.Key.Evidence.Resolved);
            else if (captured.Key.Evidence is not null)
            {
                state.ResolvingCapture = captured;
                state.RelationshipResolution = new MentorRelationshipResolutionWork(captured.Key.Evidence);
            }
            else
                Diagnostics.RecordDrop(MentorDropReason.CaptureUnavailable, captured.EventCount, grant: false);
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

    private void QualifyCaptured(
        DomainState state,
        DomainCatalog catalog,
        MentorCapturedEvent captured,
        MentorRelationshipSnapshot relationship)
    {
        var resolvedKey = new MentorCaptureKey(
            captured.Key.Source,
            captured.Key.Uuid,
            captured.Key.MasteryLevel,
            captured.Key.Discovered,
            captured.Key.ProgressionEpoch,
            relationship);
        var qualification = MentorRelationshipQualification.Evaluate(
            resolvedKey, catalog.RelationshipEpoch, catalog.HighestMastery, catalog.Recipients.Count);
        if (qualification == MentorQualificationStatus.NoRecipients)
        {
            Diagnostics.RecordDrop(MentorDropReason.NoRecipients, captured.EventCount, grant: false);
            return;
        }
        if (qualification != MentorQualificationStatus.Qualified)
        {
            Diagnostics.RecordDrop(MentorDropReason.SourceIneligible, captured.EventCount, grant: false);
            return;
        }
        var capturedRelationship = relationship.ForCapture(resolvedKey);
        state.Sources.Capture(capturedRelationship, captured.Key.Uuid, captured.Amount, captured.EventCount);
        Diagnostics.RecordQualified(captured.EventCount);
    }

    private void BeginPlan(MentorDomain domain, double percent, bool continuous)
    {
        var state = _domains[domain];
        var now = Stopwatch.GetTimestamp();
        if (continuous && !DistributionDue(now, ref state.NextDistributionTimestamp, ContinuousDistributionTicks)) return;
        var batch = state.Sources.Drain();
        var recipients = batch.Relationship?.Recipients ?? _catalogs[domain].Recipients;
        if (recipients.Count == 0)
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
            _log.LogInfo($"Mentor {domain} batch: sources={batch.SourceCount}, events={batch.EventCount}, recipients={recipients.Count}, share={percent:0.##}%");
    }

    private GrantResult ProcessGrant(MentorDomain domain)
    {
        var state = _domains[domain];
        var catalog = _catalogs[domain];
        if (!state.Engine.TryPeek(out var grantUuid, out var grantAmount)) return GrantResult.NoWork;
        if (state.HasGrantBarrier)
        {
            Diagnostics.RecordDeferredGrant();
            return GrantResult.Deferred;
        }
        if (catalog.RelationshipDirty || catalog.NeedsReconcile ||
            catalog.Reconcile is not null || catalog.Refresh is not null)
        {
            Diagnostics.RecordDeferredGrant();
            return GrantResult.Deferred;
        }
        if (!catalog.ById.TryGetValue(grantUuid, out var entry))
            return DeferOrDropIdentity(grantUuid, state, catalog);
        MentorIdentityStatus identity;
        try { identity = ValidateCurrentIdentity(catalog, entry); }
        catch { identity = MentorIdentityStatus.RegistryMismatch; }
        if (identity != MentorIdentityStatus.Valid)
            return DeferOrDropIdentity(grantUuid, state, catalog);
        state.IdentityDeferrals.Remove(grantUuid);
        try
        {
            var discovered = Convert.ToBoolean(catalog.AvailabilityMethod!.Invoke(entry.Item, null) ?? false);
            var mastery = Convert.ToInt32(catalog.MasteryField!.GetValue(entry.Item) ?? 0);
            if (ObserveLiveProgression(catalog, entry, mastery, discovered) || catalog.RelationshipDirty)
            {
                Diagnostics.RecordDeferredGrant();
                return GrantResult.Deferred;
            }
            // Recipient eligibility was frozen when the native source XP was
            // captured. A later mastery/discovery transition may delay this
            // grant while the live cache settles, but must not erase it.
            _guarded = true;
            var progressionEpochBeforeGrant = catalog.ProgressionEpoch;
            var value = new BigDouble(grantAmount.Mantissa, grantAmount.Exponent);
            if (domain == MentorDomain.Spells) ((SpellRecipeSO)entry.Item).GainMasteryExp(value);
            else if (domain == MentorDomain.Alchemy) InvokeRequired(entry.Item, "GainMasteryXp", value);
            else GrantArtifact(entry.Item, value);
            // The native grant may synchronously trigger a domain-specific
            // progression hook. Even when it does not visibly change mastery
            // or discovery, force the relationship cache to settle before a
            // second native grant is admitted.
            RequestRelationshipRefresh(catalog, advanceProgressionEpoch: false);
            var masteryAfterGrant = Convert.ToInt32(catalog.MasteryField.GetValue(entry.Item) ?? 0);
            var discoveredAfterGrant = Convert.ToBoolean(catalog.AvailabilityMethod.Invoke(entry.Item, null) ?? false);
            ObserveLiveProgression(
                catalog,
                entry,
                masteryAfterGrant,
                discoveredAfterGrant,
                epochAlreadyAdvanced: catalog.ProgressionEpoch != progressionEpochBeforeGrant);
            state.Engine.Complete(grantUuid);
            Diagnostics.RecordGrant();
            if (_config.DetailedLogging.Value)
                _log.LogInfo($"Mentor {domain} grant: recipient={entry.DisplayName} ({grantUuid}), amount={grantAmount.Mantissa}e{grantAmount.Exponent}");
            return GrantResult.Granted;
        }
        catch (Exception ex)
        {
            BlockDomainTransient(domain, $"{domain} native mastery grant failed: {ex.GetBaseException().Message}");
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
        catalog.RelationshipRequests.Request();
        entry.MasteryLevel = cachedMastery;
        entry.IsDiscovered = cachedDiscovered;
        return true;
    }

    private static void RequestReconcile(DomainCatalog catalog)
    {
        catalog.ReconcileRequests.Request();
        catalog.NeedsReconcile = true;
    }

    private static void RequestRelationshipRefresh(DomainCatalog catalog, bool advanceProgressionEpoch)
    {
        if (advanceProgressionEpoch) catalog.ProgressionEpoch++;
        catalog.RelationshipRequests.Request();
        catalog.RelationshipDirty = true;
    }

    private static void AppendRelationshipEvidence(
        DomainCatalog catalog,
        string uuid,
        int mastery,
        bool discovered)
    {
        if (catalog.Evidence is null)
        {
            if (catalog.Relationship is null) return;
            catalog.Evidence = MentorRelationshipEvidence.FromSnapshot(catalog.Relationship);
        }
        catalog.Evidence = catalog.Evidence.WithChange(
            uuid, mastery, discovered, catalog.ProgressionEpoch);
    }

    private GrantResult DeferOrDropIdentity(string grantUuid, DomainState state, DomainCatalog catalog)
    {
        if (state.IdentityDeferrals.Add(grantUuid))
        {
            RequestReconcile(catalog);
            Diagnostics.RecordDeferredGrant();
            return GrantResult.Deferred;
        }
        state.IdentityDeferrals.Remove(grantUuid);
        state.Engine.Complete(grantUuid);
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
        {
            catalog.RegistryLookupArguments[0] = guid;
            try { current = catalog.RegistryLookupMethod!.Invoke(null, catalog.RegistryLookupArguments); }
            finally { catalog.RegistryLookupArguments[0] = null; }
        }
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
        RecordPendingDrops(domain, reason);
        state.CancelPending();
        state.IdentityDeferrals.Clear();
        state.NextDistributionTimestamp = 0;
        var catalog = _catalogs[domain];
        if (!clearCatalog)
        {
            if (catalog.Reconcile is not null)
            {
                catalog.Reconcile.Dispose();
                catalog.Reconcile = null;
                RequestReconcile(catalog);
            }
            if (catalog.Refresh is not null)
            {
                catalog.Refresh = null;
                RequestRelationshipRefresh(catalog, advanceProgressionEpoch: false);
            }
            return;
        }
        catalog.Reconcile?.Dispose();
        catalog.Reconcile = null;
        catalog.Refresh = null;
        catalog.Entries.Clear();
        catalog.ById.Clear();
        catalog.ByObject.Clear();
        catalog.MentorIds.Clear();
        catalog.Recipients = new List<MentorRecipe>();
        catalog.Relationship = null;
        catalog.Evidence = null;
        catalog.HighestMastery = int.MinValue;
        catalog.NextLiveRefresh = 0;
        catalog.NextReconcile = 0;
        catalog.Initialized = false;
        RequestRelationshipRefresh(catalog, advanceProgressionEpoch: false);
        RequestReconcile(catalog);
        catalog.ProgressionEpoch = 0;
        catalog.RelationshipEpoch = 0;
    }

    private void RecordPendingDrops(MentorDomain domain, MentorDropReason reason)
    {
        var state = _domains[domain];
        if (state.Captures.EventCount > 0) Diagnostics.RecordDrop(reason, state.Captures.EventCount, grant: false);
        if (state.ResolvingCapture is not null)
            Diagnostics.RecordDrop(reason, state.ResolvingCapture.EventCount, grant: false);
        if (state.Sources.EventCount > 0) Diagnostics.RecordDrop(reason, state.Sources.EventCount, grant: false);
        if (state.ActivePlan is not null && state.ActivePlan.RemainingCount > 0)
            Diagnostics.RecordDrop(reason, state.ActivePlan.RemainingCount, grant: true);
        if (state.Engine.PendingCount > 0) Diagnostics.RecordDrop(reason, state.Engine.PendingCount, grant: true);
    }

    public void ResetLifecycle()
    {
        _lifecycleReset.TryConsume();
        Array.Clear(_domainResetPending, 0, _domainResetPending.Length);
        Cancel(MentorDropReason.LifecycleReset);
        _failures.ResetLifecycle();
        _captureFailureLogged = false;
    }

    public void RequestLifecycleReset() => _lifecycleReset.Request();

    public void RequestDomainReset(MentorDomain domain) => _domainResetPending[(int)domain] = true;

    public void BlockPermanent(string reason)
    {
        if (_failures.Global.PermanentReason is not null) return;
        _failures.Global.BlockPermanent(reason);
        Cancel(MentorDropReason.ContractFailure);
        _log.LogError($"Orb Mentor permanently blocked: {reason}");
    }

    private void BlockTransient(string reason)
    {
        _failures.Global.BlockTransient(reason);
        Cancel(MentorDropReason.ContractFailure);
        _log.LogError($"Orb Mentor blocked for this lifecycle: {reason}");
    }

    public void QuarantineDomain(MentorDomain domain, string reason)
    {
        if (domain == MentorDomain.Spells)
        {
            BlockPermanent(reason);
            return;
        }
        var failure = _failures.For(domain);
        if (failure.PermanentReason is not null) return;
        failure.BlockPermanent(reason);
        CancelDomain(domain, MentorDropReason.ContractFailure, clearCatalog: true);
        _log.LogError($"Orb Mentor {domain} sharing permanently disabled: {reason}");
    }

    private void BlockDomainTransient(MentorDomain domain, string reason)
    {
        if (domain == MentorDomain.Spells)
        {
            BlockTransient(reason);
            return;
        }
        _failures.For(domain).BlockTransient(reason);
        CancelDomain(domain, MentorDropReason.ContractFailure, clearCatalog: true);
        _log.LogError($"Orb Mentor {domain} sharing blocked for this lifecycle: {reason}");
    }

    private void FailDomainContract(MentorDomain domain, string reason)
    {
        if (domain == MentorDomain.Spells) BlockPermanent(reason);
        else QuarantineDomain(domain, reason);
    }

    private bool ProcessReconcileStep(MentorDomain domain, DomainCatalog catalog, long now)
    {
        try
        {
            if (catalog.Reconcile is null)
            {
                if (!ResolveSchema(domain, catalog)) return false;
                var registry = catalog.RegistryField!.GetValue(null) as IEnumerable;
                if (registry is null)
                {
                    FailDomainContract(domain, $"{NativeTypeName(domain)}.All is unavailable");
                    return false;
                }
                var capacity = registry is ICollection collection ? collection.Count : 0;
                var requestGeneration = catalog.ReconcileRequests.Current;
                catalog.NeedsReconcile = false;
                catalog.Reconcile = new ReconcileWork(
                    registry.GetEnumerator(),
                    capacity,
                    catalog.Initialized,
                    requestGeneration);
                return true;
            }

            var work = catalog.Reconcile;
            if (!work.EnumerationComplete)
            {
                bool hasNext;
                try { hasNext = work.Enumerator.MoveNext(); }
                catch (InvalidOperationException)
                {
                    work.Dispose();
                    catalog.Reconcile = null;
                    RequestReconcile(catalog);
                    catalog.NextReconcile = now;
                    return true;
                }
                if (hasNext)
                {
                    var value = work.Enumerator.Current;
                    if (value is null || IsDestroyed(value)) return true;
                    var uuid = ReadUuid(catalog.IdentityMethod!, value);
                    if (string.IsNullOrWhiteSpace(uuid) || work.ById.ContainsKey(uuid))
                    {
                        FailDomainContract(domain, $"registered {domain} UUID is missing or duplicated");
                        return false;
                    }
                    object? artifactContainer = null;
                    if (domain == MentorDomain.Artifacts)
                        artifactContainer = catalog.ArtifactContainerMethod!.Invoke(value, null);
                    var sameIdentity = catalog.ById.TryGetValue(uuid, out var existing) &&
                                       ReferenceEquals(existing.Item, value);
                    if (work.CatalogWasInitialized && !sameIdentity)
                        MarkReconcileIdentityChanged(domain, catalog, work);
                    var entry = sameIdentity
                        ? existing!
                        : new NativeEntry(uuid, value, SafeName(value), artifactContainer);
                    entry.ArtifactContainer = artifactContainer;
                    if (!work.Order.TryAdd(uuid, entry))
                    {
                        FailDomainContract(domain, $"registered {domain} UUID is duplicated");
                        return false;
                    }
                    work.ById.Add(uuid, entry);
                    work.ByObject.Add(value, entry);
                    return true;
                }

                work.EnumerationComplete = true;
                if (work.CatalogWasInitialized && work.Order.Count != catalog.Entries.Count)
                    MarkReconcileIdentityChanged(domain, catalog, work);
                return true;
            }

            if (!work.OrderingComplete)
            {
                if (work.Order.TryTakeNext(out var orderedEntry))
                {
                    work.Entries.Add(orderedEntry);
                    return true;
                }
                work.OrderingComplete = true;
                return true;
            }

            catalog.Entries = work.Entries;
            catalog.ById = work.ById;
            catalog.ByObject = work.ByObject;
            work.Dispose();
            catalog.Reconcile = null;
            catalog.Refresh = null;
            catalog.Initialized = true;
            catalog.NeedsReconcile = !catalog.ReconcileRequests.IsCurrent(work.RequestGeneration);
            RequestRelationshipRefresh(catalog, advanceProgressionEpoch: false);
            catalog.NextReconcile = catalog.NeedsReconcile ? now : now + ReconcileTicks;
            return true;
        }
        catch (Exception ex)
        {
            catalog.Reconcile?.Dispose();
            catalog.Reconcile = null;
            FailDomainContract(domain, $"{domain} catalog reconciliation failed: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private void MarkReconcileIdentityChanged(MentorDomain domain, DomainCatalog catalog, ReconcileWork work)
    {
        if (work.IdentityChanged) return;
        work.IdentityChanged = true;
        RecordPendingDrops(domain, MentorDropReason.CatalogIdentityChanged);
        MentorIdentityTransition.CancelPendingOnChange(true, _domains[domain]);
        _domains[domain].IdentityDeferrals.Clear();
        RequestRelationshipRefresh(catalog, advanceProgressionEpoch: true);
    }

    private bool ResolveSchema(MentorDomain domain, DomainCatalog catalog)
    {
        if (catalog.ExpectedType is not null) return true;
        var expected = Type.GetType(NativeTypeName(domain) + ", Assembly-CSharp", false);
        var idType = Type.GetType("IdScriptableObject, Assembly-CSharp", false);
        if (expected is null || idType is null) { FailDomainContract(domain, $"{domain} native type is unavailable"); return false; }
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
            FailDomainContract(domain, $"{domain} native catalog/accessor contract is unavailable");
            return false;
        }
        return true;
    }

    private bool ProcessRefreshStep(MentorDomain domain, DomainCatalog catalog, long now)
    {
        try
        {
            var work = catalog.Refresh;
            if (work is null || work.ProgressionEpoch != catalog.ProgressionEpoch ||
                !catalog.RelationshipRequests.IsCurrent(work.RequestGeneration))
            {
                var requestGeneration = catalog.RelationshipRequests.Current;
                var wasDirty = catalog.RelationshipDirty;
                catalog.RelationshipDirty = false;
                catalog.Refresh = new RefreshWork(catalog.ProgressionEpoch, requestGeneration, wasDirty, catalog.Entries.Count);
                return true;
            }
            if (!work.ReadComplete)
            {
                if (work.ReadIndex < catalog.Entries.Count)
                {
                    var entry = catalog.Entries[work.ReadIndex++];
                    if (IsDestroyed(entry.Item))
                    {
                        catalog.Refresh = null;
                        RequestReconcile(catalog);
                        return true;
                    }
                    var mastery = Convert.ToInt32(catalog.MasteryField!.GetValue(entry.Item) ?? 0);
                    var discovered = Convert.ToBoolean(catalog.AvailabilityMethod!.Invoke(entry.Item, null) ?? false);
                    work.Changed |= mastery != entry.MasteryLevel || discovered != entry.IsDiscovered;
                    entry.MasteryLevel = mastery;
                    entry.IsDiscovered = discovered;
                    if (discovered && mastery > work.HighestMastery) work.HighestMastery = mastery;
                    return true;
                }
                MentorProgressionObservation.AdvanceRefreshEpoch(
                    ref catalog.ProgressionEpoch,
                    catalog.RelationshipEpoch,
                    work.Changed);
                work.ProgressionEpoch = catalog.ProgressionEpoch;
                work.ReadComplete = true;
                if (!work.WasDirty && !work.Changed)
                {
                    catalog.Refresh = null;
                    catalog.RelationshipDirty = !catalog.RelationshipRequests.IsCurrent(work.RequestGeneration);
                    catalog.NextLiveRefresh = catalog.RelationshipDirty ? now : now + LiveRefreshTicks;
                }
                return true;
            }
            if (work.BuildIndex < catalog.Entries.Count)
            {
                var entry = catalog.Entries[work.BuildIndex++];
                if (!entry.IsDiscovered) return true;
                var recipe = new MentorRecipe(entry.Uuid, entry.MasteryLevel, true);
                work.Discovered.Add(recipe);
                work.DiscoveredIds.Add(recipe.Uuid);
                if (entry.MasteryLevel == work.HighestMastery) work.MentorIds.Add(entry.Uuid);
                else if (entry.MasteryLevel < work.HighestMastery)
                {
                    work.Recipients.Add(recipe);
                    work.RecipientIds.Add(recipe.Uuid);
                }
                return true;
            }
            catalog.HighestMastery = work.HighestMastery;
            catalog.MentorIds = work.MentorIds;
            catalog.Recipients = work.Recipients;
            catalog.Relationship = MentorRelationshipSnapshot.CreatePreindexed(
                work.ProgressionEpoch, work.HighestMastery, work.Discovered, work.Recipients,
                work.DiscoveredIds, work.RecipientIds);
            catalog.Evidence = MentorRelationshipEvidence.FromSnapshot(catalog.Relationship);
            catalog.Refresh = null;
            catalog.RelationshipDirty = !catalog.RelationshipRequests.IsCurrent(work.RequestGeneration);
            catalog.RelationshipEpoch = catalog.ProgressionEpoch;
            catalog.NextLiveRefresh = catalog.RelationshipDirty ? now : now + LiveRefreshTicks;
            return true;
        }
        catch (Exception ex)
        {
            catalog.Refresh = null;
            FailDomainContract(domain, $"{domain} live relationship refresh failed: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private double SharePercent(MentorDomain domain) => domain switch
    {
        MentorDomain.Artifacts => _config.ArtifactSharePercent.Value,
        MentorDomain.Alchemy => _config.AlchemySharePercent.Value,
        _ => _config.SharePercent.Value,
    };

    private bool DomainConfigured(MentorDomain domain) => domain switch
    {
        MentorDomain.Artifacts => _config.ArtifactsEnabled.Value,
        MentorDomain.Alchemy => _config.AlchemyEnabled.Value,
        _ => true,
    };

    private bool DomainBlocked(MentorDomain domain) =>
        _failures.IsDomainBlocked(domain);

    private bool DomainEnabled(MentorDomain domain) => DomainConfigured(domain) && !DomainBlocked(domain);

    private string DomainStatus(MentorDomain domain, double percent)
    {
        if (!DomainConfigured(domain)) return $"{domain} off";
        if (DomainBlocked(domain)) return $"{domain} blocked";
        return $"{domain} {percent:0.##}%";
    }

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
