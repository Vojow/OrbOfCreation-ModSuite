using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace OrbAutomata;

internal sealed class SpellCompositionNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "spell-composition.player-instance-action",
        "spell-composition.player-output-level-action",
        "spell-composition.player-maximum-output-level-action",
        "int-variable.as-int",
        "int-variable.set-value",
        "spell-manager.instance",
        "spell-manager.active-spells",
        "spell-workbench.spell-list-type-action",
        "spell-workbench.list-value-action",
        "spell-workbench.spell-guid-container-action",
        "discovery-tree-offer.guid-container-value",
        "spell-workbench.spell-reference-action",
        "id-scriptable-object.get-guid-action",
        "spell-composition.glyph-all-action",
        "spell-workbench.glyph-is-available",
        "spell-workbench.glyph-is-augment",
        "spell-composition.glyph-get-maximum-usages-action",
        "spell-composition.glyph-meets-non-level-requirements-action",
        "spell-composition.glyph-get-mastery-requirement-action",
        "spell-composition.spell-get-augment-glyphs-action",
        "spell-composition.spell-get-glyph-quantity-action",
        "spell-composition.spell-get-recipe-mastery-action",
        "spell-composition.spell-set-augments-action",
        "spell-composition.stacked-record-type-action",
        "spell-composition.stacked-record-construct-action",
        "spell-composition.stacked-record-set-action",
    };

    private SpellCompositionNativeBindings(
        Type spellType,
        Type glyphType,
        Func<object?> player,
        Func<object?> outputVariable,
        Func<object, object> maximumOutputVariable,
        Func<object, int> asInt,
        Action<object, int> setInt,
        Func<object?> manager,
        Func<object, object> active,
        Func<object, IList> activeValues,
        Func<object, object?> spellGuid,
        Func<object, Guid> guidValue,
        Func<object, object?> spellReference,
        Func<object, Guid> identity,
        Func<IList> glyphs,
        Func<object, bool> glyphAvailable,
        Func<object, bool> glyphAugment,
        Func<object, int> glyphMaximumUsages,
        Func<object, object, bool> meetsNonLevelRequirements,
        Func<object, int> masteryRequirement,
        Func<IList> createGlyphList,
        Func<object, IList> spellAugments,
        Func<object, object, int> glyphQuantity,
        Func<object, int> recipeMastery,
        Func<object> createRecord,
        Action<object, object, int> setRecord,
        Action<object, object> setAugments)
    {
        SpellType = spellType;
        GlyphType = glyphType;
        ReadPlayer = player;
        ReadOutputVariable = outputVariable;
        ReadMaximumOutputVariable = maximumOutputVariable;
        ReadInt = asInt;
        SetInt = setInt;
        ReadManager = manager;
        ReadActive = active;
        ReadActiveValues = activeValues;
        ReadSpellGuid = spellGuid;
        ReadGuidValue = guidValue;
        ReadSpellReference = spellReference;
        ReadIdentity = identity;
        ReadGlyphs = glyphs;
        IsGlyphAvailable = glyphAvailable;
        IsGlyphAugment = glyphAugment;
        GetGlyphMaximumUsages = glyphMaximumUsages;
        MeetsNonLevelRequirements = meetsNonLevelRequirements;
        GetMasteryRequirement = masteryRequirement;
        CreateGlyphList = createGlyphList;
        ReadSpellAugments = spellAugments;
        GetGlyphQuantity = glyphQuantity;
        GetRecipeMastery = recipeMastery;
        CreateRecord = createRecord;
        SetRecord = setRecord;
        SetAugments = setAugments;
    }

    internal Type SpellType { get; }
    internal Type GlyphType { get; }
    internal Func<object?> ReadPlayer { get; }
    internal Func<object?> ReadOutputVariable { get; }
    internal Func<object, object> ReadMaximumOutputVariable { get; }
    internal Func<object, int> ReadInt { get; }
    internal Action<object, int> SetInt { get; }
    internal Func<object?> ReadManager { get; }
    internal Func<object, object> ReadActive { get; }
    internal Func<object, IList> ReadActiveValues { get; }
    internal Func<object, object?> ReadSpellGuid { get; }
    internal Func<object, Guid> ReadGuidValue { get; }
    internal Func<object, object?> ReadSpellReference { get; }
    internal Func<object, Guid> ReadIdentity { get; }
    internal Func<IList> ReadGlyphs { get; }
    internal Func<object, bool> IsGlyphAvailable { get; }
    internal Func<object, bool> IsGlyphAugment { get; }
    internal Func<object, int> GetGlyphMaximumUsages { get; }
    internal Func<object, object, bool> MeetsNonLevelRequirements { get; }
    internal Func<object, int> GetMasteryRequirement { get; }
    internal Func<IList> CreateGlyphList { get; }
    internal Func<object, IList> ReadSpellAugments { get; }
    internal Func<object, object, int> GetGlyphQuantity { get; }
    internal Func<object, int> GetRecipeMastery { get; }
    internal Func<object> CreateRecord { get; }
    internal Action<object, object, int> SetRecord { get; }
    internal Action<object, object> SetAugments { get; }

    internal static bool TryCreate(
        Func<string, Type?> resolveType,
        Func<string, bool> includeContract,
        out SpellCompositionNativeBindings? bindings,
        out string reason)
    {
        bindings = null;
        try
        {
            foreach (var id in ContractIds) Require(id, includeContract);
            Type T(string name) => resolveType(name) ??
                throw new InvalidOperationException(name + " was unavailable.");

            var playerType = T("Player");
            var intType = T("IntVariable");
            var managerType = T("SpellManager");
            var spellListType = T("SpellListVariable");
            var spellType = T("Spell");
            var recipeType = T("SpellRecipeSO");
            var glyphType = T("GlyphSO");
            var identityType = T("IdScriptableObject");
            var guidType = T("GuidContainer");
            var stackedOpen = T("Stacked.StackedIdRecord`1");
            var stackedType = stackedOpen.MakeGenericType(glyphType);
            var glyphListType = typeof(List<>).MakeGenericType(glyphType);
            var spellListValueType = typeof(List<>).MakeGenericType(spellType);

            var playerInstance = Field(playerType, "_instance", playerType, true);
            var output = StaticMethod(playerType, "GetSpellOutputLevel", intType);
            var maximumOutput = Field(playerType, "maxSpellOutputLevel", intType, false);
            var asInt = Method(intType, "AsInt", typeof(int));
            var setInt = Method(intType, "SetValue", typeof(void), typeof(int));
            var managerInstance = Field(managerType, "instance", managerType, true);
            var activeField = Field(managerType, "activeSpells", spellListType, false);
            var activeValues = HierarchyField(spellListType, "value", spellListValueType);
            var spellGuid = Field(spellType, "guidContainer", guidType, false);
            var guidValue = Method(guidType, "get_guid", typeof(Guid));
            var spellReference = Method(spellType, "get_reference", recipeType);
            var identity = Method(identityType, "GetGuid", typeof(Guid));
            var glyphAll = Field(glyphType, "All", glyphListType, true);
            var glyphAvailable = Method(glyphType, "IsAvailable", typeof(bool));
            var glyphAugment = Method(glyphType, "IsSpellAugment", typeof(bool));
            var glyphMaximum = Method(glyphType, "GetMaxUsages", typeof(int));
            var meets = StaticMethod(
                glyphType,
                "MeetsNonLvRequirements",
                typeof(bool),
                glyphListType,
                spellType);
            var mastery = StaticMethod(
                glyphType,
                "GetMasterReqOfList",
                typeof(int),
                glyphListType);
            var spellAugments = Method(spellType, "GetAugmentGlyphs", glyphListType);
            var glyphQuantity = Method(
                spellType,
                "GetQuantityOfGlyph",
                typeof(int),
                glyphType);
            var recipeMastery = Method(spellType, "GetRecipeMasteryLevel", typeof(int));
            var setAugments = Method(spellType, "SetAugmentGlyphs", typeof(void), stackedType);
            var recordConstructor = stackedType.GetConstructor(Type.EmptyTypes) ??
                throw new InvalidOperationException(stackedType.Name + " default constructor was unavailable.");
            var recordSet = HierarchyMethod(
                stackedType,
                "Set",
                typeof(void),
                glyphType,
                typeof(int));

            bindings = new SpellCompositionNativeBindings(
                spellType,
                glyphType,
                StaticObject(playerInstance),
                StaticCall(output),
                ObjectField(maximumOutput),
                InstanceFunc<int>(asInt),
                InstanceValueAction<int>(setInt),
                StaticObject(managerInstance),
                ObjectField(activeField),
                ListField(activeValues),
                NullableObjectField(spellGuid),
                InstanceFunc<Guid>(guidValue),
                InstanceNullableObject(spellReference),
                InstanceFunc<Guid>(identity),
                StaticList(glyphAll),
                InstanceFunc<bool>(glyphAvailable),
                InstanceFunc<bool>(glyphAugment),
                InstanceFunc<int>(glyphMaximum),
                StaticObjectObjectFunc<bool>(meets),
                StaticObjectFunc<int>(mastery),
                ListConstructor(glyphListType),
                InstanceList(spellAugments),
                InstanceObjectFunc<int>(glyphQuantity),
                InstanceFunc<int>(recipeMastery),
                ObjectConstructor(recordConstructor),
                InstanceObjectValueAction<int>(recordSet),
                InstanceObjectAction(setAugments));
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or AmbiguousMatchException)
        {
            reason = "The complete spell composition binding set is unavailable: " + ex.Message;
            return false;
        }
    }

    private static void Require(string id, Func<string, bool> include)
    {
        if (!include(id)) throw new InvalidOperationException("Required contract " + id + " was withheld.");
    }

    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static FieldInfo Field(Type type, string name, Type valueType, bool isStatic)
    {
        var field = type.GetField(name, isStatic ? Static : Instance);
        if (field is null || field.FieldType != valueType || field.IsStatic != isStatic)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return field;
    }

    private static FieldInfo HierarchyField(Type type, string name, Type valueType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, Instance | BindingFlags.DeclaredOnly);
            if (field is not null && field.FieldType == valueType) return field;
        }
        throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
    }

    private static MethodInfo Method(Type type, string name, Type result, params Type[] arguments)
    {
        var method = type.GetMethod(name, Instance, null, arguments, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return method;
    }

    private static MethodInfo StaticMethod(Type type, string name, Type result, params Type[] arguments)
    {
        var method = type.GetMethod(name, Static, null, arguments, null);
        if (method is null || !method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return method;
    }

    private static MethodInfo HierarchyMethod(Type type, string name, Type result, params Type[] arguments)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(name, Instance | BindingFlags.DeclaredOnly, null, arguments, null);
            if (method is not null && !method.IsStatic && method.ReturnType == result) return method;
        }
        throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
    }

    private static Func<object?> StaticObject(FieldInfo field) =>
        Expression.Lambda<Func<object?>>(Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();

    private static Func<IList> StaticList(FieldInfo field) =>
        Expression.Lambda<Func<IList>>(Expression.Convert(Expression.Field(null, field), typeof(IList))).Compile();

    private static Func<object?> StaticCall(MethodInfo method) =>
        Expression.Lambda<Func<object?>>(Expression.Convert(Expression.Call(method), typeof(object))).Compile();

    private static Func<object, object> ObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object));
        return Expression.Lambda<Func<object, object>>(
            Expression.Convert(Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(object)),
            target).Compile();
    }

    private static Func<object, object?> NullableObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object));
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(object)),
            target).Compile();
    }

    private static Func<object, IList> ListField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object));
        return Expression.Lambda<Func<object, IList>>(
            Expression.Convert(Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(IList)),
            target).Compile();
    }

    private static Func<object, T> InstanceFunc<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object));
        return Expression.Lambda<Func<object, T>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method), typeof(T)),
            target).Compile();
    }

    private static Func<object, object?> InstanceNullableObject(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object));
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method), typeof(object)),
            target).Compile();
    }

    private static Func<object, IList> InstanceList(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object));
        return Expression.Lambda<Func<object, IList>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method), typeof(IList)),
            target).Compile();
    }

    private static Action<object, T> InstanceValueAction<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object));
        var value = Expression.Parameter(typeof(T));
        return Expression.Lambda<Action<object, T>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method, value),
            target,
            value).Compile();
    }

    private static Action<object, object> InstanceObjectAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object));
        var value = Expression.Parameter(typeof(object));
        return Expression.Lambda<Action<object, object>>(
            Expression.Call(
                Expression.Convert(target, method.DeclaringType!),
                method,
                Expression.Convert(value, method.GetParameters()[0].ParameterType)),
            target,
            value).Compile();
    }

    private static Func<object, object, T> InstanceObjectFunc<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object));
        var value = Expression.Parameter(typeof(object));
        return Expression.Lambda<Func<object, object, T>>(
            Expression.Convert(
                Expression.Call(
                    Expression.Convert(target, method.DeclaringType!),
                    method,
                    Expression.Convert(value, method.GetParameters()[0].ParameterType)),
                typeof(T)),
            target,
            value).Compile();
    }

    private static Func<object, object, T> StaticObjectObjectFunc<T>(MethodInfo method)
    {
        var first = Expression.Parameter(typeof(object));
        var second = Expression.Parameter(typeof(object));
        var parameters = method.GetParameters();
        return Expression.Lambda<Func<object, object, T>>(
            Expression.Convert(
                Expression.Call(
                    method,
                    Expression.Convert(first, parameters[0].ParameterType),
                    Expression.Convert(second, parameters[1].ParameterType)),
                typeof(T)),
            first,
            second).Compile();
    }

    private static Func<object, T> StaticObjectFunc<T>(MethodInfo method)
    {
        var value = Expression.Parameter(typeof(object));
        return Expression.Lambda<Func<object, T>>(
            Expression.Convert(
                Expression.Call(method, Expression.Convert(value, method.GetParameters()[0].ParameterType)),
                typeof(T)),
            value).Compile();
    }

    private static Func<IList> ListConstructor(Type type) =>
        Expression.Lambda<Func<IList>>(Expression.Convert(Expression.New(type), typeof(IList))).Compile();

    private static Func<object> ObjectConstructor(ConstructorInfo constructor) =>
        Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(constructor), typeof(object))).Compile();

    private static Action<object, object, T> InstanceObjectValueAction<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object));
        var item = Expression.Parameter(typeof(object));
        var value = Expression.Parameter(typeof(T));
        return Expression.Lambda<Action<object, object, T>>(
            Expression.Call(
                Expression.Convert(target, method.DeclaringType!),
                method,
                Expression.Convert(item, method.GetParameters()[0].ParameterType),
                value),
            target,
            item,
            value).Compile();
    }
}
