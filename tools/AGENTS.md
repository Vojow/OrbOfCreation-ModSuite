# Tooling and release agent guidance

This file supplements the repository-root `AGENTS.md` for scripts under `tools/`.

## Packaging invariants

- Public archives use an explicit supported-plugin allowlist.
- `OrbChronomancer.dll` and `OrbAchievementResonance.dll` are forbidden until separately promoted and explicitly approved for the target release.
- Package creation must fail when a forbidden or unexpected DLL appears.
- Never infer package scope from projects that happen to build or exist on `main`.
- Keep archives portable: relative forward-slash paths, no rooted entries, and expected `BepInEx/plugins/` destinations.
- Output release rehearsals only under `artifacts/`.
- Generate and verify SHA-256 checksums.

## Release verification

- Run the complete validation pipeline before packaging.
- Inspect raw archive entries and list the exact included plugin DLLs.
- Verify package versions against project files and `PluginIds`.
- Confirm the archive was built from the intended commit and a clean tracked working tree.
- Treat package creation and GitHub publication as separate actions.
- Tooling agents may build and inspect local artifacts but must not push, tag, publish, replace, or delete a release.

