# Orb Of Creation ModSuite agent guidance

## Repository purpose

This repository builds BepInEx 5 mods for Orb Of Creation. Runtime behavior must preserve native game progression, action queues, saves, and player control. Prefer audited native APIs and fail closed when a game contract is unknown.

## Start every task with current facts

- Inspect `git status --short`, the current branch, and the recent log before editing.
- Preserve all pre-existing user changes and keep unrelated changes out of the task.
- Read any nested `AGENTS.md` in a target subtree before editing files there.
- Read the nearest applicable plan and module README before changing behavior.
- Treat `docs/plans/README.md` as the lifecycle index and `CHANGELOG.md` as the user-visible change record.
- Do not rely on conversation memory for release contents, versions, test results, or installed DLLs; verify them from the repository and artifacts.

## Product and release boundaries

- The supported suite package is an explicit allowlist, never "every project on main."
- `OrbChronomancer` and `OrbAchievementResonance` live only on `codex/experimental-chronomancer-resonance` until their lifecycle is explicitly promoted and the user approves their release scope.
- Experimental DLLs must never enter a supported suite archive accidentally.
- Build success is not permission to install DLLs into the game. Install only when the user explicitly asks, then verify source/destination hashes.
- A release request requires a release review before publication: exact commit, tag, versions, plugin allowlist, archive entries, test evidence, and prerelease/stable status.
- Replacing or deleting an existing public release or tag requires explicit user authorization naming the target.
- Only the main agent may stage, commit, push, install into the game, create tags, or mutate GitHub releases unless the user explicitly delegates that action.

## Runtime safety

- Never edit an active save file.
- Unity objects and game APIs stay on the Unity main thread unless an audited contract proves otherwise.
- Use stable UUID plus expected native type for identity; names are diagnostics only.
- Registry presence is not player availability. Content can be locked initially, unlock later, register later, queue, complete, reset, or change across NG+ and save loads.
- `IsAvailable() == false` is not completion evidence.
- Keep the game authoritative for availability, cost, quantity, queue room, completion, and final mutation validation.
- Preserve native multi-buy and other global state across every code path, including exceptions.
- Concurrent overlapping automation plugins are unsupported; do not add coexistence behavior unless explicitly scoped.

## Performance rules

- Follow `docs/plans/performance-suite.md` for suite-wide optimization work.
- Do not put `Resources.FindObjectsOfTypeAll`, complete registry rebuilds, reflection discovery, or full recommendation sorts in a per-frame path.
- Cache stable definitions separately from lifecycle-bound native references and live values.
- Bound work by operation count and measured CPU time; make long work resumable.
- Disabled modules must not scan or rebuild catalogs in the background.
- Avoid hot-path LINQ, temporary arrays, repeated string formatting, and per-action logging.
- Optimization must not drop Mentor XP, skip reserve checks, purchase completed upgrades, or permanently omit locked content.

## Verification

Portable tests:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true
```

Installed-game contracts and real-reference builds:

```powershell
tools/test-modsuite.ps1 -GameRoot "C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation"
```

Supported suite package rehearsal:

```powershell
tools/package-mentor.ps1 -GameRoot "C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation" -IncludeSupportedSuite
```

- Run verification proportional to the change. Documentation-only changes require at least `git diff --check` and link/path inspection.
- Do not report a test as passing unless it ran against the current working tree or commit.
- Runtime behavior still requires the appropriate interactive validation gate in `docs/development/runtime-validation.md`.

## Documentation and versions

- Update behavior documentation with the implementation; plans must not be presented as released behavior.
- If packaged runtime behavior changes, keep the project version, `PluginIds`, module README, root README version table, and changelog consistent.
- Update lifecycle labels and `docs/plans/README.md` together when promoting a module.
- Record unresolved assumptions explicitly rather than silently treating them as verified game behavior.

## Delegation policy

Use subagents only when independent read-heavy work will reduce context noise or materially improve review quality. Do not delegate tightly coupled single-file changes.

- `context_keeper`: summarize branch, diff, plans, versions, release state, and unresolved decisions.
- `performance_auditor`: inspect hot paths, lifecycle correctness, allocations, and the performance plan.
- `runtime_contract_auditor`: verify reflected and patched game contracts against installed assemblies.
- `test_verifier`: run bounded tests/builds and return exact evidence without editing tracked source.
- `release_guardian`: perform a read-only go/no-go review of a proposed package or release.

For parallel work, assign non-overlapping bounded tasks, use at most three subagents at once, wait for all requested results, and have the main agent integrate conclusions. Prefer read-only agents. Never let multiple agents edit the same files concurrently.
