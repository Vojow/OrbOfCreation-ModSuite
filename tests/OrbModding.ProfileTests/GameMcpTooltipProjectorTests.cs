using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpTooltipProjectorTests
{
    [Fact]
    public void TypedTreeIncludesNestedComputedAndInspectedContent()
    {
        var computations = 0;
        var linked = new FakeTooltip("Linked", new TooltipNode("linked row"));
        var computed = new TooltipNode(string.Empty)
        {
            nodeType = TooltipNode.NodeType.IconText,
            textFn = () =>
            {
                computations++;
                return "live value 42";
            },
            tooltipable = linked,
        };
        var root = new TooltipNode("section")
        {
            nodeType = TooltipNode.NodeType.Parent,
            parentType = TooltipNode.ParentType.Boxed,
            children = new List<TooltipNode>
            {
                new("authored child"),
                computed,
            },
        };
        var primary = new FakeTooltip("Primary", root);
        var nested = new FakeTooltip("Nested", new TooltipNode("nested row"));
        var inspected = new FakeTooltip("Inspected", new TooltipNode("panel row"));

        var result = GameMcpTestHarness.Json(GameMcpTooltipProjector.Project(
            primary,
            new[] { nested },
            new[] { inspected }));

        Assert.Equal("direct_unity_main_thread_read", (string?)result["dataSource"]);
        Assert.Contains("not published", (string?)result["collectorGap"]);
        Assert.Equal("primary", (string?)result["tooltip"]?["role"]);
        var rootRow = Assert.Single(result["tooltip"]!["nodes"]!.Values<JObject>());
        Assert.Equal("Parent", (string?)rootRow!["nodeKind"]);
        Assert.Equal("Boxed", (string?)rootRow["parentKind"]);
        var children = rootRow["children"]!.Values<JObject>();
        Assert.Collection(
            children,
            authored =>
            {
                Assert.Equal("authored", (string?)authored!["textKind"]);
                Assert.Equal("authored child", (string?)authored["text"]);
            },
            live =>
            {
                Assert.Equal("computed", (string?)live!["textKind"]);
                Assert.Equal("live value 42", (string?)live["text"]);
                Assert.Equal("available", (string?)live["computationStatus"]);
                Assert.Equal("Linked", (string?)live["linkedTooltip"]?["name"]);
            });
        Assert.Equal(1, computations);
        Assert.Equal(
            "authored_nested",
            (string?)Assert.Single(result["nestedTooltips"]!.Values<JObject>())!["role"]);
        var inspectedRow = Assert.Single(result["inspectedPanels"]!.Values<JObject>());
        Assert.Equal("inspected_panel", (string?)inspectedRow!["role"]);
        Assert.Equal("panel row", (string?)inspectedRow["nodes"]![0]!["text"]);
    }

    [Fact]
    public void TooltipCyclesFailClosedWithAStableCode()
    {
        var tooltip = new FakeTooltip("Cycle");
        var node = new TooltipNode("cycle") { tooltipable = tooltip };
        tooltip.Nodes.Add(node);

        var result = GameMcpTestHarness.Json(
            GameMcpTooltipProjector.Project(tooltip, null, null));

        var cycle = result["tooltip"]!["nodes"]![0]!["linkedTooltip"]!;
        Assert.Equal("unavailable", (string?)cycle["status"]);
        Assert.Equal("tooltip_cycle", (string?)cycle["reasonCode"]);
        Assert.Null(result["nestedTooltips"]);
        Assert.Null(result["inspectedPanels"]);
        Assert.Null(result["tooltip"]!["altNodes"]);
        Assert.Null(result["tooltip"]!["nodes"]![0]!["children"]);
        Assert.Null(result["tooltip"]!["nodes"]![0]!["subTooltips"]);
        Assert.Null(result["tooltip"]!["nodes"]![0]!["computationReason"]);
    }

    private sealed class FakeTooltip : ITooltipable
    {
        internal FakeTooltip(string name, params TooltipNode[] nodes)
        {
            Name = name;
            Nodes.AddRange(nodes);
        }

        private string Name { get; }
        internal List<TooltipNode> Nodes { get; } = new();
        public string GetName() => Name;
        public string GetDisplayType() => "Fixture";
        public UnityEngine.Sprite GetIcon() => new();
        public UnityEngine.Color GetColor() => UnityEngine.Color.white;
        public bool IsColoredIcon() => false;
        public bool HasAltTooltips() => false;
        public string GetDescription() => Name + " description";
        public List<TooltipNode> GetTooltipNodes() => Nodes;
        public List<TooltipNode> GetAltTooltipNodes() => new();
    }
}
