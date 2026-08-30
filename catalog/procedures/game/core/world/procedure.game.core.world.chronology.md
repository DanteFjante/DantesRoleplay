---
id: procedure.game.core.world.chronology
category: game.core.world.chronology
name: Govern dedicated dated World chronology
governs: commit(kind: "component") declaring game.core.world.chronology; commit(kind: "effects") recording, correcting, or archiving chronology entries and their game.core.world.chronology.in-world and game.core.world.chronology.about relationships
status: active
---

## Description

Defines durable dated setting history for one World. A chronology entry has one explicit calendar
coordinate and authored display date, one World scope, and optional exact subjects. It is not a
campaign recap, knowledge record, structural event, clock history, or automatic consequence of
another write.

## Instructions

1. Declare `game.core.world.chronology` once. Its complete closed data contains status, title,
   summary, calendar identity, signed occurrence minute, precision, authored date label, and
   descriptive visibility. Use the entity name only as administrative identity; the chronology
   title is the narrative heading presented to a reader.
2. Every chronology entity has exactly one empty-data
   `game.core.world.chronology.in-world` relationship to one active World root with one valid
   `game.core.world.clock`. The entry's `calendarId` must exactly equal that clock's immutable
   calendar identity.
3. A chronology entity may have zero through ten empty-data
   `game.core.world.chronology.about` relationships to exact entities already proven by their own
   governing contracts to belong to the same World. This relationship adds no ownership, location,
   allegiance, discovery, or containment meaning.
4. Author reviewed setup with one inspected effects list ordered entity creation, complete component
   addition, the required World-scope relationship, then optional subject relationships in lexical
   target-ID order. Correct or archive an entry by replacing the complete closed component; never
   merge a partial date or narrative.
5. The signed minute supplies deterministic chronology order and may precede the root clock's zero
   epoch. `dateLabel` is independently authored because no calendar formatter exists. `precision`
   states whether the coordinate is exact, approximate, or an era ordering point; neither field is
   derived from the other.
6. For a trusted-GM read, use the chronology recipe in `procedure.game.core.world.read`, validate
   every returned chronology record and scope, omit archived entries from the ordinary view, then
   order active entries by `occurredAtMinute` and permanent entity ID. Equal minutes are distinct
   simultaneous/ordering-equivalent records and are never merged.

## Constraints

- Status is exactly active or archived; precision is exactly exact, approximate, or era; visibility
  is exactly public, party, or gm. All text is trimmed, nonempty, and within the component schema's
  bounds. Occurrence minute is a safe integer from -1,000,000,000 through 1,000,000,000.
- `in-world` is directed chronology → root, unique, non-self, and exact `{}`. Missing, duplicate,
  reversed, nonempty, inactive, absent-clock, or calendar-mismatched scope violates this feature.
- `about` is directed chronology → subject, non-self, unique per target, exact `{}`, capped at ten,
  and same-world by the target owner's existing scope proof. This feature never invents a generic
  World-membership rule for otherwise unscoped entities.
- The component contains no World ID, subject IDs, campaign/session/quest ID, event/operation ID,
  wall-clock timestamp, relative age, duration, current flag, cause, consequence, map, media,
  authorization result, or generated prose.
- Creating, correcting, archiving, or reading chronology never changes the root clock, knowledge,
  campaign recap, event ledger, topology, faction, map, or any subject. Clock advance, travel,
  campaign completion, knowledge reveal, and structural events never create chronology implicitly.
- This feature creates no mechanic, event type, subscription, notification, migration, player-safe
  projection, web route, or UI consumer.
