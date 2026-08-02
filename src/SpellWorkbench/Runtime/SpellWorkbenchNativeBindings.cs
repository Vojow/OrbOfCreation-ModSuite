using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace OrbAutomata;

internal sealed class SpellWorkbenchNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "spell-manager.instance",
        "spell-workbench.glyph-list-type-action",
        "spell-workbench.spell-list-type-action",
        "spell-workbench.manager-selected-core-action",
        "spell-workbench.manager-selected-augments-action",
        "spell-manager.active-spells",
        "spell-workbench.manager-get-from-recipe",
        "spell-workbench.manager-get-create-cost-action",
        "spell-workbench.manager-get-usage-cost-action",
        "spell-workbench.manager-discover",
        "spell-workbench.manager-create",
        "spell-workbench.recipe-all-action",
        "id-scriptable-object.get-guid-action",
        "spell-recipe.is-discovered",
        "spell-workbench.recipe-get-glyphs-action",
        "spell-workbench.recipe-create-empty-action",
        "spell-workbench.recipe-selected-level-action",
        "spell-workbench.recipe-usage-requirements-action",
        "spell-workbench.recipe-can-discover",
        "spell-workbench.recipe-is-creatable",
        "spell-workbench.recipe-get-discover-cost-action",
        "resource-cost-list.has-enough",
        "resource-cost-list.perform-cost",
        "spell-workbench.glyph-is-available",
        "spell-workbench.glyph-is-augment",
        "spell-composition.glyph-maximum-uses-action",
        "spell-composition.glyph-meets-non-level-requirements-action",
        "spell-workbench.list-value-action",
        "spell-workbench.list-empty-action",
        "spell-workbench.list-add-action",
        "spell-workbench.loadout-has-empty-action",
        "spell-workbench.spell-reference-action",
        "spell-workbench.spell-guid-container-action",
        "spell-workbench.spell-set-level-action",
        "spell-workbench.spell-is-unique-action",
        "spell-composition.spell-set-augments-action",
        "spell-composition.spell-get-augment-glyphs-action",
        "spell-composition.stacked-record-type-action",
        "spell-composition.stacked-record-construct-action",
        "spell-composition.stacked-record-set-action",
        "discovery-tree-offer.guid-container-value",
    };

    private SpellWorkbenchNativeBindings(Type recipeType, Type glyphType,
        Func<object?> manager, Func<IList> recipes,
        Func<object, object> core, Func<object, object> augments, Func<object, object> active,
        Func<object, IList> glyphValues, Func<object, IList> activeValues, Func<object, Guid> identity,
        Func<object, IList> recipeGlyphs, Func<object, bool> discovered,
        Func<object, bool> canDiscover, Func<object, bool> creatable,
        Func<object, object> discoverCost,
        Func<object, bool> hasEnough, Action<object> performCost,
        Func<IList> createGlyphList, Func<object, bool> glyphAvailable,
        Func<object, bool> glyphAugment, Func<object, int> glyphMaximumUsages,
        Action<object> empty,
        Action<object, object> add, Func<object, bool> hasEmpty,
        Func<object, object, object?> resolveRecipe, Func<object, object, object?> createCost,
        Func<object, object> usageCost,
        Action<object> discover,
        Action<object> create, Func<object, int, object> createEmpty,
        Func<object, int> selectedLevel, Func<object, bool> usageRequirements,
        Action<object, int> setLevel, Action<object, object> setAugments,
        Func<object, bool> unique, Func<object, IList> spellAugments,
        Func<IList, object, bool> meetsNonLevelRequirements,
        Func<object> createStackedRecord, Action<object, object, int> setStackedRecord,
        Func<object, object?> spellReference,
        Func<object, object?> spellGuid, Func<object, Guid> guidValue)
    {
        RecipeType = recipeType;
        GlyphType = glyphType;
        ReadManager = manager;
        ReadRecipes = recipes;
        ReadCore = core;
        ReadAugments = augments;
        ReadActive = active;
        ReadGlyphValues = glyphValues;
        ReadActiveValues = activeValues;
        ReadIdentity = identity;
        ReadRecipeGlyphs = recipeGlyphs;
        IsDiscovered = discovered;
        CanDiscover = canDiscover;
        IsCreatable = creatable;
        GetDiscoverCost = discoverCost;
        HasEnough = hasEnough;
        PerformCost = performCost;
        CreateGlyphList = createGlyphList;
        IsGlyphAvailable = glyphAvailable;
        IsGlyphAugment = glyphAugment;
        GetGlyphMaximumUsages = glyphMaximumUsages;
        Empty = empty;
        Add = add;
        HasEmpty = hasEmpty;
        ResolveRecipe = resolveRecipe;
        GetCreateCost = createCost;
        GetUsageCost = usageCost;
        Discover = discover;
        Create = create;
        CreateEmptySpell = createEmpty;
        GetSelectedSpellLevel = selectedLevel;
        HasMetUsageRequirements = usageRequirements;
        SetSpellLevel = setLevel;
        SetSpellAugments = setAugments;
        IsUniqueSpell = unique;
        ReadSpellAugments = spellAugments;
        MeetsNonLevelRequirements = meetsNonLevelRequirements;
        CreateStackedRecord = createStackedRecord;
        SetStackedRecord = setStackedRecord;
        ReadSpellReference = spellReference;
        ReadSpellGuid = spellGuid;
        ReadGuidValue = guidValue;
    }

    internal Type RecipeType { get; }
    internal Type GlyphType { get; }
    internal Func<object?> ReadManager { get; }
    internal Func<IList> ReadRecipes { get; }
    internal Func<object, object> ReadCore { get; }
    internal Func<object, object> ReadAugments { get; }
    internal Func<object, object> ReadActive { get; }
    internal Func<object, IList> ReadGlyphValues { get; }
    internal Func<object, IList> ReadActiveValues { get; }
    internal Func<object, Guid> ReadIdentity { get; }
    internal Func<object, IList> ReadRecipeGlyphs { get; }
    internal Func<object, bool> IsDiscovered { get; }
    internal Func<object, bool> CanDiscover { get; }
    internal Func<object, bool> IsCreatable { get; }
    internal Func<object, object> GetDiscoverCost { get; }
    internal Func<object, bool> HasEnough { get; }
    internal Action<object> PerformCost { get; }
    internal Func<IList> CreateGlyphList { get; }
    internal Func<object, bool> IsGlyphAvailable { get; }
    internal Func<object, bool> IsGlyphAugment { get; }
    internal Func<object, int> GetGlyphMaximumUsages { get; }
    internal Action<object> Empty { get; }
    internal Action<object, object> Add { get; }
    internal Func<object, bool> HasEmpty { get; }
    internal Func<object, object, object?> ResolveRecipe { get; }
    internal Func<object, object, object?> GetCreateCost { get; }
    internal Func<object, object> GetUsageCost { get; }
    internal Action<object> Discover { get; }
    internal Action<object> Create { get; }
    internal Func<object, int, object> CreateEmptySpell { get; }
    internal Func<object, int> GetSelectedSpellLevel { get; }
    internal Func<object, bool> HasMetUsageRequirements { get; }
    internal Action<object, int> SetSpellLevel { get; }
    internal Action<object, object> SetSpellAugments { get; }
    internal Func<object, bool> IsUniqueSpell { get; }
    internal Func<object, IList> ReadSpellAugments { get; }
    internal Func<IList, object, bool> MeetsNonLevelRequirements { get; }
    internal Func<object> CreateStackedRecord { get; }
    internal Action<object, object, int> SetStackedRecord { get; }
    internal Func<object, object?> ReadSpellReference { get; }
    internal Func<object, object?> ReadSpellGuid { get; }
    internal Func<object, Guid> ReadGuidValue { get; }

    internal static bool TryCreate(Func<string, Type?> resolveType,
        Func<string, bool> includeContract, out SpellWorkbenchNativeBindings? bindings,
        out string reason)
    {
        bindings = null;
        try
        {
            for (var index = 0; index < ContractIds.Length; index++)
                Require(ContractIds[index], includeContract);

            Type T(string name)
            {
                return resolveType(name) ?? throw new InvalidOperationException(name + " was unavailable.");
            }

            var managerType = T("SpellManager");
            var glyphListType = T("GlyphListVariable");
            var spellListType = T("SpellListVariable");
            var recipeType = T("SpellRecipeSO");
            var identityType = T("IdScriptableObject");
            var glyphType = T("GlyphSO");
            var costType = T("ResourceCostList");
            var spellType = T("Spell");
            var guidType = T("GuidContainer");
            var openStackedType = T("Stacked.StackedIdRecord`1");
            var glyphList = typeof(List<>).MakeGenericType(glyphType);
            var recipeList = typeof(List<>).MakeGenericType(recipeType);
            var spellList = typeof(List<>).MakeGenericType(spellType);
            var stackedType = openStackedType.MakeGenericType(glyphType);

            var managerInstance = Field(managerType, "instance", managerType, true);
            var selectedCore = Field(managerType, "selectedCoreGlyphs", glyphListType, false);
            var selectedAugments = Field(managerType, "selectedAugmentGlyphs", glyphListType, false);
            var active = Field(managerType, "activeSpells", spellListType, false);
            var allRecipes = Field(recipeType, "All", recipeList, true);
            // RecipeSO and GlyphSO share the audited IdScriptableObject identity method. Bind the
            // declaring contract once so the resulting delegate is valid for both concrete kinds.
            var identity = Method(identityType, "GetGuid", typeof(Guid));
            var recipeGlyphs = Method(recipeType, "GetGlyphRecipe", glyphList);
            var discovered = Method(recipeType, "IsDiscovered", typeof(bool));
            var canDiscover = Method(recipeType, "CanDiscover", typeof(bool));
            var creatable = Method(recipeType, "IsCreatable", typeof(bool));
            var discoverCost = Method(recipeType, "GetDiscoverCost", costType);
            var createEmpty = Method(recipeType, "CreateEmpty", spellType, typeof(int));
            var selectedLevel = Method(recipeType, "GetSelectedSpellLevel", typeof(int));
            var usageRequirements = Method(recipeType, "HasMetUsageRequirements", typeof(bool));
            var enough = Method(costType, "HasEnough", typeof(bool));
            var performCost = Method(costType, "PerformCost", typeof(void));
            var glyphAvailable = Method(glyphType, "IsAvailable", typeof(bool));
            var glyphAugment = Method(glyphType, "IsSpellAugment", typeof(bool));
            var glyphMaximumUsages = Method(glyphType, "GetMaxUsages", typeof(int));
            var listValue = HierarchyField(glyphListType, "value", glyphList);
            var activeValue = HierarchyField(spellListType, "value", spellList);
            var empty = HierarchyMethod(glyphListType, "Empty", typeof(void));
            var add = HierarchyMethod(glyphListType, "Add", typeof(void), glyphType);
            var hasEmpty = HierarchyMethod(spellListType, "HasEmptySpot", typeof(bool));
            var resolve = Method(managerType, "GetSpellFromRecipe", recipeType, glyphList);
            var createCost = Method(managerType, "GetSpellCreateCost", costType, glyphList);
            var usageCost = StaticMethod(managerType, "GetUsageCostOfSpell", costType, spellType);
            var discover = Method(managerType, "DiscoverSpell", typeof(void));
            var create = Method(managerType, "CreateSpell", typeof(void));
            var setLevel = Method(spellType, "SetLevel", typeof(void), typeof(int));
            var setAugments = Method(spellType, "SetAugmentGlyphs", typeof(void), stackedType);
            var unique = Method(spellType, "IsUniqueSpell", typeof(bool));
            var spellAugments = Method(spellType, "GetAugmentGlyphs", glyphList);
            var meetsNonLevel = StaticMethod(
                glyphType, "MeetsNonLvRequirements", typeof(bool), glyphList, spellType);
            var stackedConstructor = stackedType.GetConstructor(Type.EmptyTypes) ??
                throw new InvalidOperationException(stackedType.Name + ".ctor was unavailable.");
            var setStacked = HierarchyMethod(stackedType, "Set", typeof(void), glyphType, typeof(int));
            var spellReference = Method(spellType, "get_reference", recipeType);
            var spellGuid = Field(spellType, "guidContainer", guidType, false);
            var guidValue = Method(guidType, "get_guid", typeof(Guid));

            bindings = new SpellWorkbenchNativeBindings(
                recipeType, glyphType, StaticObject(managerInstance), StaticList(allRecipes),
                ObjectField(selectedCore), ObjectField(selectedAugments), ObjectField(active),
                ListField(listValue), ListField(activeValue), InstanceFunc<Guid>(identity),
                InstanceList(recipeGlyphs), InstanceFunc<bool>(discovered),
                InstanceFunc<bool>(canDiscover), InstanceFunc<bool>(creatable),
                InstanceObject(discoverCost), InstanceFunc<bool>(enough), InstanceAction(performCost),
                NewList(glyphList),
                InstanceFunc<bool>(glyphAvailable), InstanceFunc<bool>(glyphAugment),
                InstanceFunc<int>(glyphMaximumUsages),
                InstanceAction(empty), InstanceObjectAction(add), InstanceFunc<bool>(hasEmpty),
                InstanceObjectObject(resolve), InstanceObjectObject(createCost),
                StaticObjectObject(usageCost), InstanceAction(discover), InstanceAction(create),
                InstanceIntObject(createEmpty), InstanceFunc<int>(selectedLevel),
                InstanceFunc<bool>(usageRequirements), InstanceIntAction(setLevel),
                InstanceObjectAction(setAugments), InstanceFunc<bool>(unique),
                InstanceList(spellAugments), StaticListObjectBoolean(meetsNonLevel),
                NewObject(stackedConstructor), InstanceObjectIntAction(setStacked),
                InstanceNullableObject(spellReference), ObjectNullableField(spellGuid),
                InstanceFunc<Guid>(guidValue));
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or AmbiguousMatchException)
        {
            reason = "The complete spell workbench binding set is unavailable: " + ex.Message;
            return false;
        }
    }

    private static void Require(string id, Func<string, bool> include)
    {
        if (!include(id)) throw new InvalidOperationException("Required contract " + id + " was withheld.");
    }

    private static FieldInfo Field(Type type, string name, Type valueType, bool isStatic)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic |
            (isStatic ? BindingFlags.Static : BindingFlags.Instance));
        if (field is null || field.FieldType != valueType || field.IsStatic != isStatic)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return field;
    }

    private static FieldInfo HierarchyField(Type type, string name, Type valueType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null && field.FieldType == valueType) return field;
        }
        throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
    }

    private static MethodInfo Method(Type type, string name, Type result, params Type[] arguments)
    {
        var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic, null, arguments, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return method;
    }

    private static MethodInfo HierarchyMethod(Type type, string name, Type result, params Type[] arguments)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(name, BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, arguments, null);
            if (method is not null && !method.IsStatic && method.ReturnType == result) return method;
        }
        throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
    }

    private static MethodInfo StaticMethod(Type type, string name, Type result, params Type[] arguments)
    {
        var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public |
            BindingFlags.NonPublic, null, arguments, null);
        if (method is null || !method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return method;
    }

    private static Func<object?> StaticObject(FieldInfo field) =>
        Expression.Lambda<Func<object?>>(Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();

    private static Func<IList> StaticList(FieldInfo field) =>
        Expression.Lambda<Func<IList>>(Expression.Convert(Expression.Field(null, field), typeof(IList))).Compile();

    private static Func<IList> NewList(Type type) =>
        Expression.Lambda<Func<IList>>(
            Expression.Convert(Expression.New(type), typeof(IList))).Compile();

    private static Func<object> NewObject(ConstructorInfo constructor) =>
        Expression.Lambda<Func<object>>(
            Expression.Convert(Expression.New(constructor), typeof(object))).Compile();

    private static Func<object, object> ObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object>>(
            Expression.Convert(Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(object)), target).Compile();
    }

    private static Func<object, object?> ObjectNullableField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(object)), target).Compile();
    }

    private static Func<object, IList> ListField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, IList>>(
            Expression.Convert(
                Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
                typeof(IList)), target).Compile();
    }

    private static Func<object, T> InstanceFunc<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method), typeof(T)), target).Compile();
    }

    private static Func<object, object> InstanceObject(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method), typeof(object)), target).Compile();
    }

    private static Func<object, int, object> InstanceIntObject(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(int), "value");
        return Expression.Lambda<Func<object, int, object>>(
            Expression.Convert(Expression.Call(
                Expression.Convert(target, method.DeclaringType!), method, value), typeof(object)),
            target, value).Compile();
    }

    private static Action<object, int> InstanceIntAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(int), "value");
        return Expression.Lambda<Action<object, int>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method, value),
            target, value).Compile();
    }

    private static Func<object, object?> InstanceNullableObject(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method), typeof(object)), target).Compile();
    }

    private static Func<object, IList> InstanceList(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, IList>>(
            Expression.Convert(
                Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
                typeof(IList)), target).Compile();
    }

    private static Action<object> InstanceAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }

    private static Action<object, object> InstanceObjectAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Action<object, object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(value, method.GetParameters()[0].ParameterType)), target, value).Compile();
    }

    private static Func<object, object, object?> InstanceObjectObject(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Func<object, object, object?>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(value, method.GetParameters()[0].ParameterType)), typeof(object)), target, value).Compile();
    }

    private static Action<object, object, int> InstanceObjectIntAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var item = Expression.Parameter(typeof(object), "item");
        var quantity = Expression.Parameter(typeof(int), "quantity");
        return Expression.Lambda<Action<object, object, int>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(item, method.GetParameters()[0].ParameterType), quantity),
            target, item, quantity).Compile();
    }

    private static Func<object, object> StaticObjectObject(MethodInfo method)
    {
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Func<object, object>>(
            Expression.Convert(Expression.Call(
                method, Expression.Convert(value, method.GetParameters()[0].ParameterType)),
                typeof(object)), value).Compile();
    }

    private static Func<IList, object, bool> StaticListObjectBoolean(MethodInfo method)
    {
        var values = Expression.Parameter(typeof(IList), "values");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Func<IList, object, bool>>(
            Expression.Call(method,
                Expression.Convert(values, method.GetParameters()[0].ParameterType),
                Expression.Convert(value, method.GetParameters()[1].ParameterType)),
            values, value).Compile();
    }
}
