namespace OrbModding.RuntimeReplay;

public sealed record ReplayIdentity(string Uuid, string ExpectedNativeType);

public sealed record ReplayCandidate(
    ReplayIdentity Identity,
    decimal BaseCost,
    decimal CostScaling,
    bool Available,
    int? MaximumLevel);

public sealed record ReplayResource(
    ReplayIdentity Identity,
    decimal InitialQuantity);

public sealed record ReplaySetup(
    int QueueCapacity,
    ReplayResource PrimaryResource,
    IReadOnlyList<ReplayCandidate> Candidates);

public sealed record RuntimeReplay(
    string Schema,
    int SchemaVersion,
    string ReplayId,
    ReplaySetup Setup,
    IReadOnlyList<ReplayEvent> Events)
{
    public const string SchemaIdentifier = "orb-of-creation/runtime-replay";
    public const int CurrentSchemaVersion = 1;
    public const long MaximumFrame = 100_000;
    public const long MaximumMicroseconds = 86_400_000_000;
}

public abstract record ReplayEvent(int Sequence, long AtFrame, long AtMicroseconds)
{
    public abstract string Kind { get; }
}

public sealed record LifecycleReplayEvent(
    int Sequence,
    long AtFrame,
    long AtMicroseconds,
    string Transition,
    string SceneName,
    string NativeIdentityToken) : ReplayEvent(Sequence, AtFrame, AtMicroseconds)
{
    public override string Kind => "lifecycle";
}

public sealed record ResourceReplayEvent(
    int Sequence,
    long AtFrame,
    long AtMicroseconds,
    ReplayIdentity Identity,
    decimal Quantity) : ReplayEvent(Sequence, AtFrame, AtMicroseconds)
{
    public override string Kind => "resource";
}

public sealed record QueueReplayEvent(
    int Sequence,
    long AtFrame,
    long AtMicroseconds,
    int ManualActions) : ReplayEvent(Sequence, AtFrame, AtMicroseconds)
{
    public override string Kind => "queue";
}

public sealed record ProgressionReplayEvent(
    int Sequence,
    long AtFrame,
    long AtMicroseconds,
    ReplayIdentity Identity,
    bool Available) : ReplayEvent(Sequence, AtFrame, AtMicroseconds)
{
    public override string Kind => "progression";
}

public sealed record InventoryReplayEvent(
    int Sequence,
    long AtFrame,
    long AtMicroseconds,
    ReplayIdentity Identity,
    int Quantity) : ReplayEvent(Sequence, AtFrame, AtMicroseconds)
{
    public override string Kind => "inventory";
}

public sealed record ConfigurationReplayEvent(
    int Sequence,
    long AtFrame,
    long AtMicroseconds,
    string Setting,
    bool Enabled) : ReplayEvent(Sequence, AtFrame, AtMicroseconds)
{
    public override string Kind => "configuration";
}

public sealed record CompletionReplayEvent(
    int Sequence,
    long AtFrame,
    long AtMicroseconds,
    ReplayIdentity Identity,
    int Count) : ReplayEvent(Sequence, AtFrame, AtMicroseconds)
{
    public override string Kind => "completion";
}

public sealed class ReplayFormatException : FormatException
{
    public ReplayFormatException(string message) : base(message)
    {
    }
}
