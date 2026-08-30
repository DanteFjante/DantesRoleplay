# D&D 2024 active prototype recovery

Status: accepted

## Boundary

Recover the D&D 2024 prototype records removed during catalog flattening, translate their canonical
IDs from the legacy `content.dnd2024`/`item.dnd2024` order to the active `dnd2024.*` namespace,
place them under `catalog/applications/dnd2024/`, activate them, and materialize their authored
entities in `dnd2024-main`. Preserve newer flat catalog records where a migrated replacement exists.

The recovered character-creation composition retains its matching ability, species, and class
progression child contracts. Interaction failures without committed operations do not cite phantom
operation IDs.

## Acceptance evidence

- `roleplay validate catalog`: valid, with the existing near-duplicate procedure warning only.
- Application preview: valid, zero problems.
- Live activation and populated-state compatible rebind completed.
- 103 recovered content entities synchronized into `dnd2024-main` in six bounded batches.
- Character creation executed successfully for `actor.caldris.ganji`; the actor has twelve core
  components and an active `world.caldris` participation record.

## Deliberate exclusions

No obsolete duplicate replaces a newer flat component definition. Unimplemented D&D feature
behaviors remain explicit deferred entitlements owned by the character-creation mechanic.
