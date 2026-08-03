using System;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Exact v1.0.5 binding set for the player-visible Back to Menu button.</summary>
internal sealed class ReturnToMenuNativeBindings
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static readonly string[] ContractIds =
    {
        "return-to-menu.button.type-action",
        "return-to-menu.button-action",
        "return-to-menu.screen-flash.type-action",
        "return-to-menu.screen-flash-instance-action",
        "return-to-menu.screen-flash-active-action",
    };

    private ReturnToMenuNativeBindings(
        Type buttonType,
        Action<object> backToMenu,
        Func<object?> screenFlash,
        Func<object, bool> flashActive)
    {
        ButtonType = buttonType;
        BackToMenu = backToMenu;
        ScreenFlash = screenFlash;
        FlashActive = flashActive;
    }

    internal Type ButtonType { get; }
    internal Action<object> BackToMenu { get; }
    internal Func<object?> ScreenFlash { get; }
    internal Func<object, bool> FlashActive { get; }

    internal static bool TryCreate(
        out ReturnToMenuNativeBindings? bindings,
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        bindings = null;
        resolveType ??= ReflectionUtil.FindLoadedType;
        includeContract ??= static _ => true;
        try
        {
            Require(0, includeContract);
            var button = resolveType("UIBackToMenuButton") ??
                throw new InvalidOperationException("UIBackToMenuButton was unavailable");
            var backToMenu = Method(1, button, "BackToMenu", typeof(void), includeContract);
            Require(2, includeContract);
            var flash = resolveType("UIScreenFlash") ??
                throw new InvalidOperationException("UIScreenFlash was unavailable");
            var instance = Field(3, flash, "instance", flash, true, includeContract);
            var active = Field(4, flash, "isActive", typeof(bool), false, includeContract);
            bindings = new ReturnToMenuNativeBindings(
                button, Action(backToMenu), StaticObject(instance), FieldFunc<bool>(active));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or AmbiguousMatchException or NotSupportedException)
        {
            reason = "Back to Menu contracts are unavailable: " +
                exception.GetBaseException().Message;
            return false;
        }
    }

    private static void Require(int index, Func<string, bool> include)
    {
        if (!include(ContractIds[index]))
            throw new InvalidOperationException(ContractIds[index] + " was unavailable");
    }

    private static MethodInfo Method(
        int index,
        Type owner,
        string name,
        Type result,
        Func<string, bool> include)
    {
        Require(index, include);
        var method = owner.GetMethod(name, Instance, null, Type.EmptyTypes, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match.");
        return method;
    }

    private static FieldInfo Field(
        int index,
        Type owner,
        string name,
        Type type,
        bool isStatic,
        Func<string, bool> include)
    {
        Require(index, include);
        var field = owner.GetField(name, isStatic ? Static : Instance);
        if (field is null || field.IsStatic != isStatic || field.FieldType != type)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match.");
        return field;
    }

    private static Action<object> Action(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            target).Compile();
    }

    private static Func<object?> StaticObject(FieldInfo field) =>
        Expression.Lambda<Func<object?>>(
            Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();

    private static Func<object, T> FieldFunc<T>(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
            target).Compile();
    }
}
