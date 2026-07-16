using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace OrbAutomata;

/// <summary>
/// Classifies stable Structure definitions once. Unknown or incomplete native
/// effect contracts deliberately receive no priority.
/// </summary>
internal static class NativeStructurePriorityClassifier
{
    private static readonly BigAmount One = new BigAmount(1.0, 0);
    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Dictionary<Type, Dictionary<string, FieldInfo?>> FieldCache =
        new Dictionary<Type, Dictionary<string, FieldInfo?>>();
    private static readonly Dictionary<Type, ModifierPreviewSchema?> ModifierCache =
        new Dictionary<Type, ModifierPreviewSchema?>();

    public static AutoBuyEconomicPriority Classify(object structure)
    {
        try
        {
            return ClassifyCore(structure);
        }
        catch (Exception)
        {
            return AutoBuyEconomicPriority.None;
        }
    }

    private static AutoBuyEconomicPriority ClassifyCore(object structure)
    {
        if (!HasNativeType(structure.GetType(), "StructureSO") ||
            ReadField(structure, "structureProperties") is not IEnumerable properties)
        {
            return AutoBuyEconomicPriority.None;
        }

        var priority = AutoBuyEconomicPriority.None;
        foreach (var property in properties)
        {
            if (property is null)
            {
                continue;
            }

            priority |= ClassifyResourceEffects(property);
            priority |= ClassifyUpgradeableEffects(property);
            if (priority == (AutoBuyEconomicPriority.CostReduction | AutoBuyEconomicPriority.QualityIncrease))
            {
                break;
            }
        }

        return priority;
    }

    private static AutoBuyEconomicPriority ClassifyResourceEffects(object property)
    {
        if (ReadField(property, "resourceEffects") is not IEnumerable effects)
        {
            return AutoBuyEconomicPriority.None;
        }

        var result = AutoBuyEconomicPriority.None;
        foreach (var effect in effects)
        {
            if (effect is null ||
                ReadField(effect, "resource") is not object resource ||
                !HasNativeType(resource.GetType(), "ResourceSO") ||
                ReadField(effect, "upgradeType") is not object upgradeType ||
                ReadField(effect, "modifier") is not object modifier)
            {
                continue;
            }

            var propertyName = upgradeType.ToString();
            if (string.Equals(propertyName, "Quality", StringComparison.Ordinal) &&
                TryPreviewModifier(modifier, out var qualityRatio) &&
                qualityRatio.CompareTo(One) > 0)
            {
                result |= AutoBuyEconomicPriority.QualityIncrease;
            }
            else if (string.Equals(propertyName, "AttributeCostMod", StringComparison.Ordinal) &&
                     TryPreviewModifier(modifier, out var costRatio) &&
                     costRatio.CompareTo(One) < 0)
            {
                result |= AutoBuyEconomicPriority.CostReduction;
            }
        }

        return result;
    }

    private static AutoBuyEconomicPriority ClassifyUpgradeableEffects(object property)
    {
        if (ReadField(property, "upgradeableObjectEffects") is not IEnumerable effects)
        {
            return AutoBuyEconomicPriority.None;
        }

        var result = AutoBuyEconomicPriority.None;
        foreach (var effect in effects)
        {
            if (effect is null ||
                ReadField(effect, "useTargetRef") is not bool useTargetReference ||
                useTargetReference ||
                ReadField(effect, "upgradeableObject") is not object target ||
                ReadField(effect, "propertyType") is not string propertyType ||
                ReadField(effect, "modifier") is not object modifier ||
                !TryPreviewModifier(modifier, out var ratio))
            {
                continue;
            }

            if (HasNativeType(target.GetType(), "StructureSO") &&
                (string.Equals(propertyType, "Cost", StringComparison.Ordinal) ||
                 string.Equals(propertyType, "CostScaling", StringComparison.Ordinal)) &&
                ratio.CompareTo(One) < 0)
            {
                result |= AutoBuyEconomicPriority.CostReduction;
            }
            else if (HasNativeType(target.GetType(), "ResourceSO"))
            {
                if (string.Equals(propertyType, "Quality", StringComparison.Ordinal) &&
                    ratio.CompareTo(One) > 0)
                {
                    result |= AutoBuyEconomicPriority.QualityIncrease;
                }
                else if ((string.Equals(propertyType, "AttributeCost", StringComparison.Ordinal) ||
                          string.Equals(propertyType, "AttributeCostMod", StringComparison.Ordinal)) &&
                         ratio.CompareTo(One) < 0)
                {
                    result |= AutoBuyEconomicPriority.CostReduction;
                }
            }
        }

        return result;
    }

    private static bool TryPreviewModifier(object modifier, out BigAmount ratio)
    {
        ratio = default;
        try
        {
            var schema = GetModifierSchema(modifier.GetType());
            if (schema is null)
            {
                return false;
            }
            return BigAmount.TryRead(
                schema.Adjust.Invoke(modifier, new[] { schema.NativeOne }),
                out ratio);
        }
        catch (Exception ex) when (
            ex is TargetInvocationException ||
            ex is ArgumentException ||
            ex is InvalidOperationException ||
            ex is AmbiguousMatchException)
        {
            return false;
        }
    }

    private static object? ReadField(object source, string name)
    {
        var sourceType = source.GetType();
        FieldInfo? field;
        lock (FieldCache)
        {
            if (!FieldCache.TryGetValue(sourceType, out var fields))
            {
                fields = new Dictionary<string, FieldInfo?>(StringComparer.Ordinal);
                FieldCache.Add(sourceType, fields);
            }

            if (!fields.TryGetValue(name, out field))
            {
                field = FindField(sourceType, name);
                fields.Add(name, field);
            }
        }

        return field?.GetValue(source);
    }

    private static FieldInfo? FindField(Type sourceType, string name)
    {
        for (var type = sourceType; type is not null; type = type.BaseType)
        {
            var field = type.GetField(name, ReflectionUtil.InstanceFlags | BindingFlags.DeclaredOnly);
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }

    private static ModifierPreviewSchema? GetModifierSchema(Type modifierType)
    {
        lock (ModifierCache)
        {
            if (ModifierCache.TryGetValue(modifierType, out var cached))
            {
                return cached;
            }

            var resolved = ResolveModifierSchema(modifierType);
            ModifierCache.Add(modifierType, resolved);
            return resolved;
        }
    }

    private static ModifierPreviewSchema? ResolveModifierSchema(Type modifierType)
    {
        if (!string.Equals(modifierType.Name, "ValueModifier", StringComparison.Ordinal))
        {
            return null;
        }

        MethodInfo? adjust = null;
        var methods = modifierType.GetMethods(ReflectionUtil.InstanceFlags);
        for (var i = 0; i < methods.Length; i++)
        {
            var parameters = methods[i].GetParameters();
            if (!string.Equals(methods[i].Name, "Adjust", StringComparison.Ordinal) ||
                parameters.Length != 1 ||
                !string.Equals(parameters[0].ParameterType.Name, "BigDouble", StringComparison.Ordinal) ||
                methods[i].ReturnType != parameters[0].ParameterType)
            {
                continue;
            }

            if (adjust is not null)
            {
                return null;
            }

            adjust = methods[i];
        }

        if (adjust is null)
        {
            return null;
        }

        var bigDoubleType = adjust.GetParameters()[0].ParameterType;
        var nativeOne = bigDoubleType.GetField("One", StaticFlags)?.GetValue(null);
        return nativeOne is null ? null : new ModifierPreviewSchema(adjust, nativeOne);
    }

    private static bool HasNativeType(Type type, string expectedName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.Name, expectedName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class ModifierPreviewSchema
    {
        public ModifierPreviewSchema(MethodInfo adjust, object nativeOne)
        {
            Adjust = adjust;
            NativeOne = nativeOne;
        }

        public MethodInfo Adjust { get; }

        public object NativeOne { get; }
    }
}
