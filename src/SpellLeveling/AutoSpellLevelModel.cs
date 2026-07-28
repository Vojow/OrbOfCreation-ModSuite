namespace OrbAutomata;

/// <summary>
/// What the game currently lets spell leveling do, in increasing order of capability.
/// </summary>
/// <remarks>
/// The UI's vocabulary rather than the worker's. <see cref="Single"/> and <see cref="All"/> are
/// derivable from the world snapshot; <see cref="Locked"/> is not, because it is the leveling
/// prerequisite and that is only reachable through a call capture refuses to make (W59). So the
/// boundary is what fills this in, and <see cref="SpellLevelCapabilityState"/> is where it is kept.
/// </remarks>
internal enum AutoSpellLevelCapability
{
    Locked,
    Single,
    All,
}
