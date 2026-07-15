using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace OrbAchievementResonance;

internal static class NativeReflection
{
    public const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Dictionary<string, Type?> TypeCache = new Dictionary<string, Type?>(StringComparer.Ordinal);
    private static readonly Dictionary<string, object?> AssetCache = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public static Type? FindType(string name)
    {
        if (TypeCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? exact = null;
            try
            {
                exact = assembly.GetType(name, false);
            }
            catch (ReflectionTypeLoadException)
            {
            }

            if (exact is not null)
            {
                TypeCache[name] = exact;
                return exact;
            }

            foreach (var type in GetTypesSafe(assembly))
            {
                if (string.Equals(type.Name, name, StringComparison.Ordinal) ||
                    string.Equals(type.FullName, name, StringComparison.Ordinal) ||
                    string.Equals(type.FullName, name.Replace("+", "."), StringComparison.Ordinal) ||
                    string.Equals(type.FullName, name, StringComparison.Ordinal) ||
                    EndsWithNestedName(type, name))
                {
                    TypeCache[name] = type;
                    return type;
                }
            }
        }

        TypeCache[name] = null;
        return null;
    }

    public static object? CreateInstance(Type type)
    {
        try
        {
            return Activator.CreateInstance(type, true);
        }
        catch
        {
            try
            {
                return FormatterServices.GetUninitializedObject(type);
            }
            catch
            {
                return null;
            }
        }
    }

    public static object? InvokeParameterless(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, AnyInstance, null, Type.EmptyTypes, null);
        return method?.Invoke(instance, Array.Empty<object>());
    }

    public static object? InvokeStaticParameterless(Type type, string methodName)
    {
        var method = type.GetMethod(methodName, AnyStatic, null, Type.EmptyTypes, null);
        return method?.Invoke(null, Array.Empty<object>());
    }

    public static object? GetMemberValue(object instance, string memberName)
    {
        var type = instance.GetType();
        var property = type.GetProperty(memberName, AnyInstance);
        if (property is not null && property.GetIndexParameters().Length == 0)
        {
            return property.GetValue(instance, null);
        }

        var field = type.GetField(memberName, AnyInstance);
        return field?.GetValue(instance);
    }

    public static bool SetMemberValue(object instance, MemberInfo member, object? value)
    {
        try
        {
            if (member is FieldInfo field)
            {
                field.SetValue(instance, CoerceValue(value, field.FieldType));
                return true;
            }

            if (member is PropertyInfo property && property.SetMethod is not null)
            {
                property.SetValue(instance, CoerceValue(value, property.PropertyType), null);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    public static bool TrySetUuid(object instance, string uuid)
    {
        return TrySetFirstStringMember(instance, uuid, "uuid", "UUID", "Uuid", "guid", "Guid", "id", "ID", "_uuid");
    }

    public static bool TrySetFirstStringMember(object instance, string value, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var member in GetWritableMembers(instance.GetType()))
            {
                if (!string.Equals(member.Name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                var memberType = GetMemberType(member);
                if (memberType == typeof(string) && SetMemberValue(instance, member, value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool TrySetSingleAssignableMember(object instance, object value)
    {
        var matches = GetWritableMembers(instance.GetType())
            .Where(member => GetMemberType(member).IsInstanceOfType(value))
            .ToArray();
        return matches.Length == 1 && SetMemberValue(instance, matches[0], value);
    }

    public static bool TrySetNamedAssignableMember(object instance, object value, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var member in GetWritableMembers(instance.GetType()))
            {
                if (string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    GetMemberType(member).IsInstanceOfType(value) &&
                    SetMemberValue(instance, member, value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool TryAddToNamedCollection(object instance, object value, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var member in GetReadableMembers(instance.GetType()))
            {
                if (!string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if (GetValue(instance, member) is IList list && !list.GetType().IsArray)
                    {
                        list.Add(value);
                        return true;
                    }
                }
                catch
                {
                }
            }
        }

        return false;
    }

    public static bool TrySetNamedNumericMember(object instance, double value, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var member in GetWritableMembers(instance.GetType()))
            {
                if (!string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var memberType = GetMemberType(member);
                if (IsNumeric(memberType) && SetMemberValue(instance, member, value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static string? GetLikelyUuid(object? instance)
    {
        if (instance is null)
        {
            return null;
        }

        if (instance is string direct)
        {
            return NormalizeUuid(direct);
        }

        foreach (var methodName in new[] { "GetGuid", "GetUuid", "GetUUID", "GetId" })
        {
            try
            {
                var value = InvokeParameterless(instance, methodName);
                var methodUuid = ExtractUuid(value);
                if (methodUuid is not null)
                {
                    return methodUuid;
                }
            }
            catch
            {
            }
        }

        var names = new[] { "uuid", "UUID", "Uuid", "guid", "Guid", "id", "ID", "_uuid" };
        foreach (var name in names)
        {
            var value = GetMemberValue(instance, name);
            var uuid = ExtractUuid(value);
            if (uuid is not null)
            {
                return uuid;
            }
        }

        foreach (var member in GetReadableMembers(instance.GetType()))
        {
            object? value;
            try
            {
                value = GetValue(instance, member);
            }
            catch
            {
                continue;
            }

            var uuid = ExtractUuid(value);
            if (uuid is not null)
            {
                return uuid;
            }
        }

        return null;
    }

    public static bool ContainsOwnedUuid(object? instance)
    {
        return ContainsMatchingUuid(instance, ResonanceModifierIds.IsOwned, 0, new HashSet<object>(ReferenceComparer.Instance));
    }

    public static bool ContainsUuid(object? instance, string uuid)
    {
        return ContainsMatchingUuid(
            instance,
            candidate => string.Equals(candidate, uuid, StringComparison.OrdinalIgnoreCase),
            0,
            new HashSet<object>(ReferenceComparer.Instance));
    }

    public static object? FindAssetByUuid(string uuid, params string[] preferredTypeNames)
    {
        if (AssetCache.TryGetValue(uuid, out var cached))
        {
            return cached;
        }

        foreach (var typeName in preferredTypeNames)
        {
            var type = FindType(typeName);
            var found = FindAssetByUuid(uuid, type);
            if (found is not null)
            {
                AssetCache[uuid] = found;
                return found;
            }
        }

        var scriptableObjectType = FindType("UnityEngine.ScriptableObject");
        var fallback = FindAssetByUuid(uuid, scriptableObjectType);
        AssetCache[uuid] = fallback;
        return fallback;
    }

    public static object? CreateStackingModifier(string modifierUuid, double perStrengthRate)
    {
        var type = FindType("ValueModifier");
        if (type is null || !Guid.TryParse(modifierUuid, out var modifierGuid))
        {
            return null;
        }

        foreach (var method in type.GetMethods(AnyStatic))
        {
            if (!string.Equals(method.Name, "Stacking", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 2 || parameters[0].ParameterType != typeof(Guid) ||
                !TryCoerceNumericLike(perStrengthRate, parameters[1].ParameterType, out var nativeRate))
            {
                continue;
            }

            try
            {
                return method.Invoke(null, new[] { (object)modifierGuid, nativeRate });
            }
            catch
            {
            }
        }

        foreach (var method in type.GetMethods(AnyStatic))
        {
            if (!string.Equals(method.Name, "Stacking", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 1 || !TryCoerceNumericLike(perStrengthRate, parameters[0].ParameterType, out var nativeRate))
            {
                continue;
            }

            try
            {
                var modifier = method.Invoke(null, new[] { nativeRate });
                if (modifier is not null)
                {
                    var setGuid = modifier.GetType().GetMethod("SetGuid", AnyInstance, null, new[] { typeof(Guid) }, null);
                    return setGuid?.Invoke(modifier, new object[] { modifierGuid }) ?? modifier;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    public static bool TryConvertToDouble(object? value, out double result)
    {
        try
        {
            if (value is not null)
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException)
        {
        }

        result = 0.0;
        return false;
    }

    public static IReadOnlyList<MemberInfo> GetWritableMembers(Type type)
    {
        var fields = type.GetFields(AnyInstance).Cast<MemberInfo>();
        var properties = type.GetProperties(AnyInstance)
            .Where(property => property.SetMethod is not null && property.GetIndexParameters().Length == 0)
            .Cast<MemberInfo>();
        return fields.Concat(properties).ToArray();
    }

    public static IReadOnlyList<MemberInfo> GetReadableMembers(Type type)
    {
        var fields = type.GetFields(AnyInstance).Cast<MemberInfo>();
        var properties = type.GetProperties(AnyInstance)
            .Where(property => property.GetMethod is not null && property.GetIndexParameters().Length == 0)
            .Cast<MemberInfo>();
        return fields.Concat(properties).ToArray();
    }

    public static Type GetMemberType(MemberInfo member)
    {
        if (member is FieldInfo field)
        {
            return field.FieldType;
        }

        return ((PropertyInfo)member).PropertyType;
    }

    public static object? GetValue(object instance, MemberInfo member)
    {
        if (member is FieldInfo field)
        {
            return field.GetValue(instance);
        }

        return ((PropertyInfo)member).GetValue(instance, null);
    }

    private static object? FindAssetByUuid(string uuid, Type? type)
    {
        if (type is null)
        {
            return null;
        }

        var resourcesType = FindType("UnityEngine.Resources");
        var method = resourcesType?.GetMethods(AnyStatic)
            .FirstOrDefault(candidate =>
            {
                var parameters = candidate.GetParameters();
                return string.Equals(candidate.Name, "FindObjectsOfTypeAll", StringComparison.Ordinal) &&
                       parameters.Length == 1 &&
                       parameters[0].ParameterType == typeof(Type);
            });

        if (method is null)
        {
            return null;
        }

        var objects = method.Invoke(null, new object[] { type }) as IEnumerable;
        if (objects is null)
        {
            return null;
        }

        foreach (var candidate in objects)
        {
            if (candidate is not null &&
                string.Equals(GetLikelyUuid(candidate), uuid, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<Type> GetTypesSafe(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null).Cast<Type>();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private static bool ContainsMatchingUuid(
        object? instance,
        Func<string?, bool> predicate,
        int depth,
        ISet<object> visited)
    {
        if (instance is null || depth > 5)
        {
            return false;
        }

        if (predicate(GetLikelyUuid(instance)))
        {
            return true;
        }

        var type = instance.GetType();
        if (instance is string || type.IsPrimitive || type.IsEnum)
        {
            return false;
        }

        if (!type.IsValueType && !visited.Add(instance))
        {
            return false;
        }

        if (instance is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (ContainsMatchingUuid(item, predicate, depth + 1, visited))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var member in GetReadableMembers(type))
        {
            if (!IsOwnershipMember(member.Name))
            {
                continue;
            }

            object? value;
            try
            {
                value = GetValue(instance, member);
            }
            catch
            {
                continue;
            }

            if (ContainsMatchingUuid(value, predicate, depth + 1, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOwnershipMember(string memberName)
    {
        return memberName.IndexOf("modifier", StringComparison.OrdinalIgnoreCase) >= 0 ||
               memberName.IndexOf("script", StringComparison.OrdinalIgnoreCase) >= 0 ||
               memberName.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0 ||
               memberName.IndexOf("block", StringComparison.OrdinalIgnoreCase) >= 0 ||
               string.Equals(memberName, "guid", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(memberName, "uuid", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(memberName, "id", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(memberName, "_uuid", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCoerceNumericLike(double value, Type targetType, out object nativeValue)
    {
        if (IsNumeric(targetType))
        {
            nativeValue = CoerceValue(value, targetType)!;
            return true;
        }

        try
        {
            var constructor = targetType.GetConstructor(AnyInstance, null, new[] { typeof(double) }, null);
            if (constructor is not null)
            {
                nativeValue = constructor.Invoke(new object[] { value });
                return true;
            }

            var implicitConversion = targetType.GetMethods(AnyStatic)
                .FirstOrDefault(method =>
                {
                    if (method.Name != "op_Implicit" || method.ReturnType != targetType)
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType == typeof(double);
                });
            if (implicitConversion is not null)
            {
                nativeValue = implicitConversion.Invoke(null, new object[] { value })!;
                return true;
            }
        }
        catch
        {
        }

        nativeValue = null!;
        return false;
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private static bool EndsWithNestedName(Type type, string name)
    {
        if (name.IndexOf("+", StringComparison.Ordinal) < 0)
        {
            return false;
        }

        return string.Equals(type.FullName, name, StringComparison.Ordinal) ||
               string.Equals(type.FullName, name.Replace("+", "/"), StringComparison.Ordinal) ||
               string.Equals(type.FullName, name.Replace("+", "."), StringComparison.Ordinal) ||
               type.FullName?.EndsWith("." + name, StringComparison.Ordinal) == true;
    }

    private static object? CoerceValue(object? value, Type targetType)
    {
        if (value is null)
        {
            return null;
        }

        var nullable = Nullable.GetUnderlyingType(targetType);
        if (nullable is not null)
        {
            targetType = nullable;
        }

        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (targetType == typeof(string))
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        if (targetType.IsEnum && value is string enumName)
        {
            return Enum.Parse(targetType, enumName);
        }

        if (IsNumeric(targetType))
        {
            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        return value;
    }

    private static bool IsNumeric(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(byte) ||
               type == typeof(sbyte) ||
               type == typeof(short) ||
               type == typeof(ushort) ||
               type == typeof(int) ||
               type == typeof(uint) ||
               type == typeof(long) ||
               type == typeof(ulong) ||
               type == typeof(float) ||
               type == typeof(double) ||
               type == typeof(decimal);
    }

    private static string? ExtractUuid(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string text)
        {
            return NormalizeUuid(text);
        }

        if (value is Guid guid)
        {
            return guid.ToString("D");
        }

        return NormalizeUuid(value.ToString());
    }

    private static string? NormalizeUuid(string? text)
    {
        if (text is null)
        {
            return null;
        }

        return Guid.TryParse(text, out var guid) ? guid.ToString("D") : null;
    }
}
