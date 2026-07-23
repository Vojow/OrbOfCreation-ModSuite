using System;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
#endif

namespace OrbAutomata;

internal sealed class AutoHarvestStableIdAccessor
{
    private readonly Type _type;

    private AutoHarvestStableIdAccessor(Type type) => _type = type;

    public static AutoHarvestStableIdAccessor Bind(Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (!typeof(IdScriptableObject).IsAssignableFrom(type))
            throw new InvalidOperationException($"{type.FullName} does not use the native identity contract.");
        return new AutoHarvestStableIdAccessor(type);
    }

    public bool TryRead(object instance, out Guid value)
    {
        if (instance is null || instance.GetType() != _type)
        {
            value = default;
            return false;
        }

        value = ((IdScriptableObject)instance).GetGuid();
        return true;
    }

#if SERVICE_CYCLE_PROFILE
    public bool TryRead(
        object instance,
        out Guid value,
        AutoHarvestProfileOperations operations)
    {
        operations.AddStableIdRead();
        return TryRead(instance, out value);
    }
#endif
}
