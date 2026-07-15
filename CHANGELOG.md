# Changelog

## Orb Of Creation Mod Suite 0.1.0 Beta 1 — 2026-07-15

- Added the complete suite package with Orb Automata, Orb Mentor, Orb Mod Config, Orb Achievement Resonance, Orb Chronomancer, and Orb Modding Common.
- Bundled Orb Mod Config 0.5.1.
- Made the Mods configuration tab available from the start of a new save instead of requiring the NG+-gated Time tab.
- Kept Mods as the final item when native navigation tabs unlock or reorder.

- Default fresh Auto Cast configurations to a 0% resource-fullness threshold while retaining affordability and reserve checks.
- Write release ZIP entries with portable `/` separators and validate their layout for Linux, SteamOS, and Bazzite extraction.
- Add Orb Mod Config 0.5.0 with feature-oriented tabs, contextual labels, hidden compatibility switches, dependency-aware controls, apply indicators, and optional Steam Deck keyboard input.
- Add the Orb Mentor 0.1.0 spells-only MVP with native mastery grants, guarded recursion suppression, Shared Pool and Per Recipient economies, bounded frame processing, `Alt+M`, status control, live typed configuration, installed-game contracts, portable tests, and packaging support.
- Extend opt-in sharing to created artifacts and available alchemy recipes, using separate domain pools and native grant paths.
- Prevent continuously replenished artifact and alchemy batches from starving later recipients by preserving FIFO pending order.
- Add cohesive Mentor, Auto Buy, and Auto Cast status controls with native hover tooltips.
- Keep the logging probe development-only and fresh installations disabled.

All notable user-facing changes are documented here. The project follows semantic versioning per plugin while the suite remains in beta.

- Public-repository documentation, contribution guidance, CI, and release hygiene.

## Orb Automata 0.4.0

- Removed DryRun, runtime-probe, expert-override, per-session purchase-limit, and deprecated Auto Research settings from the release UI and generated configuration.
- Defaulted Auto Buy to Active and Auto Cast to Disabled.
- Added separate structure and upgrade affordability policies.
- Added optional action-multiplier handling capped to available queue room with per-level resource and reserve validation.
- Changed fresh-install reserves to zero so affordability modes are the default spending margin.
- Continued CPU-sliced scans and prepared queue batches every frame, removing the evaluation-interval gap while work is pending.
- Kept normal operational logs opt-in while retaining startup, warning, and error records.
- Isolated stub-linked test output from deployable Release binaries.

## Orb Automata 0.3.5 Beta 1 — 2026-07-14

- Published queue-aware Auto Buy for native structures and upgrades.
- Added Auto Cast rotation, resource thresholds, targeting, aura/channel handling, keyboard control, and a queue-adjacent status button.
- Included Orb Mod Config 0.4.0 and Orb Modding Common in the recommended archive.
