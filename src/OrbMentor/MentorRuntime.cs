using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using OrbModding.Common;
using UnityEngine;

namespace OrbMentor;

internal enum MentorDomain { Spells, Artifacts, Alchemy }

internal sealed class MentorRuntime : IDisposable
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
        public FieldInfo? ExperienceField;
        public MethodInfo? AvailabilityMethod;
        public MethodInfo? IdentityMethod;
        public MethodInfo? RegistryLookupMethod;
        public readonly object?[] RegistryLookupArguments = new object?[1];
        public MethodInfo? ArtifactContainerMethod;
        public List<MentorRecipe> Recipients = new();
        public MentorRelationshipSnapshot? Relationship;
        public readonly MentorRelationshipEvidenceBuffer EvidenceBuffer = new();
        public MentorRelationshipEvidence? Evidence => EvidenceBuffer.Head;
        public MentorRelationshipRequirement? Requirement;
        public long NextLiveRefresh;
        public long NextReconcile;
        public int HighestMastery = int.MinValue;
        public bool Initialized;
        public bool RelationshipDirty = true;
        public bool NeedsReconcile = true;
        public object? LastMutationEvidence;
        public long ProgressionEpoch;
        public long RelationshipEpoch;
        public long SettledRefreshGeneration;
        public readonly MentorWorkGeneration ReconcileRequests = new();
        public readonly MentorWorkGeneration RelationshipRequests = new();
        public ReconcileWork? Reconcile;
        public RefreshWork? Refresh;
    }

    private readonly struct GrantMutationState
    {
        public GrantMutationState(int masteryLevel, MentorAmount experience)
        {
            MasteryLevel = masteryLevel;
            Experience = experience;
        }

        public int MasteryLevel { get; }
        public MentorAmount Experience { get; }

        public override string ToString() =>
            $"mastery={MasteryLevel},xp={Experience.Mantissa:R}e{Experience.Exponent}";
    }

    private sealed class DomainState : MentorPendingWork
    {
        public readonly HashSet<string> IdentityDeferrals = new(StringComparer.Ordinal);
        public long NextDistributionTimestamp;
        public long NextSummaryTimestamp;
        public string MentorSummary = "None";
    }

    private readonly struct DomainFeatureStatusFingerprint : IEquatable<DomainFeatureStatusFingerprint>
    {
        public DomainFeatureStatusFingerprint(
            bool configured,
            MentorFeatureFailureKind failure,
            string? failureReason,
            AutomationDecisionCode failureCause,
            MentorDomainUnlockSnapshot unlock,
            bool catalogReady)
        {
            Configured = configured;
            Failure = failure;
            FailureReason = failureReason;
            FailureCause = failureCause;
            UnlockState = unlock.State;
            UnlockReasonCode = unlock.StatusReasonCode;
            UnlockReason = unlock.Reason;
            CatalogReady = catalogReady;
        }

        public bool Configured { get; }
        public MentorFeatureFailureKind Failure { get; }
        public string? FailureReason { get; }
        public AutomationDecisionCode FailureCause { get; }
        public MentorDomainUnlockState UnlockState { get; }
        public FeatureStatusReasonCode UnlockReasonCode { get; }
        public string UnlockReason { get; }
        public bool CatalogReady { get; }

        public MentorDomainUnlockSnapshot Unlock =>
            new(UnlockState, UnlockReason, UnlockReasonCode);

        public bool Equals(DomainFeatureStatusFingerprint other) =>
            Configured == other.Configured &&
            Failure == other.Failure &&
            FailureCause == other.FailureCause &&
            string.Equals(FailureReason, other.FailureReason, StringComparison.Ordinal) &&
            UnlockState == other.UnlockState &&
            UnlockReasonCode == other.UnlockReasonCode &&
            string.Equals(UnlockReason, other.UnlockReason, StringComparison.Ordinal) &&
            CatalogReady == other.CatalogReady;
    }

    private readonly struct FeatureStatusFingerprint : IEquatable<FeatureStatusFingerprint>
    {
        public FeatureStatusFingerprint(
            bool parentConfigured,
            bool emergencyDisabled,
            MentorFeatureFailureKind globalFailure,
            string? globalFailureReason,
            AutomationDecisionCode globalFailureCause,
            long lifecycleGeneration,
            DomainFeatureStatusFingerprint spells,
            DomainFeatureStatusFingerprint artifacts,
            DomainFeatureStatusFingerprint alchemy)
        {
            ParentConfigured = parentConfigured;
            EmergencyDisabled = emergencyDisabled;
            GlobalFailure = globalFailure;
            GlobalFailureReason = globalFailureReason;
            GlobalFailureCause = globalFailureCause;
            LifecycleGeneration = lifecycleGeneration;
            Spells = spells;
            Artifacts = artifacts;
            Alchemy = alchemy;
        }

        public bool ParentConfigured { get; }
        public bool EmergencyDisabled { get; }
        public MentorFeatureFailureKind GlobalFailure { get; }
        public string? GlobalFailureReason { get; }
        public AutomationDecisionCode GlobalFailureCause { get; }
        public long LifecycleGeneration { get; }
        public DomainFeatureStatusFingerprint Spells { get; }
        public DomainFeatureStatusFingerprint Artifacts { get; }
        public DomainFeatureStatusFingerprint Alchemy { get; }

        public DomainFeatureStatusFingerprint For(MentorDomain domain) => domain switch
        {
            MentorDomain.Spells => Spells,
            MentorDomain.Artifacts => Artifacts,
            _ => Alchemy,
        };

        public bool Equals(FeatureStatusFingerprint other) =>
            ParentConfigured == other.ParentConfigured &&
            EmergencyDisabled == other.EmergencyDisabled &&
            GlobalFailure == other.GlobalFailure &&
            GlobalFailureCause == other.GlobalFailureCause &&
            string.Equals(GlobalFailureReason, other.GlobalFailureReason, StringComparison.Ordinal) &&
            LifecycleGeneration == other.LifecycleGeneration &&
            Spells.Equals(other.Spells) &&
            Artifacts.Equals(other.Artifacts) &&
            Alchemy.Equals(other.Alchemy);
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
            DiscoveredIndices = new Dictionary<string, int>(Math.Max(0, capacity), StringComparer.Ordinal);
            RecipientIndices = new Dictionary<string, int>(Math.Max(0, capacity), StringComparer.Ordinal);
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
        public Dictionary<string, int> DiscoveredIndices { get; }
        public Dictionary<string, int> RecipientIndices { get; }
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
    private static readonly long UnlockRefreshTicks = Math.Max(1, Stopwatch.Frequency / 4);
    private static readonly long EquippedSnapshotTicks = Math.Max(1, Stopwatch.Frequency / 4);
    private static readonly long SummaryRefreshTicks = Math.Max(1, Stopwatch.Frequency);
    private static readonly long LiveRefreshTicks = Math.Max(1, Stopwatch.Frequency);
    private static readonly long ReconcileTicks = Math.Max(1, Stopwatch.Frequency * 10);

    private readonly MentorConfig _config;
    private readonly ManualLogSource _log;
    private readonly MentorFailureRegistry _failures = new();
    private readonly MentorDomainUnlockGate _unlockGate;
    private readonly MentorDomainUnlockSnapshot[] _domainUnlocks = new MentorDomainUnlockSnapshot[DomainOrder.Length];
    private readonly MentorLifecycleSignal _lifecycleReset = new();
    private readonly Dictionary<MentorDomain, DomainState> _domains = new();
    private readonly Dictionary<MentorDomain, DomainCatalog> _catalogs = new();
    private readonly bool[] _domainResetPending = new bool[DomainOrder.Length];
    private readonly MentorAlchemyDomainGate _alchemyDomainGate;
    private readonly MentorCoordinatorWork? _coordinatorWork;
    private readonly Func<long> _readTimestamp;
    private readonly Func<long> _readLifecycleGeneration;
    private readonly FeatureStatusRegistration?[] _domainStatusRegistrations =
        new FeatureStatusRegistration?[DomainOrder.Length];
    private readonly FeatureStatusSnapshot[] _domainFeatureStatuses =
        new FeatureStatusSnapshot[DomainOrder.Length];
    private FeatureStatusRegistration? _rootStatusRegistration;
    private FeatureStatusSnapshot _rootFeatureStatus;
    private FeatureStatusFingerprint _featureStatusFingerprint;
    private bool _hasFeatureStatusFingerprint;
    private long _statusLifecycleGeneration;
    private bool _guarded;
    private bool _artifactWasEnabled;
    private bool _alchemyWasEnabled;
    private bool _captureFailureLogged;
    private int _nextPlanningDomain;
    private int _nextGrantDomain;
    private object? _activeArtifact;
    private object? _activeArtifactContainer;
    private MentorAmount _activeArtifactXp;
    private readonly HashSet<string> _equippedSpellUuids = new(StringComparer.Ordinal);
    private readonly HashSet<string> _equippedSpellScratch = new(StringComparer.Ordinal);
    private MentorSpellSourcePolicy _spellSourcePolicy;
    private long _nextEquippedSnapshot;
    private bool _equippedSnapshotReady;
    private long _nextAlchemyDomainInitialization;
    private long _nextUnlockRefresh;

    internal MentorRuntime(
        MentorConfig config,
        ManualLogSource log,
        SuitePerformanceCoordinator? coordinator = null,
        Func<long>? readFrameIdentity = null,
        MentorAlchemyDomainGate? alchemyDomainGate = null,
        MentorDomainUnlockGate? unlockGate = null,
        Func<long>? readTimestamp = null,
        FeatureStatusRegistry? featureStatusRegistry = null,
        Func<long>? readLifecycleGeneration = null)
    {
        _config = config;
        _log = log;
        _alchemyDomainGate = alchemyDomainGate ?? new MentorAlchemyDomainGate();
        _unlockGate = unlockGate ?? new MentorDomainUnlockGate();
        _readTimestamp = readTimestamp ?? Stopwatch.GetTimestamp;
        _readLifecycleGeneration = readLifecycleGeneration ??
            (() => GameLifecycleMonitor.Shared.Current.Generation);
        _statusLifecycleGeneration = Math.Max(0, _readLifecycleGeneration());
        if (coordinator is not null)
            _coordinatorWork = new MentorCoordinatorWork(
                coordinator,
                readFrameIdentity ?? throw new ArgumentNullException(nameof(readFrameIdentity)));
        Diagnostics = new MentorDiagnostics();
        foreach (var domain in DomainOrder)
        {
            _domains.Add(domain, new DomainState());
            _catalogs.Add(domain, new DomainCatalog());
            _domainUnlocks[(int)domain] = new MentorDomainUnlockSnapshot(
                MentorDomainUnlockState.Waiting,
                "native progression unlock is awaiting lifecycle evaluation",
                FeatureStatusReasonCode.Initializing);
        }
        _artifactWasEnabled = config.ArtifactsEnabled.Value;
        _alchemyWasEnabled = config.AlchemyEnabled.Value;
        _spellSourcePolicy = config.SpellSourcePolicy.Value;
        if (BigDoubleMantissa is null || BigDoubleExponent is null)
            BlockPermanent("BigDouble mantissa/exponent contract is unavailable");
        RefreshFeatureStatus();
        if (featureStatusRegistry is not null) RegisterFeatureStatuses(featureStatusRegistry);
    }

    internal MentorDiagnostics Diagnostics { get; }
    internal AlchemyDomainClassifierStatus AlchemyClassifierStatus => _alchemyDomainGate.Status;
    public string? BlockedReason => _failures.Global.Reason;
    public bool IsBlocked => _failures.Global.IsBlocked;
    public bool IsWaiting
    {
        get
        {
            if (!_config.Active) return false;
            for (var index = 0; index < DomainOrder.Length; index++)
            {
                var domain = DomainOrder[index];
                if (DomainConfigured(domain) && !DomainBlocked(domain) && !DomainUnlocked(domain))
                    return true;
            }
            return false;
        }
    }

    internal FeatureStatusSnapshot RootFeatureStatus => _rootFeatureStatus;
    internal FeatureStatusSnapshot DomainFeatureStatus(MentorDomain domain) =>
        _domainFeatureStatuses[(int)domain];
    internal long FeatureStatusProjectionCount { get; private set; }

    internal void RefreshFeatureStatus()
    {
        var fingerprint = CaptureFeatureStatusFingerprint();
        if (_hasFeatureStatusFingerprint && _featureStatusFingerprint.Equals(fingerprint)) return;
        _featureStatusFingerprint = fingerprint;
        _hasFeatureStatusFingerprint = true;
        FeatureStatusProjectionCount++;

        foreach (var domain in DomainOrder)
        {
            var domainFingerprint = fingerprint.For(domain);
            var input = new MentorDomainFeatureStatusInput(
                fingerprint.ParentConfigured,
                domainFingerprint.Configured,
                fingerprint.EmergencyDisabled,
                fingerprint.GlobalFailure,
                fingerprint.GlobalFailureReason,
                fingerprint.GlobalFailureCause,
                domainFingerprint.Failure,
                domainFingerprint.FailureReason,
                domainFingerprint.FailureCause,
                domainFingerprint.Unlock,
                domainFingerprint.CatalogReady,
                fingerprint.LifecycleGeneration);
            var status = MentorFeatureStatus.ProjectDomain(domain, input);
            _domainFeatureStatuses[(int)domain] = status;
            _domainStatusRegistrations[(int)domain]?.Update(status);
        }

        _rootFeatureStatus = MentorFeatureStatus.ProjectRoot(
            fingerprint.ParentConfigured,
            fingerprint.EmergencyDisabled,
            fingerprint.GlobalFailure,
            fingerprint.GlobalFailureReason,
            fingerprint.GlobalFailureCause,
            _domainFeatureStatuses,
            fingerprint.LifecycleGeneration);
        _rootStatusRegistration?.Update(_rootFeatureStatus);
    }

    private FeatureStatusFingerprint CaptureFeatureStatusFingerprint() => new(
        _config.Enabled.Value && _config.Mode.Value == MentorOperationMode.Active,
        _config.EmergencyDisable.Value,
        FailureKind(_failures.Global),
        _failures.Global.Reason,
        _failures.Global.Circuit.Cause,
        _statusLifecycleGeneration,
        CaptureDomainFeatureStatusFingerprint(MentorDomain.Spells),
        CaptureDomainFeatureStatusFingerprint(MentorDomain.Artifacts),
        CaptureDomainFeatureStatusFingerprint(MentorDomain.Alchemy));

    private DomainFeatureStatusFingerprint CaptureDomainFeatureStatusFingerprint(MentorDomain domain) => new(
        DomainConfigured(domain),
        FailureKind(_failures.For(domain)),
        _failures.For(domain).Reason,
        _failures.For(domain).Circuit.Cause,
        DomainUnlock(domain),
        CatalogReady(_catalogs[domain]));

    private void RegisterFeatureStatuses(FeatureStatusRegistry registry)
    {
        try
        {
            foreach (var domain in DomainOrder)
                _domainStatusRegistrations[(int)domain] =
                    registry.Register(_domainFeatureStatuses[(int)domain]);
            _rootStatusRegistration = registry.Register(_rootFeatureStatus);
        }
        catch
        {
            _rootStatusRegistration?.Dispose();
            _rootStatusRegistration = null;
            for (var index = _domainStatusRegistrations.Length - 1; index >= 0; index--)
            {
                _domainStatusRegistrations[index]?.Dispose();
                _domainStatusRegistrations[index] = null;
            }
            throw;
        }
    }

    private static bool CatalogReady(DomainCatalog catalog) =>
        catalog.Initialized && catalog.Relationship is not null && !catalog.RelationshipDirty;

    private static MentorFeatureFailureKind FailureKind(MentorFailureState failure) =>
        failure.IsPermanent
            ? MentorFeatureFailureKind.Permanent
            : failure.IsTransient
                ? MentorFeatureFailureKind.Transient
                : MentorFeatureFailureKind.None;

    internal MentorDomainUnlockSnapshot DomainUnlock(MentorDomain domain) => _domainUnlocks[(int)domain];

    public string CurrentMentor(MentorDomain domain)
    {
        var state = _domains[domain];
        if (!_config.Active || !DomainConfigured(domain)) return state.MentorSummary = "Inactive";
        if (DomainBlocked(domain))
            return state.MentorSummary = $"Blocked: {DomainBlockedReason(domain)}";
        var unlock = DomainUnlock(domain);
        if (!unlock.IsUnlocked) return state.MentorSummary = $"Waiting: {unlock.Reason}";
        var now = Stopwatch.GetTimestamp();
        if (now < state.NextSummaryTimestamp) return state.MentorSummary;
        state.NextSummaryTimestamp = now + SummaryRefreshTicks;
        var catalog = _catalogs[domain];
        if (!catalog.Initialized || catalog.RelationshipDirty) return state.MentorSummary = "Pending";
        if (domain == MentorDomain.Spells && _config.SpellSourcePolicy.Value == MentorSpellSourcePolicy.EquippedSpells)
        {
            if (!_equippedSnapshotReady) return state.MentorSummary = "Pending equipped loadout";
            if (_equippedSpellUuids.Count == 0) return state.MentorSummary = "None equipped";
            var equippedNames = new List<string>(Math.Min(3, _equippedSpellUuids.Count));
            foreach (var entry in catalog.Entries)
            {
                if (equippedNames.Count >= 3) break;
                if (_equippedSpellUuids.Contains(entry.Uuid)) equippedNames.Add(entry.DisplayName);
            }
            var extraEquipped = Math.Max(0, _equippedSpellUuids.Count - equippedNames.Count);
            return state.MentorSummary = equippedNames.Count == 0
                ? $"{_equippedSpellUuids.Count} equipped"
                : string.Join(", ", equippedNames) + (extraEquipped > 0 ? $" +{extraEquipped}" : string.Empty);
        }
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
        var spells = DomainStatus(MentorDomain.Spells, _config.SharePercent.Value);
        var artifact = DomainStatus(MentorDomain.Artifacts, _config.ArtifactSharePercent.Value);
        var alchemy = DomainStatus(MentorDomain.Alchemy, _config.AlchemySharePercent.Value);
        var warning = _config.EconomyMode.Value == MentorEconomyMode.PerRecipient ? " Warning: total bonus scales with recipient count." : string.Empty;
        var drops = Diagnostics.DroppedEvents + Diagnostics.DroppedGrants;
        var dropSummary = drops > 0 ? $" Dropped work: {drops}." : string.Empty;
        return $"{_config.EconomyMode.Value}. {spells} from {_config.SpellSourcePolicy.Value}; {artifact}; {alchemy}.{warning}{dropSummary}";
    }

    public void Observe(SpellRecipeSO source, BigDouble xp)
    {
        if (TryAmount(xp, out var amount)) CaptureDomain(MentorDomain.Spells, source, amount);
    }

    public void NotifyEquippedLoadoutChanged()
    {
        _equippedSnapshotReady = false;
        _equippedSpellUuids.Clear();
        _nextEquippedSnapshot = 0;
    }

    public void ObserveAlchemy(object source, BigDouble xp)
    {
        if (!_config.Active || !DomainEnabled(MentorDomain.Alchemy)) return;
        if (!_alchemyDomainGate.TryGetCached(source, out var classification))
        {
            RequestReconcile(_catalogs[MentorDomain.Alchemy]);
            return;
        }
        if (classification.Domain == AlchemyGameplayDomain.ScholarConcept && classification.IsMutationGrade) return;
        if (classification.Domain != AlchemyGameplayDomain.OrdinaryAlchemy || !classification.IsMutationGrade)
        {
            if (_alchemyDomainGate.Status == AlchemyDomainClassifierStatus.Ready ||
                _alchemyDomainGate.Status == AlchemyDomainClassifierStatus.Blocked)
                BlockAlchemyDomain("XP capture", classification);
            return;
        }
        if (!TryAmount(xp, out var amount)) return;
        CaptureDomain(MentorDomain.Alchemy, source, amount);
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
            if (domain == MentorDomain.Spells &&
                _config.SpellSourcePolicy.Value == MentorSpellSourcePolicy.EquippedSpells &&
                (!_equippedSnapshotReady || !_equippedSpellUuids.Contains(uuid)))
            {
                Diagnostics.RecordDrop(MentorDropReason.SourceIneligible, 1, grant: false);
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
                if (sourceChanged) AppendRelationshipEvidence(domain, catalog, uuid, mastery, discovered);
            }
            var relationship = !catalog.RelationshipDirty && !catalog.NeedsReconcile &&
                catalog.Reconcile is null && catalog.Refresh is null
                ? catalog.Relationship
                : null;
            var requirement = relationship is null && catalog.Refresh is not null &&
                catalog.RelationshipRequests.IsCurrent(catalog.Refresh.RequestGeneration)
                ? GetRelationshipRequirement(catalog, catalog.Refresh.RequestGeneration)
                : null;
            var evidence = relationship is null && requirement is null ? catalog.Evidence : null;
            if (relationship is null && evidence is null && requirement is null)
                requirement = GetRelationshipRequirement(catalog, catalog.RelationshipRequests.Current);
            var result = _domains[domain].Captures.Capture(
                new MentorCaptureKey(
                    source, uuid, mastery, discovered, catalog.ProgressionEpoch,
                    relationship, evidence, requirement), amount);
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
        if (!DomainEnabled(domain)) return;
        if (domain == MentorDomain.Alchemy)
        {
            if (!DomainConfigured(domain)) return;
            if (changedSource is not null)
            {
                if (!_alchemyDomainGate.TryGetCached(changedSource, out var classification))
                {
                    RequestReconcile(_catalogs[domain]);
                    return;
                }
                if (classification.Domain == AlchemyGameplayDomain.ScholarConcept && classification.IsMutationGrade) return;
                if (classification.Domain != AlchemyGameplayDomain.OrdinaryAlchemy || !classification.IsMutationGrade)
                {
                    if (_alchemyDomainGate.Status == AlchemyDomainClassifierStatus.Ready ||
                        _alchemyDomainGate.Status == AlchemyDomainClassifierStatus.Blocked)
                        BlockAlchemyDomain("progression observation", classification);
                    return;
                }
            }
        }
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
                AppendRelationshipEvidence(domain, catalog, uuid, mastery, discovered);
            if (entry is not null)
            {
                entry.MasteryLevel = mastery;
                entry.IsDiscovered = discovered;
            }
        }
        catch { }
    }

    public bool TryGetStableProgressionEntityId(MentorDomain domain, object? changedSource, out string entityId)
    {
        entityId = string.Empty;
        if (changedSource is null) return false;
        var catalog = _catalogs[domain];
        if (catalog.ByObject.TryGetValue(changedSource, out var entry))
        {
            entityId = entry.Uuid;
            return !string.IsNullOrWhiteSpace(entityId);
        }
        return false;
    }

    public void LateTick()
    {
        if (_lifecycleReset.TryConsume()) ResetLifecycle();
        foreach (var domain in DomainOrder)
        {
            var index = (int)domain;
            if (!_domainResetPending[index]) continue;
            _domainResetPending[index] = false;
            if (domain == MentorDomain.Alchemy) InvalidateAlchemyDomainLifecycle();
            CancelDomain(domain, MentorDropReason.LifecycleReset, clearCatalog: true);
            _failures.For(domain).ResetLifecycle();
        }
        var unlockStateChanged = RefreshDomainUnlocks();
        RefreshSpellSourcePolicy();
        RefreshEquippedSpellSnapshot(Stopwatch.GetTimestamp());
        if (!_config.Active || IsBlocked || unlockStateChanged)
        {
            _coordinatorWork?.SetState(false, false, false);
            RefreshFeatureStatus();
            return;
        }
        if (!_config.ArtifactsEnabled.Value && _artifactWasEnabled)
            CancelDomain(MentorDomain.Artifacts, MentorDropReason.Disabled, clearCatalog: false);
        if (!_config.AlchemyEnabled.Value && _alchemyWasEnabled)
            CancelDomain(MentorDomain.Alchemy, MentorDropReason.Disabled, clearCatalog: false);
        _artifactWasEnabled = _config.ArtifactsEnabled.Value;
        _alchemyWasEnabled = _config.AlchemyEnabled.Value;

        if (_coordinatorWork is null) LateTickLegacy();
        else LateTickCoordinated();
        RefreshFeatureStatus();
    }

    private bool RefreshDomainUnlocks()
    {
        if (!_config.Active)
        {
            _nextUnlockRefresh = 0;
            return false;
        }
        var now = _readTimestamp();
        if (now < _nextUnlockRefresh) return false;
        _nextUnlockRefresh = now + UnlockRefreshTicks;
        var changed = false;
        foreach (var domain in DomainOrder)
        {
            if (DomainBlocked(domain)) continue;
            var previous = _domainUnlocks[(int)domain];
            var current = _unlockGate.Evaluate(domain);
            _domainUnlocks[(int)domain] = current;
            if (current.IsContractBlocked)
            {
                QuarantineUnlockDomain(domain, $"{domain} progression-unlock contract failed: {current.Reason}");
                changed = true;
                continue;
            }
            if (previous.IsUnlocked == current.IsUnlocked) continue;
            changed = true;
            CancelDomain(domain, MentorDropReason.LifecycleReset, clearCatalog: true);
            if (current.IsUnlocked)
                _log.LogInfo($"Mentor {domain} sharing is available; native mastery and domain progression are unlocked.");
            else
                _log.LogInfo($"Mentor {domain} sharing is waiting: {current.Reason} Pending work was cleared.");
        }
        return changed;
    }

    private void RefreshSpellSourcePolicy()
    {
        var current = _config.SpellSourcePolicy.Value;
        if (current == _spellSourcePolicy) return;
        _spellSourcePolicy = current;
        CancelDomain(MentorDomain.Spells, MentorDropReason.Disabled, clearCatalog: false);
        _equippedSpellUuids.Clear();
        _equippedSnapshotReady = false;
        _nextEquippedSnapshot = 0;
        _log.LogInfo($"Mentor spell sharing sources changed to {current}; pending spell grants were cleared.");
    }

    private void RefreshEquippedSpellSnapshot(long now)
    {
        if (!_config.Active || !DomainUnlocked(MentorDomain.Spells) ||
            _spellSourcePolicy != MentorSpellSourcePolicy.EquippedSpells)
        {
            _equippedSpellUuids.Clear();
            _equippedSnapshotReady = false;
            return;
        }
        if (now < _nextEquippedSnapshot) return;
        _nextEquippedSnapshot = now + EquippedSnapshotTicks;
        _equippedSpellScratch.Clear();
        try
        {
            var managerType = Type.GetType("SpellManager, Assembly-CSharp", false);
            var manager = FindField(managerType!, "instance")?.GetValue(null);
            var activeSpells = manager is null ? null : FindField(manager.GetType(), "activeSpells")?.GetValue(manager);
            var activeValues = activeSpells is null
                ? null
                : FindField(activeSpells.GetType(), "value")?.GetValue(activeSpells) as IList;
            if (activeValues is null ||
                !string.Equals(activeSpells!.GetType().Name, "SpellListVariable", StringComparison.Ordinal))
            {
                _equippedSpellUuids.Clear();
                _equippedSnapshotReady = false;
                return;
            }
            for (var index = 0; index < activeValues.Count; index++)
            {
                var spell = activeValues[index];
                if (spell is null) continue;
                var reference = Invoke(spell, "get_reference");
                var uuid = reference is null ? null : StableId(reference);
                if (!string.IsNullOrWhiteSpace(uuid)) _equippedSpellScratch.Add(uuid);
            }
            _equippedSpellUuids.Clear();
            foreach (var uuid in _equippedSpellScratch) _equippedSpellUuids.Add(uuid);
            _equippedSnapshotReady = true;
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException || ex is NullReferenceException)
        {
            _equippedSpellUuids.Clear();
            _equippedSnapshotReady = false;
        }
    }

    private void LateTickLegacy()
    {
        var started = Stopwatch.GetTimestamp();
        var cpuBudget = Math.Clamp(_config.CpuBudgetMilliseconds.Value, 0.1, 1.0);

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

    private void LateTickCoordinated()
    {
        var now = Stopwatch.GetTimestamp();
        var cooperativePending = HasCooperativeWork(now);
        var mutationPending = TryFindGrantDomain(now, out var mutationDomain, out var mutationIndex);
        _coordinatorWork!.SetState(true, cooperativePending, mutationPending);

        if (cooperativePending)
        {
            _coordinatorWork.TryRunCooperative(RunCooperativeSlice);
        }

        now = Stopwatch.GetTimestamp();
        cooperativePending = HasCooperativeWork(now);
        mutationPending = TryFindGrantDomain(now, out mutationDomain, out mutationIndex);
        _coordinatorWork.SetState(true, cooperativePending, mutationPending);
        if (mutationPending)
        {
            _coordinatorWork.TryRunMutation(() =>
            {
                _nextGrantDomain = (mutationIndex + 1) % DomainOrder.Length;
                ProcessGrant(mutationDomain);
                return 1;
            });
        }

        now = Stopwatch.GetTimestamp();
        _coordinatorWork.SetState(
            true,
            HasCooperativeWork(now),
            TryFindGrantDomain(now, out _, out _));
    }

    private int RunCooperativeSlice()
    {
        var started = Stopwatch.GetTimestamp();
        var cpuBudget = Math.Clamp(_config.CpuBudgetMilliseconds.Value, 0.1, 1.0);
        var operations = 0;
        var empty = 0;
        while (operations < PlanningOperationsPerFrame &&
               ElapsedMilliseconds(started) < cpuBudget &&
               empty < DomainOrder.Length)
        {
            var domain = DomainOrder[_nextPlanningDomain++ % DomainOrder.Length];
            if (!DomainEnabled(domain) || !ProcessDomainStep(domain, Stopwatch.GetTimestamp()))
            {
                empty++;
                continue;
            }
            operations++;
            empty = 0;
        }
        return operations;
    }

    private bool HasCooperativeWork(long now)
    {
        foreach (var domain in DomainOrder)
        {
            if (!DomainEnabled(domain)) continue;
            if (DomainHasCooperativeWork(domain, now)) return true;
        }
        return false;
    }

    private bool DomainHasCooperativeWork(MentorDomain domain, long now)
    {
        var catalog = _catalogs[domain];
        var state = _domains[domain];
        return MentorDomainMutationEligibility.HasCooperativeWork(
            catalog.Initialized,
            catalog.NeedsReconcile,
            catalog.Reconcile is not null,
            now >= catalog.NextReconcile,
            catalog.RelationshipDirty,
            catalog.Refresh is not null,
            now >= catalog.NextLiveRefresh,
            state.HasCooperativePlanning ||
            state.ParkedGrants.HasReady(catalog.SettledRefreshGeneration));
    }

    private bool TryFindGrantDomain(long now, out MentorDomain domain, out int domainIndex)
    {
        for (var offset = 0; offset < DomainOrder.Length; offset++)
        {
            domainIndex = (_nextGrantDomain + offset) % DomainOrder.Length;
            domain = DomainOrder[domainIndex];
            if (!DomainEnabled(domain)) continue;
            var state = _domains[domain];
            if (!state.Engine.TryPeek(out _, out _) || DomainHasCooperativeWork(domain, now)) continue;
            return true;
        }
        domain = default;
        domainIndex = -1;
        return false;
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
        if (state.ParkedGrants.TryTakeReady(
                catalog.SettledRefreshGeneration,
                out var parkedGrant))
        {
            if (catalog.ById.TryGetValue(parkedGrant.Uuid, out var parkedEntry) &&
                MentorRecipientEligibility.Evaluate(
                    parkedEntry.IsDiscovered,
                    parkedEntry.MasteryLevel,
                    parkedGrant.MasteryCeilingExclusive) == MentorRecipientEligibilityStatus.Eligible)
            {
                state.Engine.Consolidate(parkedGrant);
            }
            else
            {
                state.ParkedGrants.Park(parkedGrant, catalog.SettledRefreshGeneration);
            }
            return true;
        }
        if (state.RelationshipResolution is not null)
        {
            state.RelationshipResolution.Step();
            if (!state.RelationshipResolution.IsComplete) return true;
            var resolvedCapture = state.ResolvingCapture!;
            var relationship = state.RelationshipResolution.Result!;
            state.RelationshipResolution.Dispose();
            state.RelationshipResolution = null;
            state.ResolvingCapture = null;
            try { QualifyCaptured(state, catalog, resolvedCapture, relationship); }
            finally { ReleaseEvidenceCapture(catalog, resolvedCapture); }
            return true;
        }
        if (state.Captures.TryTake(out var captured))
        {
            if (!TryValidateCapturedSource(catalog, captured))
            {
                ReleaseEvidenceCapture(catalog, captured);
                Diagnostics.RecordDrop(MentorDropReason.SourceIdentityChanged, captured.EventCount, grant: false);
                return true;
            }
            if (captured.Key.Relationship is not null)
                QualifyCaptured(state, catalog, captured, captured.Key.Relationship);
            else if (captured.Key.Evidence?.Resolved is not null)
            {
                try { QualifyCaptured(state, catalog, captured, captured.Key.Evidence.Resolved); }
                finally { ReleaseEvidenceCapture(catalog, captured); }
            }
            else if (captured.Key.Evidence is not null)
            {
                state.ResolvingCapture = captured;
                state.RelationshipResolution = new MentorRelationshipResolutionWork(captured.Key.Evidence);
            }
            else if (captured.Key.Requirement?.Resolved is not null)
                QualifyCaptured(state, catalog, captured, captured.Key.Requirement.Resolved);
            else if (captured.Key.Requirement is not null)
            {
                if (!state.Unroutable.Retain(captured))
                    Diagnostics.RecordDrop(MentorDropReason.CaptureOverflow, captured.EventCount, grant: false);
            }
            else
            {
                if (!state.Unroutable.Retain(captured))
                    Diagnostics.RecordDrop(MentorDropReason.CaptureOverflow, captured.EventCount, grant: false);
            }
            return true;
        }
        if (state.ActivePlan is null && state.Sources.HasPending)
        {
            BeginPlan(domain, SharePercent(domain), domain != MentorDomain.Spells);
            return true;
        }
        if (state.ActivePlan is not null)
        {
            if (state.ActivePlan.TryTake(out var grant) &&
                !state.ParkedGrants.TryAccumulate(grant))
                state.Engine.Consolidate(grant);
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
        var equippedSpellPolicy = ReferenceEquals(catalog, _catalogs[MentorDomain.Spells]) &&
            _config.SpellSourcePolicy.Value == MentorSpellSourcePolicy.EquippedSpells;
        var capturedRelationship = equippedSpellPolicy
            ? relationship.ForEquippedCapture(resolvedKey)
            : relationship.ForCapture(resolvedKey);
        var qualification = equippedSpellPolicy
            ? (!resolvedKey.Discovered
                ? MentorQualificationStatus.SourceIneligible
                : capturedRelationship.Recipients.Count > 0
                    ? MentorQualificationStatus.Qualified
                    : MentorQualificationStatus.NoRecipients)
            : MentorRelationshipQualification.Evaluate(
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
        state.Sources.Capture(capturedRelationship, captured.Key.Uuid, captured.Amount, captured.EventCount);
        Diagnostics.RecordQualified(captured.EventCount);
    }

    private static void ReleaseEvidenceCapture(DomainCatalog catalog, MentorCapturedEvent captured)
    {
        captured.ReleaseEvidence();
        if (catalog.EvidenceBuffer.CaptureReferences != 0 || catalog.Relationship is null ||
            catalog.RelationshipDirty || catalog.NeedsReconcile ||
            catalog.Refresh is not null || catalog.Reconcile is not null) return;
        catalog.EvidenceBuffer.Rebase(catalog.Relationship);
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
        state.ActivePlan = state.Engine.CreatePlan(
            batch.Amount,
            percent,
            _config.EconomyMode.Value,
            recipients,
            batch.EventCount,
            batch.Relationship?.HighestMastery ?? _catalogs[domain].HighestMastery);
        if (state.ActivePlan is null)
            Diagnostics.RecordDrop(MentorDropReason.ContractFailure, batch.EventCount, grant: false);
        else if (_config.DetailedLogging.Value)
            _log.LogInfo($"Mentor {domain} batch: sources={batch.SourceCount}, events={batch.EventCount}, recipients={recipients.Count}, share={percent:0.##}%");
    }

    private GrantResult ProcessGrant(MentorDomain domain)
    {
        var state = _domains[domain];
        var catalog = _catalogs[domain];
        if (!state.Engine.TryPeek(out MentorGrant grant)) return GrantResult.NoWork;
        var grantUuid = grant.Uuid;
        var grantAmount = grant.Amount;
        if (DomainHasCooperativeWork(domain, Stopwatch.GetTimestamp()))
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
            if (MentorRecipientEligibility.Evaluate(
                    discovered,
                    mastery,
                    grant.MasteryCeilingExclusive) != MentorRecipientEligibilityStatus.Eligible)
            {
                // Park at this authoritative settled generation. The exact
                // amount is reconsidered cooperatively only after a later
                // real refresh pass completes; this validation does not
                // manufacture its own immediate refresh/retry cycle.
                var parkResult = state.ParkedGrants.Park(
                    grant,
                    catalog.SettledRefreshGeneration);
                if (parkResult == MentorParkResult.Overflow)
                {
                    Diagnostics.RecordParkedGrantOverflow();
                    BlockDomainTransient(
                        domain,
                        $"{domain} parked grant capacity exceeded",
                        AutomationDecisionCode.CapacityOverflow);
                    return GrantResult.Dropped;
                }
                if (!state.Engine.Complete(grant))
                {
                    BlockDomainTransient(domain, $"{domain} pending grant changed while parking");
                    return GrantResult.Dropped;
                }
                Diagnostics.RecordDeferredGrant();
                return GrantResult.Deferred;
            }
            if (DomainHasCooperativeWork(domain, Stopwatch.GetTimestamp()))
            {
                Diagnostics.RecordDeferredGrant();
                return GrantResult.Deferred;
            }
            if (domain == MentorDomain.Alchemy)
            {
                var classification = _alchemyDomainGate.ClassifyAndCache(entry.Item);
                if (classification.Domain != AlchemyGameplayDomain.OrdinaryAlchemy || !classification.IsMutationGrade)
                {
                    BlockAlchemyDomain("final grant validation", classification);
                    return GrantResult.Dropped;
                }
            }
            _guarded = true;
            var progressionEpochBeforeGrant = catalog.ProgressionEpoch;
            var value = new BigDouble(grantAmount.Mantissa, grantAmount.Exponent);
            var expectedExperience = grantAmount;
            var evidence = NativeMutationVerifier.Execute(
                $"Mentor {domain} XP grant",
                grantUuid,
                $"XP exact delta +{grantAmount.Mantissa:R}e{grantAmount.Exponent}",
                () => CaptureGrantState(domain, catalog, entry),
                () =>
                {
                    if (domain == MentorDomain.Spells) ((SpellRecipeSO)entry.Item).GainMasteryExp(value);
                    else if (domain == MentorDomain.Alchemy) InvokeRequired(entry.Item, "GainMasteryXp", value);
                    else GrantArtifact(entry.Item, value);
                },
                (before, after) => AmountsEquivalent(
                    before.Experience.Add(expectedExperience),
                    after.Experience));
            catalog.LastMutationEvidence = evidence;
            if (!evidence.IsVerified)
            {
                BlockDomainTransient(
                    domain,
                    $"native XP mutation postcondition failed: {evidence.Format(state => state.ToString())}",
                    AutomationDecisionCode.PostconditionFailed);
                return GrantResult.Dropped;
            }
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
            state.Engine.Complete(grant);
            Diagnostics.RecordGrant();
            _failures.RecordSuccess(domain);
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
        catalog.Requirement?.MarkUncertain();
        catalog.Requirement = null;
        if (advanceProgressionEpoch) catalog.ProgressionEpoch++;
        catalog.RelationshipRequests.Request();
        catalog.RelationshipDirty = true;
    }

    private void AppendRelationshipEvidence(
        MentorDomain domain,
        DomainCatalog catalog,
        string uuid,
        int mastery,
        bool discovered)
    {
        if (catalog.EvidenceBuffer.Head is null)
        {
            if (catalog.Relationship is null) return;
            catalog.EvidenceBuffer.Rebase(catalog.Relationship);
        }
        var result = catalog.EvidenceBuffer.Append(
            uuid, mastery, discovered, catalog.ProgressionEpoch);
        if (result != MentorEvidenceAppendResult.Overflow) return;

        var state = _domains[domain];
        var overflowEvents = state.Captures.TransferEvidence(catalog.EvidenceBuffer, state.Unroutable);
        if (state.ResolvingCapture?.Key.Evidence is not null &&
            state.ResolvingCapture.Key.Evidence.BelongsTo(catalog.EvidenceBuffer))
        {
            var resolving = state.ResolvingCapture;
            state.RelationshipResolution?.Dispose();
            state.RelationshipResolution = null;
            state.ResolvingCapture = null;
            if (resolving.Key.Evidence.Resolved is not null)
            {
                if (!state.Captures.RequeueResolved(resolving, resolving.Key.Evidence.Resolved))
                    overflowEvents += resolving.EventCount;
            }
            else if (!state.Unroutable.Retain(resolving)) overflowEvents += resolving.EventCount;
            resolving.ReleaseEvidence();
        }
        if (overflowEvents > 0)
            Diagnostics.RecordDrop(MentorDropReason.CaptureOverflow, overflowEvents, grant: false);

        // Every immutable head backed by this buffer is now either resolved or
        // retained without routing. Drop the unsafe history and let the active
        // bounded pass finish; the new request forces an immediate authoritative
        // follow-up before a fresh evidence basis is published.
        catalog.EvidenceBuffer.Invalidate();
        catalog.Requirement?.MarkUncertain();
        catalog.Requirement = null;
        catalog.RelationshipRequests.Request();
        catalog.RelationshipDirty = true;
    }

    private static MentorRelationshipRequirement GetRelationshipRequirement(
        DomainCatalog catalog,
        long requestGeneration)
    {
        if (catalog.Requirement?.RequestGeneration == requestGeneration && !catalog.Requirement.IsUncertain)
            return catalog.Requirement;
        catalog.Requirement?.MarkUncertain();
        return catalog.Requirement = new MentorRelationshipRequirement(requestGeneration);
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

    private static GrantMutationState CaptureGrantState(
        MentorDomain domain,
        DomainCatalog catalog,
        NativeEntry entry)
    {
        var mastery = Convert.ToInt32(catalog.MasteryField!.GetValue(entry.Item) ?? 0);
        object? nativeExperience;
        if (domain == MentorDomain.Artifacts)
        {
            var container = entry.ArtifactContainer ??
                catalog.ArtifactContainerMethod!.Invoke(entry.Item, Array.Empty<object>()) ??
                throw new MissingMemberException("artifact experience container unavailable");
            entry.ArtifactContainer = container;
            nativeExperience = InvokeRequired(container, "GetExperience");
        }
        else
        {
            nativeExperience = catalog.ExperienceField!.GetValue(entry.Item);
        }

        if (!TryReadNonNegativeAmount(nativeExperience, out var experience))
        {
            throw new InvalidOperationException("native mastery XP state is unavailable or invalid");
        }

        return new GrantMutationState(mastery, experience);
    }

    private static bool TryReadNonNegativeAmount(object? value, out MentorAmount amount)
    {
        if (value is null ||
            BigDoubleMantissa?.GetValue(value) is not double mantissa ||
            BigDoubleExponent?.GetValue(value) is not long exponent ||
            !double.IsFinite(mantissa) ||
            mantissa < 0.0)
        {
            amount = default;
            return false;
        }

        amount = mantissa == 0.0 ? default : new MentorAmount(mantissa, exponent);
        return true;
    }

    private static bool AmountsEquivalent(MentorAmount expected, MentorAmount actual)
    {
        if (!expected.IsValidPositive || !actual.IsValidPositive)
        {
            return expected.IsValidPositive == actual.IsValidPositive;
        }

        if (expected.Exponent != actual.Exponent)
        {
            return false;
        }

        var scale = Math.Max(Math.Abs(expected.Mantissa), Math.Abs(actual.Mantissa));
        return Math.Abs(expected.Mantissa - actual.Mantissa) <= scale * 1e-12;
    }

    public void Cancel(MentorDropReason reason = MentorDropReason.LifecycleReset)
    {
        foreach (var domain in DomainOrder) CancelDomain(domain, reason, clearCatalog: true);
        _activeArtifact = null;
        _activeArtifactContainer = null;
        _activeArtifactXp = default;
        _coordinatorWork?.SetState(false, false, false);
    }

    public void Dispose()
    {
        Cancel(MentorDropReason.LifecycleReset);
        _alchemyDomainGate.Dispose();
        _coordinatorWork?.Dispose();
        _rootStatusRegistration?.Dispose();
        _rootStatusRegistration = null;
        foreach (var registration in _domainStatusRegistrations) registration?.Dispose();
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
        catalog.EvidenceBuffer.Invalidate();
        catalog.Requirement = null;
        catalog.HighestMastery = int.MinValue;
        catalog.NextLiveRefresh = 0;
        catalog.NextReconcile = 0;
        catalog.Initialized = false;
        RequestRelationshipRefresh(catalog, advanceProgressionEpoch: false);
        RequestReconcile(catalog);
        catalog.ProgressionEpoch = 0;
        catalog.RelationshipEpoch = 0;
        catalog.SettledRefreshGeneration = 0;
    }

    private void RecordPendingDrops(MentorDomain domain, MentorDropReason reason)
    {
        var state = _domains[domain];
        if (state.Captures.EventCount > 0) Diagnostics.RecordDrop(reason, state.Captures.EventCount, grant: false);
        if (state.ResolvingCapture is not null)
            Diagnostics.RecordDrop(reason, state.ResolvingCapture.EventCount, grant: false);
        if (state.Unroutable.EventCount > 0)
            Diagnostics.RecordDrop(reason, state.Unroutable.EventCount, grant: false);
        if (state.Sources.EventCount > 0) Diagnostics.RecordDrop(reason, state.Sources.EventCount, grant: false);
        if (state.ActivePlan is not null && state.ActivePlan.RemainingCount > 0)
            Diagnostics.RecordDrop(reason, state.ActivePlan.RemainingCount, grant: true);
        if (state.Engine.PendingCount > 0) Diagnostics.RecordDrop(reason, state.Engine.PendingCount, grant: true);
        if (state.ParkedGrants.Count > 0) Diagnostics.RecordDrop(reason, state.ParkedGrants.Count, grant: true);
    }

    public void ResetLifecycle()
    {
        _lifecycleReset.TryConsume();
        Array.Clear(_domainResetPending, 0, _domainResetPending.Length);
        InvalidateAlchemyDomainLifecycle();
        Cancel(MentorDropReason.LifecycleReset);
        _failures.ResetLifecycle();
        _captureFailureLogged = false;
        _statusLifecycleGeneration = Math.Max(0, _readLifecycleGeneration());
        _nextUnlockRefresh = 0;
        foreach (var domain in DomainOrder)
            if (!DomainBlocked(domain))
                _domainUnlocks[(int)domain] = new MentorDomainUnlockSnapshot(
                    MentorDomainUnlockState.Waiting,
                    "native progression unlock is awaiting lifecycle evaluation",
                    FeatureStatusReasonCode.LifecycleTransition);
        RefreshFeatureStatus();
    }

    public void RequestLifecycleReset() => _lifecycleReset.Request();

    public void RequestDomainReset(MentorDomain domain)
    {
        _domainResetPending[(int)domain] = true;
        _nextUnlockRefresh = 0;
    }

    public void BlockPermanent(string reason)
    {
        if (_failures.Global.PermanentReason is not null) return;
        _failures.Global.BlockPermanent(reason);
        Cancel(MentorDropReason.ContractFailure);
        RefreshFeatureStatus();
        _log.LogError($"Orb Mentor permanently blocked: {reason}");
    }

    private void BlockTransient(
        string reason,
        AutomationDecisionCode cause = AutomationDecisionCode.NativeMutationFailed)
    {
        _failures.Global.BlockTransient(reason, cause);
        Cancel(MentorDropReason.ContractFailure);
        RefreshFeatureStatus();
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
        RefreshFeatureStatus();
        _log.LogError($"Orb Mentor {domain} sharing permanently disabled: {reason}");
    }

    private void QuarantineUnlockDomain(MentorDomain domain, string reason)
    {
        var failure = _failures.For(domain);
        if (failure.PermanentReason is not null) return;
        failure.BlockPermanent(reason);
        CancelDomain(domain, MentorDropReason.ContractFailure, clearCatalog: true);
        RefreshFeatureStatus();
        _log.LogError($"Orb Mentor {domain} sharing permanently disabled: {reason}");
    }

    private void BlockDomainTransient(
        MentorDomain domain,
        string reason,
        AutomationDecisionCode cause = AutomationDecisionCode.NativeMutationFailed)
    {
        if (domain == MentorDomain.Spells)
        {
            BlockTransient(reason, cause);
            return;
        }
        _failures.For(domain).BlockTransient(reason, cause);
        CancelDomain(domain, MentorDropReason.ContractFailure, clearCatalog: true);
        RefreshFeatureStatus();
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
                if (domain == MentorDomain.Alchemy && !EnsureAlchemyDomainReady(now)) return false;
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
                    if (domain == MentorDomain.Alchemy)
                    {
                        var classification = _alchemyDomainGate.ClassifyAndCache(value);
                        if (classification.Domain == AlchemyGameplayDomain.ScholarConcept && classification.IsMutationGrade) return true;
                        if (classification.Domain != AlchemyGameplayDomain.OrdinaryAlchemy || !classification.IsMutationGrade)
                        {
                            BlockAlchemyDomain("catalog reconciliation", classification);
                            return false;
                        }
                    }
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
        var experienceField = domain switch
        {
            MentorDomain.Spells => FindField(expected, "masteryExperience"),
            MentorDomain.Alchemy => FindField(expected, "masteryXp"),
            _ => null,
        };
        catalog.ExperienceField = experienceField?.FieldType == typeof(BigDouble)
            ? experienceField
            : null;
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
            (domain != MentorDomain.Artifacts && catalog.ExperienceField is null) ||
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
            if (MentorRefreshPassContinuity.ShouldStartNewPass(work is not null))
            {
                var requestGeneration = catalog.RelationshipRequests.Current;
                var wasDirty = catalog.RelationshipDirty;
                catalog.RelationshipDirty = false;
                catalog.Refresh = new RefreshWork(catalog.ProgressionEpoch, requestGeneration, wasDirty, catalog.Entries.Count);
                return true;
            }
            work = catalog.Refresh!;
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
                    var changed = mastery != entry.MasteryLevel || discovered != entry.IsDiscovered;
                    if (changed)
                    {
                        // A requirement created before this read cannot prove
                        // whether the native delta preceded or followed its
                        // capture. Retain those amounts unrouted; captures
                        // after this exact observation receive a fresh token.
                        MentorRefreshCaptureOrdering.ObserveDelta(
                            ref catalog.Requirement, work.RequestGeneration);
                        AppendRelationshipEvidence(domain, catalog, entry.Uuid, mastery, discovered);
                    }
                    work.Changed |= changed;
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
                    catalog.SettledRefreshGeneration++;
                }
                return true;
            }
            if (work.BuildIndex < catalog.Entries.Count)
            {
                var entry = catalog.Entries[work.BuildIndex++];
                if (!entry.IsDiscovered) return true;
                var recipe = new MentorRecipe(entry.Uuid, entry.MasteryLevel, true);
                work.Discovered.Add(recipe);
                work.DiscoveredIndices.Add(recipe.Uuid, work.Discovered.Count - 1);
                if (entry.MasteryLevel == work.HighestMastery) work.MentorIds.Add(entry.Uuid);
                else if (entry.MasteryLevel < work.HighestMastery)
                {
                    work.Recipients.Add(recipe);
                    work.RecipientIndices.Add(recipe.Uuid, work.Recipients.Count - 1);
                }
                return true;
            }
            catalog.HighestMastery = work.HighestMastery;
            catalog.MentorIds = work.MentorIds;
            catalog.Recipients = work.Recipients;
            catalog.Relationship = MentorRelationshipSnapshot.CreatePreindexed(
                work.ProgressionEpoch, work.HighestMastery, work.Discovered, work.Recipients,
                work.DiscoveredIndices, work.RecipientIndices);
            MentorRefreshCaptureOrdering.Commit(
                catalog.Requirement, work.RequestGeneration, catalog.Relationship);
            var passIsCurrent = !MentorRefreshPassContinuity.RequiresFollowUp(
                catalog.RelationshipRequests, work.RequestGeneration);
            if (passIsCurrent && catalog.EvidenceBuffer.CaptureReferences == 0)
                catalog.EvidenceBuffer.Rebase(catalog.Relationship);
            catalog.Refresh = null;
            catalog.RelationshipDirty = !passIsCurrent;
            catalog.RelationshipEpoch = work.ProgressionEpoch;
            catalog.NextLiveRefresh = catalog.RelationshipDirty ? now : now + LiveRefreshTicks;
            catalog.SettledRefreshGeneration++;
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

    private string? DomainBlockedReason(MentorDomain domain) =>
        _failures.Global.Reason ?? _failures.For(domain).Reason;

    private bool DomainUnlocked(MentorDomain domain) => _domainUnlocks[(int)domain].IsUnlocked;

    private bool DomainEnabled(MentorDomain domain) =>
        DomainConfigured(domain) && DomainUnlocked(domain) && !DomainBlocked(domain);

    private bool EnsureAlchemyDomainReady(long now)
    {
        if (_alchemyDomainGate.Status == AlchemyDomainClassifierStatus.Ready) return true;
        if (_alchemyDomainGate.Status == AlchemyDomainClassifierStatus.Blocked)
        {
            BlockDomainTransient(MentorDomain.Alchemy,
                $"Alchemy domain classifier blocked: {_alchemyDomainGate.StatusReason}");
            return false;
        }
        if (now < _nextAlchemyDomainInitialization) return false;
        if (_alchemyDomainGate.TryInitialize(out var reason)) return true;
        if (_alchemyDomainGate.Status == AlchemyDomainClassifierStatus.Blocked)
            BlockDomainTransient(MentorDomain.Alchemy, $"Alchemy domain classifier blocked: {reason}");
        else
            _nextAlchemyDomainInitialization = now + LiveRefreshTicks;
        return false;
    }

    private void BlockAlchemyDomain(string operation, AlchemyDomainClassification classification)
    {
        BlockDomainTransient(
            MentorDomain.Alchemy,
            $"Alchemy {operation} could not prove an ordinary-alchemy recipe. " +
            $"RecipeUuid={classification.RecipeUuid?.ToString() ?? "unavailable"}, " +
            $"Evidence={classification.Evidence}, Level={classification.Assessment.Level}, " +
            $"Sources={classification.Assessment.Sources}, Contradictory={classification.Assessment.IsContradictory}, " +
            $"Reason={classification.Reason}");
    }

    private void InvalidateAlchemyDomainLifecycle()
    {
        _alchemyDomainGate.InvalidateLifecycle();
        _nextAlchemyDomainInitialization = 0;
    }

    private string DomainStatus(MentorDomain domain, double percent)
    {
        if (!DomainConfigured(domain)) return $"{domain} off";
        if (DomainBlocked(domain)) return $"{domain} blocked: {DomainBlockedReason(domain)}";
        var unlock = DomainUnlock(domain);
        if (!unlock.IsUnlocked) return $"{domain} waiting: {unlock.Reason}";
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
