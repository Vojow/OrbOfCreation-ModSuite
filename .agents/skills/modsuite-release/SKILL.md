---
name: modsuite-release
description: Use when preparing, packaging, rehearsing, tagging, or publishing an Orb Of Creation ModSuite release, suite archive, or GitHub release, or when reviewing release readiness or version consistency. Covers the supported-plugin allowlist, package invariants, checksums, and go/no-go review.
---

# ModSuite release workflow

Releases are rare and high-stakes. Follow the checklist docs in order; do not
improvise scope.

## Non-negotiable rails

- The supported suite is an explicit allowlist, never "every project that
  builds on main."
- `OrbChronomancer.dll` and `OrbAchievementResonance.dll` are forbidden in any
  supported archive until their lifecycle is explicitly promoted and the user
  approves the release scope by name.
- Tagging, publishing, replacing, or deleting a release or tag requires the
  user's explicit authorization naming the target. Package creation and
  publication are separate actions.
- Build success is not publication approval, and installing DLLs into a game
  is a separate action the user must explicitly request.
- Confirm the archive is built from the intended clean commit, and record
  versions, hashes, and evidence.

## Procedure

1. Read `docs/development/releases.md` (candidate review, package gate,
   runtime gate, publication).
2. Enforce the packaging invariants in `tools/AGENTS.md`: explicit allowlist,
   portable archive entries, output only under `artifacts/`, SHA-256
   checksums, and no push/tag/publish by tooling agents.
3. Runtime evidence follows the applicable gates in
   `docs/testing/runtime-validation.md`.
4. For a go/no-go review of a proposed package or release, use the
   `release_guardian` agent if available, or review manually against the
   candidate-review list in `docs/development/releases.md`.

## Version consistency

When packaged runtime behavior changes, keep the project version, `PluginIds`,
module README, root README version table, and `CHANGELOG.md` consistent. Do not
bump versions outside an actual release task.
