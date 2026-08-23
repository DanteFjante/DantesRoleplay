---
id: procedure.mechanic.dnd2024.conditions
category: ruleset.dnd2024.core.state.conditions
name: Govern D&D 2024 creature conditions
governs: commit(kind: "component") declaring dnd2024.conditions; commit(kind: "mechanic") authoring mechanic.dnd2024.conditions.write; commit(kind: "event-type") declaring dnd2024.exhaustion.reached-lethal; commit(kind: "action") recording, applying, clearing, exhausting, or recovering creature condition instances
status: active
---

## Description

Owns the closed state for the fourteen non-Exhaustion conditions and the single leveled Exhaustion
condition in the D&D 2024 SRD, plus its one normal administrative writer. It stores condition
instances and their optional entity-source provenance; it neither causes a condition nor derives
any D20 Test, movement, damage, or death effect. A level-six Exhaustion transition announces a
fact for Feature 17 to consume; it does not apply death state.

## Instructions

1. `dnd2024.conditions` has exactly `entries` and a fixed `sourceRef` of
   `source.dnd2024.srd-5.2.1` at `Rules Glossary`. Entries use only these stable ids: blinded,
   charmed, deafened, frightened, grappled, incapacitated, invisible, paralyzed, petrified,
   poisoned, prone, restrained, stunned, unconscious, and exhaustion. The fourteen non-Exhaustion
   entries may carry optional `sourceEntityId`; the one source-free Exhaustion entry instead
   requires integer `level` 1 through 6. A level of zero is represented by no Exhaustion entry.
2. `mechanic.dnd2024.conditions.write` is the only normal record/apply/clear/exhaust/recover path.
   `record` creates the empty known state with `component.add`; every other mode requires valid
   existing state and uses exactly one complete `component.set`. Every proposed add or replacement,
   including one from a later event reaction, is validated by
   `mechanic.dnd2024.conditions.guard` before it can commit.
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
6. `apply` and `clear` are only for non-Exhaustion ids. `exhaust` and `recover` each take exactly
   `{"mode":"<mode>","levels":<integer 1..6>}`, have no source role, and change the one
   Exhaustion aggregate. Gain fails above level 6; recovery fails at level 0 or below it. Reaching
   exactly level 6 declares `dnd2024.exhaustion.reached-lethal` once with `creatureId`, constant
   `level: 6`, and source reference `Rules Glossary > Exhaustion`.

## Constraints

- Inputs are closed. Callers never provide source identity, source references, entries, duration,
  a resulting level, effective conditions, a condition cause, derived effects, events, or effects.
- Reject corrupt state, duplicates, unavailable clear targets, unsupported ids, self/missing sources,
  and invalid Petrified/Poisoned combinations before proposing any effect. Never repair malformed
  stored state implicitly.
- The writer changes only the subject's condition component, names only that subject on its lethal
  event, and consumes no randomness.
- This contract is distinct from `procedure.game.core.world.condition`, which owns scheduled route
  closure state rather than creature condition instances.
