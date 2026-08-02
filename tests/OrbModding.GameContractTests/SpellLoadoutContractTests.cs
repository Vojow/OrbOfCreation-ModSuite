using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class SpellLoadoutContractTests
{
    [GameAssemblyFact]
    public void SpellLoadoutBindings_PinEveryNewNativeMemberToken()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x06001027, assembly.GetMethodToken("Spell", "IsEmpty"));
        Assert.Equal(0x06001038, assembly.GetMethodToken("Spell", "CanRemove"));
        Assert.Equal(0x06001087, assembly.GetMethodToken("Spell", "GetName"));
        Assert.Equal(0x0600074C, assembly.GetMethodToken("SpellManager", "RemoveSpell"));
        Assert.Equal(0x060014ED, assembly.GetMethodToken("AbstractListVariable", "UpdateObservable"));

        Assert.Contains(
            assembly.GetMethods("AbstractListVariable`1", "SwapPositions"),
            method => method.Visibility == "public" &&
                !method.IsStatic &&
                method.ReturnType == "System.Void" &&
                method.ParameterTypes.SequenceEqual(new[] { "System.Int32", "System.Int32" }));
    }

    [GameAssemblyFact]
    public void RemoveAvailability_IsThePlayerFacingNativeChargeAndCastingGate()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var chargeAvailable = assembly.MethodReferenceOffset(
            "Spell", "CanRemove", "Spell", "IsChargeAvailable");
        var casting = assembly.MethodReferenceOffset(
            "Spell", "CanRemove", "Spell", "IsCasting");

        Assert.True(chargeAvailable >= 0, "CanRemove must consult live charge availability.");
        Assert.True(casting > chargeAvailable, "The live casting guard must follow charge availability.");
    }

    [GameAssemblyFact]
    public void RemoveSpell_RemovesTheExactInstanceThenDestroysAndRecomputesWeight()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var references = References(assembly, "SpellManager", "RemoveSpell");

        var remove = references.Single(reference => reference.MemberName == "Remove");
        var destroy = references.Single(reference =>
            reference.DeclaringType == "Spell" && reference.MemberName == "Destroy");
        var recompute = references.Single(reference =>
            reference.DeclaringType == "SpellManager" &&
            reference.MemberName == "RecomputeSpellWeight");

        Assert.True(destroy.Offset > remove.Offset, "The removed spell must be destroyed after list removal.");
        Assert.True(recompute.Offset > destroy.Offset, "Weight must be recomputed after destroying the removed spell.");
    }

    [GameAssemblyFact]
    public void SpellListDrop_ValidatesIdentityThenSwapsAndPublishesTheNewOrder()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var references = References(assembly, "UISpellList", "OnDrop");

        var listsMatch = Offset(references, "DragDropContext", "ListsMatch");
        var indicesMatch = Offset(references, "DragDropContext", "IndicesMatch");
        var swap = references.Single(reference =>
            reference.MemberName == "SwapPositions" &&
            reference.DeclaringType == "AbstractListVariable`1<Spell>").Offset;
        var update = Offset(references, "AbstractListVariable", "UpdateObservable");

        Assert.True(indicesMatch > listsMatch, "The UI must reject cross-list drops before comparing indices.");
        Assert.True(swap > indicesMatch, "The exact slot swap must follow both identity guards.");
        Assert.True(update > swap, "The reordered list must notify observers only after the swap.");
    }

    private static MethodBodyDefinitionReference[] References(
        GameAssemblyMetadata assembly,
        string typeName,
        string methodName) =>
        assembly.GetMethodBodyDefinitionReferences(typeName, methodName)
            .Concat(assembly.GetMethodBodyMemberReferences(typeName, methodName))
            .OrderBy(reference => reference.Offset)
            .ToArray();

    private static int Offset(
        MethodBodyDefinitionReference[] references,
        string declaringType,
        string memberName) =>
        references.Single(reference =>
            reference.DeclaringType == declaringType && reference.MemberName == memberName)
        .Offset;
}
