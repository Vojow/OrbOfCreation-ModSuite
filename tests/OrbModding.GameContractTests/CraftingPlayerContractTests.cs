using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class CraftingPlayerContractTests
{
    [GameAssemblyFact]
    public void CraftingPlayerBinding_PinsEveryNewNativeMemberToken()
    {
        var paths = GameAssemblyPaths.Require();
        using var assembly = new GameAssemblyMetadata(paths.AssemblyCSharp);
        using var unity = new GameAssemblyMetadata(paths.UnityCore);

        Assert.Equal(0x04000F6A,
            assembly.GetFieldToken("UICraftingPage", "availableRecipes"));
        Assert.Equal(0x04000F6B,
            assembly.GetFieldToken("UICraftingPage", "craftingQueueInstances"));
        Assert.Equal(0x04000F6D,
            assembly.GetFieldToken("UICraftingPage", "craftMode"));
        Assert.Equal(0x04000F6E,
            assembly.GetFieldToken("UICraftingPage", "mainCraftType"));
        Assert.Equal(0x040005F2,
            assembly.GetFieldToken("CraftingRecipeSO", "timeToComplete"));
        Assert.Equal(0x06000A2A,
            assembly.GetMethodToken("CraftingRecipeSO", "CanBuy"));
        Assert.Equal(0x06000A30,
            assembly.GetMethodToken("CraftingRecipeSO", "GetPurchaseQuantity"));
        Assert.Equal(0x06000A32,
            assembly.GetMethodToken("CraftingRecipeSO", "Execute"));
        Assert.Equal(0x0600164F,
            assembly.GetMethodToken("CraftingInstanceListVariable", "GetQuantity"));
        Assert.Equal(0x06000DE2,
            assembly.GetMethodToken("CraftingInstance", "AddQuantity"));

        AssertMethod(
            unity,
            "UnityEngine.Resources",
            "FindObjectsOfTypeAll",
            isStatic: true,
            "UnityEngine.Object[]",
            "System.Type");
    }

    [GameAssemblyFact]
    public void CraftingRecipeExecute_IsTheSynchronousDirectCompositeUsedByTheUiFallback()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "UICraftingRecipeList", "ClickCraft", "CraftingRecipeSO", "Execute"));
        Assert.Equal(
            new[]
            {
                "IL_0002 0x06000A2A method CraftingRecipeSO.CanBuy",
                "IL_0011 0x06000A30 method CraftingRecipeSO.GetPurchaseQuantity",
                "IL_0027 0x040005F1 field CraftingRecipeSO.recipeCost",
                "IL_002D 0x06001E3B method ResourceCostList.Multiply",
                "IL_0032 0x06001E19 method ResourceCostList.PerformCost",
                "IL_0038 0x04000600 field CraftingRecipeSO.effectChannel",
                "IL_0042 0x06003A9F method PassiveObservable+Channel.Update",
            },
            Definitions(assembly, "CraftingRecipeSO", "Execute"));
    }

    [GameAssemblyFact]
    public void CraftingPageUi_InstallsTheExactRecipeCallbackAndAuthoredPageRelations()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("CraftingRecipeListVariable",
            assembly.GetFieldType("UICraftingPage", "availableRecipes"));
        Assert.Equal("CraftingInstanceListVariable",
            assembly.GetFieldType("UICraftingPage", "craftingQueueInstances"));
        Assert.Equal("IntVariable", assembly.GetFieldType("UICraftingPage", "craftMode"));
        Assert.Equal("CraftingRecipeTypeSO",
            assembly.GetFieldType("UICraftingPage", "mainCraftType"));
        Assert.True(assembly.MethodReferencesMethod(
            "UICraftingPage", "UIStart", "UICraftingPage", "ContextRecipeClick"));
        Assert.True(assembly.MethodReferencesMethod(
            "UICraftingPage", "UIStart", "UICraftingPage", "ContextRecipeInteraction"));
        Assert.True(assembly.MethodReferencesField(
            "UICraftingPage", "UIStart", "UICraftingPage", "availableRecipes"));
        Assert.True(assembly.MethodReferencesField(
            "UICraftingPage", "UIStart", "UICraftingPage", "craftingQueueInstances"));
        Assert.True(assembly.MethodReferencesField(
            "UICraftingPage", "UIStart", "UICraftingPage", "mainCraftType"));

        var quantity = assembly.MethodReferenceOffset(
            "UICraftingPage", "ContextRecipeClick",
            "CraftingInstanceListVariable", "GetQuantity");
        var purchaseAmount = assembly.MethodReferenceOffset(
            "UICraftingPage", "ContextRecipeClick",
            "CraftingRecipeSO", "GetPurchaseQuantity");
        var queue = assembly.MethodReferenceOffset(
            "UICraftingPage", "ContextRecipeClick", "UICraftingPage", "QueueCraft");
        Assert.True(quantity >= 0 && purchaseAmount > quantity && queue > purchaseAmount);
    }

    [GameAssemblyFact]
    public void QueueCraft_PaysBeforeTheModeSpecificOutcomeAndSpaceCheckAllowsStacking()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var quantity = assembly.MethodReferenceOffset(
            "UICraftingPage", "QueueCraft", "CraftingInstanceListVariable", "GetQuantity");
        var payment = assembly.MethodReferenceOffset(
            "UICraftingPage", "QueueCraft", "CraftingRecipeSO", "PurchaseQuantity");
        var mode = assembly.MethodReferenceOffset(
            "UICraftingPage", "QueueCraft", "UICraftingPage", "GetCraftMode");
        var stack = assembly.MethodReferenceOffset(
            "UICraftingPage", "QueueCraft", "CraftingInstance", "AddQuantity");
        var instantCheck = assembly.MethodReferenceOffset(
            "UICraftingPage", "QueueCraft", "CraftingInstance", "CheckInstantCraft");
        var instant = assembly.MethodReferenceOffset(
            "UICraftingPage", "QueueCraft", "CraftingInstance", "InstantCraft");
        var initiate = assembly.MethodReferenceOffset(
            "UICraftingPage", "QueueCraft", "CraftingInstance", "Initiate");
        Assert.True(quantity >= 0 && payment > quantity && mode > payment);
        Assert.True(stack > mode);
        Assert.True(instantCheck > stack && instant > instantCheck && initiate > instant);

        Assert.True(assembly.MethodReferencesMethod(
            "UICraftingPage", "HasSpaceForCraft", "UICraftingPage", "GetCraftMode"));
        Assert.True(assembly.MethodReferencesMethod(
            "UICraftingPage", "HasSpaceForCraft", "AbstractListVariable", "IsAtMax"));
        Assert.Contains(
            assembly.GetMethodBodyMemberReferences("UICraftingPage", "HasSpaceForCraft"),
            reference => reference.MemberName == "Find");
    }

    private static void AssertMethod(
        GameAssemblyMetadata assembly,
        string typeName,
        string methodName,
        bool isStatic,
        string returnType,
        params string[] parameters)
    {
        Assert.Contains(assembly.GetMethods(typeName, methodName), method =>
            method.IsStatic == isStatic &&
            method.ReturnType == returnType &&
            method.ParameterTypes.SequenceEqual(parameters));
    }

    private static string[] Definitions(
        GameAssemblyMetadata assembly,
        string typeName,
        string methodName) =>
        assembly.GetMethodBodyDefinitionReferences(typeName, methodName)
            .Select(reference =>
                $"IL_{reference.Offset:X4} 0x{reference.Token:X8} {reference.Kind} " +
                $"{reference.DeclaringType}.{reference.MemberName}")
            .ToArray();
}
