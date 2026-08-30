# Feature 14, Slice 0 — ordinary-action declared-event propagation receipt

Date: 2026-08-21  
Status: **Verified**

## Delivered boundary

Ordinary mechanics now pass their declared events from `ActionRunner` through the transactional
effect pipeline. The pipeline validates the event type, payload, and named entities, writes its
ledger row with the action operation id as correlation/root id, and routes it normally. A rejected
declaration rolls back the action's effects and writes no event.

Invalid declared events now return `INVALID_DECLARED_EVENT`, with event-type guidance, rather than
incorrectly reporting a guard veto. Reaction diagnostics name their actual producer, including a
root action, rather than assuming every declaration came from a subscription.

## Evidence

- `ActionRunnerTests.A_root_action_commits_its_declared_event_with_its_effects` proves an effect,
  custom declared event, ledger correlation, entity index, and normal reaction all commit as one
  root action.
- `ActionRunnerTests.An_invalid_root_declared_event_rolls_back_the_action_effects` proves an
  unregistered root declaration changes neither the component nor the event ledger.
- Focused kernel tests: **39 passed, 0 failed** (`ActionRunnerTests`, `EventLedgerTests`, and
  `DerivedEventTests`).
- Disposable catalog validation: **253 records valid, 0 warnings**.
- Rebuilt, isolated serial full suite: **532 passed, 0 failed, 0 skipped**.
- `git diff --check` completed without whitespace errors (only pre-existing line-ending notices).

## Next boundary

Slice 1 may now revise the `dnd2024.conditions` catalog owner and writer for the single leveled
Exhaustion entry, add its two dedicated transitions, and define the level-six lethal event. It does
not define death; Feature 17 owns that resulting state.
