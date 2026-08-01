using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class DiscoveryTreeOfferContractTests
{
    [GameAssemblyFact]
    public void DiscoveryTreeDecisionReader_PinsEveryNewNativeMemberToken()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(0x04000647, assembly.GetFieldToken("DiscoveryTreeSO", "currentChoiceIds"));
        Assert.Equal(0x06001B44, assembly.GetMethodToken("GuidContainer", "get_guid"));
        Assert.Equal(0x06000AD5, assembly.GetMethodToken("DiscoveryTreeSO", "IsVisible"));
        Assert.Equal(0x06000AC6,
            assembly.GetMethodToken("DiscoveryTreeSO", "HasImmediateRequiredDiscover"));
        Assert.Equal(0x06000AB8, assembly.GetMethodToken("DiscoveryTreeSO", "GetNextItemCost"));
        Assert.Equal(0x06001E50, assembly.GetMethodToken("ResourceCostList", "GetEntries"));
        Assert.Equal(0x06001E0F, assembly.GetMethodToken("ResourceCostList", "HasEnough"));
        Assert.Equal(0x06001F96, assembly.GetMethodToken("ResourceTuple", "GetValue"));
        Assert.Equal(0x060012BE, assembly.GetMethodToken("ResourceSO", "GetTrueQuantity"));
    }

    [GameAssemblyFact]
    public void DiscoveryTreeOffersCanContainExactlyThePublishedExplainableEntityFamilies()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var discoverables = new[]
        {
            "AlchemyRecipeSO",
            "EquipmentSO",
            "GlyphSO",
            "RitualSO",
            "SpellRecipeSO",
            "TimeRuneSO",
        };
        Assert.Equal(discoverables, assembly.GetTypesImplementing("IDiscoverable"));
        Assert.All(discoverables, type =>
            Assert.True(
                assembly.ImplementsInterface(type, "ITooltipable"),
                type + " does not implement ITooltipable through its native base chain"));
    }

    [GameAssemblyFact]
    public void DiscoveryTreeInitiate_IsSynchronousDataPipelineWithExactNativeRerollClamp()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(0x06000AA8, assembly.GetMethodToken("DiscoveryTreeSO", "InitiateCraftingMode"));
        Assert.Equal(0x06000AA9, assembly.GetMethodToken("DiscoveryTreeSO", "EnterCraftingMode"));
        Assert.Equal(0x06000AA7, assembly.GetMethodToken("DiscoveryTreeSO", "EnterMode"));
        Assert.Equal(0x06000ACB, assembly.GetMethodToken("DiscoveryTreeSO", "GetMaxRerolls"));

        Assert.Equal(
            new[]
            {
                "IL_0002 0x04000646 field DiscoveryTreeSO.usedRerollsLastDiscover",
                "IL_000B 0x04000645 field DiscoveryTreeSO.rerollsLeft",
                "IL_0013 0x06000ACB method DiscoveryTreeSO.GetMaxRerolls",
                "IL_002A 0x06000AB3 method DiscoveryTreeSO.FetchRarityLevels",
                "IL_0030 0x06000AA9 method DiscoveryTreeSO.EnterCraftingMode",
            },
            References(assembly, "DiscoveryTreeSO", "InitiateCraftingMode"));
        Assert.Equal(
            new[]
            {
                "IL_0003 0x06000AA7 method DiscoveryTreeSO.EnterMode",
                "IL_0009 0x04000637 field DiscoveryTreeSO.startCraftSound",
                "IL_000E 0x0600191F method AudioInstance.Play",
            },
            References(assembly, "DiscoveryTreeSO", "EnterCraftingMode"));
        Assert.Equal(
            new[]
            {
                "IL_0003 0x04000643 field DiscoveryTreeSO.actionMode",
                "IL_000F 0x04000644 field DiscoveryTreeSO.actionTime",
                "IL_0015 0x04000656 field DiscoveryTreeSO.modeObservable",
                "IL_001A 0x06001DD9 method PassiveObservable.UpdateObservable",
            },
            References(assembly, "DiscoveryTreeSO", "EnterMode"));
        Assert.Equal(
            new[] { "IL_0003 0x04000CF4 field PassiveObservable.observedId" },
            References(assembly, "PassiveObservable", "UpdateObservable"));

        var synchronousReferences = References(assembly, "DiscoveryTreeSO", "InitiateCraftingMode")
            .Concat(References(assembly, "DiscoveryTreeSO", "EnterCraftingMode"))
            .Concat(References(assembly, "DiscoveryTreeSO", "EnterMode"));
        Assert.DoesNotContain(synchronousReferences, reference =>
            reference.Contains("UIDiscoveryTreePage", System.StringComparison.Ordinal));
    }

    [GameAssemblyFact]
    public void DiscoveryTreeOffer_MatchesCompleteLifecycleBindingSet()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.HasType("DiscoveryTreeSO"));
        Assert.True(assembly.HasType("DiscoveryTreeSO+DiscoveryTreeModes"));
        Assert.True(assembly.HasType("GuidContainer"));
        Assert.True(assembly.HasType("IDiscoverable"));
        Assert.True(assembly.HasType("IHasGuid"));
        Assert.Equal(
            new[] { "Idle", "Crafting", "Choice" },
            assembly.GetInt32EnumMembers("DiscoveryTreeSO+DiscoveryTreeModes").Keys.ToArray());

        Assert.Equal("System.Collections.Generic.List`1<DiscoveryTreeSO>",
            assembly.GetFieldType("DiscoveryTreeSO", "All"));
        Assert.Equal("DiscoveryTreeSO+DiscoveryTreeModes",
            assembly.GetFieldType("DiscoveryTreeSO", "actionMode"));
        Assert.Equal("BigDouble", assembly.GetFieldType("DiscoveryTreeSO", "actionTime"));
        Assert.Equal("System.Int32", assembly.GetFieldType("DiscoveryTreeSO", "rerollsLeft"));
        Assert.Equal("System.Boolean",
            assembly.GetFieldType("DiscoveryTreeSO", "usedRerollsLastDiscover"));
        Assert.Equal("System.Collections.Generic.List`1<GuidContainer>",
            assembly.GetFieldType("DiscoveryTreeSO", "currentChoiceIds"));
        Assert.Equal("System.Collections.Generic.List`1<GuidContainer>",
            assembly.GetFieldType("DiscoveryTreeSO", "nextExcludedIds"));
        Assert.Equal("GuidContainer",
            assembly.GetFieldType("DiscoveryTreeSO", "selectedChoiceId"));
        Assert.Equal("System.Int32",
            assembly.GetFieldType("DiscoveryTreeSO", "totalDiscoveredCount"));
        Assert.Equal("System.Int32",
            assembly.GetFieldType("DiscoveryTreeSO", "poolDiscoveredCount"));

        AssertMethod(assembly, "IdScriptableObject", "GetGuid", false, "System.Guid");
        AssertMethod(assembly, "GuidContainer", "get_guid", false, "System.Guid");
        AssertMethod(assembly, "DiscoveryTreeSO", "IsVisible", false, "System.Boolean");
        AssertMethod(assembly, "DiscoveryTreeSO", "IsInIdleMode", false, "System.Boolean");
        AssertMethod(assembly, "DiscoveryTreeSO", "IsInCraftingMode", false, "System.Boolean");
        AssertMethod(assembly, "DiscoveryTreeSO", "IsInChoiceMode", false, "System.Boolean");
        AssertMethod(assembly, "DiscoveryTreeSO", "HasCurrentlyRemMainPoolDiscoveries", false, "System.Boolean");
        AssertMethod(assembly, "DiscoveryTreeSO", "HasImmediateRequiredDiscover", false, "System.Boolean");
        AssertMethod(assembly, "DiscoveryTreeSO", "GetMaxRerolls", false, "System.Int32");
        AssertMethod(assembly, "DiscoveryTreeSO", "GetNextItemCost", false, "ResourceCostList");
        AssertMethod(assembly, "DiscoveryTreeSO", "GetItemFromGuid", false, "IDiscoverable", "System.Guid");
        AssertMethod(assembly, "DiscoveryTreeSO", "InitiateCraftingMode", false, "System.Void");
        AssertMethod(assembly, "DiscoveryTreeSO", "SelectItemId", false, "System.Void", "System.Guid");
        AssertMethod(assembly, "DiscoveryTreeSO", "DiscoverSelectedItem", false, "System.Void");
        AssertMethod(assembly, "DiscoveryTreeSO", "RerollChoices", false, "System.Void");
        AssertMethod(assembly, "IHasGuid", "GetGuid", false, "System.Guid");
        AssertMethod(assembly, "IDiscoverable", "IsDiscovered", false, "System.Boolean");
        AssertMethod(assembly, "IDiscoverable", "IsDiscoverRequired", false, "System.Boolean");
        AssertMethod(assembly, "ResourceCostList", "GetEntries", false,
            "System.Collections.Generic.List`1<ResourceTuple>");
        AssertMethod(assembly, "ResourceCostList", "HasEnough", false, "System.Boolean");
        AssertMethod(assembly, "ResourceCostList", "PerformCost", false, "System.Void");
        Assert.Equal("ResourceSO", assembly.GetFieldType("ResourceTuple", "resource"));
        AssertMethod(assembly, "ResourceTuple", "GetValue", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueQuantity", false, "BigDouble");
    }

    [GameAssemblyFact]
    public void DiscoveryTreeUi_ReachesTheAuditedDataPipelineAndPaysBeforeInitiate()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoveryTreePage", "OnDiscoveryClick", "DiscoveryTreeSO", "IsInIdleMode"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoveryTreePage", "OnDiscoveryClick", "DiscoveryTreeSO", "InitiateCraftingMode"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoveryTreePage", "SelectItemGuid", "DiscoveryTreeSO", "SelectItemId"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoveryTreePage", "OnConfirmClick", "DiscoveryTreeSO", "DiscoverSelectedItem"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoveryTreePage", "OnRerollClick", "DiscoveryTreeSO", "RerollChoices"));

        var enough = assembly.MethodReferenceOffset(
            "UICostButton", "OnClick", "ResourceCostList", "HasEnough");
        var payment = assembly.MethodReferenceOffset(
            "UICostButton", "OnClick", "ResourceCostList", "PerformCost");
        Assert.True(enough >= 0, "UICostButton.OnClick must check HasEnough.");
        Assert.True(payment > enough, "UICostButton.OnClick must perform cost after affordability.");
        Assert.False(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "InitiateCraftingMode", "ResourceCostList", "PerformCost"));
    }

    [GameAssemblyFact]
    public void DiscoveryTreeNativeStages_PinDelayedOffersConfirmCountsAndStaleRerollSelection()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "IncrementCrafting", "DiscoveryTreeSO", "EnterChoiceMode"));
        Assert.True(assembly.MethodReferencesField(
            "DiscoveryTreeSO", "EnterChoiceMode", "DiscoveryTreeSO", "currentChoiceIds"));
        Assert.True(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "DiscoverSelectedItem", "DiscoveryTreeSO", "DiscoverItem"));
        Assert.True(assembly.MethodReferencesField(
            "DiscoveryTreeSO", "DiscoverItem", "DiscoveryTreeSO", "totalDiscoveredCount"));
        Assert.True(assembly.MethodReferencesField(
            "DiscoveryTreeSO", "DiscoverItem", "DiscoveryTreeSO", "poolDiscoveredCount"));
        Assert.True(assembly.MethodReferencesField(
            "DiscoveryTreeSO", "RerollChoices", "DiscoveryTreeSO", "currentChoiceIds"));
        Assert.True(assembly.MethodReferencesField(
            "DiscoveryTreeSO", "RerollChoices", "DiscoveryTreeSO", "nextExcludedIds"));
        Assert.False(assembly.MethodReferencesField(
            "DiscoveryTreeSO", "RerollChoices", "DiscoveryTreeSO", "selectedChoiceId"));
    }

    private static void AssertMethod(
        GameAssemblyMetadata assembly,
        string typeName,
        string methodName,
        bool isStatic,
        string returnType,
        params string[] parameterTypes)
    {
        var matches = assembly.GetMethods(typeName, methodName);
        Assert.Contains(matches, method =>
            method.IsStatic == isStatic &&
            method.ReturnType == returnType &&
            method.ParameterTypes.SequenceEqual(parameterTypes));
    }

    private static string[] References(
        GameAssemblyMetadata assembly,
        string typeName,
        string methodName) =>
        assembly.GetMethodBodyDefinitionReferences(typeName, methodName)
            .Select(reference =>
                $"IL_{reference.Offset:X4} 0x{reference.Token:X8} {reference.Kind} " +
                $"{reference.DeclaringType}.{reference.MemberName}")
            .ToArray();
}
