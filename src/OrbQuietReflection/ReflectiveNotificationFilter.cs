using System;

namespace OrbQuietReflection;

internal static class ReflectiveNotificationFilter
{
    internal static readonly Guid ReflectivePassiveTypeId =
        new Guid("95a27ac0-751c-4972-922c-cc6b8c0949da");

    public static bool IsReflectivePassiveType(Guid passiveTypeId)
    {
        return passiveTypeId == ReflectivePassiveTypeId;
    }
}
