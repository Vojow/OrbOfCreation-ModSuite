using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

/// <summary>
/// Preflight disposition of a single native purchase submission, before any audited mutation
/// was invoked. <see cref="Proceeded"/> means the native mutation was attempted and the
/// submission carries verifier evidence; the others are the reasons a submission never got that far.
/// </summary>
internal enum AutoBuyPurchasePreflight
{
    Proceeded = 0,
    CandidateUnavailable,
    NotAdmissible,
    SingleBuyUnavailable,
}

/// <summary>
/// The neutral outcome of one native purchase submission: either a preflight rejection with no
/// mutation, or an attempted audited mutation carrying its <see cref="NativeMutationOutcome"/> and
/// call evidence. It never exposes a native object — the action port maps it to a service result.
/// </summary>
internal readonly struct AutoBuyPurchaseSubmission
{
    private AutoBuyPurchaseSubmission(
        AutoBuyPurchasePreflight preflight,
        bool hasEvidence,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        int requestedLevels,
        int committedLevels,
        in AutoBuyAdmissionDiagnosis diagnosis)
    {
        Preflight = preflight;
        HasEvidence = hasEvidence;
        Outcome = outcome;
        CallOutcome = callOutcome;
        RequestedLevels = requestedLevels;
        CommittedLevels = committedLevels;
        Diagnosis = diagnosis;
    }

    public AutoBuyPurchasePreflight Preflight { get; }
    public bool HasEvidence { get; }
    public NativeMutationOutcome Outcome { get; }
    public NativeMutationCallOutcome CallOutcome { get; }
    public bool Verified => HasEvidence && Outcome == NativeMutationOutcome.Verified;

    /// <summary>
    /// Which admission term refused, when the preflight was
    /// <see cref="AutoBuyPurchasePreflight.NotAdmissible"/>. Every other disposition leaves it unread.
    /// </summary>
    public AutoBuyAdmissionDiagnosis Diagnosis { get; }

    /// <summary>How many levels this single native call was asked to buy (the "Y" of "X of Y").</summary>
    public int RequestedLevels { get; }

    /// <summary>
    /// How many levels the call actually committed (the "X" of "X of Y") — the queued-level delta the
    /// verifier observed. A bulk upgrade may commit fewer than <see cref="RequestedLevels"/> yet still
    /// be a success; only zero is a failure.
    /// </summary>
    public int CommittedLevels { get; }

    public static AutoBuyPurchaseSubmission Rejected(AutoBuyPurchasePreflight preflight) =>
        Rejected(preflight, default);

    public static AutoBuyPurchaseSubmission Rejected(
        AutoBuyPurchasePreflight preflight,
        in AutoBuyAdmissionDiagnosis diagnosis)
    {
        if (preflight == AutoBuyPurchasePreflight.Proceeded)
            throw new ArgumentOutOfRangeException(nameof(preflight));
        return new AutoBuyPurchaseSubmission(
            preflight, hasEvidence: false, default, default, 0, 0, in diagnosis);
    }

    public static AutoBuyPurchaseSubmission Attempted(NativeMutationEvidence<int> evidence, int requestedLevels)
    {
        var committed = evidence.HasBefore && evidence.HasAfter
            ? Math.Max(0, evidence.After - evidence.Before)
            : 0;
        return new AutoBuyPurchaseSubmission(
            AutoBuyPurchasePreflight.Proceeded,
            hasEvidence: true,
            evidence.Outcome,
            NativeMutationCallOutcome.FromEvidence(evidence),
            requestedLevels,
            committed,
            default);
    }
}

/// <summary>
/// The native execution surface for one Auto Buy purchase, and the only place the service mutates
/// the game. Given a candidate identity the worker planned on, it re-resolves the live native
/// object, revalidates admission against state that can have moved since planning, and submits the
/// purchase through the audited <see cref="NativeMutationVerifier"/>, which reads the queued level
/// either side of the mutation so the returned submission carries evidence of what actually
/// committed rather than what was asked for. A refused submission carries a diagnosis naming which
/// admission term said no.
/// </summary>
internal interface IAutoBuyNativePurchasePort
{
    AutoBuyPurchaseSubmission Submit(
        AutoBuyCandidateKind kind,
        Guid uuid,
        int count
#if SERVICE_CYCLE_PROFILE
        , in ServiceActionContext context
#endif
        );
}

internal sealed class AutoBuyNativePurchaseAdapter : IAutoBuyNativePurchasePort
{
    private readonly System.Collections.Generic.Dictionary<Type, PurchaseAccessors?> _accessors =
        new System.Collections.Generic.Dictionary<Type, PurchaseAccessors?>();
    private readonly CandidateIndex?[] _candidateIndices = new CandidateIndex?[2];
#if SERVICE_CYCLE_PROFILE
    private readonly AutomataProfileOperations _profileOperations;

    public AutoBuyNativePurchaseAdapter(AutomataProfileOperations profileOperations)
    {
        _profileOperations = profileOperations ??
            throw new ArgumentNullException(nameof(profileOperations));
    }
#endif

    public AutoBuyPurchaseSubmission Submit(
        AutoBuyCandidateKind kind,
        Guid uuid,
        int count
#if SERVICE_CYCLE_PROFILE
        , in ServiceActionContext context
#endif
        )
    {
        if (kind is not (AutoBuyCandidateKind.Structure or AutoBuyCandidateKind.Upgrade) || count < 1)
            return AutoBuyPurchaseSubmission.Rejected(AutoBuyPurchasePreflight.CandidateUnavailable);

        bool resolved;
        object source;
        PurchaseAccessors accessors;
#if SERVICE_CYCLE_PROFILE
        var resolutionStage = _profileOperations.Begin(
            ServiceCycleProfileSpan.AutoBuyActionCandidateResolution,
            in context,
            ServiceCycleProfileTemperature.Warm);
        try
        {
#endif
        resolved = TryResolveCandidate(kind, uuid, out source, out accessors);
#if SERVICE_CYCLE_PROFILE
        }
        finally { resolutionStage.Complete(); }
#endif
        if (!resolved)
            return AutoBuyPurchaseSubmission.Rejected(AutoBuyPurchasePreflight.CandidateUnavailable);

        var readable = false;
        var admitted = false;
#if SERVICE_CYCLE_PROFILE
        var admissionStage = _profileOperations.Begin(
            ServiceCycleProfileSpan.AutoBuyActionAdmissionRevalidation,
            in context,
            ServiceCycleProfileTemperature.Warm);
        try
        {
            _profileOperations.AddReflectedMethodCalls(1);
#endif
        // The one live question the plan cannot answer for itself. CanPurchase() folds together
        // availability, the level caps, the price and the per-level prerequisites, and every one of
        // them can move between the world the worker planned from and this call.
        readable = accessors.TryReadAdmission(source, out admitted);
#if SERVICE_CYCLE_PROFILE
        }
        finally { admissionStage.Complete(); }
#endif
        if (!readable)
            return AutoBuyPurchaseSubmission.Rejected(AutoBuyPurchasePreflight.CandidateUnavailable);
        if (!admitted)
        {
            // A refusal of a purchase the worker planned is a disagreement between the plan and the
            // game, which is a planner bug — so the fold is taken apart here, on the cold path, to
            // name the term that bit. Nothing else in this method pays for it.
            return AutoBuyPurchaseSubmission.Rejected(
                AutoBuyPurchasePreflight.NotAdmissible, accessors.Diagnose(source));
        }

#if SERVICE_CYCLE_PROFILE
        var submissionStage = _profileOperations.Begin(
            ServiceCycleProfileSpan.AutoBuyActionNativeSubmission,
            in context,
            ServiceCycleProfileTemperature.Warm);
        try
        {
            // Queued-level read before and after the audited Purchase invocation.
            _profileOperations.AddReflectedMethodCalls(3);
            if (kind == AutoBuyCandidateKind.Structure)
            {
                // A structure buys one level per call, so every level past the first is another
                // Purchase and the CanPurchase that guards it, each with its own argument array.
                var extraLevels = Math.Max(0, count - 1);
                _profileOperations.AddReflectedMethodCalls((uint)(extraLevels * 2));
                for (var level = 0; level <= extraLevels; level++)
                    _profileOperations.AddInvocationArgumentArray();
            }
#endif
        return kind == AutoBuyCandidateKind.Structure
            ? SubmitStructure(uuid, source, accessors, count)
            : SubmitUpgrade(uuid, source, accessors, count);
#if SERVICE_CYCLE_PROFILE
        }
        finally { submissionStage.Complete(); }
#endif
    }

    /// <summary>
    /// Queues up to <paramref name="count"/> structure levels as one audited mutation.
    /// </summary>
    /// <remarks>
    /// The native structure <c>Purchase(true)</c> forces exactly one level and consults no
    /// multiplier, so bulk development is what it has always been: the same call made again, each
    /// time behind the game's own admission check. One verifier scope spans the group, so the
    /// evidence is the queued-level delta the whole group produced — a group that stops early
    /// because the next level is unaffordable is a partial success, exactly like an upgrade
    /// multi-buy, not a refusal.
    /// </remarks>
    private static AutoBuyPurchaseSubmission SubmitStructure(
        Guid uuid,
        object source,
        PurchaseAccessors accessors,
        int count)
    {
        var evidence = NativeMutationVerifier.Execute(
            "Auto Buy Structure",
            uuid.ToString(),
            count == 1
                ? "GetQueuedQuantity exact delta +1"
                : $"GetQueuedQuantity delta in [1, {count}]",
            () => accessors.ReadQueuedLevel(source),
            () =>
            {
                accessors.InvokePurchase(source);
                for (var level = 1; level < count; level++)
                {
                    if (!accessors.TryReadAdmission(source, out var admitted) || !admitted) break;
                    accessors.InvokePurchase(source);
                }
            },
            (before, after) => count == 1
                ? after == before + 1
                : after > before && after <= before + count);
        return AutoBuyPurchaseSubmission.Attempted(evidence, requestedLevels: count);
    }

    private static AutoBuyPurchaseSubmission SubmitUpgrade(
        Guid uuid,
        object source,
        PurchaseAccessors accessors,
        int count)
    {
        // The native upgrade Purchase() honours the global multi-buy multiplier, so requesting
        // exactly `count` levels means pinning the multiplier to `count` for the single call. The
        // scope restores the operator's value afterwards; if it cannot guarantee that, no mutation
        // is attempted. The call commits between one and `count` levels (the game may afford fewer):
        // any committed level is a success, only zero is a failure.
        if (!NativeMultiBuyScope.TryEnter(count, out var scope, out _))
            return AutoBuyPurchaseSubmission.Rejected(AutoBuyPurchasePreflight.SingleBuyUnavailable);

        using (scope)
        {
            var evidence = NativeMutationVerifier.Execute(
                "Auto Buy Upgrade",
                uuid.ToString(),
                $"GetQueuedPurchaseLevel delta in [1, {count}]",
                () => accessors.ReadQueuedLevel(source),
                () => accessors.InvokePurchase(source),
                (before, after) => after > before && after <= before + count);
            return AutoBuyPurchaseSubmission.Attempted(evidence, requestedLevels: count);
        }
    }

    // The native StructureSO.All / UpgradeSO.All membership is constant after game start (only
    // per-candidate state changes), so resolution keeps a UUID index per kind instead of scanning
    // the list per action. The index is keyed to the list's identity and count and self-heals with
    // one rebuild when a lookup misses or a hit's stable id no longer matches (game reload rebuilt
    // the list in place); the lifecycle-epoch guard in the action adapter remains the authority
    // that rejects stale cycles.
    private bool TryResolveCandidate(
        AutoBuyCandidateKind kind,
        Guid uuid,
        out object source,
        out PurchaseAccessors accessors)
    {
        source = null!;
        accessors = null!;
        var listType = kind == AutoBuyCandidateKind.Structure ? "StructureSO" : "UpgradeSO";
        var list = ReadStaticList(listType, "All");
        var index = ResolveIndex(kind, list, rebuild: false);
        if (!TryLookupFresh(index, uuid, out var candidate))
        {
            index = ResolveIndex(kind, list, rebuild: true);
            if (!TryLookupFresh(index, uuid, out candidate)) return false;
        }

        var resolved = ResolveAccessors(candidate.GetType(), kind);
        if (resolved is null) return false;
        source = candidate;
        accessors = resolved;
        return true;
    }

    private bool TryLookupFresh(
        System.Collections.Generic.Dictionary<Guid, object> index,
        Guid uuid,
        out object candidate)
    {
        if (!index.TryGetValue(uuid, out candidate!)) return false;
#if SERVICE_CYCLE_PROFILE
        _profileOperations.AddStableIdRead();
#endif
        return Guid.TryParse(ReflectionUtil.ReadStableId(candidate), out var liveId) && liveId == uuid;
    }

    private System.Collections.Generic.Dictionary<Guid, object> ResolveIndex(
        AutoBuyCandidateKind kind,
        IList list,
        bool rebuild)
    {
        var slot = kind == AutoBuyCandidateKind.Structure ? 0 : 1;
        var cached = _candidateIndices[slot];
        if (!rebuild && cached is not null &&
            ReferenceEquals(cached.Source, list) && cached.Count == list.Count)
        {
            return cached.ByUuid;
        }

        var index = new System.Collections.Generic.Dictionary<Guid, object>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            var candidate = list[i];
#if SERVICE_CYCLE_PROFILE
            _profileOperations.AddListEntry();
#endif
            if (candidate is null) continue;
#if SERVICE_CYCLE_PROFILE
            _profileOperations.AddStableIdRead();
#endif
            if (!Guid.TryParse(ReflectionUtil.ReadStableId(candidate), out var candidateId) ||
                candidateId == Guid.Empty)
                continue;
            // First match wins, mirroring the previous linear-scan semantics.
            if (!index.ContainsKey(candidateId)) index.Add(candidateId, candidate);
        }

        _candidateIndices[slot] = new CandidateIndex(list, list.Count, index);
        return index;
    }

    private sealed class CandidateIndex
    {
        internal CandidateIndex(
            IList source,
            int count,
            System.Collections.Generic.Dictionary<Guid, object> byUuid)
        {
            Source = source;
            Count = count;
            ByUuid = byUuid;
        }

        internal IList Source { get; }
        internal int Count { get; }
        internal System.Collections.Generic.Dictionary<Guid, object> ByUuid { get; }
    }

    private PurchaseAccessors? ResolveAccessors(Type type, AutoBuyCandidateKind kind)
    {
        if (!_accessors.TryGetValue(type, out var accessors))
        {
            accessors = PurchaseAccessors.TryCreate(type, kind);
            _accessors[type] = accessors;
        }

        return accessors;
    }

    private static IList ReadStaticList(string typeName, string memberName)
    {
        var type = ReflectionUtil.FindLoadedType(typeName);
        var value = type?.GetField(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) ??
                    type?.GetProperty(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null, null);
        return value as IList ?? Array.Empty<object>();
    }

    /// <summary>
    /// Duplicated mutation contract for one candidate kind (AB-SC-013): the exact-type + assembly
    /// guard and the no-arg method discovery mirror the legacy purchase adapters. Structure purchase
    /// is <c>Purchase(bool)</c> verified by <c>GetQueuedQuantity</c>; upgrade purchase is
    /// <c>Purchase()</c> verified by <c>GetQueuedPurchaseLevel</c>.
    /// </summary>
    private sealed class PurchaseAccessors
    {
        private readonly AutoBuyCandidateKind _kind;
        private readonly MethodInfo _canPurchase;
        private readonly MethodInfo _queuedLevel;
        private readonly MethodInfo _purchase;
        private readonly MethodInfo? _isAvailable;
        private readonly MethodInfo? _isMaxLevel;
        private readonly MethodInfo? _isMaxQueuedLevel;
        private readonly MethodInfo? _purchaseCost;
        private Type? _costListType;
        private MethodInfo? _hasEnough;

        private PurchaseAccessors(
            AutoBuyCandidateKind kind,
            MethodInfo canPurchase,
            MethodInfo queuedLevel,
            MethodInfo purchase,
            MethodInfo? isAvailable,
            MethodInfo? isMaxLevel,
            MethodInfo? isMaxQueuedLevel,
            MethodInfo? purchaseCost)
        {
            _kind = kind;
            _canPurchase = canPurchase;
            _queuedLevel = queuedLevel;
            _purchase = purchase;
            _isAvailable = isAvailable;
            _isMaxLevel = isMaxLevel;
            _isMaxQueuedLevel = isMaxQueuedLevel;
            _purchaseCost = purchaseCost;
        }

        public static PurchaseAccessors? TryCreate(Type type, AutoBuyCandidateKind kind)
        {
            var expected = kind == AutoBuyCandidateKind.Structure ? "StructureSO" : "UpgradeSO";
            if (!string.Equals(type.FullName, expected, StringComparison.Ordinal) ||
                !string.Equals(type.Assembly.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal))
            {
                return null;
            }

            var canPurchase = FindNoArg(type, "CanPurchase", typeof(bool));
            var queuedLevel = FindNoArg(
                type,
                kind == AutoBuyCandidateKind.Structure ? "GetQueuedQuantity" : "GetQueuedPurchaseLevel",
                typeof(int));
            var purchase = kind == AutoBuyCandidateKind.Structure
                ? FindPurchase(type, new[] { typeof(bool) })
                : FindPurchase(type, Type.EmptyTypes);
            if (canPurchase is null || queuedLevel is null || purchase is null)
                return null;

            // The diagnosis half is optional by design: a kind that has no bounded level declares no
            // level cap to read, and a missing reader means one fewer term named rather than a
            // candidate that cannot be bought.
            return new PurchaseAccessors(
                kind,
                canPurchase,
                queuedLevel,
                purchase,
                FindNoArg(type, "IsAvailable", typeof(bool)),
                FindNoArg(type, "IsMaxLevel", typeof(bool)),
                FindNoArg(type, "IsMaxQueuedLevel", typeof(bool)),
                FindNoArgReturning(type, "GetPurchaseCost"));
        }

        public bool TryReadAdmission(object source, out bool admitted) =>
            TryInvokeBool(_canPurchase, source, out admitted);

        /// <summary>
        /// Reads each admission term the game exposes on its own, so a refusal can name a cause. Only
        /// ever called after <see cref="TryReadAdmission"/> already said no, and it answers with
        /// <see cref="AutoBuyAdmissionTerm.Unread"/> rather than throwing, because a diagnosis that
        /// can fail the action it is diagnosing is worse than no diagnosis.
        /// </summary>
        public AutoBuyAdmissionDiagnosis Diagnose(object source) =>
            new AutoBuyAdmissionDiagnosis(
                ReadTerm(_isAvailable, source, refusesWhen: false),
                ReadTerm(_isMaxLevel, source, refusesWhen: true),
                ReadTerm(_isMaxQueuedLevel, source, refusesWhen: true),
                ReadHasEnough(source));

        private static AutoBuyAdmissionTerm ReadTerm(MethodInfo? method, object source, bool refusesWhen)
        {
            if (method is null || !TryInvokeBool(method, source, out var value))
                return AutoBuyAdmissionTerm.Unread;
            return value == refusesWhen ? AutoBuyAdmissionTerm.Refused : AutoBuyAdmissionTerm.Passed;
        }

        /// <summary>
        /// The game's own verdict on the price, from the cost list it builds for the next level. The
        /// list type is resolved from the instance the call returns and cached, because the suite
        /// names no game type at compile time.
        /// </summary>
        private AutoBuyAdmissionTerm ReadHasEnough(object source)
        {
            if (_purchaseCost is null) return AutoBuyAdmissionTerm.Unread;
            object? costList;
            try
            {
                costList = _purchaseCost.Invoke(source, Array.Empty<object>());
            }
            catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException ||
                                       ex is InvalidOperationException || ex is TargetException ||
                                       ex is MemberAccessException)
            {
                return AutoBuyAdmissionTerm.Unread;
            }

            if (costList is null) return AutoBuyAdmissionTerm.Unread;
            var costListType = costList.GetType();
            if (!ReferenceEquals(costListType, _costListType))
            {
                _costListType = costListType;
                _hasEnough = FindNoArg(costListType, "HasEnough", typeof(bool));
            }

            return ReadTerm(_hasEnough, costList, refusesWhen: false);
        }

        public int ReadQueuedLevel(object source)
        {
            if (_queuedLevel.Invoke(source, Array.Empty<object>()) is int value)
                return value;
            throw new InvalidOperationException("native queued level is unavailable");
        }

        public void InvokePurchase(object source) =>
            _purchase.Invoke(
                source,
                _kind == AutoBuyCandidateKind.Structure ? new object[] { true } : Array.Empty<object>());

        private static MethodInfo? FindNoArg(Type type, string name, Type returnType)
        {
            var method = type.GetMethod(name, ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);
            return method is not null && method.DeclaringType == type && method.ReturnType == returnType
                ? method
                : null;
        }

        /// <summary>
        /// The same discovery for a call whose return type the suite cannot name at compile time —
        /// <c>GetPurchaseCost()</c> hands back a <c>ResourceCostList</c>, which lives in the game.
        /// Any reference return will do; what it can answer is decided from the instance itself.
        /// </summary>
        private static MethodInfo? FindNoArgReturning(Type type, string name)
        {
            var method = type.GetMethod(name, ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);
            return method is not null && method.DeclaringType == type && method.ReturnType != typeof(void)
                ? method
                : null;
        }

        private static MethodInfo? FindPurchase(Type type, Type[] parameters)
        {
            var method = type.GetMethod("Purchase", ReflectionUtil.InstanceFlags, null, parameters, null);
            return method is not null && method.DeclaringType == type && method.ReturnType == typeof(void)
                ? method
                : null;
        }

        private static bool TryInvokeBool(MethodInfo method, object source, out bool value)
        {
            value = false;
            try
            {
                if (method.Invoke(source, Array.Empty<object>()) is bool result)
                {
                    value = result;
                    return true;
                }
            }
            catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException ||
                                       ex is InvalidOperationException || ex is TargetException)
            {
            }

            return false;
        }
    }
}
