# Plans and lifecycle status

[Back to documentation](../README.md) · [Project roadmap](roadmap.md)

These documents record design intent, implementation sequencing, or historical decisions. They are not promises of released behavior.

| Plan | Status | Notes |
|---|---|---|
| [Project roadmap](roadmap.md) | Active | Portfolio-level direction and sequencing. |
| [Orb Automata](automata.md) | Implemented / evolving | Auto Buy, Auto Cast, Auto Concept, and progression-aware spell leveling are in public beta. |
| [Auto Buy rejection-aware scheduler](autobuy-rejection-index.md) | Structure threshold parking implemented / runtime gate pending | Structure reserve/affordability waits use exact quantity crossings; conservative Upgrade handling, unavailable-resource backoff, and Steam Deck profiling remain. |
| [Shared queue-capacity snapshots](queue-capacity.md) | Implemented / runtime validation pending | Centralized native capacity, occupancy, automation allocation, and manual reservation arithmetic is adopted by Auto Buy; an interactive capacity-change probe remains. |
| [Auto Concept mastery balancing](auto-concept.md) | Beta / runtime validation pending | Disabled-by-default catch-up or timed concept rotation is released through native mutation paths; post-release Proton profiling remains. |
| [Mod suite performance](performance-suite.md) | P0-P3 implemented / runtime validation pending | Shared scheduling, lifecycle-aware indexes, dirty updates, and resource snapshots are implemented; post-release Steam Deck profiling remains. |
| [Auto Cast MVP](auto-cast-mvp.md) | Implemented | Historical MVP contract; current behavior lives in the mod reference. |
| [Orb Mod Config](mod-config-ui.md) | Implemented / evolving | Optional configuration UI supports staged typed editing and compound feature dependencies; interactive validation of the unified locking pass remains. |
| [Orb Mentor](mentor.md) | Beta / runtime validation pending | Equipped-source and highest-only spell policies are released; extended interactive validation remains. |
| [Mentor artifacts and alchemy](mentor-artifacts-alchemy.md) | Beta / runtime validation pending | Independent, disabled-by-default domains are released; interactive native-progression and performance gates remain. |
| [Orb Insights](insights.md) | Planned | Design only. |
| [Orb Toolbox](toolbox.md) | Planned | Design only. |

Each plan begins with a lifecycle label. Update that label and this table when implementation status changes.
