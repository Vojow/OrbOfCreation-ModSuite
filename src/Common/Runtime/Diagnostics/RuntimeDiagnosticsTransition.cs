namespace OrbModding.Common.Runtime;

public enum RuntimeDiagnosticsTransitionKind
{
    Added = 0,
    Changed = 1,
    Removed = 2,
}

public readonly struct RuntimeDiagnosticsTransition
{
    internal RuntimeDiagnosticsTransition(
        RuntimeDiagnosticsTransitionKind kind,
        RuntimeServiceDiagnosticsSnapshot? previous,
        RuntimeServiceDiagnosticsSnapshot? current,
        long revision)
    {
        Kind = kind;
        Previous = previous;
        Current = current;
        Revision = revision;
    }

    public RuntimeDiagnosticsTransitionKind Kind { get; }
    public RuntimeServiceDiagnosticsSnapshot? Previous { get; }
    public RuntimeServiceDiagnosticsSnapshot? Current { get; }
    public FeatureStatusKey Key => Current?.Key ?? Previous!.Key;
    public long Revision { get; }
}
