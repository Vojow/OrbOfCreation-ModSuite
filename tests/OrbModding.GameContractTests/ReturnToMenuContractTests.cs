using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class ReturnToMenuContractTests
{
    [GameAssemblyFact]
    public void BackToMenuBindingsAndStartDestinationStayTokenExact()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x0600280d, assembly.GetMethodToken("UIBackToMenuButton", "BackToMenu"));
        Assert.Equal(0x0400148c, assembly.GetFieldToken("UIBackToMenuButton", "manualSave"));
        Assert.Equal(0x060006ff, assembly.GetMethodToken("SaveStateManager", "BackToMainMenu"));
        Assert.Equal(0x06000700, assembly.GetMethodToken("SaveStateManager", "AnimateChangeScene",
            "System.String"));
        Assert.Equal(0x0400130a, assembly.GetFieldToken("UIScreenFlash", "instance"));
        Assert.Equal(0x04001308, assembly.GetFieldToken("UIScreenFlash", "isActive"));
        Assert.Equal(0x060026a3, assembly.GetMethodToken("UIScreenFlash", "FadeIn",
            "System.Single", "System.Single"));
        Assert.Equal(
            new byte[] { 0x02, 0x72, 0xcd, 0x3a, 0x00, 0x70, 0x28, 0x00, 0x07, 0x00, 0x06, 0x2a },
            assembly.GetMethodBodyBytes("SaveStateManager", "BackToMainMenu"));
    }

    [GameAssemblyFact]
    public void VisibleButtonSavesBeforeSchedulingTheOneGameWrittenTransitionSentinel()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var ui = assembly.GetMethodBodyDefinitionReferences("UIBackToMenuButton", "BackToMenu");
        Assert.True(Offset(ui, "VoidEventChannel", "Raise") <
            Offset(ui, "SaveStateManager", "BackToMainMenu"));

        var manager = assembly.GetMethodBodyDefinitionReferences(
            "SaveStateManager", "AnimateChangeScene", "System.String");
        Assert.True(Offset(manager, "UIScreenFlash", "FadeIn") <
            Offset(manager, "UIScreenFlash", "SetLoadingAnim"));
        Assert.True(Offset(manager, "UIScreenFlash", "SetLoadingAnim") <
            Offset(manager, "UIScreenFlash", "OnAnimComplete"));

        var fade = assembly.GetMethodBodyDefinitionReferences(
            "UIScreenFlash", "FadeIn", "System.Single", "System.Single");
        Assert.True(Offset(fade, "UIScreenFlash", "isActive") <
            Offset(fade, "UIScreenFlash", "AnimateAlpha"));
    }

    private static int Offset(
        System.Collections.Generic.IReadOnlyList<MethodBodyDefinitionReference> references,
        string declaringType,
        string memberName) =>
        references.Single(reference =>
            reference.DeclaringType == declaringType &&
            reference.MemberName == memberName).Offset;
}
