using System;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class GenericDiscoverableContractTests
{
    [GameAssemblyFact]
    public void DiscoverableInterface_HasExactlySixImplementersAndPinnedMemberTokens()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            new[]
            {
                "AlchemyRecipeSO",
                "EquipmentSO",
                "GlyphSO",
                "RitualSO",
                "SpellRecipeSO",
                "TimeRuneSO",
            },
            assembly.GetTypesImplementing("IDiscoverable"));
        Assert.Equal(0x06001C8F, assembly.GetMethodToken("IDiscoverable", "GetGlyphRecipe"));
        Assert.Equal(0x06001C90, assembly.GetMethodToken("IDiscoverable", "GetResourceRecipe"));
        Assert.Equal(0x06001C92, assembly.GetMethodToken("IDiscoverable", "GetDiscoverCost"));
        Assert.Equal(0x06001C93, assembly.GetMethodToken("IDiscoverable", "IsDiscoverVisible"));
        Assert.Equal(0x06001C94, assembly.GetMethodToken("IDiscoverable", "CanDiscover"));
        Assert.Equal(0x06001C95, assembly.GetMethodToken("IDiscoverable", "IsDiscovered"));
        Assert.Equal(0x06001C96, assembly.GetMethodToken("IDiscoverable", "IsDiscoverRequired"));
        Assert.Equal(0x06001C97, assembly.GetMethodToken("IDiscoverable", "Discover"));
    }

    [GameAssemblyFact]
    public void EveryConcreteDiscoveryPipeline_KeepsItsPinnedVerdictAndMutationTokens()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        AssertTokens(assembly, "AlchemyRecipeSO", 0x06000863, 0x06000845, 0x06000844, 0x06000850);
        AssertTokens(assembly, "EquipmentSO", 0x06000B35, 0x06000B3B, 0x06000B3C, 0x06000B10);
        AssertTokens(assembly, "GlyphSO", 0x06000BF3, 0x06000BF5, 0x06000BF6, 0x06000BFB);
        AssertTokens(assembly, "RitualSO", 0x060013BB, 0x060013C2, 0x060013C3, 0x06001366);
        AssertTokens(assembly, "SpellRecipeSO", 0x06001442, 0x06001451, 0x06001454, 0x06001432);
        AssertTokens(assembly, "TimeRuneSO", 0x06001851, 0x06001855, 0x06001856, 0x06001858);
    }

    [GameAssemblyFact]
    public void GenericDiscoverableUi_UsesInterfaceCallbackAndCostButtonPaysBeforeCallback()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x0600231C, assembly.GetMethodToken("UIDiscoverablePage", "HandleClick"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoverablePage", "HandleClick", "IDiscoverable", "Discover"));

        var enough = assembly.MethodReferenceOffset(
            "UICostButton", "OnClick", "ResourceCostList", "HasEnough");
        var payment = assembly.MethodReferenceOffset(
            "UICostButton", "OnClick", "ResourceCostList", "PerformCost");
        var callback = assembly.GetMethodBodyMemberReferences("UICostButton", "OnClick")
            .Where(reference => reference.MemberName == "Invoke")
            .Select(reference => reference.Offset)
            .DefaultIfEmpty(-1)
            .Max();
        Assert.True(enough >= 0, "UICostButton.OnClick must check native affordability.");
        Assert.True(payment > enough, "UICostButton.OnClick must pay only after affordability.");
        Assert.True(
            callback > payment,
            "UICostButton.OnClick must invoke the page callback only after payment. Native refs: " +
            string.Join("; ", References(assembly, "UICostButton", "OnClick")));
    }

    [GameAssemblyFact]
    public void GenericDiscoverableUi_ResolvesConfiguredCandidatesByRecipeCountsAndMembership()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            0x06002321,
            assembly.GetMethodToken("UIDiscoverablePage", "GetDiscoverableFromRecipe"));
        Assert.True(assembly.MethodReferencesField(
            "UIDiscoverablePage",
            "GetDiscoverableFromRecipe",
            "UIDiscoverablePage",
            "availableItems"));
        var predicateTypes = new[]
        {
            "UIDiscoverablePage+<>c__DisplayClass31_0",
            "UIDiscoverablePage+<>c__DisplayClass31_1",
            "UIDiscoverablePage+<>c__DisplayClass31_2",
        };
        var references = predicateTypes
            .SelectMany(predicateType => assembly.GetMethods(predicateType)
                .Where(method => method.Name.StartsWith(
                    "<GetDiscoverableFromRecipe>", StringComparison.Ordinal))
                .SelectMany(method => assembly.GetMethodBodyDefinitionReferences(
                        predicateType, method.Name)
                    .Concat(assembly.GetMethodBodyMemberReferences(predicateType, method.Name))))
            .ToArray();
        Assert.Contains(
            references,
            reference => reference.DeclaringType == "IDiscoverable" &&
                         reference.MemberName == "GetGlyphRecipe");
        Assert.Contains(
            references,
            reference => reference.DeclaringType == "IDiscoverable" &&
                         reference.MemberName == "GetResourceRecipe");
        Assert.True(
            references.Count(reference => reference.MemberName == "get_Count") >= 2,
            "The UI resolver must compare authored and submitted recipe counts.");
        Assert.True(
            references.Count(reference => reference.MemberName == "Contains") >= 2,
            "The UI resolver must test every submitted glyph and resource for membership.");
    }

    [GameAssemblyFact]
    public void EquipmentDiscovery_DelegatesToCreateAsTheArtifactOutcome()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x06000B11, assembly.GetMethodToken("EquipmentSO", "Create"));
        Assert.True(assembly.MethodReferencesMethod(
            "EquipmentSO", "Discover", "EquipmentSO", "Create"));
    }

    private static void AssertTokens(
        GameAssemblyMetadata assembly,
        string type,
        int cost,
        int canDiscover,
        int isDiscovered,
        int discover)
    {
        Assert.Equal(cost, assembly.GetMethodToken(type, "GetDiscoverCost"));
        Assert.Equal(canDiscover, assembly.GetMethodToken(type, "CanDiscover"));
        Assert.Equal(isDiscovered, assembly.GetMethodToken(type, "IsDiscovered"));
        Assert.Equal(discover, assembly.GetMethodToken(type, "Discover"));
    }

    private static string[] References(
        GameAssemblyMetadata assembly,
        string type,
        string method) =>
        assembly.GetMethodBodyDefinitionReferences(type, method)
            .Concat(assembly.GetMethodBodyMemberReferences(type, method))
            .OrderBy(reference => reference.Offset)
            .Select(reference =>
                $"IL_{reference.Offset:X4} 0x{reference.Token:X8} " +
                $"{reference.DeclaringType}.{reference.MemberName}")
            .ToArray();
}
