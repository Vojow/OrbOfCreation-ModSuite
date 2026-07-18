# Orb Mentor interactive runtime checklist

[Back to development documentation](../README.md) · [Orb Mentor reference](../../src/OrbMentor/README.md)

Use only on a backed-up save and a manually installed test copy of the built DLLs. Automated checks do not install anything into the live game.

- Confirm the supported assembly hash and a quiet startup with `Mode=Disabled`.
- Confirm the compact control appears beside the queue controls, reads `M OFF`, and its status notice lists economy, percentage, tied mentors, and recipient count.
- On progression states before mastery or a domain screen unlocks, activate Mentor and confirm `M WAIT` names the native lock without errors, catalog/log churn, tooltip scans, XP capture, or configuration changes.
- Unlock spell mastery, the artifact workshop, and alchemy independently; confirm only the newly eligible domain starts catalog and relationship work promptly, without a scene restart.
- Load or reset to a progression state where one or more domains are locked again; confirm pending captures/plans/grants are cancelled and the affected domains return to `WAIT` before any stale mutation.
- Toggle with the button and `Alt+M`; verify `ON`, `OFF`, and forced `BLOCKED` presentation and immediate pending-work cancellation.
- At 1× speed, compare native XP and detailed logs for instant, channelled, aura/toggled, rapid, and large-batch spell events.
- Test one mentor, tied mentors, a changing highest tier, lower/equal/higher mastery recipes, locked recipes, active recipients, and ready-to-confirm recipients with banked XP.
- Verify Shared Pool at 0%, 10%, and 100% conserves the configured bonus pool; verify Per Recipient at the same values scales exactly with eligible count.
- Confirm source XP is unchanged, every recipient grant occurs once, and Mentor-generated grants create no second batch.
- Confirm spell-type XP changes only after each native `PurchaseLevel` confirmation, including bulk confirmation.
- Disable or emergency-block while a large batch spans frames; confirm all ungranted bonus work is discarded.
- Save/load, change scenes, reset/prestige, remove the plugin, reload, and reinstall; confirm no stale grants or save repair.
- Repeat at normal and accelerated game speeds and with/without Automata and Orb Mod Config.
- Confirm normal logs remain quiet and detailed logs identify source, batch, recipient UUID, and amount when enabled.

## Alchemy extension

- Enable only the Alchemy domain and confirm continuous active-recipe XP is observed once at the exact native amount.
- Complete a recipe, including a multi-completion batch, and confirm the final multiplied completion XP is shared once.
- Verify only discovered lower-mastery recipes receive XP and native automatic mastery/type progression occurs once.
- Put a Scholar concept above every ordinary recipe's mastery level; confirm the highest ordinary recipe remains the Alchemy mentor and the concept is neither a mentor nor a recipient.
- Earn Scholar concept XP and confirm it creates no Mentor capture, grant, or dropped-work warning.
- With Alchemy sharing disabled, exercise Scholar/ordinary progression and confirm the shared classifier remains uninitialized and the normal log stays quiet.
- Save/load and cross reset/NG+ after Alchemy has initialized; confirm the classifier refreshes its lifecycle evidence before any later ordinary-alchemy grant.
- In a development fixture, introduce unknown or contradictory domain evidence and confirm Alchemy alone shows `Blocked` before mutation.
- Confirm Mentor grants do not change active instances, quantities, recipe time, advancement, costs, or completion effects.

## Artifact extension

- Enable only the Artifacts domain and confirm only equipped, fully attuned artifacts create mentor events.
- Confirm unequipped, attuning, and merely created artifacts do not create source XP events.
- Verify created lower-mastery artifacts can receive XP without being equipped or attuned and without Mentor charging usage costs or changing creation state.
- Cross one and several mastery thresholds; compare recipient XP, mastery level, total equipment mastery, sounds/logs, and saved values with native equipped progression.
- Confirm Mentor does not change loadout membership, stack quantity, attunement, effects, slots, costs, or creation state.
- Run spells, artifacts, and alchemy together and confirm the round-robin worker prevents any domain from starving.

Do not call the beta production-ready until every applicable item has runtime evidence.
