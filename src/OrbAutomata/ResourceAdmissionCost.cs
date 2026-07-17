namespace OrbAutomata;

internal sealed class ResourceAdmissionCost
{
    public ResourceAdmissionCost(
        string resourceId,
        string resourceName,
        BigAmount cost,
        BigAmount currentQuantity,
        BigAmount? capacity = null,
        bool isBandwidth = false)
    {
        ResourceId = resourceId;
        ResourceName = resourceName;
        Cost = cost;
        CurrentQuantity = currentQuantity;
        Capacity = capacity;
        IsBandwidth = isBandwidth;
    }

    public string ResourceId { get; }

    public string ResourceName { get; }

    public BigAmount Cost { get; }

    public BigAmount CurrentQuantity { get; }

    public BigAmount? Capacity { get; }

    public bool IsBandwidth { get; }
}
