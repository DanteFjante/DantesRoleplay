# Platform E8 — Slice 1 receipt

## Delivered

- Event schemas may declare canonical `x-dantes-entity-payload-fields` metadata for direct string
  payload properties.
- Subscription versions now persist a zero-or-one `roleFromEventPayload` mapping, including
  catalog import/export, fingerprints, migration, query details, validator, and commit surface.
- Reaction routing binds the mapped role only when the accepted event's own type version declares
  the field and its value names exactly one accepted event entity. Existing projection resolution
  then verifies the entity and requested components.
- Invalid registration is rejected before persistence. Corrupt runtime mappings or payload values
  abort the root change atomically.
- Empty mappings preserve prior fixed-role behavior. No fan-out, scheduler, or ruleset behavior
  was added.

## Evidence

- Focused storage, routing, migration, catalog, and coverage tests passed.
- `roleplay validate catalog` validated 415 records without touching live data (82 existing
  duplicate-match warnings).
- Full suite: 799 passed, 0 failed, 0 skipped.

## Boundary

Slice 2 remains separate: bounded fan-out over an explicitly materialized entity set. Feature 33
or any other ruleset consumer must not be added until that generic platform slice is specified and
accepted.
