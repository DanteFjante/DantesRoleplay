# Platform E8 — Slice 1 implementation boundary

## Goal

Make one declared top-level event payload field available as one ordinary reaction role. This is
generic infrastructure: it does not name a ruleset, rest, creature, character, or gameplay
concept.

## Confirmed public contract

- An event type may declare root schema extension
  `x-dantes-entity-payload-fields`. It is absent, or a canonical ordinal-sorted, distinct array of
  1–12 direct root `properties` whose schemas have `type: "string"`.
- A subscription version stores `roleFromEventPayload`, a closed JSON object with zero or one
  entry mapping an ordinary reaction role to one declared payload field.
- A nonempty mapping is accepted only when its role is declared by the reaction mechanic, is not
  fixed, all remaining required roles are fixed, the mechanic has no children, and the field is
  declared by the current event-type schema.
- On dispatch, the event payload value must be a trimmed nonempty string occurring exactly once in
  the accepted event's entity ids. It is then resolved through the existing projection resolver.
- Empty mappings preserve prior subscription behavior exactly. Slice 1 does not fan out, schedule
  work, or add a game-specific consumer.

## Acceptance

- Metadata and mapping round-trip through storage and catalog export/import and participate in
  source hashes.
- Invalid declarations or mappings are rejected before persistence.
- A corrupt runtime mapping or payload aborts the root change atomically.
- Focused tests cover a successful dynamic role, invalid registration, runtime rollback, and the
  no-mapping compatibility path.
