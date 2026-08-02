using System;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class ResearchNativeBindings
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static readonly string[] ContractIds =
    {
        "research-action.research.type-action", "research-action.cost.type-action",
        "research-action.settings.type-action", "research-action.globals.type-action",
        "research-action.int-variable.type-action", "research-action.level-action",
        "research-action.waiting-levels-action", "research-action.stage-action",
        "research-action.self-bonus-action", "research-action.active-action",
        "research-action.developing-action", "research-action.max-level-action",
        "research-action.can-develop-action", "research-action.within-range-action",
        "research-action.can-bonus-action", "research-action.purchased-levels-action",
        "research-action.bonus-level-action", "research-action.total-level-action",
        "research-action.queued-levels-action", "research-action.investment-level-action",
        "research-action.time-ratio-action", "research-action.free-bonus-action",
        "research-action.development-cost-action", "research-action.cost-enough-action",
        "research-action.queue-mode-action", "research-action.multi-buy-action",
        "research-action.int-as-int-action", "research-action.purchase-action",
        "research-action.pause-action", "research-action.resume-action",
        "research-action.cancel-action", "research-action.submit-bonus-action",
        "research-action.has-max-level-action", "research-action.development-cost-at-level-action",
        "research-action.within-range-at-action", "research-action.cost-add-action",
    };

    private ResearchNativeBindings(Type researchType,
        Func<object, int> level, Func<object, int> waiting, Func<object, int> stage,
        Func<object, int> selfBonus, Func<object, bool> active, Func<object, bool> developing,
        Func<object, int> maximum, Func<object, bool> canDevelop, Func<object, bool> withinRange,
        Func<object, bool> canBonus, Func<object, int> purchased, Func<object, int> bonus,
        Func<object, int> total, Func<object, int> queued, Func<object, int> investment,
        Func<object, BigDouble> timeRatio, Func<object, int> freeBonus,
        Func<object, object?> developmentCost, Func<object, bool> enough,
        Func<bool> queueMode, Func<object?> multiBuy, Func<object, int> asInt,
        Action<object> purchase, Action<object> pause, Action<object> resume,
        Action<object> cancel, Action<object> submitBonus, Func<object, bool> hasMaxLevel,
        Func<object, int, object?> developmentCostAtLevel,
        Func<object, int, bool> withinRangeAt, Func<object, object, object?> addCost)
    {
        ResearchType = researchType; Level = level; WaitingLevels = waiting; Stage = stage;
        SelfBonusLevels = selfBonus; IsActive = active; IsDeveloping = developing;
        MaxLevel = maximum; CanDevelop = canDevelop; WithinDevelopRange = withinRange;
        CanApplyBonusLevel = canBonus; PurchasedLevels = purchased; BonusLevel = bonus;
        TotalLevel = total; QueuedLevels = queued; CurrentInvestmentLevel = investment;
        TimeRatio = timeRatio; FreeBonusLevels = freeBonus; DevelopmentCost = developmentCost;
        HasEnough = enough; QueueMode = queueMode; MultiBuy = multiBuy; AsInt = asInt;
        Purchase = purchase; Pause = pause; Resume = resume; Cancel = cancel;
        SubmitBonus = submitBonus; HasMaxLevel = hasMaxLevel;
        DevelopmentCostAtLevel = developmentCostAtLevel;
        WithinDevelopRangeAt = withinRangeAt; AddCost = addCost;
    }

    internal Type ResearchType { get; }
    internal Func<object, int> Level { get; }
    internal Func<object, int> WaitingLevels { get; }
    internal Func<object, int> Stage { get; }
    internal Func<object, int> SelfBonusLevels { get; }
    internal Func<object, bool> IsActive { get; }
    internal Func<object, bool> IsDeveloping { get; }
    internal Func<object, int> MaxLevel { get; }
    internal Func<object, bool> CanDevelop { get; }
    internal Func<object, bool> WithinDevelopRange { get; }
    internal Func<object, bool> CanApplyBonusLevel { get; }
    internal Func<object, int> PurchasedLevels { get; }
    internal Func<object, int> BonusLevel { get; }
    internal Func<object, int> TotalLevel { get; }
    internal Func<object, int> QueuedLevels { get; }
    internal Func<object, int> CurrentInvestmentLevel { get; }
    internal Func<object, BigDouble> TimeRatio { get; }
    internal Func<object, int> FreeBonusLevels { get; }
    internal Func<object, object?> DevelopmentCost { get; }
    internal Func<object, bool> HasEnough { get; }
    internal Func<bool> QueueMode { get; }
    internal Func<object?> MultiBuy { get; }
    internal Func<object, int> AsInt { get; }
    internal Action<object> Purchase { get; }
    internal Action<object> Pause { get; }
    internal Action<object> Resume { get; }
    internal Action<object> Cancel { get; }
    internal Action<object> SubmitBonus { get; }
    internal Func<object, bool> HasMaxLevel { get; }
    internal Func<object, int, object?> DevelopmentCostAtLevel { get; }
    internal Func<object, int, bool> WithinDevelopRangeAt { get; }
    internal Func<object, object, object?> AddCost { get; }

    internal static bool TryCreate(out ResearchNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null)
    {
        bindings = null;
        resolveType ??= ReflectionUtil.FindLoadedType;
        includeContract ??= static _ => true;
        try
        {
            Type T(int index, string name)
            {
                Require(ContractIds[index], includeContract);
                return resolveType(name) ?? throw new InvalidOperationException(name + " was unavailable");
            }
            var research = T(0, "ResearchSO");
            var cost = T(1, "ResourceCostList");
            var settings = T(2, "SettingsManager");
            var globals = T(3, "GlobalVariables");
            var integer = T(4, "IntVariable");
            bindings = new ResearchNativeBindings(research,
                Func<int>(Field(5, research, "level", typeof(int), includeContract)),
                Func<int>(Field(6, research, "queuedLevels", typeof(int), includeContract)),
                Func<int>(Field(7, research, "researchStage", typeof(int), includeContract)),
                Func<int>(Field(8, research, "selfBonusLevels", typeof(int), includeContract)),
                Func<bool>(Field(9, research, "isActive", typeof(bool), includeContract)),
                Func<bool>(Field(10, research, "isDeveloping", typeof(bool), includeContract)),
                Func<int>(Field(11, research, "maxLevel", typeof(int), includeContract)),
                Func<bool>(Method(12, research, "CanDevelop", typeof(bool), includeContract)),
                Func<bool>(Method(13, research, "IsWithinDevelopRange", typeof(bool), includeContract)),
                Func<bool>(Method(14, research, "CanApplyBonusLevels", typeof(bool), includeContract)),
                Func<int>(Method(15, research, "GetPurchasedLevels", typeof(int), includeContract)),
                Func<int>(Method(16, research, "GetBonusLevels", typeof(int), includeContract)),
                Func<int>(Method(17, research, "GetLevel", typeof(int), includeContract)),
                Func<int>(Method(18, research, "GetQueuedLevels", typeof(int), includeContract)),
                Func<int>(Method(19, research, "GetCurrentInvestmentLevel", typeof(int), includeContract)),
                Func<BigDouble>(Method(20, research, "GetTimeRatio", typeof(BigDouble), includeContract)),
                Func<int>(Method(21, research, "GetFreeBonusLevelsLeft", typeof(int), includeContract)),
                ObjectFunc(Method(22, research, "GetDevelopmentCost", cost, includeContract)),
                Func<bool>(Method(23, cost, "HasEnough", typeof(bool), includeContract)),
                StaticFunc<bool>(StaticMethod(24, settings, "IsResearchQueueMode", typeof(bool), includeContract)),
                StaticObject(StaticMethod(25, globals, "GetMultiBuy", integer, includeContract)),
                Func<int>(Method(26, integer, "AsInt", typeof(int), includeContract)),
                Action1(Method(27, research, "PurchaseLevel", typeof(void), includeContract)),
                Action1(Method(28, research, "PauseResearch", typeof(void), includeContract)),
                Action1(Method(29, research, "ResumeResearch", typeof(void), includeContract)),
                Action1(Method(30, research, "CancelDevelopment", typeof(void), includeContract)),
                Action1(Method(31, research, "SubmitBonusLevel", typeof(void), includeContract)),
                Func<bool>(Method(32, research, "HasMaxLevel", typeof(bool), includeContract)),
                IntObjectFunc(Method(33, research, "GetDevelopmentCostAtLevel", cost,
                    includeContract, typeof(int))),
                IntFunc<bool>(Method(34, research, "IsWithinDevelopRangeAt", typeof(bool),
                    includeContract, typeof(int))),
                ObjectObjectFunc(Method(35, cost, "Add", cost, includeContract, cost)));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or AmbiguousMatchException or ArgumentException)
        {
            reason = "The complete research action binding set is unavailable: " + exception.Message;
            return false;
        }
    }

    private static void Require(string id, Func<string, bool> include)
    { if (!include(id)) throw new InvalidOperationException("Required contract " + id + " was withheld"); }

    private static FieldInfo Field(int index, Type owner, string name, Type type, Func<string, bool> include)
    {
        Require(ContractIds[index], include);
        var field = owner.GetField(name, Instance);
        if (field is null || field.IsStatic || field.FieldType != type)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited field");
        return field;
    }

    private static MethodInfo Method(int index, Type owner, string name, Type result,
        Func<string, bool> include, params Type[] parameters)
    {
        Require(ContractIds[index], include);
        var method = owner.GetMethod(name, Instance, null, parameters, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited signature");
        return method;
    }

    private static MethodInfo StaticMethod(int index, Type owner, string name, Type result,
        Func<string, bool> include)
    {
        Require(ContractIds[index], include);
        var method = owner.GetMethod(name, Static, null, Type.EmptyTypes, null);
        if (method is null || !method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited static signature");
        return method;
    }

    private static Func<object, T> Func<T>(MemberInfo member)
    {
        var target = Expression.Parameter(typeof(object), "target");
        Expression value = member is FieldInfo field
            ? Expression.Field(Expression.Convert(target, field.DeclaringType!), field)
            : Expression.Call(Expression.Convert(target, member.DeclaringType!), (MethodInfo)member);
        return Expression.Lambda<Func<object, T>>(Expression.Convert(value, typeof(T)), target).Compile();
    }

    private static Func<T> StaticFunc<T>(MethodInfo method) =>
        Expression.Lambda<Func<T>>(Expression.Call(method)).Compile();

    private static Func<object?> StaticObject(MethodInfo method) =>
        Expression.Lambda<Func<object?>>(Expression.Convert(Expression.Call(method), typeof(object))).Compile();

    private static Func<object, object?> ObjectFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method), typeof(object)), target).Compile();
    }

    private static Action<object> Action1(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }

    private static Func<object, int, T> IntFunc<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(int), "value");
        return Expression.Lambda<Func<object, int, T>>(Expression.Convert(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method, value), typeof(T)), target, value).Compile();
    }

    private static Func<object, int, object?> IntObjectFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(int), "value");
        return Expression.Lambda<Func<object, int, object?>>(Expression.Convert(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method, value), typeof(object)), target, value).Compile();
    }

    private static Func<object, object, object?> ObjectObjectFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Func<object, object, object?>>(Expression.Convert(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method,
            Expression.Convert(value, method.GetParameters()[0].ParameterType)), typeof(object)), target, value).Compile();
    }
}
