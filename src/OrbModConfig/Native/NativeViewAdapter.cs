using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace OrbModConfig;

/// <summary>
/// Narrow reflection adapter for the audited native top-navigation contract.
/// Native view objects never escape the navigation host.
/// </summary>
internal static class NativeViewAdapter
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Type, NativeButtonContract> ButtonContracts = new();
    private static readonly Dictionary<Type, NativeViewContract> ViewContracts = new();

    public static bool IsAlive(object? value)
    {
        if (value is null) return false;
        return value is not UnityEngine.Object unityObject || unityObject != null;
    }

    public static object? ReadView(Component component) =>
        GetButtonContract(component.GetType()).ViewField.GetValue(component);

    public static bool IsActive(object view)
    {
        try
        {
            return GetViewContract(view.GetType()).IsActive.Invoke(view, null) as bool? == true;
        }
        catch
        {
            return false;
        }
    }

    public static void SetActive(object view, bool active)
    {
        try
        {
            if (!IsAlive(view)) return;
            GetViewContract(view.GetType()).SetActive.Invoke(view, new object[] { active });
        }
        catch { }
    }

    public static Sprite? ReadSprite(Component component, string fieldName)
    {
        var contract = GetButtonContract(component.GetType());
        var field = string.Equals(fieldName, "baseImage", StringComparison.Ordinal)
            ? contract.InactiveSpriteField
            : string.Equals(fieldName, "activeImage", StringComparison.Ordinal)
                ? contract.ActiveSpriteField
                : null;
        return field?.GetValue(component) as Sprite;
    }

    internal static bool TryValidateViewType(Type type, out string reason)
    {
        try
        {
            GetViewContract(type);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    internal static bool IsViewTypeCached(Type type)
    {
        lock (Gate) return ViewContracts.ContainsKey(type);
    }

    private static NativeButtonContract GetButtonContract(Type type)
    {
        lock (Gate)
        {
            if (!ButtonContracts.TryGetValue(type, out var contract))
            {
                contract = NativeButtonContract.Create(type);
                ButtonContracts.Add(type, contract);
            }

            return contract;
        }
    }

    private static NativeViewContract GetViewContract(Type type)
    {
        lock (Gate)
        {
            if (!ViewContracts.TryGetValue(type, out var contract))
            {
                contract = NativeViewContract.Create(type);
                ViewContracts.Add(type, contract);
            }

            return contract;
        }
    }

    private static FieldInfo? FindField(Type type, string fieldName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(fieldName, flags);
            if (field is not null) return field;
        }

        return null;
    }

    private sealed class NativeButtonContract
    {
        private NativeButtonContract(
            FieldInfo viewField,
            FieldInfo? inactiveSpriteField,
            FieldInfo? activeSpriteField)
        {
            ViewField = viewField;
            InactiveSpriteField = inactiveSpriteField;
            ActiveSpriteField = activeSpriteField;
        }

        public FieldInfo ViewField { get; }
        public FieldInfo? InactiveSpriteField { get; }
        public FieldInfo? ActiveSpriteField { get; }

        public static NativeButtonContract Create(Type type) => new(
            FindField(type, "item") ??
                throw new MissingFieldException(type.FullName, "item"),
            FindField(type, "baseImage"),
            FindField(type, "activeImage"));
    }

    private sealed class NativeViewContract
    {
        private NativeViewContract(MethodInfo isActive, MethodInfo setActive)
        {
            IsActive = isActive;
            SetActive = setActive;
        }

        public MethodInfo IsActive { get; }
        public MethodInfo SetActive { get; }

        public static NativeViewContract Create(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var isActive = type.GetMethod("IsActive", flags, null, Type.EmptyTypes, null);
            if (isActive is null || isActive.ReturnType != typeof(bool))
                throw new MissingMethodException(type.FullName, "bool IsActive()");
            var setActive = type.GetMethod("SetActive", flags, null, new[] { typeof(bool) }, null);
            if (setActive is null || setActive.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, "void SetActive(bool)");
            return new NativeViewContract(isActive, setActive);
        }
    }
}
