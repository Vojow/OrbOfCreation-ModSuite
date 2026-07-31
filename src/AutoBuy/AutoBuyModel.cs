using System;

namespace OrbAutomata;

internal enum AutoBuyCandidateKind
{
    Structure,
    Upgrade
}

[Flags]
internal enum AutoBuyCandidateKinds
{
    None = 0,
    Structures = 1 << 0,
    Upgrades = 1 << 1,
    All = Structures | Upgrades,
}
