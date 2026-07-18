using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

public struct BigDouble
{
    public BigDouble(double mantissa, long exponent) { this.mantissa = mantissa; this.exponent = exponent; }
    public double mantissa;
    public long exponent;
}

public sealed class IntVariable
{
    public int Value { get; set; } = 1;

    public int SetCalls { get; private set; }

    public int? ThrowBeforeWriteFor { get; set; }

    public int? ThrowAfterWriteFor { get; set; }

    public int AsInt() => Value;

    public void SetValue(int value)
    {
        SetCalls++;
        if (ThrowBeforeWriteFor == value)
        {
            throw new InvalidOperationException($"setter rejected {value} before write");
        }

        Value = value;
        if (ThrowAfterWriteFor == value)
        {
            throw new InvalidOperationException($"setter rejected {value} after write");
        }
    }
}

public static class GlobalVariables
{
    public static IntVariable MultiBuy { get; set; } = new IntVariable();

    public static IntVariable GetMultiBuy() => MultiBuy;
}

public static class ActionManager
{
    public static int RemainingRoom { get; set; }

    public static int GetRemainingRoom() => RemainingRoom;
}

public static class AutoBuyManager
{
    public static int RemainingRoom { get; set; }

    public static int GetRemainingRoom() => RemainingRoom;
}

public class SpellRecipeSO
{
    public static List<SpellRecipeSO> All = new List<SpellRecipeSO>();
    public string uuid = Guid.NewGuid().ToString();
    public int masteryLevel;
    public BigDouble masteryExperience;
    public bool discovered;
    public bool readyToLevel;
    public List<BigDouble> GrantedMasteryExperience { get; } = new List<BigDouble>();
    public Prerequisites.Container levelingPrerequisites = new Prerequisites.Container();
    public ResourceCostList levelCost = new ResourceCostList();
    public void GainMasteryExp(BigDouble exp)
    {
        GrantedMasteryExperience.Add(exp);
        masteryExperience = Add(masteryExperience, exp);
    }
    public Guid GetGuid() => Guid.Parse(uuid);
    public Guid GetId() => GetGuid();
    public bool IsDiscovered() => discovered;
    public bool IsReadyToLevelMastery() => readyToLevel;
    public ResourceCostList GetLevelCost() => levelCost;
    public void PurchaseLevel()
    {
        if (!readyToLevel) return;
        masteryLevel++;
        readyToLevel = false;
    }
    public string GetName() => "Spell";

    private static BigDouble Add(BigDouble left, BigDouble right)
    {
        if (left.mantissa == 0) return right;
        if (right.mantissa == 0) return left;
        var exponent = Math.Max(left.exponent, right.exponent);
        var mantissa = left.mantissa * Math.Pow(10, left.exponent - exponent) +
                       right.mantissa * Math.Pow(10, right.exponent - exponent);
        return new BigDouble(mantissa, exponent);
    }
}

public static class IdScriptableObject
{
    public static IDictionary RuntimeLookup = new Dictionary<Guid, object>();

    public static object? GetInstance(Guid guid) => RuntimeLookup.Contains(guid) ? RuntimeLookup[guid] : null;
}

public class UpgradeSO
{
    public static List<UpgradeSO> All = new List<UpgradeSO>();
    public string uuid = Guid.NewGuid().ToString();
    public int purchaseLevel;
    public int queuedPurchaseLevel;
    public bool available = true;
    public bool purchasable = true;
    public bool finite;
    public int maxLevel = int.MaxValue;
    public ResourceCostList purchaseCost = new ResourceCostList();
    public Guid GetGuid() => Guid.Parse(uuid);
    public string GetName() => "Upgrade";
    public bool IsAvailable() => available;
    public bool CanPurchase() => purchasable && !IsMaxQueuedLevel();
    public ResourceCostList GetPurchaseCost() => purchaseCost;
    public int GetPurchaseLevel() => purchaseLevel;
    public int GetQueuedPurchaseLevel() => queuedPurchaseLevel;
    public bool HasFiniteLevels() => finite;
    public bool IsMaxLevel() => finite && purchaseLevel >= maxLevel;
    public bool IsMaxQueuedLevel() => finite && queuedPurchaseLevel >= maxLevel;
    public void Purchase()
    {
        if (CanPurchase()) queuedPurchaseLevel++;
    }
    public void CompleteAction()
    {
        if (queuedPurchaseLevel <= 0) return;
        queuedPurchaseLevel--;
        purchaseLevel++;
    }
}

public class StructureSO
{
    public static List<StructureSO> All = new List<StructureSO>();
    public string uuid = Guid.NewGuid().ToString();
    public int queuedQuantity;
    public int quantity;
    public bool available = true;
    public bool purchasable = true;
    public ResourceCostList purchaseCost = new ResourceCostList();
    public Guid GetGuid() => Guid.Parse(uuid);
    public string GetName() => "Structure";
    public bool IsAvailable() => available;
    public bool CanPurchase() => purchasable;
    public ResourceCostList GetPurchaseCost() => purchaseCost;
    public int GetPurchaseLevel() => quantity;
    public int GetQueuedQuantity() => queuedQuantity;
    public void Purchase(bool forceOne)
    {
        if (forceOne && CanPurchase()) queuedQuantity++;
    }
    public void QueueBuild(int amount) => queuedQuantity += amount;
    public void CompleteAction()
    {
        if (queuedQuantity <= 0) return;
        queuedQuantity--;
        quantity++;
    }
}

public class Player
{
    public void ManagerStart()
    {
    }
}

public class SaveStateManager
{
    public void ImplementLoadedJson()
    {
    }
}

public class Spell
{
    private readonly SpellRecipeSO? reference;

    public Spell()
    {
    }

    public Spell(SpellRecipeSO reference)
    {
        this.reference = reference;
    }

    public SpellRecipeSO? get_reference() => reference;

    public void Fire()
    {
    }
}

public class Prerequisites
{
    public class Container
    {
        public bool unlocked;
        public bool Check() => unlocked;
    }
}

public class ResourceCostList
{
    public List<ResourceTuple> costs = new List<ResourceTuple>();
    public bool affordable = true;
    public int PerformCalls { get; private set; }
    public bool HasEnough() => affordable;
    public void PerformCost() { PerformCalls++; }
}

public class ResourceSO
{
    public string uuid = Guid.NewGuid().ToString();
    public string name = "Resource";
    public BigDouble quantity = new BigDouble(1.0, 3);
    public BigDouble trueQuantity = new BigDouble(1.0, 3);
    public BigDouble attributeCostMod = new BigDouble(1.0, 0);
    public bool available = true;
    public bool bandwidthResource;
    public ValueModifierRecord quality = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord maxQuantity = new ValueModifierRecord(new BigDouble(1.0, 4));
    public Guid GetGuid() => Guid.Parse(uuid);
    public string GetName() => name;
    public BigDouble GetQuantity() => quantity;
    public BigDouble GetTrueQuantity() => trueQuantity;
    public BigDouble GetAttributeCostMod() => attributeCostMod;
    public bool IsAvailable() => available;
    public bool IsBandwidthResource() => bandwidthResource;
    public BigDouble GetTrueAmount(BigDouble amount) => amount;
}

public struct ResourceTuple
{
    private readonly BigDouble value;

    public ResourceTuple(ResourceSO resource, BigDouble value)
    {
        this.resource = resource;
        this.value = value;
    }

    public ResourceSO resource;

    public BigDouble GetValue() => value;
}

public sealed class ValueModifierRecord
{
    private readonly BigDouble value;

    public ValueModifierRecord(BigDouble value)
    {
        this.value = value;
    }

    public BigDouble GetValue() => value;
}

public class SpellRecipeListVariable
{
    public List<SpellRecipeSO> value = new List<SpellRecipeSO>();
}

public class SpellManager
{
    public static SpellManager? instance;
    public static bool NativeCanCast { get; set; } = true;
    public SpellRecipeListVariable availableSpellRecipes = new SpellRecipeListVariable();
    public SpellListVariable activeSpells = new SpellListVariable();

    public static bool CanCastASpell() => NativeCanCast;

    public void FireSpellIndex(int index)
    {
        var spell = activeSpells[index];
        spell.GetType().GetMethod(
            "Fire",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null)?.Invoke(spell, Array.Empty<object>());
    }

    public void TryLevelAllSpells()
    {
        foreach (var recipe in availableSpellRecipes.value)
        {
            while (recipe.IsDiscovered() && recipe.levelingPrerequisites.Check() &&
                   recipe.IsReadyToLevelMastery() && recipe.GetLevelCost().HasEnough())
            {
                recipe.GetLevelCost().PerformCost();
                recipe.PurchaseLevel();
            }
        }
    }
}

public sealed class SpellListVariable : IEnumerable
{
    public List<object> value = new List<object>();

    public object this[int index] => value[index];

    public void Add(object spell) => value.Add(spell);

    public IEnumerator GetEnumerator() => value.GetEnumerator();
}

public sealed class EquipmentSO
{
    public static List<EquipmentSO> All = new List<EquipmentSO>();
    private readonly ExperienceElement experienceContainer = new ExperienceElement();
    public string uuid = Guid.NewGuid().ToString();
    public string name = "Equipment";
    public int masteryLevel;
    public BigDouble masteryXp;
    public bool isCreated = true;
    public Guid GetGuid() => Guid.Parse(uuid);
    public Guid GetId() => GetGuid();
    public string GetName() => name;
    public bool IsCreated() => isCreated;
    public ExperienceElement GetExperienceElement() => experienceContainer;
    private void GainMasteryLevels(int levels) => masteryLevel += levels;
}

public sealed class ExperienceElement
{
    private BigDouble experience;
    public int GainedLevels { get; set; }
    public List<BigDouble> Grants { get; } = new List<BigDouble>();

    public void GainExperience(BigDouble amount)
    {
        Grants.Add(amount);
        experience = Add(experience, amount);
    }

    public int GetGainedLevels() => GainedLevels;

    public BigDouble GetExperience() => experience;

    private static BigDouble Add(BigDouble left, BigDouble right)
    {
        if (left.mantissa == 0) return right;
        if (right.mantissa == 0) return left;
        var exponent = Math.Max(left.exponent, right.exponent);
        return new BigDouble(
            left.mantissa * Math.Pow(10, left.exponent - exponent) +
            right.mantissa * Math.Pow(10, right.exponent - exponent),
            exponent);
    }
}

public sealed class AlchemyTypeSO
{
    public AlchemyTypeSO(string uuid)
    {
        this.uuid = uuid;
    }

    public string uuid;
}

public sealed class AlchemyRecipeSO
{
    public static List<AlchemyRecipeSO> All = new List<AlchemyRecipeSO>();

    public AlchemyRecipeSO(string uuid, string name, IEnumerable<AlchemyTypeSO> types)
    {
        this.uuid = uuid;
        this.name = name;
        alchemyTypes.AddRange(types);
    }

    public string uuid;
    public string name;
    public bool discovered = true;
    public int masteryLevel;
    public BigDouble masteryXp;
    public int maxUsageSlots = 1;
    public readonly List<AlchemyTypeSO> alchemyTypes = new List<AlchemyTypeSO>();
    public ConceptCostVector drainCost = new ConceptCostVector();
    public AlchemyTypeSO coreType = new AlchemyTypeSO("scholar-slot");
    public List<BigDouble> GrantedMasteryExperience { get; } = new List<BigDouble>();

    public Guid GetGuid() => Guid.Parse(uuid);
    public Guid GetId() => GetGuid();
    public bool IsDiscovered() => discovered;
    public bool IsAvailable() => discovered;
    public int GetExperienceLevel() => masteryLevel;
    public BigDouble GetExperience() => masteryXp;
    public BigDouble GetRequiredExperience() => new BigDouble(1.0, 0);
    public int GetMaxUsageSlots() => maxUsageSlots;
    public AlchemyTypeSO GetCoreType() => coreType;
    public string GetName() => name;
    public void Discover() => discovered = true;
    public void ApplyMastery() => masteryLevel++;

    public void GainMasteryXp(BigDouble amount)
    {
        GrantedMasteryExperience.Add(amount);
        masteryXp = amount;
    }
}

public sealed class AlchemyRecipeListVariable
{
    public List<AlchemyRecipeSO> value = new List<AlchemyRecipeSO>();
}

public sealed class AlchemyInstance
{
    public AlchemyInstance(AlchemyRecipeSO reference)
    {
        this.reference = reference;
    }

    public AlchemyRecipeSO reference;
    public int quantity;
    public int queuedQuantity;
    public ConceptDrainState resourceDrain = new ConceptDrainState();

    public ConceptDrainMultiplier GetDrainCostMod() => new ConceptDrainMultiplier(this);
}

public sealed class AlchemyInstanceListVariable
{
    public List<AlchemyInstance> value = new List<AlchemyInstance>();

    public bool CanAddInstance(AlchemyRecipeSO recipe)
    {
        var instance = value.SingleOrDefault(item => ReferenceEquals(item.reference, recipe));
        if (instance is not null && instance.queuedQuantity >= recipe.GetMaxUsageSlots()) return false;
        return value.All(item =>
            ReferenceEquals(item.reference, recipe) ||
            Math.Max(item.quantity, item.queuedQuantity) == 0 ||
            !string.Equals(item.reference.GetCoreType().uuid, recipe.GetCoreType().uuid, StringComparison.Ordinal));
    }

    public void AddAlchemyInstances(AlchemyRecipeSO recipe, int delta)
    {
        var instance = value.SingleOrDefault(item => ReferenceEquals(item.reference, recipe));
        if (instance is null)
        {
            instance = new AlchemyInstance(recipe);
            value.Add(instance);
        }
        instance.queuedQuantity += delta;
    }

    public void RemoveAlchemyInstances(AlchemyRecipeSO recipe, int delta)
    {
        value.Single(item => ReferenceEquals(item.reference, recipe)).queuedQuantity -= delta;
    }

    public void RebuildCounts()
    {
        foreach (var instance in value) instance.quantity = instance.queuedQuantity;
    }

    public void SetupMaxSlotsValue()
    {
    }
}

public sealed class ConceptDrainMultiplier
{
    private readonly AlchemyInstance instance;

    public ConceptDrainMultiplier(AlchemyInstance instance)
    {
        this.instance = instance;
    }

    public double AsPercent() => instance.quantity;
}

public sealed class ConceptDrainState
{
    public ConceptCostVector Current { get; set; } = new ConceptCostVector();
    public BigDouble GetRatio() => new BigDouble(1.0, 0);
    public ConceptCostVector GetCurrentDrain() => Current;
}

public sealed class ConceptCostVector
{
    public ConceptCostVector(params ConceptCostEntry[] entries)
    {
        Entries = entries.ToList();
    }

    public List<ConceptCostEntry> Entries { get; }
    public IList GetEntries() => Entries;

    public ConceptCostVector Multiply(double multiplier) => new ConceptCostVector(
        Entries.Select(entry => new ConceptCostEntry(
            entry.resource,
            new BigDouble(entry.Value.mantissa * multiplier, entry.Value.exponent))).ToArray());

    public ConceptCostVector Subtract(ConceptCostVector other)
    {
        var remaining = new List<ConceptCostEntry>();
        foreach (var entry in Entries)
        {
            var previous = other.Entries.FirstOrDefault(item => ReferenceEquals(item.resource, entry.resource));
            remaining.Add(new ConceptCostEntry(
                entry.resource,
                new BigDouble(entry.Value.mantissa - (previous?.Value.mantissa ?? 0), entry.Value.exponent)));
        }
        return new ConceptCostVector(remaining.ToArray());
    }
}

public sealed class ConceptCostEntry
{
    public ConceptCostEntry(ConceptResource resource, BigDouble value)
    {
        this.resource = resource;
        Value = value;
    }

    public ConceptResource resource;
    public BigDouble Value { get; }
    public BigDouble GetValue() => Value;
}

public sealed class ConceptResource
{
    public string uuid = Guid.NewGuid().ToString();
    public string name = "Concept resource";
    public bool AtZero { get; set; }
    public BigDouble TrueRate { get; set; } = new BigDouble(100.0, 0);
    public BigDouble ModdedDrain { get; set; } = new BigDouble(0.0, 0);
    public BigDouble Quantity { get; set; } = new BigDouble(100.0, 0);
    public BigDouble SoftCap { get; set; } = new BigDouble(100.0, 0);
    public bool IsAtZero() => AtZero;
    public BigDouble GetTrueSpend(BigDouble amount) => amount;
    public BigDouble GetTrueRate() => TrueRate;
    public BigDouble GetModdedDrain() => ModdedDrain;
    public bool HasMaxQuantity() => true;
    public BigDouble GetQuantity() => Quantity;
    public BigDouble GetTrueSoftCap() => SoftCap;
    public string GetName() => name;
}

public static class TargetingManager
{
    public static bool Targeting { get; set; }

    public static bool IsTargeting() => Targeting;
}

public interface ITooltipable
{
    string GetName();
    string GetDisplayType();
    UnityEngine.Sprite GetIcon();
    UnityEngine.Color GetColor();
    bool IsColoredIcon();
    bool HasAltTooltips();
    string GetDescription();
    List<TooltipNode> GetTooltipNodes();
    List<TooltipNode> GetAltTooltipNodes();
}

public class TooltipableObject : UnityEngine.ScriptableObject { }

public class TooltipNode
{
    public TooltipNode(string text) { }
    public TooltipNode(string text, UnityEngine.Color color) { }
}

public class HoverTooltip : UnityEngine.MonoBehaviour
{
    public ITooltipable? tooltipItem;
    public TooltipableObject? setupObject;
    public void Setup(ITooltipable item, List<ITooltipable>? subTooltips = null) { tooltipItem = item; }
}

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin : Attribute
    {
        public BepInPlugin(string guid, string name, string version)
        {
            GUID = guid;
            Name = name;
            Version = new Version(version);
        }

        public string GUID { get; }

        public string Name { get; }

        public Version Version { get; }
    }

    public class BaseUnityPlugin : UnityEngine.MonoBehaviour
    {
        public Configuration.ConfigFile Config { get; } = new Configuration.ConfigFile();

        public Logging.ManualLogSource Logger { get; } = new Logging.ManualLogSource();
    }

    public static class Paths
    {
        public static string GameRootPath { get; set; } = string.Empty;
    }

    public sealed class PluginInfo
    {
        public BepInPlugin Metadata { get; set; } = new BepInPlugin("test.plugin", "Test Plugin", "1.0.0");

        public BaseUnityPlugin? Instance { get; set; }
    }
}

namespace BepInEx.Bootstrap
{
    public static class Chainloader
    {
        public static Dictionary<string, BepInEx.PluginInfo> PluginInfos { get; } = new Dictionary<string, BepInEx.PluginInfo>();
    }
}

namespace BepInEx.Configuration
{
    public sealed class ConfigFile : IEnumerable<KeyValuePair<ConfigDefinition, ConfigEntryBase>>
    {
        private readonly Dictionary<ConfigDefinition, ConfigEntryBase> _entries = new Dictionary<ConfigDefinition, ConfigEntryBase>();

        public bool SaveOnConfigSet { get; set; } = true;

        public int SaveCalls { get; private set; }

        public int? ThrowOnSaveCall { get; set; }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            return Bind(section, key, defaultValue, new ConfigDescription(description));
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description)
        {
            var definition = new ConfigDefinition(section, key);
            if (_entries.TryGetValue(definition, out var existing))
            {
                return (ConfigEntry<T>)existing;
            }

            var entry = new ConfigEntry<T>(this, definition, defaultValue, description);
            _entries.Add(definition, entry);
            return entry;
        }

        public void Save()
        {
            SaveCalls++;
            if (ThrowOnSaveCall == SaveCalls)
            {
                throw new InvalidOperationException($"simulated config save failure on call {SaveCalls}");
            }
        }

        public bool Remove(ConfigDefinition definition) => _entries.Remove(definition);

        public IEnumerator<KeyValuePair<ConfigDefinition, ConfigEntryBase>> GetEnumerator() => _entries.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class ConfigDefinition : IEquatable<ConfigDefinition>
    {
        public ConfigDefinition(string section, string key)
        {
            Section = section;
            Key = key;
        }

        public string Section { get; }

        public string Key { get; }

        public bool Equals(ConfigDefinition? other) => other is not null && Section == other.Section && Key == other.Key;

        public override bool Equals(object? obj) => Equals(obj as ConfigDefinition);

        public override int GetHashCode() => HashCode.Combine(Section, Key);
    }

    public abstract class ConfigEntryBase
    {
        protected ConfigEntryBase(ConfigFile configFile, ConfigDefinition definition, Type settingType, object? defaultValue, ConfigDescription description)
        {
            ConfigFile = configFile;
            Definition = definition;
            SettingType = settingType;
            DefaultValue = defaultValue;
            Description = description;
        }

        public ConfigFile ConfigFile { get; }

        public ConfigDefinition Definition { get; }

        public ConfigDescription Description { get; }

        public Type SettingType { get; }

        public object? DefaultValue { get; }

        public abstract object? BoxedValue { get; set; }

        public string GetSerializedValue() => Convert.ToString(BoxedValue, CultureInfo.InvariantCulture) ?? string.Empty;

        public void SetSerializedValue(string value)
        {
            if (SettingType == typeof(string))
            {
                BoxedValue = value;
                return;
            }

            BoxedValue = SettingType.IsEnum
                ? Enum.Parse(SettingType, value, true)
                : Convert.ChangeType(value, SettingType, CultureInfo.InvariantCulture);
        }
    }

    public sealed class ConfigEntry<T> : ConfigEntryBase
    {
        private T _value;

        public ConfigEntry(T value)
            : this(new ConfigFile(), new ConfigDefinition("Test", "Value"), value, new ConfigDescription(string.Empty))
        {
        }

        internal ConfigEntry(ConfigFile configFile, ConfigDefinition definition, T value, ConfigDescription description)
            : base(configFile, definition, typeof(T), value, description)
        {
            _value = value;
        }

        public T Value
        {
            get => _value;
            set
            {
                _value = value;
                SettingChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public override object? BoxedValue
        {
            get => Value;
            set => Value = (T)value!;
        }

        public event EventHandler? SettingChanged;

        public void RaiseChanged()
        {
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public sealed class ConfigDescription
    {
        public ConfigDescription(string description, AcceptableValueBase? acceptableValues = null, params object[] tags)
        {
            Description = description;
            AcceptableValues = acceptableValues;
            Tags = tags;
        }

        public string Description { get; }

        public AcceptableValueBase? AcceptableValues { get; }

        public object[] Tags { get; }
    }

    public abstract class AcceptableValueBase
    {
        protected AcceptableValueBase(Type valueType)
        {
            ValueType = valueType;
        }

        public Type ValueType { get; }

        public abstract object Clamp(object value);

        public abstract bool IsValid(object value);

        public abstract string ToDescriptionString();
    }

    public sealed class AcceptableValueRange<T> : AcceptableValueBase
    {
        public AcceptableValueRange(T minValue, T maxValue)
            : base(typeof(T))
        {
            MinValue = minValue;
            MaxValue = maxValue;
        }

        public T MinValue { get; }

        public T MaxValue { get; }

        public override object Clamp(object value)
        {
            if (value is not T typed)
            {
                return MinValue!;
            }

            if (Comparer<T>.Default.Compare(typed, MinValue) < 0)
            {
                return MinValue!;
            }

            return Comparer<T>.Default.Compare(typed, MaxValue) > 0 ? MaxValue! : typed!;
        }

        public override bool IsValid(object value)
        {
            return value is T typed &&
                   Comparer<T>.Default.Compare(typed, MinValue) >= 0 &&
                   Comparer<T>.Default.Compare(typed, MaxValue) <= 0;
        }

        public override string ToDescriptionString() => $"Range: {MinValue} - {MaxValue}";
    }

    public sealed class AcceptableValueList<T> : AcceptableValueBase
    {
        public AcceptableValueList(params T[] acceptableValues)
            : base(typeof(T))
        {
            AcceptableValues = acceptableValues;
        }

        public IReadOnlyList<T> AcceptableValues { get; }

        public override object Clamp(object value) => IsValid(value) ? value : AcceptableValues[0]!;

        public override bool IsValid(object value) => value is T typed && AcceptableValues.Contains(typed);

        public override string ToDescriptionString() => $"# Acceptable values: {string.Join(", ", AcceptableValues)}";
    }

    public readonly struct KeyboardShortcut
    {
        public KeyboardShortcut(UnityEngine.KeyCode mainKey, params UnityEngine.KeyCode[] modifiers)
        {
        }

        public bool IsDown()
        {
            return false;
        }
    }
}

namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public List<object> Entries { get; } = new List<object>();

        public void LogDebug(object data) => Entries.Add(data);

        public void LogInfo(object data) => Entries.Add(data);

        public void LogWarning(object data) => Entries.Add(data);

        public void LogError(object data) => Entries.Add(data);
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class HarmonyPatchAttribute : Attribute
    {
    }

    public sealed class HarmonyMethod : Attribute
    {
        public HarmonyMethod(Type type, string methodName)
        {
        }
    }

    public sealed class Harmony
    {
        public Harmony(string id)
        {
        }

        public void PatchAll(Assembly assembly)
        {
        }

        public void Patch(MethodBase original, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, HarmonyMethod? transpiler = null, HarmonyMethod? finalizer = null)
        {
        }

        public void UnpatchSelf()
        {
        }
    }

    public static class AccessTools
    {
        public static MethodInfo? Method(string typeColonMethod)
        {
            var separator = typeColonMethod.IndexOf(':');
            if (separator <= 0 || separator == typeColonMethod.Length - 1)
            {
                return null;
            }

            var type = TypeByName(typeColonMethod.Substring(0, separator));
            var methodName = typeColonMethod.Substring(separator + 1);
            return type?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name == methodName);
        }

        public static Type? TypeByName(string name)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var preferred = assemblies.FirstOrDefault(assembly =>
                assembly.GetName().Name == "Assembly-CSharp");
            if (preferred is not null)
            {
                var preferredType = FindType(preferred, name);
                if (preferredType is not null)
                {
                    return preferredType;
                }
            }

            foreach (var assembly in assemblies)
            {
                var match = FindType(assembly, name);
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }

        private static Type? FindType(Assembly assembly, string name)
        {
            try
            {
                return assembly.GetTypes().FirstOrDefault(type => type.Name == name || type.FullName == name);
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types
                    .Where(type => type is not null)
                    .FirstOrDefault(type => type!.Name == name || type.FullName == name);
            }
        }

        public static List<MethodInfo> GetDeclaredMethods(Type type)
        {
            return new List<MethodInfo>(type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        }
    }
}

namespace UnityEngine
{
    public class Object
    {
        public string name = string.Empty;

        public static T Instantiate<T>(T original, Transform parent, bool worldPositionStays) where T : Object, new()
        {
            var clone = new T();
            if (clone is GameObject gameObject)
            {
                gameObject.transform.SetParent(parent, worldPositionStays);
            }

            return clone;
        }

        public static void Destroy(Object obj)
        {
        }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; } = null!;

        public Transform transform { get; internal set; } = null!;

        public T? GetComponent<T>() where T : Component => gameObject?.GetComponent<T>();

        public Component? GetComponent(Type type) => gameObject?.GetComponent(type);

        public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component => gameObject?.GetComponent<T>();

        public T[] GetComponents<T>() where T : Component => gameObject?.GetComponents<T>() ?? Array.Empty<T>();
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; } = true;
    }

    public class MonoBehaviour : Behaviour
    {
    }

    public class GameObject : Object
    {
        private readonly List<Component> _components = new List<Component>();

        public GameObject()
            : this(string.Empty)
        {
        }

        public GameObject(string name, params Type[] components)
        {
            this.name = name;
            transform = new RectTransform { gameObject = this, name = name };
            transform.transform = transform;
            _components.Add(transform);
            foreach (var type in components.Where(type => type != typeof(RectTransform) && typeof(Component).IsAssignableFrom(type)))
            {
                var component = (Component)Activator.CreateInstance(type)!;
                component.gameObject = this;
                component.transform = transform;
                component.name = name;
                _components.Add(component);
            }
        }

        public Transform transform { get; }

        public bool activeInHierarchy { get; set; } = true;
        public bool activeSelf { get; private set; } = true;

        public static GameObject? Find(string name) => null;

        public void SetActive(bool value)
        {
            activeSelf = value;
            activeInHierarchy = value;
        }

        public T? GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();

        public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component => GetComponent<T>();

        public Component? GetComponent(Type type) => _components.FirstOrDefault(type.IsInstanceOfType);

        public T[] GetComponents<T>() where T : Component => _components.OfType<T>().ToArray();

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this, transform = transform, name = name };
            _components.Add(component);
            return component;
        }
    }

    public class Transform : Component
    {
        private readonly List<Transform> _children = new List<Transform>();

        public Transform? parent { get; private set; }

        public int childCount => _children.Count;

        public void SetParent(Transform parent, bool worldPositionStays)
        {
            if (ReferenceEquals(this.parent, parent)) return;
            this.parent?._children.Remove(this);
            this.parent = parent;
            parent._children.Add(this);
        }

        public int GetSiblingIndex() => parent?._children.IndexOf(this) ?? 0;

        public void SetSiblingIndex(int index)
        {
            if (parent is null) return;
            parent._children.Remove(this);
            parent._children.Insert(Math.Max(0, Math.Min(index, parent._children.Count)), this);
        }

        public void SetAsLastSibling()
        {
            if (parent is null) return;
            parent._children.Remove(this);
            parent._children.Add(this);
        }

        public Transform GetChild(int index) => _children[index];
    }

    public class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Rect rect { get; set; } = new Rect(0f, 0f, 44f, 44f);
    }

    public readonly struct Vector2
    {
        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public readonly float x;
        public readonly float y;
        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 one => new Vector2(1f, 1f);
    }

    public readonly struct Color
    {
        public Color(float r, float g, float b, float a = 1f)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public readonly float r;
        public readonly float g;
        public readonly float b;
        public readonly float a;
        public static Color white => new Color(1f, 1f, 1f, 1f);

        public static Color Lerp(Color a, Color b, float t) => t < 0.5f ? a : b;
    }

    public class CanvasRenderer : Component
    {
    }

    public class CanvasGroup : Behaviour
    {
    }

    public class Material : Object
    {
    }

    public class Sprite : Object
    {
    }

    public class ScriptableObject : Object
    {
    }

    public enum KeyCode
    {
        None = 0,
        Equals = 61,
        Minus = 45,
        Alpha0 = 48,
        X = 120,
        M = 109,
        LeftAlt = 308,
    }

    public static class Time
    {
        public static float timeScale { get; set; } = 1.0f;
        public static float fixedDeltaTime { get; set; } = 0.02f;
        public static float deltaTime { get; set; } = 0.016f;
        public static float unscaledDeltaTime { get; set; } = 0.016f;
        public static float realtimeSinceStartup { get; set; }
        public static int frameCount { get; set; }
    }

    public static class Resources
    {
        public static Object[] FindObjectsOfTypeAll(Type type) => Array.Empty<Object>();
    }

    public readonly struct Rect
    {
        public Rect(float x, float y, float width, float height)
        {
            this.width = width;
            this.height = height;
        }

        public readonly float width;

        public readonly float height;
    }

    public static class GUI
    {
        public static void Box(Rect position, string text)
        {
        }
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
}

namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.Behaviour
    {
        public UnityEngine.Color color { get; set; } = UnityEngine.Color.white;
        public bool raycastTarget { get; set; }
    }

    public class Image : Graphic
    {
        public UnityEngine.Sprite? sprite { get; set; }
        public bool preserveAspect { get; set; }
    }

    public class Selectable : UnityEngine.Behaviour
    {
        public bool interactable { get; set; } = true;

        public Graphic? targetGraphic { get; set; }
    }

    public class Button : Selectable
    {
        public ButtonClickedEvent onClick { get; } = new ButtonClickedEvent();

        public sealed class ButtonClickedEvent
        {
            private readonly List<UnityEngine.Events.UnityAction> _listeners = new List<UnityEngine.Events.UnityAction>();

            public void AddListener(UnityEngine.Events.UnityAction listener) => _listeners.Add(listener);

            public void RemoveListener(UnityEngine.Events.UnityAction listener) => _listeners.Remove(listener);

            public void RemoveAllListeners() => _listeners.Clear();
        }
    }

    public class RectMask2D : UnityEngine.Behaviour
    {
    }

    public class LayoutElement : UnityEngine.Behaviour
    {
        public bool ignoreLayout { get; set; }
    }

    public class ScrollRect : UnityEngine.Behaviour
    {
        public enum MovementType
        {
            Unrestricted,
            Elastic,
            Clamped,
        }

        public bool horizontal { get; set; }
        public bool vertical { get; set; }
        public MovementType movementType { get; set; }
        public float scrollSensitivity { get; set; }
        public float verticalNormalizedPosition { get; set; }
        public UnityEngine.RectTransform? viewport { get; set; }
        public UnityEngine.RectTransform? content { get; set; }
    }
}

namespace TMPro
{
    public enum TextAlignmentOptions
    {
        Center,
        Midline,
        MidlineLeft,
        TopLeft,
    }

    public enum TextOverflowModes
    {
        Overflow,
        Ellipsis,
    }

    public enum TextWrappingModes
    {
        NoWrap,
        Normal,
    }

    public class TMP_FontAsset : UnityEngine.Object
    {
    }

    public class TextMeshProUGUI : UnityEngine.UI.Graphic
    {
        public string text { get; set; } = string.Empty;
        public TMP_FontAsset? font { get; set; }
        public UnityEngine.Material? fontSharedMaterial { get; set; }
        public float fontSize { get; set; } = 16f;
        public TextAlignmentOptions alignment { get; set; }
        public TextWrappingModes textWrappingMode { get; set; }
        public TextOverflowModes overflowMode { get; set; }
    }

    public class TMP_InputField : UnityEngine.UI.Selectable
    {
        public enum LineType
        {
            SingleLine,
            MultiLineSubmit,
            MultiLineNewline,
        }

        public UnityEngine.RectTransform? textViewport { get; set; }
        public TextMeshProUGUI? textComponent { get; set; }
        public string text { get; set; } = string.Empty;
          public LineType lineType { get; set; }
          public OnChangeEvent onValueChanged { get; } = new OnChangeEvent();
          public OnChangeEvent onSelect { get; } = new OnChangeEvent();

        public sealed class OnChangeEvent
        {
            private readonly List<Action<string>> _listeners = new List<Action<string>>();

            public void AddListener(Action<string> listener) => _listeners.Add(listener);
        }
    }
}

namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode
    {
        Single = 0,
        Additive = 1,
    }

    public readonly struct Scene
    {
        public Scene(string name)
        {
            this.name = name;
        }

        public readonly string name;

        public bool IsValid() => name is not null;
    }

    public static class SceneManager
    {
        public static event Action<Scene, Scene>? activeSceneChanged;

        public static event Action<Scene, LoadSceneMode>? sceneLoaded;

        public static Scene ActiveScene { get; set; } = new Scene("Main");

        public static Scene GetActiveScene() => ActiveScene;

        public static void RaiseActiveSceneChanged(Scene previous, Scene next)
        {
            ActiveScene = next;
            activeSceneChanged?.Invoke(previous, next);
        }

        public static void RaiseSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            sceneLoaded?.Invoke(scene, mode);
        }
    }
}
