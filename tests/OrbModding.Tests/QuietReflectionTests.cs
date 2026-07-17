using System;
using OrbQuietReflection;
using Xunit;

namespace OrbModding.Tests;

public sealed class QuietReflectionTests
{
    [Fact]
    public void ReflectivePassiveType_IsRecognizedByStableUuid()
    {
        Assert.True(ReflectiveNotificationFilter.IsReflectivePassiveType(
            new Guid("95a27ac0-751c-4972-922c-cc6b8c0949da")));
        Assert.False(ReflectiveNotificationFilter.IsReflectivePassiveType(Guid.Empty));
    }

    [Fact]
    public void EnabledFilter_QuietsReflectivePassive()
    {
        var passive = new PassiveAbilitySO();
        passive.passiveTypes.Add(new PassiveAbilityTypeSO
        {
            Guid = ReflectiveNotificationFilter.ReflectivePassiveTypeId,
        });
        var quiet = false;

        ReflectiveQuietPatch.SuppressionEnabled = true;
        try
        {
            ReflectiveQuietPatch.Postfix(passive, ref quiet);
        }
        finally
        {
            ReflectiveQuietPatch.SuppressionEnabled = false;
        }

        Assert.True(quiet);
    }

    [Fact]
    public void Filter_PreservesNativeAndUnrelatedResults()
    {
        var unrelated = new PassiveAbilitySO();
        unrelated.passiveTypes.Add(new PassiveAbilityTypeSO { Guid = Guid.NewGuid() });
        var unrelatedQuiet = false;
        var nativeQuiet = true;

        ReflectiveQuietPatch.SuppressionEnabled = true;
        try
        {
            ReflectiveQuietPatch.Postfix(unrelated, ref unrelatedQuiet);
            ReflectiveQuietPatch.Postfix(new PassiveAbilitySO(), ref nativeQuiet);
        }
        finally
        {
            ReflectiveQuietPatch.SuppressionEnabled = false;
        }

        Assert.False(unrelatedQuiet);
        Assert.True(nativeQuiet);
    }

    [Fact]
    public void DisabledFilter_LeavesReflectivePassiveUnchanged()
    {
        var passive = new PassiveAbilitySO();
        passive.passiveTypes.Add(new PassiveAbilityTypeSO
        {
            Guid = ReflectiveNotificationFilter.ReflectivePassiveTypeId,
        });
        var quiet = false;

        ReflectiveQuietPatch.SuppressionEnabled = false;
        ReflectiveQuietPatch.Postfix(passive, ref quiet);

        Assert.False(quiet);
    }
}
