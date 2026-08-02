using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>The exact purchasable native family named by an owning-view relation.</summary>
internal enum WorldPurchaseCandidateKind
{
    Structure = 1,
    Upgrade = 2,
}

/// <summary>Whether one candidate has one complete, exact route through an owning view and list.</summary>
internal enum WorldPurchaseViewRelationStatus
{
    Resolved = 0,
    Missing,
    Unreadable,
    Ambiguous,
    Contradictory,
}

/// <summary>
/// One candidate's authored route to the UI: exact candidate kind and category, exact containing
/// list, and exact owning view. A non-resolved row is retained so consumers can name why the
/// candidate was withheld instead of silently dropping it.
/// </summary>
internal readonly struct WorldPurchaseViewRelation : IWorldEntity
{
    internal WorldPurchaseViewRelation(
        Guid candidateId,
        WorldPurchaseCandidateKind kind,
        Guid categoryId,
        Guid listId,
        Guid viewId,
        WorldPurchaseViewRelationStatus status)
    {
        CandidateId = candidateId;
        Kind = kind;
        CategoryId = categoryId;
        ListId = listId;
        ViewId = viewId;
        Status = status;
    }

    public Guid EntityId => CandidateId;
    internal Guid CandidateId { get; }
    internal WorldPurchaseCandidateKind Kind { get; }

    /// <summary>The exact <c>StructureTypeSO</c> identity; empty for an <c>UpgradeSO</c>.</summary>
    internal Guid CategoryId { get; }

    internal Guid ListId { get; }
    internal Guid ViewId { get; }
    internal WorldPurchaseViewRelationStatus Status { get; }
}

/// <summary>A detached relation plus the live view object used only by the action boundary.</summary>
internal readonly struct NativePurchaseViewResolution
{
    internal NativePurchaseViewResolution(in WorldPurchaseViewRelation relation, object? view)
    {
        Relation = relation;
        View = view;
    }

    internal WorldPurchaseViewRelation Relation { get; }
    internal object? View { get; }
}

/// <summary>Exact reflection/list work performed by one live owning-view resolution.</summary>
internal struct NativePurchaseViewAdmissionReadCounts
{
    internal uint FieldReads { get; private set; }
    internal uint MethodCalls { get; private set; }
    internal uint ListEntries { get; private set; }

    internal void AddFieldRead() => FieldReads = checked(FieldReads + 1);
    internal void AddMethodCall() => MethodCalls = checked(MethodCalls + 1);
    internal void AddListEntry() => ListEntries = checked(ListEntries + 1);
}

/// <summary>
/// The one audited resolver for Auto Buy owning-view admission. World collection uses it to publish
/// the immutable candidate-to-list/view relation; the action boundary uses the same implementation
/// to resolve that relation again and ask the exact live <c>ViewSO.IsAvailable()</c> immediately
/// before purchase admission.
/// </summary>
/// <remarks>
/// <para>
/// Both <c>ViewSO.relevantLists</c> and <c>ViewSO.availableLists</c> contribute normal player
/// reachability. A candidate must appear exactly once after identical view/list matches from the two
/// sources are collapsed. No display name participates.
/// </para>
/// <para>
/// Structures additionally prove their own <c>structureType</c> identity and exact membership in
/// that category's <c>structures</c> list. This keeps the published list route tied to the same exact
/// category identity the structure row carries.
/// </para>
/// </remarks>
internal sealed class NativePurchaseViewAdmissionResolver
{
    private readonly BindingSet _native;

    private NativePurchaseViewAdmissionResolver(BindingSet native) => _native = native;

    internal static bool TryCreate(
        Func<string, Type?> resolve,
        out NativePurchaseViewAdmissionResolver? resolver,
        out string reason)
    {
        if (resolve is null) throw new ArgumentNullException(nameof(resolve));
        if (!BindingSet.TryCreate(resolve, out var bindings, out reason))
        {
            resolver = null;
            return false;
        }

        resolver = new NativePurchaseViewAdmissionResolver(bindings!);
        return true;
    }

    /// <summary>Reads one relation row for every exact native Auto Buy candidate.</summary>
    internal int ReadAll(
        WorldRelationBuffer<WorldPurchaseViewRelation> output,
        out int unresolved,
        out int skipped)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        unresolved = 0;
        skipped = 0;
        var sampled = 0;
        var identities = new HashSet<Guid>();
        sampled += ReadRegistry(
            WorldPurchaseCandidateKind.Structure,
            _native.StructureAll,
            _native.StructureType,
            identities,
            output,
            ref unresolved,
            ref skipped);
        sampled += ReadRegistry(
            WorldPurchaseCandidateKind.Upgrade,
            _native.UpgradeAll,
            _native.UpgradeType,
            identities,
            output,
            ref unresolved,
            ref skipped);
        return sampled;
    }

    /// <summary>Resolves one already identity-checked candidate through the complete live graph.</summary>
    internal NativePurchaseViewResolution Resolve(
        WorldPurchaseCandidateKind kind,
        Guid candidateId,
        object candidate)
    {
        var reads = default(NativePurchaseViewAdmissionReadCounts);
        return ResolveProfiled(kind, candidateId, candidate, ref reads);
    }

    internal NativePurchaseViewResolution ResolveProfiled(
        WorldPurchaseCandidateKind kind,
        Guid candidateId,
        object candidate,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        var categoryId = Guid.Empty;
        try
        {
            var expected = CandidateType(kind);
            if (candidateId == Guid.Empty || candidate is null || candidate.GetType() != expected)
                return Failed(kind, candidateId, categoryId, WorldPurchaseViewRelationStatus.Contradictory);
            if (Identity(CandidateIdentity(kind), candidate, ref reads) != candidateId)
                return Failed(kind, candidateId, categoryId, WorldPurchaseViewRelationStatus.Contradictory);

            if (kind == WorldPurchaseCandidateKind.Structure)
            {
                var category = Exact(
                    ReadField(_native.StructureCategory, candidate, ref reads),
                    _native.StructureCategoryType,
                    "StructureSO.structureType");
                categoryId = Identity(_native.StructureCategoryIdentity, category, ref reads);
                if (categoryId == Guid.Empty ||
                    CountExactMembership(
                        List(ReadField(_native.StructureCategoryMembers, category, ref reads),
                            "StructureTypeSO.structures"),
                        _native.StructureType,
                        _native.StructureIdentity,
                        candidateId,
                        candidate,
                        ref reads) != 1)
                {
                    return Failed(
                        kind,
                        candidateId,
                        categoryId,
                        WorldPurchaseViewRelationStatus.Contradictory);
                }
            }

            return ResolveViewLists(kind, candidateId, categoryId, candidate, ref reads);
        }
        catch (RelationFailure failure)
        {
            return Failed(kind, candidateId, categoryId, failure.Status);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return Failed(kind, candidateId, categoryId, WorldPurchaseViewRelationStatus.Unreadable);
        }
    }

    /// <summary>Asks the exact resolved live <c>ViewSO</c> for its composed progression verdict.</summary>
    internal bool TryReadAvailability(
        in NativePurchaseViewResolution resolution,
        out bool available)
    {
        var reads = default(NativePurchaseViewAdmissionReadCounts);
        return TryReadAvailabilityProfiled(in resolution, ref reads, out available);
    }

    internal bool TryReadAvailabilityProfiled(
        in NativePurchaseViewResolution resolution,
        ref NativePurchaseViewAdmissionReadCounts reads,
        out bool available)
    {
        available = false;
        if (resolution.Relation.Status != WorldPurchaseViewRelationStatus.Resolved ||
            resolution.View is null ||
            resolution.View.GetType() != _native.ViewType)
            return false;
        try
        {
            reads.AddMethodCall();
            if (_native.ViewAvailable.Invoke(resolution.View, Array.Empty<object>()) is not bool value)
                return false;
            available = value;
            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
    }

    private int ReadRegistry(
        WorldPurchaseCandidateKind kind,
        FieldInfo registryField,
        Type expectedType,
        HashSet<Guid> identities,
        WorldRelationBuffer<WorldPurchaseViewRelation> output,
        ref int unresolved,
        ref int skipped)
    {
        var registry = List(registryField.GetValue(null), expectedType.Name + ".All");
        var sampled = 0;
        foreach (var value in registry)
        {
            if (value is null || value.GetType() != expectedType)
            {
                skipped++;
                continue;
            }

            var candidate = value;
            Guid id;
            try
            {
                var reads = default(NativePurchaseViewAdmissionReadCounts);
                id = Identity(CandidateIdentity(kind), candidate, ref reads);
            }
            catch (Exception ex) when (ex is RelationFailure || IsExpected(ex))
            {
                skipped++;
                continue;
            }
            if (id == Guid.Empty || !identities.Add(id))
            {
                skipped++;
                continue;
            }
            var resolution = Resolve(kind, id, candidate);
            output.Append(resolution.Relation);
            if (resolution.Relation.Status != WorldPurchaseViewRelationStatus.Resolved) unresolved++;
            sampled++;
        }
        return sampled;
    }

    private NativePurchaseViewResolution ResolveViewLists(
        WorldPurchaseCandidateKind kind,
        Guid candidateId,
        Guid categoryId,
        object candidate,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        object? matchedView = null;
        var matchedViewId = Guid.Empty;
        var matchedListId = Guid.Empty;
        var views = List(ReadField(_native.ViewAll, null, ref reads), "ViewSO.All");
        var seenViews = new HashSet<Guid>();
        foreach (var value in views)
        {
            reads.AddListEntry();
            var view = Exact(value, _native.ViewType, "ViewSO");
            var viewId = Identity(_native.ViewIdentity, view, ref reads);
            if (viewId == Guid.Empty || !seenViews.Add(viewId))
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
            MatchLists(
                kind,
                candidateId,
                candidate,
                view,
                viewId,
                _native.RelevantLists,
                ref matchedView,
                ref matchedViewId,
                ref matchedListId,
                ref reads);
            MatchLists(
                kind,
                candidateId,
                candidate,
                view,
                viewId,
                _native.AvailableLists,
                ref matchedView,
                ref matchedViewId,
                ref matchedListId,
                ref reads);
        }

        if (matchedView is null)
            return Failed(kind, candidateId, categoryId, WorldPurchaseViewRelationStatus.Missing);
        var relation = new WorldPurchaseViewRelation(
            candidateId,
            kind,
            categoryId,
            matchedListId,
            matchedViewId,
            WorldPurchaseViewRelationStatus.Resolved);
        return new NativePurchaseViewResolution(in relation, matchedView);
    }

    private void MatchLists(
        WorldPurchaseCandidateKind kind,
        Guid candidateId,
        object candidate,
        object view,
        Guid viewId,
        FieldInfo source,
        ref object? matchedView,
        ref Guid matchedViewId,
        ref Guid matchedListId,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        var lists = List(ReadField(source, view, ref reads), "ViewSO." + source.Name);
        foreach (var value in lists)
        {
            reads.AddListEntry();
            if (value is null)
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
            var runtimeType = value.GetType();
            var expectedListType = CandidateListType(kind);
            var otherListType = CandidateListType(
                kind == WorldPurchaseCandidateKind.Structure
                    ? WorldPurchaseCandidateKind.Upgrade
                    : WorldPurchaseCandidateKind.Structure);
            if (runtimeType == otherListType) continue;
            if (runtimeType != expectedListType)
            {
                if (expectedListType.IsAssignableFrom(runtimeType))
                    throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
                continue;
            }

            var listId = Identity(_native.ListIdentity, value, ref reads);
            if (listId == Guid.Empty)
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
            var members = List(
                ReadField(CandidateListMembers(kind), value, ref reads),
                runtimeType.Name + ".value");
            var matches = CountExactMembership(
                members,
                CandidateType(kind),
                CandidateIdentity(kind),
                candidateId,
                candidate,
                ref reads);
            if (matches == 0) continue;
            if (matches != 1)
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);

            if (matchedView is null)
            {
                matchedView = view;
                matchedViewId = viewId;
                matchedListId = listId;
                continue;
            }

            // The same authored route can be named in both fields. Anything else gives the candidate
            // more than one alleged owner and is not an admission relation the suite may choose among.
            if (matchedViewId != viewId || matchedListId != listId)
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Ambiguous);
        }
    }

    private static int CountExactMembership(
        IList members,
        Type expectedType,
        MethodInfo identity,
        Guid candidateId,
        object candidate,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        var matches = 0;
        foreach (var value in members)
        {
            reads.AddListEntry();
            var member = Exact(value, expectedType, expectedType.Name + " list member");
            var memberId = Identity(identity, member, ref reads);
            if (memberId == Guid.Empty)
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
            if (memberId != candidateId) continue;
            if (!ReferenceEquals(member, candidate))
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
            matches++;
        }
        return matches;
    }

    private static NativePurchaseViewResolution Failed(
        WorldPurchaseCandidateKind kind,
        Guid candidateId,
        Guid categoryId,
        WorldPurchaseViewRelationStatus status)
    {
        var relation = new WorldPurchaseViewRelation(
            candidateId,
            kind,
            categoryId,
            Guid.Empty,
            Guid.Empty,
            status);
        return new NativePurchaseViewResolution(in relation, null);
    }

    private Type CandidateType(WorldPurchaseCandidateKind kind) =>
        kind == WorldPurchaseCandidateKind.Structure ? _native.StructureType : _native.UpgradeType;

    private Type CandidateListType(WorldPurchaseCandidateKind kind) =>
        kind == WorldPurchaseCandidateKind.Structure
            ? _native.StructureListType
            : _native.UpgradeListType;

    private MethodInfo CandidateIdentity(WorldPurchaseCandidateKind kind) =>
        kind == WorldPurchaseCandidateKind.Structure
            ? _native.StructureIdentity
            : _native.UpgradeIdentity;

    private FieldInfo CandidateListMembers(WorldPurchaseCandidateKind kind) =>
        kind == WorldPurchaseCandidateKind.Structure
            ? _native.StructureListMembers
            : _native.UpgradeListMembers;

    private static Guid Identity(
        MethodInfo method,
        object target,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        reads.AddMethodCall();
        if (method.Invoke(target, Array.Empty<object>()) is Guid value) return value;
        throw new RelationFailure(WorldPurchaseViewRelationStatus.Unreadable);
    }

    private static object? ReadField(
        FieldInfo field,
        object? target,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        reads.AddFieldRead();
        return field.GetValue(target);
    }

    private static object Exact(object? value, Type expected, string contract) =>
        value is not null && value.GetType() == expected
            ? value
            : throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory, contract);

    private static IList List(object? value, string contract) =>
        value as IList ??
        throw new RelationFailure(WorldPurchaseViewRelationStatus.Unreadable, contract);

    private static bool IsExpected(Exception exception) => exception is
        TargetInvocationException or
        ArgumentException or
        InvalidOperationException or
        TargetException or
        TargetParameterCountException or
        MemberAccessException or
        TypeLoadException;

    private sealed class RelationFailure : Exception
    {
        internal RelationFailure(WorldPurchaseViewRelationStatus status, string? contract = null)
            : base(contract ?? status.ToString()) => Status = status;

        internal WorldPurchaseViewRelationStatus Status { get; }
    }

    /// <summary>One complete lifecycle-bound metadata set; partial binding never reaches a read.</summary>
    private sealed class BindingSet
    {
        private const BindingFlags Instance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private BindingSet(
            Type viewType,
            Type structureType,
            Type upgradeType,
            Type structureCategoryType,
            Type structureListType,
            Type upgradeListType,
            FieldInfo viewAll,
            FieldInfo structureAll,
            FieldInfo upgradeAll,
            FieldInfo relevantLists,
            FieldInfo availableLists,
            FieldInfo structureCategory,
            FieldInfo structureCategoryMembers,
            MethodInfo viewIdentity,
            MethodInfo structureIdentity,
            MethodInfo upgradeIdentity,
            MethodInfo structureCategoryIdentity,
            MethodInfo listIdentity,
            MethodInfo viewAvailable,
            FieldInfo structureListMembers,
            FieldInfo upgradeListMembers)
        {
            ViewType = viewType;
            StructureType = structureType;
            UpgradeType = upgradeType;
            StructureCategoryType = structureCategoryType;
            StructureListType = structureListType;
            UpgradeListType = upgradeListType;
            ViewAll = viewAll;
            StructureAll = structureAll;
            UpgradeAll = upgradeAll;
            RelevantLists = relevantLists;
            AvailableLists = availableLists;
            StructureCategory = structureCategory;
            StructureCategoryMembers = structureCategoryMembers;
            ViewIdentity = viewIdentity;
            StructureIdentity = structureIdentity;
            UpgradeIdentity = upgradeIdentity;
            StructureCategoryIdentity = structureCategoryIdentity;
            ListIdentity = listIdentity;
            ViewAvailable = viewAvailable;
            StructureListMembers = structureListMembers;
            UpgradeListMembers = upgradeListMembers;
        }

        internal Type ViewType { get; }
        internal Type StructureType { get; }
        internal Type UpgradeType { get; }
        internal Type StructureCategoryType { get; }
        internal Type StructureListType { get; }
        internal Type UpgradeListType { get; }
        internal FieldInfo ViewAll { get; }
        internal FieldInfo StructureAll { get; }
        internal FieldInfo UpgradeAll { get; }
        internal FieldInfo RelevantLists { get; }
        internal FieldInfo AvailableLists { get; }
        internal FieldInfo StructureCategory { get; }
        internal FieldInfo StructureCategoryMembers { get; }
        internal MethodInfo ViewIdentity { get; }
        internal MethodInfo StructureIdentity { get; }
        internal MethodInfo UpgradeIdentity { get; }
        internal MethodInfo StructureCategoryIdentity { get; }
        internal MethodInfo ListIdentity { get; }
        internal MethodInfo ViewAvailable { get; }
        internal FieldInfo StructureListMembers { get; }
        internal FieldInfo UpgradeListMembers { get; }

        internal static bool TryCreate(
            Func<string, Type?> resolve,
            out BindingSet? bindings,
            out string reason)
        {
            bindings = null;
            try
            {
                var id = Type(resolve, "IdScriptableObject");
                var abstractList = Type(resolve, "AbstractListVariable");
                var view = Type(resolve, "ViewSO");
                var structure = Type(resolve, "StructureSO");
                var upgrade = Type(resolve, "UpgradeSO");
                var structureCategory = Type(resolve, "StructureTypeSO");
                var structureList = Type(resolve, "StructureListVariable");
                var upgradeList = Type(resolve, "UpgradeListVariable");
                var listCollection = typeof(List<>).MakeGenericType(abstractList);

                bindings = new BindingSet(
                    view,
                    structure,
                    upgrade,
                    structureCategory,
                    structureList,
                    upgradeList,
                    CollectionField(view, "All", view, Static),
                    CollectionField(structure, "All", structure, Static),
                    CollectionField(upgrade, "All", upgrade, Static),
                    Field(view, "relevantLists", Instance, listCollection),
                    Field(view, "availableLists", Instance, listCollection),
                    Field(structure, "structureType", Instance, structureCategory),
                    CollectionField(structureCategory, "structures", structure),
                    MethodFromHierarchy(view, id, "GetGuid", typeof(Guid)),
                    MethodFromHierarchy(structure, id, "GetGuid", typeof(Guid)),
                    MethodFromHierarchy(upgrade, id, "GetGuid", typeof(Guid)),
                    MethodFromHierarchy(structureCategory, id, "GetGuid", typeof(Guid)),
                    MethodFromHierarchy(structureList, id, "GetGuid", typeof(Guid)),
                    Method(view, "IsAvailable", typeof(bool)),
                    FieldFromHierarchy(
                        structureList,
                        "value",
                        typeof(List<>).MakeGenericType(structure)),
                    FieldFromHierarchy(
                        upgradeList,
                        "value",
                        typeof(List<>).MakeGenericType(upgrade)));
                reason = string.Empty;
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or AmbiguousMatchException)
            {
                reason = "The exact Auto Buy owning-view binding set is unavailable: " + ex.Message;
                return false;
            }
        }

        private static Type Type(Func<string, Type?> resolve, string name) =>
            resolve(name) ?? throw new InvalidOperationException(name + " was unavailable.");

        private static FieldInfo CollectionField(
            Type owner,
            string name,
            Type element,
            BindingFlags flags = Instance) =>
            Field(owner, name, flags, typeof(List<>).MakeGenericType(element));

        private static FieldInfo Field(Type owner, string name, BindingFlags flags, Type fieldType)
        {
            var field = owner.GetField(name, flags | BindingFlags.DeclaredOnly);
            return field is not null && field.FieldType == fieldType
                ? field
                : throw new InvalidOperationException(
                    $"{owner.Name}.{name} : {fieldType.Name} was unavailable.");
        }

        private static FieldInfo FieldFromHierarchy(Type owner, string name, Type fieldType)
        {
            var field = owner.GetField(name, Instance);
            var declaring = field?.DeclaringType;
            return field is not null && field.FieldType == fieldType && declaring is not null &&
                   declaring.IsGenericType &&
                   declaring.GetGenericTypeDefinition().Name == "AbstractListVariable`1"
                ? field
                : throw new InvalidOperationException(
                    $"{owner.Name}.{name} inherited from AbstractListVariable`1 was unavailable.");
        }

        private static MethodInfo Method(Type owner, string name, Type returnType)
        {
            var method = owner.GetMethod(
                name,
                Instance | BindingFlags.DeclaredOnly,
                null,
                System.Type.EmptyTypes,
                null);
            return method is not null && method.ReturnType == returnType
                ? method
                : throw new InvalidOperationException(
                    $"{owner.Name}.{name}() : {returnType.Name} was unavailable.");
        }

        private static MethodInfo MethodFromHierarchy(
            Type owner,
            Type declaringType,
            string name,
            Type returnType)
        {
            var method = owner.GetMethod(name, Instance, null, System.Type.EmptyTypes, null);
            return method is not null && method.DeclaringType == declaringType &&
                   method.ReturnType == returnType
                ? method
                : throw new InvalidOperationException(
                    $"{owner.Name}.{name}() inherited from {declaringType.Name} was unavailable.");
        }
    }
}

/// <summary>Collects authored Auto Buy view relations once per lifecycle epoch.</summary>
internal sealed class WorldPurchaseViewRelationReader : IWorldCategoryReader
{
    internal const string CategoryName = "purchase view relations";

    private readonly NativePurchaseViewAdmissionResolver? _resolver;
    private readonly string _unavailable;

    internal WorldPurchaseViewRelationReader(Func<string, Type?> resolve)
    {
        if (NativePurchaseViewAdmissionResolver.TryCreate(resolve, out var resolver, out var reason))
        {
            _resolver = resolver;
            _unavailable = string.Empty;
        }
        else
        {
            _unavailable = reason;
        }
    }

    public string Category => CategoryName;
    public bool IsAvailable => _resolver is not null;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        frame.PurchaseViewRelations.Reset();
        if (_resolver is null) return WorldCategoryReport.Missing(Category, _unavailable);
        try
        {
            var sampled = _resolver.ReadAll(
                frame.PurchaseViewRelations,
                out var unresolved,
                out var skipped);
            return new WorldCategoryReport(
                Category,
                WorldCategoryOutcome.Collected,
                sampled,
                skipped,
                unresolved == 0 && skipped == 0
                    ? string.Empty
                    : unresolved + " candidate owning-view relation(s) were retained as named fail-closed facts; " +
                      skipped + " candidate(s) lacked a publishable exact identity");
        }
        catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or
                                   InvalidOperationException or TargetException or MemberAccessException)
        {
            return WorldCategoryReport.Missing(Category, ex.GetBaseException().Message);
        }
    }
}
