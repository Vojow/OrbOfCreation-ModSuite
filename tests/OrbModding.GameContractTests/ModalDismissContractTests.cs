using System;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class ModalDismissContractTests
{
    [GameAssemblyFact]
    public void NativeModalCloseOwnsAnImmediateClosingSentinelAndDelayedDisable()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var close = References(assembly, "UIModal", "CloseModal");
        Assert.Contains(close, reference =>
            reference.DeclaringType == "UIModal" && reference.MemberName == "isClosing");
        Assert.Contains(close, reference =>
            reference.DeclaringType == "UIModal" && reference.MemberName == "graceTime");
        Assert.Contains(close, reference =>
            reference.DeclaringType == "UIModal" && reference.MemberName == "DisableElement");
        Assert.Contains(References(assembly, "UIModal", "IsOpen"), reference =>
            reference.DeclaringType == "UIModal" && reference.MemberName == "isOpen");
    }

    [Fact]
    public void ManifestNamesTheCompleteModalDismissBindingSet()
    {
        var manifest = NativeContractManifest.Load();
        var expected = new[]
        {
            "modal-dismiss.resources.type-action",
            "modal-dismiss.unity-object.type-action",
            "modal-dismiss.modal.type-action",
            "modal-dismiss.find-all-action",
            "modal-dismiss.modal-open-action",
            "modal-dismiss.modal-closing-action",
            "modal-dismiss.modal-grace-action",
            "modal-dismiss.modal-close-action",
        };

        Assert.All(expected, id => Assert.Single(
            manifest.Contracts,
            contract => contract.Id == id));
    }

    private static MethodBodyDefinitionReference[] References(
        GameAssemblyMetadata assembly,
        string type,
        string method,
        params string[] parameters) =>
        assembly.GetMethodBodyDefinitionReferences(type, method, parameters)
            .Concat(assembly.GetMethodBodyMemberReferences(type, method, parameters))
            .OrderBy(reference => reference.Offset)
            .ToArray();
}
