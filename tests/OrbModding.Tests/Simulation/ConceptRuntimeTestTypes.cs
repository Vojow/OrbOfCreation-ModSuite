using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace OrbModding.Tests.NativeConcepts;

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
    public int maxUsageSlots = 1;
    public readonly List<AlchemyTypeSO> alchemyTypes = new();
    public ConceptCostVector drainCost = new();
    public AlchemyTypeSO coreType = new("scholar-slot");

    public bool IsDiscovered() => discovered;

    public int GetExperienceLevel() => masteryLevel;

    public BigDouble GetExperience() => new(0.0, 0);

    public BigDouble GetRequiredExperience() => new(1.0, 0);

    public int GetMaxUsageSlots() => maxUsageSlots;

    public AlchemyTypeSO GetCoreType() => coreType;

    public string GetName() => name;

    public void Discover() => discovered = true;

    public void ApplyMastery() => masteryLevel++;
}

public sealed class AlchemyRecipeListVariable
{
    public List<AlchemyRecipeSO> value = new();
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
    public ConceptDrainState resourceDrain = new();

    public ConceptDrainMultiplier GetDrainCostMod() => new(this);
}

public sealed class AlchemyInstanceListVariable
{
    public List<AlchemyInstance> value = new();

    public bool CanAddInstance(AlchemyRecipeSO recipe)
    {
        var instance = value.SingleOrDefault(item => ReferenceEquals(item.reference, recipe));
        if (instance is not null && instance.queuedQuantity >= recipe.GetMaxUsageSlots())
        {
            return false;
        }

        return value.All(item =>
            ReferenceEquals(item.reference, recipe) ||
            Math.Max(item.quantity, item.queuedQuantity) == 0 ||
            !string.Equals(
                item.reference.GetCoreType().uuid,
                recipe.GetCoreType().uuid,
                StringComparison.Ordinal));
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
        var instance = value.Single(item => ReferenceEquals(item.reference, recipe));
        instance.queuedQuantity -= delta;
    }

    public void RebuildCounts()
    {
        foreach (var instance in value)
        {
            instance.quantity = instance.queuedQuantity;
        }
    }

    public void SetupMaxSlotsValue()
    {
    }
}

public sealed class ConceptDrainMultiplier
{
    private readonly AlchemyInstance _instance;

    public ConceptDrainMultiplier(AlchemyInstance instance)
    {
        _instance = instance;
    }

    public double AsPercent() => _instance.quantity;
}

public sealed class ConceptDrainState
{
    public ConceptCostVector Current { get; set; } = new();

    public BigDouble GetRatio() => new(1.0, 0);

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

    public ConceptCostVector Multiply(double multiplier)
    {
        return new ConceptCostVector(Entries
            .Select(entry => new ConceptCostEntry(
                entry.resource,
                new BigDouble(entry.Value.mantissa * multiplier, entry.Value.exponent)))
            .ToArray());
    }

    public ConceptCostVector Subtract(ConceptCostVector other)
    {
        var remaining = new List<ConceptCostEntry>();
        foreach (var entry in Entries)
        {
            var previous = other.Entries.FirstOrDefault(item => ReferenceEquals(item.resource, entry.resource));
            var previousMantissa = previous?.Value.mantissa ?? 0.0;
            remaining.Add(new ConceptCostEntry(
                entry.resource,
                new BigDouble(entry.Value.mantissa - previousMantissa, entry.Value.exponent)));
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
    public BigDouble TrueRate { get; set; } = new(100.0, 0);
    public BigDouble ModdedDrain { get; set; } = new(0.0, 0);
    public BigDouble Quantity { get; set; } = new(100.0, 0);
    public BigDouble SoftCap { get; set; } = new(100.0, 0);

    public bool IsAtZero() => AtZero;

    public BigDouble GetTrueSpend(BigDouble amount) => amount;

    public BigDouble GetTrueRate() => TrueRate;

    public BigDouble GetModdedDrain() => ModdedDrain;

    public bool HasMaxQuantity() => true;

    public BigDouble GetQuantity() => Quantity;

    public BigDouble GetTrueSoftCap() => SoftCap;

    public string GetName() => name;
}
