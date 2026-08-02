# Strategy

How to play Orb of Creation well. This is the opinionated layer: policies, trade-offs, and
run planning. The factual layer it builds on is [`../game-systems/`](../game-systems/README.md)
— read that first; nothing here re-explains mechanics.

This playbook changes as we learn. When a page disagrees with observed reality, the page is
wrong: fix it in the same change that proves it wrong.

## Pages

1. [principles.md](principles.md) — the durable decision rules: hidden-magnitude one-way
   doors, reservation-shaped advice, binding constraints, commitment points, the watermark
   problem.
2. [resource-policies.md](resource-policies.md) — the per-resource policy model: lifecycle
   (scarce → frontier → commodity → meaningless), policy vocabulary, and the four spend modes.
3. [run-plan.md](run-plan.md) — planning a run: NG+ start protocol, milestone chains, Time
   Advancement distribution, challenge selection.
4. [advisor.md](advisor.md) — the advisor-first program: scoring moves, actuators, and the
   failure modes any automation must be tested against.

## Scoring protocol

Options are rated **−10 to +10**: 0 = doesn't matter, negative = would actively slow the run,
+10 = the single best move available in the game right now. Every rating comes with a short
"why". Ratings are relative to the current state and must be relitigated when the state
changes — a purchase can move from +3 to +8 because its cost pools hit capacity.
