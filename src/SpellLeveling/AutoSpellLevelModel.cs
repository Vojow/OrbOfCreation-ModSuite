namespace OrbAutomata;

internal enum AutoSpellLevelCapability
{
    Locked,
    Single,
    All,
}

internal sealed class NativeSpellLevelCandidate
{
    public NativeSpellLevelCandidate(string uuid, string displayName, object recipe, int masteryLevel)
    {
        Uuid = uuid;
        DisplayName = displayName;
        Recipe = recipe;
        MasteryLevel = masteryLevel;
    }

    public string Uuid { get; }
    public string DisplayName { get; }
    public object Recipe { get; }
    public int MasteryLevel { get; }
}

internal readonly struct AutoSpellLevelSnapshot
{
    public AutoSpellLevelSnapshot(AutoSpellLevelCapability capability, NativeSpellLevelCandidate? candidate)
    {
        Capability = capability;
        Candidate = candidate;
    }

    public AutoSpellLevelCapability Capability { get; }
    public NativeSpellLevelCandidate? Candidate { get; }
}
