using System;
using System.Collections.Generic;

public sealed class ScalingInfo
{
    private readonly BigDouble drainCostMod;

    public ScalingInfo(BigDouble drainMultiplier) =>
        drainCostMod = new BigDouble(
            drainMultiplier.Mantissa * 100.0,
            drainMultiplier.Exponent);

    public BigDouble GetDrainCostMod() => drainCostMod;
}

public sealed class ResourceDrain
{
    private ResourceCostList currentDrain = new ResourceCostList();

    public ResourceCostList GetCurrentDrain() => currentDrain;

    public void SetCurrentDrain(ResourceCostList value) => currentDrain = value;
}

public sealed class AudioInstance
{
    public int PlayCalls { get; private set; }

    public void Play() => PlayCalls++;
}

public sealed class HarvestActionSO : IdScriptableObject
{
    public AudioInstance equipSound = new AudioInstance();
    public Func<int, BigDouble> DrainMultiplierAtLevel { get; set; } =
        level => new BigDouble(level, 0);
    public Action<int>? AfterLevelChanged { get; set; }
}

public sealed class HarvestActionInstance
{
    private readonly HarvestElementSO element;
    private readonly HarvestActionSO action;
    private readonly ResourceDrain resourceDrain = new ResourceDrain();

    public HarvestActionInstance(HarvestElementSO element, HarvestActionSO action)
    {
        this.element = element;
        this.action = action;
    }

    public int instances;
    public bool visible = true;

    public HarvestActionSO GetAction() => action;

    public HarvestElementSO GetElement() => element;

    public HarvestElementSO.HarvestActionReference GetActionRef() =>
        element.actionReference;

    public bool IsVisible() => visible;

    public int GetMaximumInstances() => element.masteryLevel + 1;

    public ScalingInfo GetScalingInfo(int level) =>
        new ScalingInfo(action.DrainMultiplierAtLevel(level));

    public void ChangeInstance(int change)
    {
        instances = Math.Min(Math.Max(instances + change, 0), GetMaximumInstances());
        var multiplier = action.DrainMultiplierAtLevel(instances);
        var current = new ResourceCostList();
        foreach (var entry in element.actionReference.actionCost.costs)
        {
            var raw = entry.GetValue() * multiplier;
            current.costs.Add(new ResourceTuple(entry.resource, raw));
            entry.resource.trueRate =
                entry.resource.baseRate - entry.resource.GetTrueSpend(raw);
        }

        if (element.actionReference.elementCost > 0)
        {
            var raw = (BigDouble)element.actionReference.elementCost * multiplier;
            current.costs.Add(new ResourceTuple(element.internalResource, raw));
            element.internalResource.trueRate =
                element.internalResource.baseRate -
                element.internalResource.GetTrueSpend(raw);
        }

        resourceDrain.SetCurrentDrain(current);
        action.AfterLevelChanged?.Invoke(instances);
    }
}

public sealed class HarvestActionInstanceListVariable : IdScriptableObject
{
    public List<HarvestActionInstance> value = new List<HarvestActionInstance>();
    public int capacity = 8;

    public HarvestActionInstanceListVariable() =>
        SetGuid(Guid.Parse("e4a9d4c3-61cc-4f94-bab9-7bc8e841cc32"));

    public HarvestActionInstance? FindInstance(HarvestActionInstance other) =>
        value.Find(instance =>
            ReferenceEquals(instance.GetAction(), other.GetAction()) &&
            ReferenceEquals(instance.GetElement(), other.GetElement()));

    public bool HasEmptySpot() => value.Count < capacity;

    public void AddInstance(HarvestActionInstance actionInstance, int instance)
    {
        var current = FindInstance(actionInstance);
        if (current is not null)
        {
            current.ChangeInstance(instance);
            return;
        }

        if (!HasEmptySpot()) return;
        current = new HarvestActionInstance(
            actionInstance.GetElement(),
            actionInstance.GetAction());
        value.Add(current);
        current.ChangeInstance(instance);
    }

    public void RemoveInstance(HarvestActionInstance actionInstance, int instance)
    {
        var current = FindInstance(actionInstance);
        if (current is null) return;
        current.ChangeInstance(-instance);
        if (current.instances <= 0) value.Remove(current);
    }
}

public class UIGenericItem<T> where T : class
{
    private Action<T>? clickAction;
    public T? item;

    public void SetupClick(Action<T> action) => clickAction = action;

    public void Click(T selected)
    {
        item = selected;
        OnItemClick();
    }

    private void OnItemClick()
    {
        if (item is not null) clickAction?.Invoke(item);
    }
}

public sealed class UIHarvestAction : UIGenericItem<HarvestActionInstance>
{
    public HarvestActionInstanceListVariable? actionListVariable;
    public int FlashCalls { get; private set; }

    public void Flash() => FlashCalls++;
}

public sealed class UIHarvestActionList
{
    public HarvestActionInstanceListVariable? actionListVariable;
    public HarvestActionInstanceListVariable? listVariable;

    public void PostSetupItem(UIGenericItem<HarvestActionInstance> listItem)
    {
        listItem.SetupClick(OnActionClick);
        if (listItem is UIHarvestAction harvestAction)
            harvestAction.actionListVariable = actionListVariable;
    }

    public void OnActionClick(HarvestActionInstance instance)
    {
        if (actionListVariable is not null)
            actionListVariable.AddInstance(instance, GlobalVariables.GetMultiBuy().AsInt());
        else
            listVariable?.RemoveInstance(instance, GlobalVariables.GetMultiBuy().AsInt());
    }

    public object? GetRenderedItem(HarvestActionInstance instance) => null;
}
