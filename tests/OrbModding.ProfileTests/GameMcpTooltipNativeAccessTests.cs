using System.Collections.Generic;
using System.Threading;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpTooltipNativeAccessTests
{
    [Fact]
    public void StartupBindingRequiresTheExactAuditedCollectionShape()
    {
        Assert.False(GameMcpTooltipNativeAccess.TryCreate(
            typeof(MissingSubTooltips), out _, out var missingReason));
        Assert.Contains("subTooltips", missingReason);

        Assert.False(GameMcpTooltipNativeAccess.TryCreate(
            typeof(WrongElementType), out _, out var wrongReason));
        Assert.Contains("List<ITooltipable>", wrongReason);
    }

    [Fact]
    public void BoundAccessorReadsLiveObjectsWithoutMemberDiscoveryAtExecution()
    {
        Assert.True(GameMcpTooltipNativeAccess.TryCreate(
            typeof(global::HoverTooltip), out var access, out var bindingReason),
            bindingReason);
        var child = new FakeTooltip("nested");
        var hover = new global::HoverTooltip();
        hover.Setup(
            new FakeTooltip("primary"),
            new List<global::ITooltipable> { child });

        Assert.True(access.TryReadSubTooltips(hover, out var nested, out var readReason),
            readReason);
        Assert.Same(child, Assert.Single(nested));
    }

    [Fact]
    public void BoundAccessorRejectsOffThreadNativeAccess()
    {
        Assert.True(GameMcpTooltipNativeAccess.TryCreate(
            typeof(global::HoverTooltip), out var access, out var bindingReason),
            bindingReason);
        var hover = new global::HoverTooltip();
        hover.Setup(new FakeTooltip("primary"));

        var accepted = true;
        var refusalReason = string.Empty;
        var thread = new Thread(() =>
        {
            accepted = access.TryReadSubTooltips(hover, out _, out refusalReason);
        });
        thread.Start();
        thread.Join();

        Assert.False(accepted);
        Assert.Contains("off the Unity startup thread", refusalReason);
    }

    private sealed class MissingSubTooltips
    {
    }

    private sealed class WrongElementType
    {
#pragma warning disable CS0414
        private readonly List<string> subTooltips = new();
#pragma warning restore CS0414
    }

    private sealed class FakeTooltip : global::ITooltipable
    {
        internal FakeTooltip(string name) => Name = name;
        private string Name { get; }
        public string GetName() => Name;
        public string GetDisplayType() => "Fixture";
        public UnityEngine.Sprite GetIcon() => new();
        public UnityEngine.Color GetColor() => UnityEngine.Color.white;
        public bool IsColoredIcon() => false;
        public bool HasAltTooltips() => false;
        public string GetDescription() => Name;
        public List<global::TooltipNode> GetTooltipNodes() => new();
        public List<global::TooltipNode> GetAltTooltipNodes() => new();
    }
}
