using System.Linq;
using OrbModding.Common;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class MentorPerformanceContractTests
{
    [GameAssemblyFact]
    public void ProgressionDirtyHooksAndRegistryLookupMatchInstalledGame()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        AssertVoidNoArgs(assembly, "SpellRecipeSO", "Discover");
        AssertVoidNoArgs(assembly, "SpellRecipeSO", "PurchaseLevel");
        AssertVoidNoArgs(assembly, "SpellRecipeSO", "ResetData");
        AssertVoidNoArgs(assembly, "AlchemyRecipeSO", "Discover");
        AssertVoidNoArgs(assembly, "AlchemyRecipeSO", "ApplyMastery");
        AssertVoidNoArgs(assembly, "AlchemyRecipeSO", "ResetData");
        AssertVoidNoArgs(assembly, "EquipmentSO", "Discover");
        AssertVoidNoArgs(assembly, "EquipmentSO", "Create");
        AssertVoidNoArgs(assembly, "EquipmentSO", "ResetData");
        Assert.Contains(
            assembly.GetMethods("EquipmentSO", "GainMasteryLevels"),
            method => !method.IsStatic && method.ReturnType == "System.Void" &&
                      method.ParameterTypes.SequenceEqual(new[] { "System.Int32" }));
        Assert.Contains(
            assembly.GetMethods("IdScriptableObject", "GetInstance"),
            method => method.IsStatic && method.ReturnType == "IdScriptableObject" &&
                      method.ParameterTypes.SequenceEqual(new[] { "System.Guid" }));
    }

    private static void AssertVoidNoArgs(GameAssemblyMetadata assembly, string type, string method) =>
        Assert.Contains(
            assembly.GetMethods(type, method),
            candidate => !candidate.IsStatic && candidate.ReturnType == "System.Void" && candidate.ParameterTypes.Count == 0);
}
