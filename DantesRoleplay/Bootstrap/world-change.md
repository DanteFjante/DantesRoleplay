---
id: procedure.world.change
category: world
name: Change world state
governs: commit(kind: "effects"), creating or deleting entities, writing component data, moving things, relating things
revised-by: Claude Opus 5, 2026-08-18 — three-verb call forms; added when to prefer a rule over direct effects
status: active
---

## Description
The one procedure for changing anything in the world directly. Every structural change goes
through `commit(kind: "effects")`, which validates the whole list and then applies it as a single
transaction.

## Instructions
1. Read the current state first. `query(kind: "entities", ids: [...])` returns each entity in full
   — its components, what contains it, what it contains and its relationships. Changing something
   you have not read is how you overwrite data you did not know was there.
2. Check the component definitions you intend to use exist, with `query(kind: "world")`. If one is
   missing, `commit(kind: "component")` it before you reference it — see `procedure.world.model`.
3. There are exactly nine kinds of effect, and they are the whole vocabulary of structural change:

   | type | fields |
   | --- | --- |
   | `entity.create` | `entityId`, `name` — the id is yours to choose and permanent |
   | `entity.delete` | `entityId` — the id stays taken afterwards |
   | `component.add` | `entityId`, `definitionId`, `data` — fails if already present |
   | `component.set` | `entityId`, `definitionId`, `data` — replaces the data wholesale |
   | `component.merge` | `entityId`, `definitionId`, `data` — patches top-level keys only |
   | `component.remove` | `entityId`, `definitionId` |
   | `containment.move` | `entityId`, `toEntityId`, `slot` — omit `toEntityId` to take it out |
   | `relationship.create` | `entityId`, `toEntityId`, `kind`, `data` |
   | `relationship.remove` | `entityId`, `toEntityId`, `kind` |

   `data` is a JSON object encoded as a string, e.g. `"{\"strength\":12}"`. There are no
   game-specific verbs and there never will be: a status, a score or an inventory is a component
   definition plus data, not a new kind of effect. `query(kind: "capabilities")` returns this same
   table.
4. Assemble the whole change as one list of effects, in the order they should happen. A later
   effect may rely on an earlier one, so creating an entity and populating it belongs in a single
   call rather than three.
5. Call `commit(kind: "effects", payload: {"effects": [...]}, dryRun: true)`. Validation reports
   **every** fault at once, each with the position of the offending effect and what would make it
   right.
6. Read what came back. If there are problems, fix them and dry-run again. A dry run you did not
   read is worse than none, because it makes the commit that follows look considered.
7. Call again with `dryRun` omitted to commit exactly the list you validated. Do not add "one
   more thing" between the dry run and the commit — validate what you send.
8. Confirm with `query(kind: "entities", ids: [...])` that the result reads the way you intended,
   and quote the returned `operationId` when you report what you did.

## Constraints
- **Prefer a rule when the outcome is uncertain.** Direct effects are for setting the scene and
  for consequences already decided: placing a character, moving an object, recording something
  that simply happened. When the question is "does it work?", "how much?", or "what happens?", the
  answer belongs to a mechanic — `commit(kind: "action")`, governed by `procedure.action.run` —
  because a rule is stored, versioned, reusable and replayable, and a hand-applied effect is a
  decision nobody can review or repeat. Deciding an outcome yourself and writing it in as effects
  is how a system full of rules stops being used.
- There is no partial application. If any effect in a list is invalid, none of them happen — so
  do not split a change into several calls to "get part of it in".
- `component.set` replaces the component's data wholesale. If you mean to change one key, use
  `component.merge`.
- `component.add` fails when the component is already present. That is the point: use it when
  applying the thing twice would be a bug.
- Entity ids are permanent, and stay taken after the entity is deleted. Choose them as carefully
  as procedure ids.
- Removing something that is not there is reported as a fault, not quietly ignored. If you get
  that error, your picture of the world is wrong — go back to step 1.
- Never describe an outcome you did not apply. If the effects were rejected, nothing changed.
