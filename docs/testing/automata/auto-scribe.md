# Auto Scribe testing

[Automata test map](README.md) ·
[Native pipeline](../../reverse-engineering/auto-scribe-native-pipeline.md)

Auto Scribe portable coverage is under
`tests/OrbModding.Tests/Services/AutoScribe`.

Run the focused scope with:

```sh
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj \
  -p:UseGameStubs=true --filter "FullyQualifiedName~AutoScribe"
```

Required planning and lifecycle coverage includes fair semantic cost-rank rotation, per-Scroll
progression frontiers, shared-ceiling isolation, next-level affordability probes that remain
suppressed while owned frontier supply exists, and capacity-replacement blocking while any queued
quantity, active preparation, unexpired use, or manual/automatic Scribe work exists at any level,
native carry-cap suppression of futile same-level demand while preserving stronger replacement,
highest-affordable bounded search, cycle-pinned role narrowing, whole-publication blocking when one
enabled role has unknown evidence, player-owned automatic production pressure, exact recipe/Scroll
mismatch, queue-full and no-target preflights, post-permit live revalidation, exact queue and
instant-stock verification,
lifecycle quarantine reset, and post-payment injected failure at payment, construction, initiation,
and final admission. Runtime-health coverage also requires a native `QueueFull` failure to outrank
an older evidence decision, and an impossible `EvidenceBlocked/None` projection to report an
invariant violation rather than claim complete evidence. Advisory actions must continue to contain copied Unity-free values only; raw
resource objects and spend modifiers belong exclusively to the main-thread GameAction.

Required raw-spend receipt cases are:

- ordinary spending at 100% quality;
- non-default quality converted through `GetTrueSpend`;
- active decay reducing the immediate raw debit;
- replenish or reverberation without an immediate debit change;
- quality changing after payment without changing the captured raw-debit expectation;
- a positive debit below `BigDouble` resolution producing the exact unchanged native post-state;
- duplicate rows with sufficient aggregate raw balance;
- duplicate rows reproduced in authored order when sequential rounding differs from aggregating
  their debit;
- duplicate rows redistributed after payment with the same aggregate cost, rejected as a changed
  row shape;
- duplicate rows that are individually affordable but aggregate-insufficient, rejected before
  payment;
- injected zero, partial, and excessive debits, all quarantined;
- cost rows changing during execution, failed closed;
- bandwidth-resource refusal before payment;
- preservation of the first fault across repeated quarantined submissions, without fault-revision
  or warning spam; and
- lifecycle replacement clearing quarantine and permitting a fresh binding set.

The stubs must reproduce native row-by-row `ResourceCostList.HasEnough()` and
`ResourceSO.Spend()` behavior, including quality conversion, decay, and zero clamping.
`PurchaseQuantity` must advance `maxStartingLevel`; a test that only counts method calls does not
prove this boundary.

Run the complete portable gate after the focused scope. The Scribe world reader and GameAction
binding set also require the portable contract gate and the installed contract project against the
accepted game baseline. The installed binding assertions include `ResourceSO.GetQuantity()`,
`GetTrueSpend(BigDouble)`, `HasDecay()`, `GetDecayPercent()`, and `IsBandwidthResource()`.
They also cover `ConsumableSO.GetQueued()`, `currentPrepTime`, `consumableUsages`, and the exact
`ConsumableUsage.en`/`dr` expiry evidence used by the replacement interlock.
Portable target fakes inject the candidate set and therefore do not prove the game's complete
structure condition, enchantment ranking, durable Scroll effect, native spend behavior, or
lifecycle behavior inside Unity. Those remain runtime-validation evidence.
