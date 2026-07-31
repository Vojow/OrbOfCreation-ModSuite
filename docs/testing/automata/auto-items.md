# Auto Items testing

[Automata test map](README.md) ·
[Native pipeline](../../reverse-engineering/auto-items-native-pipeline.md)

Auto Items portable coverage is under
`tests/OrbModding.Tests/Services/AutoItems/Runtime/ServiceCycle`.

Run the focused scope with:

```sh
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj \
  -p:UseGameStubs=true --filter "FullyQualifiedName~AutoItems"
```

The required defect cases are live family change, native busy, lost ownership permit, manual stock
race, empty Scroll target selection, ambiguous postcondition quarantine, lifecycle reset, direct
action-adapter result mapping, one-attempt publication planning, and exact health explanations.
Temporary-item coverage additionally requires exact and near-miss allowlists, visibility/stock/
preparation/cooldown/duration/cost/Toxicity guard failures, stock/queue/usage/Toxicity mutation
evidence, mutual exclusion in both directions, exact-item mutation quarantine, and publication
injections for double usage, premature expiry, and missing engagement evidence. Cross-feature
action-family tests prove that committed master disable releases consumable ownership even when
Auto Buy keeps the shared multi-buy lease.

Picker coverage additionally requires discovered-only family/name/stock/icon enumeration and
ordering, exact staged serialization through Apply, an always-visible approval count, removable
unresolvable stored entries, visually distinct healthy-empty and discovery-failure states, and a
composition assertion that the allowlist has no text input. Filters, family toggles, select-all,
blacklists, and raw editing are forbidden regressions.

Run the complete portable gate after the focused scope. Any reflected member or exact native type
change also requires both the portable contract project and the installed contract project against
the accepted game baseline.

Portable target fakes preserve the exact authored object shape but inject the target candidate list.
They do not prove the game's complete structure-eligibility calculation, eventual random choice,
preparation completion, or any durable consumable effect. Those remain runtime-validation evidence
and must not be inferred from a passing portable or metadata gate.
