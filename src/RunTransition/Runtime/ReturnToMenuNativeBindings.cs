using System;
using System.Collections.Generic;
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
        "return-to-menu.button-component-action",
        "return-to-menu.component-game-object-action",
        "return-to-menu.behaviour-enabled-action",
        "return-to-menu.game-object-active-action",
        "return-to-menu.selectable-interactable-action",
        "return-to-menu.object-name-action",
    };

    private ReturnToMenuNativeBindings(
        Type buttonType,
        Action<object> backToMenu,
        Func<object?> screenFlash,
        Func<object, bool> flashActive,
        Func<object, bool> controlLive,
        Func<object, string> controlName)
    {
        ButtonType = buttonType;
        BackToMenu = backToMenu;
        ScreenFlash = screenFlash;
        FlashActive = flashActive;
        ControlLive = controlLive;
        ControlName = controlName;
    }

    internal Type ButtonType { get; }
    internal Action<object> BackToMenu { get; }
    internal Func<object?> ScreenFlash { get; }
    internal Func<object, bool> FlashActive { get; }
    internal Func<object, bool> ControlLive { get; }
    internal Func<object, string> ControlName { get; }

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
            var controlButton = Field(5, button, "button", typeof(UnityEngine.UI.Button),
                false, includeContract);
            var gameObject = Method(6, typeof(UnityEngine.Component), "get_gameObject",
                typeof(UnityEngine.GameObject), includeContract);
            var enabled = Method(7, typeof(UnityEngine.Behaviour), "get_enabled",
                typeof(bool), includeContract);
            var activeInHierarchy = Method(8, typeof(UnityEngine.GameObject),
                "get_activeInHierarchy", typeof(bool), includeContract);
            var interactable = Method(9, typeof(UnityEngine.UI.Selectable), "IsInteractable",
                typeof(bool), includeContract);
            var objectName = Method(10, typeof(UnityEngine.Object), "get_name",
                typeof(string), includeContract);
            bindings = new ReturnToMenuNativeBindings(
                button,
                Action(backToMenu),
                StaticObject(instance),
                FieldFunc<bool>(active),
                LiveControl(controlButton, gameObject, enabled, activeInHierarchy, interactable),
                StringMethod(objectName));
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

    private static Func<object, bool> LiveControl(
        FieldInfo buttonField,
        MethodInfo gameObject,
        MethodInfo enabled,
        MethodInfo activeInHierarchy,
        MethodInfo interactable)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var component = Expression.Convert(target, buttonField.DeclaringType!);
        var nativeButton = Expression.Variable(buttonField.FieldType, "button");
        var componentGameObject = Expression.Call(component, gameObject);
        var buttonGameObject = Expression.Call(nativeButton, gameObject);
        var body = Expression.Block(
            new[] { nativeButton },
            Expression.Assign(nativeButton, Expression.Field(component, buttonField)),
            Expression.AndAlso(
                Expression.NotEqual(nativeButton, Expression.Constant(null, buttonField.FieldType)),
                Expression.AndAlso(
                    Expression.Call(component, enabled),
                    Expression.AndAlso(
                        Expression.Call(componentGameObject, activeInHierarchy),
                        Expression.AndAlso(
                            Expression.Call(nativeButton, enabled),
                            Expression.AndAlso(
                                Expression.Call(buttonGameObject, activeInHierarchy),
                                Expression.Call(nativeButton, interactable)))))));
        return Expression.Lambda<Func<object, bool>>(body, target).Compile();
    }

    private static Func<object, string> StringMethod(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, string>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            target).Compile();
    }
}
