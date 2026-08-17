---
id: procedure.world.change
category: world
name: Change world state
governs: apply_effects, creating or deleting entities, writing component data, moving things, relating things
status: active
---

## Description
The one procedure for changing anything in the world. Every structural change goes through
`apply_effects`, which validates the whole list and then applies it as a single transaction.

## Instructions
1. Read the current state first. `get_entities(ids: [...])` returns an entity in full — its
   components, what contains it, what it contains and its relationships. Changing something you
   have not read is how you overwrite data you did not know was there.
2. Check the component definitions you intend to use exist, with `describe_world()`. If one is
   missing, `define_component` it before you reference it — see `procedure.world.model`.
3. Assemble the whole change as one list of effects, in the order they should happen. A later
   effect may rely on an earlier one, so creating an entity and populating it belongs in a single
   call rather than three.
4. Call `apply_effects(effects: [...], dryRun: true)`. Validation reports **every** fault at once,
   each with the position of the offending effect and what would make it right.
5. Read what came back. If there are problems, fix them and dry-run again. A dry run you did not
   read is worse than none, because it makes the commit that follows look considered.
6. Call again with `dryRun` omitted to commit exactly the list you validated. Do not add "one
   more thing" between the dry run and the commit — validate what you send.
7. Confirm with `get_entities` that the result reads the way you intended, and quote the returned
   `operationId` when you report what you did.

## Constraints
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
