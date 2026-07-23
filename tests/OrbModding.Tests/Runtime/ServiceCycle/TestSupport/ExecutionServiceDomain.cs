namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal sealed class ExecutionFrame
{
    public int Value { get; internal set; }
}

internal sealed class ExecutionConfig
{
    internal ExecutionConfig(int value, ActionPayload? payload = null)
    {
        Value = value;
        Payload = payload;
    }

    public int Value { get; }
    public ActionPayload? Payload { get; }
}

internal sealed class ExecutionState
{
    internal int Evaluations;
}

internal sealed class ActionPayload
{
    internal ActionPayload(int value) => Value = value;
    public int Value { get; }
}

internal readonly struct ExecutionAction
{
    internal ExecutionAction(int value, ActionPayload payload)
    {
        Value = value;
        Payload = payload;
    }

    public int Value { get; }
    public ActionPayload Payload { get; }
}
