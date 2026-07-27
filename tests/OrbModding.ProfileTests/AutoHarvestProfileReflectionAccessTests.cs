using OrbAutomata;
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;
using static OrbModding.ProfileTests.AutoHarvestProfileTestSupport;

namespace OrbModding.ProfileTests;

public sealed class AutoHarvestProfileReflectionAccessTests
{
    [Fact]
    public void CountsCallsAndOnlyNonemptyArgumentArrays()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutomataProfileOperations(probe);
        var context = ActionContext(serviceOrdinal: 0, frameIdentity: 1);
        var target = new ReflectionTarget();
        var field = typeof(ReflectionTarget).GetField(nameof(ReflectionTarget.Value))!;
        var visible = typeof(ReflectionTarget).GetMethod(nameof(ReflectionTarget.IsVisible))!;
        var add = typeof(ReflectionTarget).GetMethod(nameof(ReflectionTarget.Add))!;
        var stage = operations.Begin(
            ServiceCycleProfileSpan.AutoHarvestBindingAndCoherence,
            context,
            ServiceCycleProfileTemperature.Warm);
        try
        {
            Assert.Equal(4, AutoHarvestReflectionAccess.GetValue(field, target, operations));
            Assert.True(AutoHarvestReflectionAccess.InvokeBool(visible, target, operations));
            Assert.Equal(
                7,
                AutoHarvestReflectionAccess.InvokeInt(
                    add,
                    target,
                    operations,
                    new object[] { 3 }));
            stage.Complete();
        }
        finally
        {
            stage.Abandon();
        }

        var captured = Assert.Single(measurement.Completed);
        Assert.Equal((uint)1, captured.Operations.ReflectedFieldReads);
        Assert.Equal((uint)2, captured.Operations.ReflectedMethodCalls);
        Assert.Equal((uint)1, captured.Operations.InvocationArgumentArrays);
        Assert.Same(measurement, probe.Detach());
    }

    private sealed class ReflectionTarget
    {
        public int Value = 4;
        public bool IsVisible() => true;
        public int Add(int value) => Value + value;
    }
}
