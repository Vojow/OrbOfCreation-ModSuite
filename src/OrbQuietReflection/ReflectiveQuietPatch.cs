using System;

namespace OrbQuietReflection;

internal static class ReflectiveQuietPatch
{
    internal static bool SuppressionEnabled { get; set; }

    internal static Action<Exception>? ContractFailure { get; set; }

    internal static void Postfix(PassiveAbilitySO __instance, ref bool __result)
    {
        if (__result || !SuppressionEnabled || __instance is null)
        {
            return;
        }

        try
        {
            var passiveTypes = __instance.passiveTypes;
            if (passiveTypes is null)
            {
                return;
            }

            for (var i = 0; i < passiveTypes.Count; i++)
            {
                var passiveType = passiveTypes[i];
                if (passiveType is not null &&
                    ReflectiveNotificationFilter.IsReflectivePassiveType(passiveType.GetGuid()))
                {
                    __result = true;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            ContractFailure?.Invoke(ex);
        }
    }
}
