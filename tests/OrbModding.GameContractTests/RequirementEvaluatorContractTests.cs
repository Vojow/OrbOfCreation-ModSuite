using Xunit;

namespace OrbModding.GameContractTests;

public sealed class RequirementEvaluatorContractTests
{
    [GameAssemblyFact]
    public void StructureQuantityRequirementUsesPurchasedQuantityNotGrantedOrTotalLevels()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var evaluator = Assert.Single(
            assembly.GetMethods("Requirements.StructureRequirement", "InternalIsValid"));
        Assert.Equal("System.Boolean", evaluator.ReturnType);
        Assert.True(assembly.MethodReferencesField(
            "Requirements.StructureRequirement",
            "InternalIsValid",
            "StructureSO",
            "quantity"));
        Assert.False(assembly.MethodReferencesField(
            "Requirements.StructureRequirement",
            "InternalIsValid",
            "StructureSO",
            "selfBonusLevels"));
    }
}
