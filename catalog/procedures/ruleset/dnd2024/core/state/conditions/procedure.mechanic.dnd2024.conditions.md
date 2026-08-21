---
id: procedure.mechanic.dnd2024.conditions
category: ruleset.dnd2024.core.state.conditions
name: Govern D&D 2024 creature conditions
governs: commit(kind: "component") declaring dnd2024.conditions; commit(kind: "mechanic") authoring mechanic.dnd2024.conditions.write; commit(kind: "action") recording, applying, or clearing creature condition instances
status: active
---

## Description

Owns the closed state for the fourteen non-Exhaustion conditions in the D&D 2024 SRD and its one
normal administrative writer. It stores condition instances and their optional entity-source
provenance; it neither causes a condition nor derives any D20 Test, movement, damage, or death
effect. Those consumers are later Feature 13 slices.

## Instructions

1. `dnd2024.conditions` has exactly `entries` and a fixed `sourceRef` of
   `source.dnd2024.srd-5.2.1` at `Rules Glossary`. Entries use only these stable ids: blinded,
   charmed, deafened, frightened, grappled, incapacitated, invisible, paralyzed, petrified,
   poisoned, prone, restrained, stunned, and unconscious. Exhaustion is excluded until Feature 14.
2. `mechanic.dnd2024.conditions.write` is the only normal record/apply/clear path. `record` creates
   the empty known state with `component.add`; `apply` and `clear` require valid existing state and
   use exactly one complete `component.set`.
3. An instance's optional `sourceEntityId` comes only from the writer's optional `source` role. A
   supplied role must resolve to an entity when an instance is added. Stored source identity is
   historical provenance: it remains clearable if that source later disappears.
4. Instances are unique by `(condition, sourceEntityId)` and sort by the fixed condition order,
   with unattributed instances before source identities. Charmed, Frightened, and Grappled require
   a non-self source. With no source role, clear removes every instance of each requested condition;
   with it, clear removes only that source's matching instances.
5. Petrified excludes Poisoned state. Applying Poisoned while Petrified is effective fails.
   Applying Petrified removes all Poisoned instances in the same replacement; clearing Petrified
   never restores them.

## Constraints

- Inputs are closed. Callers never provide source identity, source references, entries, duration,
  level, effective conditions, a condition cause, derived effects, or effects.
- Reject corrupt state, duplicates, unavailable clear targets, unsupported ids, self/missing sources,
  and invalid Petrified/Poisoned combinations before proposing any effect. Never repair malformed
  stored state implicitly.
- The writer changes only the subject's condition component and consumes no randomness.
- This contract is distinct from `procedure.game.core.world.condition`, which owns scheduled route
  closure state rather than creature condition instances.
