# Native mutation postcondition verification

> **Lifecycle: Implemented for the next beta; interactive validation pending.** This foundation covers every active Automata gameplay mutation and every Mentor XP grant. It is not part of the frozen release candidate until the next-beta branch is validated and promoted.

[Back to plans](README.md) · [Runtime validation](../development/runtime-validation.md)

## Goal

Native methods returning without an exception are not sufficient evidence that the game accepted the intended mutation. Every active mutation follows one synchronous boundary:

1. Capture authoritative native state immediately before execution.
2. Execute exactly one bounded native mutation.
3. Capture the same authoritative state immediately afterward, including after an exception when possible.
4. Verify the family-specific expected change.
5. Accept success only after the postcondition passes.

## Authoritative evidence

| Mutation family | Identity | Before/after state | Accepted postcondition |
|---|---|---|---|
| Auto Buy Structure/Upgrade | Stable native UUID and exact type | Native queued quantity or queued purchase level | Exact `+1` |
| Auto Concept add/remove | Stable recipe UUID and exact `AlchemyRecipeSO` | Active instance queued quantity; absent instance is zero | Exact requested positive or negative delta |
| Single spell level | Stable spell UUID and exact recipe | Native mastery level | Exact `+1` after cost and purchase |
| Native level-all | Stable ready spell UUID | Sum of native mastery levels | Positive delta; the native action owns how many affordable levels it completes |
| Auto Cast | Stable spell UUID, native object, type, and slot | Audited `Spell.Fire` hook epoch | Exact `+1`; transient `IsCasting` is not universal evidence |
| Mentor spell XP | Stable spell UUID and exact recipe | `SpellRecipeSO.masteryExperience` | Expected numeric XP delta |
| Mentor alchemy XP | Stable recipe UUID, exact type, and ordinary-domain proof | `AlchemyRecipeSO.masteryXp` | Expected numeric XP delta |
| Mentor artifact XP | Stable equipment UUID and exact type | Native experience-container value | Expected numeric XP delta |

## Failure and recovery policy

Evidence records feature, identity, expected change, outcome, before state, after state when available, and diagnostic detail. No-op, partial, unexpectedly large, after-capture failure, or execution exception after admission is ambiguous and cannot be retried by the ordinary scheduler.

- Auto Buy blocks the candidate.
- Auto Cast blocks the spell UUID.
- Auto Concept and spell leveling block their feature runtime.
- Mentor cancels pending bonus work and blocks the affected domain.

Recovery is explicit and lifecycle-scoped: scene transition, save load, reset, or NG+ invalidation clears transient mutation blocks and forces native identity/state reconciliation. Configuration polling and normal evaluation do not clear a block. A future manual retry control may call the same explicit recovery boundary, but no automatic timer is allowed to do so.

## Verification pyramid

- Contract unit tests cover success, no-op, partial change, unexpectedly large change, exception after mutation, and capture failure.
- Headless integration tests prove Auto Buy, Auto Cast, Auto Concept, spell leveling, and Mentor suppress immediate repeats and recover only after their explicit lifecycle boundary.
- Installed-game contract tests validate every reflected field, method, type, and hook against the current game assemblies.
- Computer-controlled UAT validates visible queue growth, Concept assignment settlement, spell behavior, and XP changes in a disposable save. UAT does not replace the headless gates.
