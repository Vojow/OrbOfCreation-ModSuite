using System;
using System.Collections.Generic;
using System.Linq;
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

        Assert.Equal("unity_main_thread", (string?)result["source"]);
        Assert.Null(result["collectorGap"]);
        Assert.Null(result["nodeLimit"]);
        Assert.Null(result["depthLimit"]);
        Assert.Null(result["tooltip"]?["role"]);
        var rootRow = Assert.IsType<JObject>(
            Assert.Single(result["tooltip"]!["nodes"]!.Children()));
        Assert.Equal("parent", (string?)rootRow["kind"]);
        Assert.Null(rootRow["parentKind"]);
        var children = rootRow["children"]!.OfType<JObject>();
        Assert.Collection(
            children,
            authored =>
            {
                var row = Assert.IsType<JObject>(authored);
                Assert.Equal("authored child", (string?)row["text"]);
                Assert.Null(row["textKind"]);
                Assert.Null(row["authoredText"]);
            },
            live =>
            {
                var row = Assert.IsType<JObject>(live);
                Assert.Equal("live value 42", (string?)row["text"]);
                Assert.Null(row["computationStatus"]);
                var reference = (string?)row["linkedTooltip"]?["ref"];
                Assert.False(string.IsNullOrWhiteSpace(reference));
                Assert.Equal("Linked", (string?)result["referencedTooltips"]![reference!]!["name"]);
            });
        Assert.Equal(1, computations);
        var nestedRef = (string?)Assert.Single(result["nestedTooltips"]!.Values<JObject>())!["ref"];
        Assert.Equal("Nested", (string?)result["referencedTooltips"]![nestedRef!]!["name"]);
        var inspectedRef = (string?)Assert.Single(result["inspectedPanels"]!.Values<JObject>())!["ref"];
        var inspectedRow = result["referencedTooltips"]![inspectedRef!];
        Assert.Equal("panel row", (string?)inspectedRow["nodes"]![0]!["text"]);
        Assert.All(
            result.DescendantsAndSelf().OfType<JObject>(),
            row =>
            {
                Assert.Null(row["path"]);
                Assert.Null(row["depth"]);
                Assert.Null(row["ordinal"]);
                Assert.Null(row["color"]);
                Assert.Null(row["textColor"]);
                Assert.Null(row["hasIcon"]);
                Assert.Null(row["iconBacked"]);
                Assert.Null(row["size"]);
            });
    }

    [Fact]
    public void TooltipCyclesSerializeAsStableReferencesWithoutRecursiveCopies()
    {
        var tooltip = new FakeTooltip("Cycle");
        var node = new TooltipNode("cycle") { tooltipable = tooltip };
        tooltip.Nodes.Add(node);

        var result = GameMcpTestHarness.Json(
            GameMcpTooltipProjector.Project(tooltip, null, null));

        var cycle = result["tooltip"]!["nodes"]![0]!["linkedTooltip"]!;
        Assert.Equal("tooltip_1", (string?)cycle["ref"]);
        Assert.Empty(result["nestedTooltips"]!);
        Assert.Empty(result["inspectedPanels"]!);
        Assert.Null(result["referencedTooltips"]);
        Assert.Null(result["truncation"]);
        Assert.Null(result["tooltip"]!["altNodes"]);
        Assert.Empty(result["tooltip"]!["nodes"]![0]!["children"]!);
        Assert.Empty(result["tooltip"]!["nodes"]![0]!["subTooltips"]!);
        Assert.Null(result["tooltip"]!["nodes"]![0]!["reason"]);
    }

    [Fact]
    public void IdenticalAlternateTreeAndRichTextCeremonyAreRemoved()
    {
        var primaryNode = new TooltipNode("<emph>Quantity:</emph>");
        var altNode = new TooltipNode("<emph>Quantity:</emph>");
        var tooltip = new FakeTooltip("Resource", primaryNode)
        {
            DisplayType = "<#BBACE2FF>Essence</color> Resource",
            Description = "<deemph>Spendable List<T> supply.</deemph>",
        };
        tooltip.AltNodes.Add(altNode);

        var result = GameMcpTestHarness.Json(
            GameMcpTooltipProjector.Project(tooltip, null, null));

        Assert.Equal("Essence Resource", (string?)result["tooltip"]!["displayType"]);
        Assert.Equal("Spendable List<T> supply.", (string?)result["tooltip"]!["description"]);
        Assert.Equal("Quantity:", (string?)result["tooltip"]!["nodes"]![0]!["text"]);
        Assert.Null(result["tooltip"]!["altNodes"]);
        Assert.True(result.ToString(Newtonsoft.Json.Formatting.None).Length < 500);
    }

    [Fact]
    public void FortyNodeDuplicateTooltipFitsTheCriticSignalBudget()
    {
        var tooltip = new FakeTooltip("Dense");
        for (var index = 0; index < 40; index++)
        {
            var text = "Fact " + index + ": <emph>1.23e24</emph>";
            tooltip.Nodes.Add(new TooltipNode(text));
            tooltip.AltNodes.Add(new TooltipNode(text));
        }

        var result = GameMcpTestHarness.Json(
            GameMcpTooltipProjector.Project(tooltip, null, null));
        var encoded = result.ToString(Newtonsoft.Json.Formatting.None);

        Assert.Null(result["tooltip"]!["altNodes"]);
        Assert.Equal(40, result["tooltip"]!["nodes"]!.Count());
        Assert.DoesNotContain("<emph>", encoded, StringComparison.Ordinal);
        Assert.True(encoded.Length <= 3_412, "compact tooltip was " + encoded.Length + " characters");
    }

    private sealed class FakeTooltip : ITooltipable
    {
        internal FakeTooltip(string name, params TooltipNode[] nodes)
        {
            Name = name;
            Nodes.AddRange(nodes);
        }

        private string Name { get; }
        internal string DisplayType { get; set; } = "Fixture";
        internal string? Description { get; set; }
        internal List<TooltipNode> Nodes { get; } = new();
        internal List<TooltipNode> AltNodes { get; } = new();
        public string GetName() => Name;
        public string GetDisplayType() => DisplayType;
        public UnityEngine.Sprite GetIcon() => new();
        public UnityEngine.Color GetColor() => UnityEngine.Color.white;
        public bool IsColoredIcon() => false;
        public bool HasAltTooltips() => AltNodes.Count > 0;
        public string GetDescription() => Description ?? Name + " description";
        public List<TooltipNode> GetTooltipNodes() => Nodes;
        public List<TooltipNode> GetAltTooltipNodes() => AltNodes;
    }
}
