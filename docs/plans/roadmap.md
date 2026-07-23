# Project roadmap

> **Lifecycle: Active.** This file lists future product work only. Current behavior is documented by each module and the runtime architecture dossier.

[Back to plans](README.md) · [Project overview](../../README.md)

## Current direction

Orb Automata, Orb Mentor, Orb Mod Config, and Orb Modding Common form the supported suite. Native game APIs remain authoritative for progression, queues, saves, availability, costs, and final mutation validation.

The next foundational change is the [Auto Buy ServiceCycle port](autobuy-service-cycle-port.md). It will reuse the neutral host already proven by Auto Harvest, move calculations into native-free worker policy, and delete the legacy Auto Buy scheduler once parity is established.

After Auto Buy is migrated:

1. compare Auto Buy and Auto Harvest traces to identify measured runtime costs;
2. address only material findings under the [performance plan](performance-suite.md);
3. complete combined-suite runtime and package validation; and
4. prepare a reviewed beta release from the supported plugin allowlist.

## Later modules

- [Orb Insights](insights.md) may add read-only gameplay explanations and diagnostics.
- [Orb Toolbox](toolbox.md) may add explicit, reversible advanced player operations.

New automation features should use the shared lifecycle, ownership, diagnostics, and ServiceCycle contracts instead of introducing another scheduler.
