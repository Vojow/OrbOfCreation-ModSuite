using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// What the collection service reports about itself: how much of the world it read, and how much of
/// it this build let it read.
/// </summary>
/// <remarks>
/// Worth projecting even though collection has no gameplay behaviour to inspect. Every other service
/// now depends on this one, so "the world looks empty" needs to be answerable as either "the save is
/// empty" or "seven categories would not bind" without attaching a debugger.
/// </remarks>
internal static class AutomataWorldCollectionProjection
{
    private const int EntitiesKey = 1;
    private const int CompleteKey = 2;
    private const int UnavailableCategoriesKey = 3;

    internal static void Write(
        in AutomataWorldCollectionState state,
        ServiceStateProjectionBuilder output)
    {
        output.Add(new ServiceProjectionKey(EntitiesKey),
            ServiceProjectionValue.FromInteger(state.LastEntities));
        output.Add(new ServiceProjectionKey(CompleteKey),
            ServiceProjectionValue.FromBoolean(state.LastPassComplete));
        output.Add(new ServiceProjectionKey(UnavailableCategoriesKey),
            ServiceProjectionValue.FromInteger(state.LastCategoriesUnavailable));
    }
}
