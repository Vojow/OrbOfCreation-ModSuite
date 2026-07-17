using System.Collections.Generic;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests;

public sealed class NativeStructurePriorityClassifierTests
{
    [Theory]
    [InlineData(ResourceProperty.Quality, 1.2, 2)]
    [InlineData(ResourceProperty.AttributeCostMod, 0.8, 1)]
    [InlineData(ResourceProperty.Quality, 0.8, 0)]
    [InlineData(ResourceProperty.AttributeCostMod, 1.2, 0)]
    public void ResourceEffect_UsesNativePreviewDirection(
        ResourceProperty property,
        double adjustedOne,
        int expected)
    {
        var structure = new PriorityStructure();
        structure.structureProperties.Add(new PriorityProperty
        {
            resourceEffects =
            {
                new ResourceEffect
                {
                    resource = new PriorityResource(),
                    upgradeType = property,
                    modifier = new ValueModifier(adjustedOne),
                },
            },
        });

        Assert.Equal((AutoBuyEconomicPriority)expected, NativeStructurePriorityClassifier.Classify(structure));
    }

    [Theory]
    [InlineData("Cost", 0.9, false, 1)]
    [InlineData("CostScaling", 0.9, false, 1)]
    [InlineData("Cost", 1.1, false, 0)]
    [InlineData("Cost", 0.9, true, 0)]
    [InlineData("Power", 0.9, false, 0)]
    public void StructureTargetEffect_RequiresExactAuditedContract(
        string propertyType,
        double adjustedOne,
        bool useTargetReference,
        int expected)
    {
        var structure = new PriorityStructure();
        structure.structureProperties.Add(new PriorityProperty
        {
            upgradeableObjectEffects =
            {
                new UpgradeableEffect
                {
                    upgradeableObject = new PriorityStructure(),
                    propertyType = propertyType,
                    modifier = new ValueModifier(adjustedOne),
                    useTargetRef = useTargetReference,
                },
            },
        });

        Assert.Equal((AutoBuyEconomicPriority)expected, NativeStructurePriorityClassifier.Classify(structure));
    }

    private sealed class PriorityStructure : StructureSO
    {
        public readonly List<PriorityProperty> structureProperties = new List<PriorityProperty>();
    }

    private sealed class PriorityResource : ResourceSO
    {
    }

    private sealed class PriorityProperty
    {
        public readonly List<ResourceEffect> resourceEffects = new List<ResourceEffect>();
        public readonly List<UpgradeableEffect> upgradeableObjectEffects = new List<UpgradeableEffect>();
    }

    private sealed class ResourceEffect
    {
        public object resource = null!;
        public ResourceProperty upgradeType;
        public object modifier = null!;
    }

    private sealed class UpgradeableEffect
    {
        public object upgradeableObject = null!;
        public string propertyType = string.Empty;
        public object modifier = null!;
        public bool useTargetRef;
    }

    public enum ResourceProperty
    {
        Quality,
        AttributeCostMod,
    }

    private sealed class ValueModifier
    {
        private readonly double _adjustedOne;

        public ValueModifier(double adjustedOne)
        {
            _adjustedOne = adjustedOne;
        }

        public BigDouble Adjust(BigDouble value) => new BigDouble(value.mantissa * _adjustedOne, value.exponent);
    }

    private readonly struct BigDouble
    {
        public static readonly BigDouble One = new BigDouble(1.0, 0);

        public BigDouble(double mantissa, long exponent)
        {
            this.mantissa = mantissa;
            this.exponent = exponent;
        }

        public readonly double mantissa;
        public readonly long exponent;
    }
}
