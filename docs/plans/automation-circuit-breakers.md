# Bounded automation circuit breakers

> **Lifecycle: Next beta / runtime validation pending.** Common provides the bounded state machine; Auto Buy resource/cost candidates and Mentor domains use it. Desktop and Steam Deck soak evidence remains a release gate.

[Back to plan index](README.md) · [Performance architecture](performance-suite.md) · [Runtime validation](../development/runtime-validation.md)

## Purpose

Repeated native read, contract, or mutation failures must not consume every scheduler turn or flood diagnostics. Recovery must remain deterministic and must never assume that an attempted native mutation was a no-op.

## Contract

Each circuit is stored inside an already bounded owner: an indexed Auto Buy candidate/resource or one of Mentor's fixed global/domain slots. Common owns no candidate registry, subscriptions, timers, logging, Unity references, or background worker.

| State | Retry behavior | Wake condition |
|---|---|---|
| `Healthy` | Work may run. | Not applicable. |
| `RetryAfterTime` | One probe after capped exponential backoff. | Deadline or an explicitly named authoritative event. |
| `RetryAfterLifecycle` | No time retry. | A strictly newer lifecycle generation only. |
| `QuarantinedUntilConfigChange` | No time/lifecycle retry. | A relevant owning-feature configuration change only. |
| `ContractFailed` | No runtime retry. | Plugin/process recreation after the contract is restored. |

Failure streaks saturate at 16 and backoff at 64 owner ticks. An early event wake retains the failure streak, so wake/fail storms increase backoff. Only successful authoritative work resets it. Strong lifecycle/contract states cannot be downgraded by later transient failures.

## Safety classification

- Temporary resource, registry, or native-state reads use bounded time retry and exact event wakes.
- A missing exact accessor/type/schema on an otherwise resolved audited type is a contract failure.
- If a native mutation was attempted but threw, produced an unavailable after-state, or failed its postcondition, that candidate/domain opens until a newer lifecycle.
- Locked, unavailable, queued, completed, unaffordable, queue-full, and native admission rejection are normal game states, not circuit failures.
- Auto Buy continues to later healthy candidates; an optional Mentor domain cannot block healthy sibling domains.
- Native queue, cost, reserve, identity, maximum, and postcondition checks remain authoritative on every permitted mutation.

## Verification

Portable tests freeze state values and cover bounded backoff, overflow-safe deadlines, early and unrelated wakes, lifecycle/config isolation, terminal contracts, strong-state precedence, resource-reader retry, exact cost-contract quarantine, mutation recovery, and Mentor sibling-domain progress. Runtime validation must additionally exercise repeated native faults, save/load recovery, healthy-candidate fairness, bounded logs, and a 30-minute soak with no scheduler starvation.
