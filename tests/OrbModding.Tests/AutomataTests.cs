using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using UnityEngine;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataTests
{
    [Fact]
    public void SpellCostReaderUsesTrueMaxQuantityInsteadOfEffectSoftCap()
    {
        var resource = new TestResourceSO
        {
            uuid = "mana",
            name = "Mana",
            quantity = new TestBigDouble(8.0, 5),
            maxQuantity = new TestValueModifierRecord(new TestBigDouble(5.0, 5)),
            trueAmountMultiplier = 2.0,
        };
        var result = Assert.Single(ReflectionCostReader.Read(new[]
        {
            new TestCostEntry(resource, new TestBigDouble(1.0, 3)),
        }));

        Assert.Equal("8e5", result.CurrentQuantity.ToString());
        Assert.Equal("1e6", result.Capacity?.ToString());
    }

    [Fact]
    public void DecisionLogGate_ThrottlesRepeatedStateButLogsTransitions()
    {
        var gate = new DecisionLogGate(TimeSpan.FromSeconds(30));

        Assert.True(gate.ShouldLog("none", TimeSpan.Zero));
        Assert.False(gate.ShouldLog("none", TimeSpan.FromSeconds(1)));
        Assert.True(gate.ShouldLog("candidate-a", TimeSpan.FromSeconds(2)));
        Assert.True(gate.ShouldLog("candidate-a", TimeSpan.FromSeconds(32)));
    }

    [Fact]
    public void DefaultConfiguration_IsReadyForReleaseUse()
    {
        var config = AutomataConfig.Bind(new ConfigFile());

        Assert.Equal(AutoBuyOperationMode.Active, config.AutoBuyMode.Value);
        Assert.Equal(AutoBuyAffordabilityMode.Excess100, config.AutoBuyAffordability.Value);
        Assert.Equal(AutoBuyAffordabilityMode.Excess100, config.UpgradeAffordability.Value);
        Assert.Equal(1024, config.AutoBuyMaxCandidatesPerScan.Value);
        Assert.Equal(AutoBuyBatchSizingMode.FillAvailableQueue, config.AutoBuyBatchSizing.Value);
        Assert.Equal(8, config.MaxPurchasesPerBatch.Value);
        Assert.Equal(AutoBuyStructureRepeatMode.BulkDevelopment, config.StructureRepeatMode.Value);
        Assert.Equal(2, config.FixedStructureLevelsPerCandidate.Value);
        Assert.False(config.RespectActionMultiplier.Value);
        Assert.Equal("0", config.AbsoluteReserve.Value);
        Assert.Equal(0.0f, config.RelativeReserveMultiplier.Value);
        Assert.Equal(AutoCastOperationMode.Disabled, config.AutoCastMode.Value);
        Assert.False(config.EnableOperationalLogging.Value);
        Assert.True(config.CanStartAutoBuyActively);
        Assert.False(config.CanStartAutoCastActively);
    }

    [Fact]
    public void ReservePolicy_RequiresCostPlusTheLargestReserveFloor()
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "100";
        config.RelativeReserveMultiplier.Value = 2.0f;
        var policy = new ReservePolicy(config);

        var accepted = policy.Evaluate(new[]
        {
            new ResourceAdmissionCost("mana", "Mana", new BigAmount(1.0, 1), new BigAmount(1.1, 2)),
        });
        var rejected = policy.Evaluate(new[]
        {
            new ResourceAdmissionCost("mana", "Mana", new BigAmount(1.0, 1), new BigAmount(1.09, 2)),
        });

        Assert.True(accepted.Passed);
        Assert.False(rejected.Passed);
    }

    private sealed class TestCostEntry
    {
        public TestCostEntry(TestResourceSO resource, TestBigDouble amount)
        {
            this.resource = resource;
            this.amount = amount;
        }

        public readonly TestResourceSO resource;
        public readonly TestBigDouble amount;
    }

    private sealed class TestResourceSO : ScriptableObject
    {
        public string uuid = string.Empty;
        public TestBigDouble quantity = new TestBigDouble(0.0, 0);
        public TestValueModifierRecord maxQuantity = new TestValueModifierRecord(new TestBigDouble(-1.0, 0));
        public double trueAmountMultiplier = 1.0;

        public TestBigDouble GetTrueQuantity() => quantity;

        public TestBigDouble GetTrueAmount(TestBigDouble amount) =>
            new TestBigDouble(amount.mantissa * trueAmountMultiplier, amount.exponent);
    }

    private sealed class TestValueModifierRecord
    {
        private readonly TestBigDouble _value;

        public TestValueModifierRecord(TestBigDouble value)
        {
            _value = value;
        }

        public TestBigDouble GetValue() => _value;
    }

    private sealed class TestBigDouble
    {
        public TestBigDouble(double mantissa, long exponent)
        {
            this.mantissa = mantissa;
            this.exponent = exponent;
        }

        public readonly double mantissa;
        public readonly long exponent;
    }
}
