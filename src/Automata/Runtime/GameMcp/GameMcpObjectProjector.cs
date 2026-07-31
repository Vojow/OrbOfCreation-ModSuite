#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace OrbAutomata.GameMcp;

/// <summary>Deterministic JSON projection for immutable suite value objects.</summary>
internal static class GameMcpObjectProjector
{
    private const int MaximumDepth = 14;

    internal static JToken Project(object? value) => Project(value, 0);

    private static JToken Project(object? value, int depth)
    {
        if (value is null) return JValue.CreateNull();
        if (depth > MaximumDepth)
            return new JObject
            {
                ["status"] = "not_available",
                ["reason"] = "the value exceeds the MCP projection depth limit",
            };

        var type = value.GetType();
        if (value is string text) return new JValue(text);
        if (value is bool boolean) return new JValue(boolean);
        if (value is Type reflectedType)
            return new JValue(reflectedType.FullName ?? reflectedType.Name);
        if (value is Guid guid) return new JValue(guid.ToString("D"));
        if (value is DateTime dateTime) return new JValue(dateTime.ToUniversalTime().ToString("O"));
        if (value is DateTimeOffset dateTimeOffset)
            return new JValue(dateTimeOffset.ToUniversalTime().ToString("O"));
        if (value is TimeSpan duration) return new JValue(duration.ToString("c", CultureInfo.InvariantCulture));
        if (type.IsEnum) return new JValue(value.ToString());
        if (IsIntegral(type)) return new JValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        if (IsFloating(type))
        {
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return double.IsNaN(number) || double.IsInfinity(number)
                ? new JValue(number.ToString("R", CultureInfo.InvariantCulture))
                : new JValue(number);
        }
        if (type.Name == "BigDouble")
        {
            return new JObject
            {
                ["text"] = value.ToString(),
                ["mantissa"] = ReadNumericProperty(value, "Mantissa"),
                ["exponent"] = ReadNumericProperty(value, "Exponent"),
            };
        }
        if (value is IEnumerable enumerable)
        {
            var array = new JArray();
            foreach (var item in enumerable) array.Add(Project(item, depth + 1));
            return array;
        }

        var result = new JObject();
        var properties = type.GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Array.Sort(properties, static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        for (var index = 0; index < properties.Length; index++)
        {
            var property = properties[index];
            if (property.GetIndexParameters().Length != 0 ||
                property.GetMethod is null ||
                property.GetMethod.IsStatic)
            {
                continue;
            }

            var name = Camel(property.Name);
            try
            {
                result[name] = Project(property.GetValue(value), depth + 1);
            }
            catch (Exception exception)
            {
                result[name] = new JObject
                {
                    ["status"] = "not_available",
                    ["reason"] = exception.GetBaseException().Message,
                };
            }
        }
        return result;
    }

    private static JToken ReadNumericProperty(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property is null) return JValue.CreateNull();
        var reading = property.GetValue(value);
        if (reading is null) return JValue.CreateNull();
        var type = reading.GetType();
        if (IsIntegral(type))
            return new JValue(Convert.ToInt64(reading, CultureInfo.InvariantCulture));
        if (IsFloating(type))
        {
            var number = Convert.ToDouble(reading, CultureInfo.InvariantCulture);
            return double.IsNaN(number) || double.IsInfinity(number)
                ? new JValue(number.ToString("R", CultureInfo.InvariantCulture))
                : new JValue(number);
        }
        return new JValue(reading.ToString());
    }

    private static bool IsIntegral(Type type) =>
        type == typeof(byte) || type == typeof(sbyte) ||
        type == typeof(short) || type == typeof(ushort) ||
        type == typeof(int) || type == typeof(uint) ||
        type == typeof(long) || type == typeof(ulong);

    private static bool IsFloating(Type type) =>
        type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    internal static string Camel(string value) =>
        value.Length == 0 || char.IsLower(value[0])
            ? value
            : char.ToLowerInvariant(value[0]) + value.Substring(1);
}
#endif
