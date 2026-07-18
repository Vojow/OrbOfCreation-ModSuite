using System;

namespace OrbModding.Common;

public enum EvidenceLevel
{
    Unresolved = 0,
    Inferred = 1,
    RuntimeObserved = 2,
    SerializedAssetVerified = 3,
    StaticallyVerified = 4,
}

[Flags]
public enum EvidenceSource
{
    None = 0,
    StaticContract = 1 << 0,
    SerializedAsset = 1 << 1,
    RuntimeNativeType = 1 << 2,
    StableIdentity = 1 << 3,
    RuntimeRegistry = 1 << 4,
    NativeRelationship = 1 << 5,
}

public readonly struct EvidenceAssessment
{
    public EvidenceAssessment(
        EvidenceLevel level,
        EvidenceSource sources,
        bool isContradictory = false)
    {
        Level = isContradictory ? EvidenceLevel.Unresolved : level;
        Sources = sources;
        IsContradictory = isContradictory;
    }

    public EvidenceLevel Level { get; }
    public EvidenceSource Sources { get; }
    public bool IsContradictory { get; }
    public bool IsResolved => Level != EvidenceLevel.Unresolved && !IsContradictory;

    public bool Meets(EvidenceLevel minimum, EvidenceSource requiredSources = EvidenceSource.None) =>
        !IsContradictory &&
        Level >= minimum &&
        (Sources & requiredSources) == requiredSources;

    public static EvidenceAssessment Unresolved(EvidenceSource sources = EvidenceSource.None) =>
        new(EvidenceLevel.Unresolved, sources);

    public static EvidenceAssessment Contradictory(EvidenceSource sources) =>
        new(EvidenceLevel.Unresolved, sources, isContradictory: true);
}
